import { test, expect } from '@playwright/test';
import {
  ADMIN_DISPLAY_NAME,
  ADMIN_EMAIL,
  NEW_USER_EMAIL,
  NEW_USER_PASSWORD,
} from '../support/config';
import { bootstrapAdmin, login, logout } from '../support/admin';
import { clearMailbox, waitForSingleMessageTo } from '../support/mailpit';

// Journey A from docs/testing-strategy.md: invite → claim → sign-in, end to end through
// Angular → API → Postgres → (Mailpit). One ordered narrative; see the config in playwright.config.ts
// (workers: 1, no parallelism) for why it runs alone.
test('journey A — invite, claim, sign in, and the claim link dies', async ({ page, request }) => {
  await test.step('bootstrap the admin (rotate password + set a name)', async () => {
    await bootstrapAdmin(page);
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();
  });

  await test.step('start from an empty mailbox', async () => {
    await clearMailbox(request);
  });

  await test.step('admin invites newuser@example.com', async () => {
    await page.goto('/admin/accounts');
    await page.getByRole('button', { name: 'New account' }).click();

    const dialog = page.getByRole('dialog');
    await dialog.locator('input[type="email"]').fill(NEW_USER_EMAIL);
    await dialog.getByRole('radio', { name: 'Send an email invite' }).check();

    const [res] = await Promise.all([
      page.waitForResponse(
        (r) => r.url().includes('/api/admin/users') && r.request().method() === 'POST',
      ),
      dialog.getByRole('button', { name: 'Send invite' }).click(),
    ]);
    expect(res.status(), 'POST /api/admin/users returns 201 Created').toBe(201);

    await expect(page.getByRole('status')).toContainText(`Invite sent to ${NEW_USER_EMAIL}.`);
    await expect(page.getByRole('row', { name: new RegExp(NEW_USER_EMAIL) })).toContainText(
      'Pending',
    );
  });

  let claimToken = '';
  await test.step('the invite email names the inviter and never leaks their address', async () => {
    const message = await waitForSingleMessageTo(request, NEW_USER_EMAIL);
    expect(message.Subject).toBe("You're invited to Keepr");

    const body = `${message.HTML}\n${message.Text}`;
    expect(body, 'inviter shown by name').toContain(`${ADMIN_DISPLAY_NAME} has invited you to Keepr`);
    expect(body, 'inviter address must never appear').not.toContain(ADMIN_EMAIL);

    const match = body.match(/\/claim\/([A-Za-z0-9_-]+)/);
    expect(match, 'claim link present in the email').not.toBeNull();
    claimToken = match![1];
  });

  await test.step('new user claims the account and lands authenticated', async () => {
    await page.goto(`/claim/${claimToken}`);
    await expect(page.getByRole('heading', { name: 'Set your password' })).toBeVisible();

    await page.locator('input[name="password"]').fill(NEW_USER_PASSWORD);
    await page.locator('input[name="confirm"]').fill(NEW_USER_PASSWORD);
    await page.getByRole('button', { name: 'Set password & sign in' }).click();

    await page.waitForURL(/\/files/);
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();
  });

  await test.step('new user can sign out and back in with the new password', async () => {
    await logout(page);
    await login(page, NEW_USER_EMAIL, NEW_USER_PASSWORD);
    await page.waitForURL(/\/files/);
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();
  });

  await test.step('the used claim link is now dead', async () => {
    await page.goto(`/claim/${claimToken}`);
    await expect(page.getByRole('heading', { name: 'Invitation not valid' })).toBeVisible();
  });
});
