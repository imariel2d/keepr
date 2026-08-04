import { test, expect } from '@playwright/test';
import { ADMIN_STATE, uniqueEmail } from '../support/config';
import { login, logout } from '../support/admin';
import { clearMailbox, messagesTo, waitForSingleMessageTo } from '../support/mailpit';

// Journey E from docs/testing-strategy.md: self-service password reset, end to end through
// Angular → API → Postgres → (Mailpit). Feature #26.
//
// forgot-password mints a link only for an EmailVerified account, and the only thing that verifies
// an account is claiming an invite (docs/feature-26-password-reset.md §3.2). So the journey first
// mints a *verified* user via invite → claim, then drives the reset. Both emails are read from
// Mailpit. The invite is created as the authenticated admin (shared storage state); the claim and
// the whole reset flow run in fresh, unauthenticated contexts — the user, not the admin.
test.use({ storageState: ADMIN_STATE });

// Passwords: >= 12 chars, must not contain the email local part ('resetuser'), and random-ish so
// they aren't in the breach corpus. FIRST is set at claim; SECOND is the reset target.
const FIRST_PASSWORD = 'Purple-Otter-2026-one';
const SECOND_PASSWORD = 'Purple-Otter-2026-two';

test('journey E — forgot password, reset by email, and the link dies', async ({
  page,
  browser,
  request,
  baseURL,
}) => {
  const account = uniqueEmail('resetuser');
  let resetToken = '';

  await test.step('mint a verified account via invite + claim', async () => {
    await clearMailbox(request);

    await page.goto('/admin/accounts');
    await page.getByRole('button', { name: 'New account' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.locator('input[type="email"]').fill(account);
    await dialog.getByRole('radio', { name: 'Send an email invite' }).check();
    await Promise.all([
      page.waitForResponse(
        (r) => r.url().includes('/api/admin/users') && r.request().method() === 'POST',
      ),
      dialog.getByRole('button', { name: 'Send invite' }).click(),
    ]);

    const invite = await waitForSingleMessageTo(request, account);
    const claimToken = `${invite.HTML}\n${invite.Text}`.match(/\/claim\/([A-Za-z0-9_-]+)/)?.[1];
    expect(claimToken, 'claim link present in the invite email').toBeTruthy();

    // A fresh context so this is the invitee's session, not the admin's. Claiming an invite is what
    // sets EmailVerified = true, which is the precondition for a self-service reset below.
    const context = await browser.newContext({ baseURL });
    const claim = await context.newPage();
    await claim.goto(`/claim/${claimToken}`);
    await claim.locator('input[name="password"]').fill(FIRST_PASSWORD);
    await claim.locator('input[name="confirm"]').fill(FIRST_PASSWORD);
    await claim.getByRole('button', { name: 'Set password & sign in' }).click();
    await claim.waitForURL(/\/files/);
    await context.close();
  });

  await test.step('login offers self-service reset, and requesting one is neutral + sends one email', async () => {
    await clearMailbox(request);
    const context = await browser.newContext({ baseURL });
    const anon = await context.newPage();

    // Mail is configured (env-SMTP → Mailpit), so capabilities.selfServiceReset is true and the
    // login screen renders an actionable "Forgot password?" link rather than the "contact admin" copy.
    await anon.goto('/login');
    await anon.getByRole('link', { name: 'Forgot password?' }).click();
    await anon.waitForURL(/\/forgot-password/);

    await anon.locator('input[name="email"]').fill(account);
    await anon.getByRole('button', { name: 'Send reset link' }).click();
    // The always-neutral confirmation — identical whether or not the address can be reset.
    await expect(anon.getByRole('heading', { name: 'Check your email' })).toBeVisible();

    const message = await waitForSingleMessageTo(request, account);
    expect(message.Subject).toBe('Reset your Keepr password');
    const match = `${message.HTML}\n${message.Text}`.match(/\/reset-password\/([A-Za-z0-9_-]+)/);
    expect(match, 'reset link present in the email').not.toBeNull();
    resetToken = match![1];

    await context.close();
  });

  await test.step('the reset link sets a new password and signs the user in', async () => {
    const context = await browser.newContext({ baseURL });
    const reset = await context.newPage();

    await reset.goto(`/reset-password/${resetToken}`);
    await expect(reset.getByRole('heading', { name: 'Reset your password' })).toBeVisible();
    // The form is primed with the account it's for (read-only), from GET /reset-password/{token}.
    await expect(reset.getByText(account)).toBeVisible();

    await reset.locator('input[name="password"]').fill(SECOND_PASSWORD);
    await reset.locator('input[name="confirm"]').fill(SECOND_PASSWORD);
    await reset.getByRole('button', { name: 'Reset password & sign in' }).click();
    await reset.waitForURL(/\/files/);
    await expect(reset.getByRole('button', { name: 'Log out' })).toBeVisible();

    await context.close();
  });

  await test.step('the old password no longer works, the new one does, and the link is spent', async () => {
    const context = await browser.newContext({ baseURL });
    const p = await context.newPage();

    // The pre-reset password is dead — a reset rotates the hash and revokes every session.
    await login(p, account, FIRST_PASSWORD);
    await expect(p.getByRole('alert')).toBeVisible();
    expect(p.url()).toContain('/login');

    // The new password signs in.
    await login(p, account, SECOND_PASSWORD);
    await p.waitForURL(/\/files/);
    await expect(p.getByRole('button', { name: 'Log out' })).toBeVisible();
    await logout(p);

    // The consumed reset link is a dead end (single-use).
    await p.goto(`/reset-password/${resetToken}`);
    await expect(p.getByRole('heading', { name: 'Link not valid' })).toBeVisible();

    await context.close();
  });

  await test.step('forgot-password for an unknown address is neutral and sends nothing', async () => {
    await clearMailbox(request);
    const unknown = uniqueEmail('nobody');
    const res = await request.post('/api/auth/forgot-password', { data: { email: unknown } });
    expect(res.status(), 'unknown address still returns the neutral 202').toBe(202);
    // No account → no send is ever dispatched. Give any (erroneous) send a beat to arrive, then
    // assert the mailbox stayed empty — the no-oracle property (§14.4) at the delivery boundary.
    await page.waitForTimeout(1500);
    expect(await messagesTo(request, unknown), 'no reset email for an unknown address').toHaveLength(0);
  });
});
