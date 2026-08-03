import { test, expect } from '@playwright/test';
import { ADMIN_STATE } from '../support/config';

// Journey C from docs/testing-strategy.md: the file lifecycle end to end through the real stack —
// a browser-direct MinIO upload, a byte-exact download, folder create + move, share (viewed by a
// fresh anonymous visitor, then revoked and dead), and delete → Trash → restore → purge. Runs as
// the authenticated admin (shared storage state) in its own, initially empty, file area.
test.use({ storageState: ADMIN_STATE });

test('journey C — files, folders, trash, and sharing', async ({ page, browser, baseURL }) => {
  const stamp = Date.now();
  const fileName = `e2e-${stamp}.txt`;
  const folderName = `e2e-folder-${stamp}`;
  const content = `Keepr e2e payload — verify the bytes match. #${stamp}\n`;
  const size = Buffer.byteLength(content);

  // The file/folder cards render as role=button whose accessible name starts with their name.
  const fileCard = () => page.getByRole('button', { name: fileName });
  const folderCard = () => page.getByRole('button', { name: folderName });

  await test.step('upload a file (browser-direct to MinIO) — it appears with the right size', async () => {
    await page.goto('/files');
    await page.locator('#file-input').setInputFiles({
      name: fileName,
      mimeType: 'text/plain',
      buffer: Buffer.from(content),
    });
    // The upload completes (init → PUT to MinIO → complete) and the list refreshes.
    await expect(fileCard()).toBeVisible({ timeout: 30_000 });
    await expect(fileCard()).toContainText(`${size} B`);
  });

  await test.step('download it — the bytes match what was uploaded', async () => {
    await fileCard().click({ button: 'right' });
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('menuitem', { name: 'Download' }).click(),
    ]);
    const stream = await download.createReadStream();
    const chunks: Buffer[] = [];
    for await (const chunk of stream) chunks.push(chunk as Buffer);
    expect(Buffer.concat(chunks).toString()).toBe(content);
  });

  await test.step('create a folder and move the file into it', async () => {
    await page.getByRole('button', { name: 'New folder' }).click();
    const create = page.getByRole('dialog');
    await create.locator('input').fill(folderName);
    await create.getByRole('button', { name: 'Create' }).click();
    await expect(folderCard()).toBeVisible();

    await fileCard().click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Move to…' }).click();
    const move = page.getByRole('dialog');
    await move.getByRole('button', { name: folderName }).click(); // browse into the destination
    await move.getByRole('button', { name: 'Move here' }).click();

    // Gone from the root, present inside the folder.
    await expect(fileCard()).toHaveCount(0);
    await folderCard().dblclick();
    await expect(fileCard()).toBeVisible();
  });

  await test.step('share it — a fresh anonymous visitor can view; after revoke the link is dead', async () => {
    await fileCard().click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Share…' }).click();
    const share = page.getByRole('dialog');

    const [res] = await Promise.all([
      page.waitForResponse(
        (r) => /\/api\/media\/.*\/share$/.test(r.url()) && r.request().method() === 'POST',
      ),
      share.getByRole('button', { name: 'Create link' }).click(),
    ]);
    expect(res.ok(), 'share link created').toBeTruthy();
    const shareUrl: string = (await res.json()).url;
    expect(shareUrl, 'response carries the public /s/<token> URL').toContain('/s/');
    await expect(share.getByText('Active', { exact: true })).toBeVisible();

    // A brand-new, unauthenticated context — the token in the URL is the whole authorization.
    const guestContext = await browser.newContext({ baseURL });
    const guest = await guestContext.newPage();
    await guest.goto(shareUrl);
    await expect(guest.getByRole('heading', { name: fileName })).toBeVisible();
    await expect(guest.getByRole('button', { name: 'Download' })).toBeVisible();

    // Owner stops sharing → every link is revoked.
    await share.getByRole('button', { name: 'Stop sharing' }).click();
    await expect(share.getByText("This file isn't shared yet.")).toBeVisible();
    await share.getByRole('button', { name: 'Done' }).click();

    // The same link is now dead.
    await guest.goto(shareUrl);
    await expect(guest.getByRole('heading', { name: 'Link unavailable' })).toBeVisible();
    await guestContext.close();
  });

  await test.step('delete to Trash, restore it, then delete it forever', async () => {
    // Delete from inside the folder.
    await fileCard().click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Delete' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Move to Trash' }).click();
    await expect(fileCard()).toHaveCount(0);

    // It shows up in the Trash; restore it.
    await page.goto('/trash');
    const trashRow = page.getByRole('listitem').filter({ hasText: fileName });
    await expect(trashRow).toBeVisible();
    await trashRow.getByRole('button', { name: 'Restore' }).click();
    await expect(page.getByText('Trash is empty.')).toBeVisible();

    // Restored back into its folder; delete it again and purge it permanently.
    await page.goto('/files');
    await folderCard().dblclick();
    await expect(fileCard()).toBeVisible();
    await fileCard().click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Delete' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Move to Trash' }).click();

    await page.goto('/trash');
    await page.getByRole('listitem').filter({ hasText: fileName }).getByRole('button', { name: 'Delete forever' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Delete forever' }).click();
    await expect(page.getByText('Trash is empty.')).toBeVisible();
  });
});
