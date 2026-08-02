import { test, expect } from '@playwright/test';
import { ADMIN_DISPLAY_NAME, ADMIN_EMAIL, ADMIN_STATE, NEW_USER_PASSWORD, uniqueEmail } from '../support/config';
import { login, logout } from '../support/admin';
import { clearMailbox, waitForSingleMessageTo } from '../support/mailpit';

// Journey A from docs/testing-strategy.md: invite → claim → sign-in, end to end through
// Angular → API → Postgres → (Mailpit). The invite is created as the authenticated admin (shared
// storage state); the claim runs in a fresh, unauthenticated context — the invitee, not the admin.
test.use({ storageState: ADMIN_STATE });

test('journey A — invite, claim, sign in, and the claim link dies', async ({
  page,
  browser,
  request,
  baseURL,
}) => {
  const invitee = uniqueEmail('newuser');
  let claimToken = '';

  await test.step('start from an empty mailbox', async () => {
    await clearMailbox(request);
  });

  await test.step('admin invites the user', async () => {
    await page.goto('/admin/accounts');
    await page.getByRole('button', { name: 'New account' }).click();

    const dialog = page.getByRole('dialog');
    await dialog.locator('input[type="email"]').fill(invitee);
    await dialog.getByRole('radio', { name: 'Send an email invite' }).check();

    const [res] = await Promise.all([
      page.waitForResponse(
        (r) => r.url().includes('/api/admin/users') && r.request().method() === 'POST',
      ),
      dialog.getByRole('button', { name: 'Send invite' }).click(),
    ]);
    expect(res.status(), 'POST /api/admin/users returns 201 Created').toBe(201);

    await expect(page.getByRole('status')).toContainText(`Invite sent to ${invitee}.`);
    await expect(page.getByRole('row').filter({ hasText: invitee })).toContainText('Pending');
  });

  await test.step('the invite email names the inviter and never leaks their address', async () => {
    const message = await waitForSingleMessageTo(request, invitee);
    expect(message.Subject).toBe("You're invited to Keepr");

    const body = `${message.HTML}\n${message.Text}`;
    expect(body, 'inviter shown by name').toContain(`${ADMIN_DISPLAY_NAME} has invited you to Keepr`);
    expect(body, 'inviter address must never appear').not.toContain(ADMIN_EMAIL);

    const match = body.match(/\/claim\/([A-Za-z0-9_-]+)/);
    expect(match, 'claim link present in the email').not.toBeNull();
    claimToken = match![1];
  });

  await test.step('the invitee claims, signs out and back in, and the link then dies', async () => {
    // A fresh context so this is the invitee's session, not the admin's.
    const context = await browser.newContext({ baseURL });
    const invite = await context.newPage();

    await invite.goto(`/claim/${claimToken}`);
    await expect(invite.getByRole('heading', { name: 'Set your password' })).toBeVisible();
    await invite.locator('input[name="password"]').fill(NEW_USER_PASSWORD);
    await invite.locator('input[name="confirm"]').fill(NEW_USER_PASSWORD);
    await invite.getByRole('button', { name: 'Set password & sign in' }).click();
    await invite.waitForURL(/\/files/);
    await expect(invite.getByRole('button', { name: 'Log out' })).toBeVisible();

    await logout(invite);
    await login(invite, invitee, NEW_USER_PASSWORD);
    await invite.waitForURL(/\/files/);
    await expect(invite.getByRole('button', { name: 'Log out' })).toBeVisible();

    await invite.goto(`/claim/${claimToken}`);
    await expect(invite.getByRole('heading', { name: 'Invitation not valid' })).toBeVisible();

    await context.close();
  });
});
