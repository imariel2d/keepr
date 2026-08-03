import { test, expect } from '@playwright/test';
import { ADMIN_EMAIL, ADMIN_STATE, NEW_USER_PASSWORD, uniqueEmail } from '../support/config';
import { createReadyUser, login } from '../support/admin';

// Journey D from docs/testing-strategy.md: the admin console's observable effects — a quota change
// and a role change reflected in the row, the self-row protected against demotion/removal (the UI
// face of the last-admin guard), and a kick that removes the account and kills its live session.
//
// Note: "audit entries appear" from the strategy has no read surface in the app (AdminActionLog is
// write-only — no admin audit view or endpoint), so it isn't asserted here; the write is exercised
// server-side. See docs/testing-strategy.md.
test.use({ storageState: ADMIN_STATE });

test('journey D — admin console effects', async ({ page, browser, baseURL }) => {
  const target = uniqueEmail('target'); // quota + role changes
  const victim = uniqueEmail('victim'); // kick + session death

  await test.step('seed two throwaway accounts', async () => {
    await createReadyUser(baseURL!, { email: target, password: NEW_USER_PASSWORD, role: 'User' });
    await createReadyUser(baseURL!, { email: victim, password: NEW_USER_PASSWORD, role: 'User' });
  });

  await page.goto('/admin/accounts');
  const targetRow = page.getByRole('row').filter({ hasText: target });
  await expect(targetRow).toBeVisible();

  await test.step('a quota change is reflected in the row', async () => {
    await targetRow.getByRole('button', { name: 'Quota' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.locator('input[type="number"]').fill('7');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('status')).toContainText(`Quota updated for ${target}.`);

    // Re-open to confirm the row now carries the new quota (the dialog seeds from the row's bytes).
    await targetRow.getByRole('button', { name: 'Quota' }).click();
    await expect(page.getByRole('dialog').locator('input[type="number"]')).toHaveValue('7');
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();
  });

  await test.step('a role change is reflected in the row', async () => {
    await targetRow.getByRole('button', { name: 'Role' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.locator('select').selectOption('Admin');
    await dialog.getByRole('button', { name: 'Save' }).click();
    await expect(page.getByRole('status')).toContainText(`${target} is now Admin.`);
    await expect(targetRow.locator('.badge')).toHaveText('Admin');
  });

  await test.step('the admin cannot demote or remove itself (last-admin guard)', async () => {
    const selfRow = page.getByRole('row').filter({ hasText: ADMIN_EMAIL });
    await expect(selfRow.getByRole('button', { name: 'Role' })).toBeDisabled();
    await expect(selfRow.getByRole('button', { name: 'Remove' })).toBeDisabled();
  });

  await test.step('kicking a user removes them and kills their live session', async () => {
    // Sign the victim in from a separate context so we can watch their session die.
    const victimContext = await browser.newContext({ baseURL });
    const victimPage = await victimContext.newPage();
    await login(victimPage, victim, NEW_USER_PASSWORD);
    await victimPage.waitForURL(/\/files/);

    const victimRow = page.getByRole('row').filter({ hasText: victim });
    await victimRow.getByRole('button', { name: 'Remove' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('textbox').fill(victim); // type-to-confirm
    await dialog.getByRole('button', { name: 'Remove account' }).click();

    await expect(page.getByRole('status')).toContainText(`${victim} has been removed.`);
    await expect(page.getByRole('row').filter({ hasText: victim })).toHaveCount(0);

    // A full reload re-probes the session server-side; the revoked session bounces to /login.
    await victimPage.goto('/files');
    await victimPage.waitForURL(/\/login/);
    await victimContext.close();
  });
});
