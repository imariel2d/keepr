/**
 * Trigger a browser download for a URL that already carries
 * `Content-Disposition: attachment` (which the API sets on download presigns).
 *
 * A plain `window.open` would open a tab instead — for an image or PDF the browser renders it
 * rather than saving, which is the bug this replaces. Clicking a synthetic anchor navigates,
 * the attachment disposition turns that navigation into a save, and the page stays put.
 *
 * The `download` attribute is deliberately not set: it is ignored cross-origin (storage is a
 * different origin), and the server's filename is the authoritative one anyway.
 */
export function saveFile(url: string): void {
  const a = document.createElement('a');
  a.href = url;
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  a.remove();
}
