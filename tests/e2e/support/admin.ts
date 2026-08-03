import { type APIRequestContext, type Page, request as apiRequest, expect } from '@playwright/test';
import {
  ADMIN_EMAIL,
  ADMIN_FIRST,
  ADMIN_INITIAL_PASSWORD,
  ADMIN_LAST,
  ADMIN_PASSWORD,
  ADMIN_STATE,
} from './config';

/** An API request context authenticated as the admin (reuses the saved storage state / cookie). */
export function adminApi(baseURL: string): Promise<APIRequestContext> {
  return apiRequest.newContext({ baseURL, storageState: ADMIN_STATE });
}

/**
 * Creates a user directly through the admin API with an immediate password (no invite email). The
 * account carries must-change (admin-chosen passwords are flagged), so it is not yet ready to sign
 * straight in — see createReadyUser for that.
 */
export async function createUser(
  api: APIRequestContext,
  opts: { email: string; password: string; role?: 'User' | 'Admin' },
): Promise<void> {
  const res = await api.post('/api/admin/users', {
    data: { email: opts.email, role: opts.role ?? 'User', sendInvite: false, password: opts.password },
  });
  expect(res.status(), `create ${opts.email} → ${await res.text()}`).toBe(201);
}

/**
 * Creates a user that can sign straight in to /files: the admin makes the account (which carries
 * must-change), then the flag is settled to `password` via the API (login → change password),
 * mirroring what a real first sign-in would do — without driving the forced-change screen by hand.
 */
export async function createReadyUser(
  baseURL: string,
  opts: { email: string; password: string; role?: 'User' | 'Admin' },
): Promise<void> {
  // A temporary password that passes PasswordPolicy (>= 12 chars, no reused email local part).
  const initial = 'Initial-Keepr-2026-pw';

  const admin = await adminApi(baseURL);
  await createUser(admin, { email: opts.email, password: initial, role: opts.role });
  await admin.dispose();

  const user = await apiRequest.newContext({ baseURL });
  const loginRes = await user.post('/api/auth/login', {
    data: { email: opts.email, password: initial },
  });
  expect(loginRes.ok(), `login ${opts.email} → ${loginRes.status()}`).toBeTruthy();
  const changeRes = await user.post('/api/me/password', {
    data: { currentPassword: initial, newPassword: opts.password },
  });
  expect(changeRes.ok(), `settle ${opts.email} → ${changeRes.status()}`).toBeTruthy();
  await user.dispose();
}

/**
 * Fills the login form and submits. cove-input renders a real <input> carrying its `name`, and
 * cove-button renders a native <button> whose text is its accessible name, so ordinary
 * role/attribute selectors work.
 */
export async function login(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

/**
 * Opens a user row's overflow (⋮) menu, where the per-row actions now live (change role, set quota,
 * reset password, remove) instead of inline buttons. The trigger's accessible name is
 * "Actions for {email}". After this resolves, click the item via
 * `page.getByRole('menuitem', { name: … })` — the menu is a single shared, page-level element.
 */
export async function openRowMenu(page: Page, email: string): Promise<void> {
  const row = page.getByRole('row').filter({ hasText: email });
  await row.getByRole('button', { name: `Actions for ${email}` }).click();
}

/** Signs the current account out from the top bar and waits for the login screen. */
export async function logout(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Log out' }).click();
  await page.waitForURL(/\/login/);
}

/**
 * Sets the admin's first/last name on the profile screen. The "Your name" card is hidden while the
 * account is in forced-password-change mode, so this must run only after must-change has cleared.
 */
async function setName(page: Page): Promise<void> {
  await page.goto('/profile');
  await page.locator('input[autocomplete="given-name"]').fill(ADMIN_FIRST);
  await page.locator('input[autocomplete="family-name"]').fill(ADMIN_LAST);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByText('Profile saved.')).toBeVisible();
}

/** Resolves once a submitted login has either signed in (navigated away) or failed (error alert). */
async function loginSettled(page: Page): Promise<boolean> {
  await Promise.race([
    page.waitForURL(/\/profile\?changePassword=1|\/files/).catch(() => {}),
    page.getByRole('alert').waitFor({ state: 'visible' }).catch(() => {}),
  ]);
  return !page.url().includes('/login');
}

/**
 * Brings the seeded bootstrap admin to a ready state: signs in, completes the forced password change
 * (rotating to ADMIN_PASSWORD) if one is pending, and sets a first/last name so the invite email's
 * inviter line is populated.
 *
 * Retry-safe: it tries the initial secret first (fresh stack), and falls back to the rotated
 * password when that's already been used (a reused DB, or a re-run after this rotated it). The
 * forced-change branch only fires while must-change is still set, so the fallback path just sets the
 * name.
 */
export async function bootstrapAdmin(page: Page): Promise<void> {
  await login(page, ADMIN_EMAIL, ADMIN_INITIAL_PASSWORD);
  if (!(await loginSettled(page))) {
    // Initial secret rejected → the password was already rotated; sign in with the rotated one.
    await login(page, ADMIN_EMAIL, ADMIN_PASSWORD);
    await page.waitForURL(/\/profile\?changePassword=1|\/files/);
  }

  if (page.url().includes('/profile')) {
    // Change the password first: the "Your name" card is hidden until must-change clears, and this
    // submit redirects to /files once it does.
    await page.locator('input[autocomplete="current-password"]').fill(ADMIN_INITIAL_PASSWORD);
    // Two new-password fields on the page: [0] = new, [1] = confirm.
    const newPasswords = page.locator('input[autocomplete="new-password"]');
    await newPasswords.nth(0).fill(ADMIN_PASSWORD);
    await newPasswords.nth(1).fill(ADMIN_PASSWORD);
    await page.getByRole('button', { name: 'Set password', exact: true }).click();
    await page.waitForURL(/\/files/);
  }

  // Now that must-change is cleared, set the name so the invite's inviter line is exercised.
  await setName(page);
}
