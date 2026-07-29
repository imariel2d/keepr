/**
 * Where a context menu should open for a given triggering event.
 *
 * A mouse click or right-click carries real pointer coordinates, so the menu opens there.
 * A *keyboard*-activated click (Enter/Space on a button) is synthesised by the browser with
 * `clientX/clientY = 0`, which would drop the menu in the top-left corner — so we detect that
 * case (`type === 'click'` with `detail === 0`, whereas a genuine right-click is a
 * `contextmenu` event) and anchor the menu just under the control that was activated instead.
 */
export function menuAnchor(event: MouseEvent): { x: number; y: number } {
  const keyboardClick = event.type === 'click' && event.detail === 0;
  if (keyboardClick) {
    const target = event.target as HTMLElement | null;
    const anchor = target?.closest('button') ?? target;
    if (anchor) {
      const rect = anchor.getBoundingClientRect();
      return { x: rect.left, y: rect.bottom + 4 };
    }
  }
  return { x: event.clientX, y: event.clientY };
}
