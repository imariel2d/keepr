import { type Page, expect } from '@playwright/test';
import {
  ADMIN_EMAIL,
  ADMIN_FIRST,
  ADMIN_INITIAL_PASSWORD,
  ADMIN_LAST,
  ADMIN_PASSWORD,
} from './config';

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

/**
 * Brings the seeded bootstrap admin to a ready state: signs in with the initial secret, completes
 * the forced password change (rotating to ADMIN_PASSWORD), and sets a first/last name so the invite
 * email's inviter line is populated.
 *
 * Assumes a fresh stack (the CI contract — the e2e job brings the compose stack up and tears it
 * down), where the account still carries must-change and signing in with the initial secret lands
 * on the forced-change screen. On a reused DB the password has already been rotated, so the initial
 * secret no longer works and this fails at sign-in — reset first with `docker compose … down -v`.
 * The `/files` branch below only covers a fresh account that somehow arrives without must-change.
 */
export async function bootstrapAdmin(page: Page): Promise<void> {
  await login(page, ADMIN_EMAIL, ADMIN_INITIAL_PASSWORD);

  // Fresh DB → forced change lands on /profile?changePassword=1. Reused DB → straight to /files.
  await page.waitForURL(/\/profile\?changePassword=1|\/files/);

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
