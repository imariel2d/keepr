import { test as setup, expect } from '@playwright/test';
import { ADMIN_STATE } from '../support/config';
import { bootstrapAdmin } from '../support/admin';

// Runs once before the journeys (the `chromium` project depends on it). It brings the seeded
// bootstrap admin to a ready state — rotating the must-change password and setting a name — and
// saves the authenticated storage state so every admin-driven journey reuses it instead of each
// re-doing (and re-racing) the one-time rotation. See docs/testing-strategy.md (Phase 3).
setup('authenticate the admin', async ({ page }) => {
  await bootstrapAdmin(page);
  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible();
  await page.context().storageState({ path: ADMIN_STATE });
});
