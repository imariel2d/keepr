import { test, expect } from '@playwright/test';
import { NEW_USER_PASSWORD, uniqueEmail } from '../support/config';
import { createReadyUser, login, logout } from '../support/admin';

// Journey B from docs/testing-strategy.md: the auth and session rules — a valid login lands on
// /files, a wrong password is rejected in place, logging out re-guards protected routes, and a
// non-admin is kept out of /admin. Uses a throwaway non-admin account created via the admin API.
test('journey B — auth and session guards', async ({ page, baseURL }) => {
  const member = uniqueEmail('member');

  await test.step('create a non-admin account', async () => {
    await createReadyUser(baseURL!, { email: member, password: NEW_USER_PASSWORD, role: 'User' });
  });

  await test.step('a valid login lands on /files', async () => {
    await login(page, member, NEW_USER_PASSWORD);
    await page.waitForURL(/\/files/);
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();
  });

  await test.step('after logout, a protected route bounces to /login', async () => {
    await logout(page);
    await page.goto('/files');
    await page.waitForURL(/\/login/);
  });

  await test.step('a wrong password is rejected in place, still on /login', async () => {
    await login(page, member, 'definitely-not-the-password');
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });

  await test.step('a non-admin is kept out of /admin', async () => {
    await login(page, member, NEW_USER_PASSWORD);
    await page.waitForURL(/\/files/);
    await page.goto('/admin');
    // adminGuard redirects non-admins to their files rather than a dead page.
    await page.waitForURL(/\/files/);
    await expect(page).not.toHaveURL(/\/admin/);
  });
});
