import {
  Component, Input, Output, EventEmitter, HostListener, ElementRef, OnChanges, SimpleChanges,
  ChangeDetectorRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export interface ContextMenuItem { label?: string; icon?: string; danger?: boolean; divider?: boolean; onSelect?: () => void; }

@Component({
  selector: 'cove-context-menu',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div *ngIf="open" role="menu" (keydown)="onKeydown($event)" [ngStyle]="menuStyle()">
      <ng-container *ngFor="let item of items; let i = index">
        <div *ngIf="item.divider" role="separator" [ngStyle]="{ height: '1px', background: 'var(--border-subtle)', margin: '6px 4px' }"></div>
        <button *ngIf="!item.divider" type="button" role="menuitem" tabindex="-1"
                (click)="select(item)" (mouseenter)="hover = i" (mouseleave)="hover = -1" [ngStyle]="rowStyle(item, i)">
          <cove-icon *ngIf="item.icon" [name]="item.icon" [size]="16" [color]="item.danger ? 'var(--danger)' : 'var(--text-secondary)'"></cove-icon>
          {{ item.label }}
        </button>
      </ng-container>
    </div>`,
})
export class ContextMenuComponent implements OnChanges {
  @Input() open = false;
  @Input() x = 0;
  @Input() y = 0;
  @Input() items: ContextMenuItem[] = [];
  @Output() closed = new EventEmitter<void>();
  hover = -1;

  /** Where focus was before the menu opened (the trigger), so we can restore it on close. */
  private returnFocus: HTMLElement | null = null;

  // The position actually rendered — starts at the requested (x, y) anchor, then corrected once
  // the menu has measured itself so it never spills off the bottom/right edge. See clampToViewport.
  protected resolvedX = 0;
  protected resolvedY = 0;

  constructor(private el: ElementRef<HTMLElement>, private cdr: ChangeDetectorRef) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['open']) return;
    if (this.open) {
      this.returnFocus = document.activeElement as HTMLElement | null;
      // First paint at the raw anchor; the microtask below measures and corrects before it's seen.
      this.resolvedX = this.x;
      this.resolvedY = this.y;
      // Once the menu has rendered: flip/clamp it into view, then focus the first item so keyboard
      // users land inside it.
      queueMicrotask(() => {
        this.clampToViewport();
        this.focusItem(0);
      });
    } else if (changes['open'].previousValue) {
      // Restore focus to the trigger. If a selected action opened a modal, that modal moves
      // focus into itself in a later microtask and wins — so this is safe either way.
      this.returnFocus?.focus?.();
      this.returnFocus = null;
    }
  }

  @HostListener('document:mousedown', ['$event'])
  onDocDown(e: MouseEvent) {
    if (this.open && !this.el.nativeElement.contains(e.target as Node | null)) this.closed.emit();
  }

  select(item: ContextMenuItem) { item.onSelect?.(); this.closed.emit(); }

  /** Roving focus + standard menu keys: arrows move, Home/End jump, Esc/Tab close. */
  onKeydown(event: KeyboardEvent): void {
    const items = this.itemEls();
    if (!items.length) return;
    const current = items.indexOf(document.activeElement as HTMLElement);
    switch (event.key) {
      case 'ArrowDown': event.preventDefault(); this.focusItem(current + 1); break;
      case 'ArrowUp': event.preventDefault(); this.focusItem(current - 1); break;
      case 'Home': event.preventDefault(); this.focusItem(0); break;
      case 'End': event.preventDefault(); this.focusItem(items.length - 1); break;
      // stopPropagation so these keys don't also reach a parent modal's document:keydown
      // listener — otherwise Escape/Tab in a menu nested in a modal would close the modal too.
      case 'Escape': event.preventDefault(); event.stopPropagation(); this.closed.emit(); break;
      case 'Tab': event.preventDefault(); event.stopPropagation(); this.closed.emit(); break; // a menu closes on Tab
      // Enter/Space activate the focused <button> natively → its (click) runs select().
    }
  }

  private itemEls(): HTMLElement[] {
    return Array.from(this.el.nativeElement.querySelectorAll<HTMLElement>('[role="menuitem"]'));
  }

  private focusItem(index: number): void {
    const items = this.itemEls();
    if (!items.length) return;
    const wrapped = (index + items.length) % items.length; // Down past the end wraps to the top
    items[wrapped].focus();
    this.hover = -1; // let the focus ring show the active row; drop any stale mouse highlight
  }

  /**
   * Keep the menu on-screen: if opening at the anchor would overflow the right/bottom edge, flip it
   * to open above / to the left of the anchor instead, then clamp so a menu taller or wider than the
   * viewport still starts at the safe margin. Runs after the menu has rendered, so its real measured
   * size is used.
   */
  private clampToViewport(): void {
    const menu = this.el.nativeElement.querySelector<HTMLElement>('[role="menu"]');
    if (!menu) return;
    const { width, height } = menu.getBoundingClientRect();
    const margin = 8;

    let x = this.x;
    let y = this.y;
    if (x + width > window.innerWidth - margin) x = this.x - width; // flip left of the anchor
    if (y + height > window.innerHeight - margin) y = this.y - height; // flip above the anchor

    this.resolvedX = Math.max(margin, Math.min(x, window.innerWidth - width - margin));
    this.resolvedY = Math.max(margin, Math.min(y, window.innerHeight - height - margin));
    this.cdr.detectChanges(); // re-render at the corrected position (safe: we're past this CD cycle)
  }

  menuStyle() {
    return {
      position: 'fixed', top: this.resolvedY + 'px', left: this.resolvedX + 'px', minWidth: '200px',
      background: 'var(--surface-overlay)', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-md)',
      border: '1px solid var(--border-subtle)', padding: '6px', zIndex: 1100, fontFamily: 'var(--font-body)',
    };
  }

  rowStyle(item: ContextMenuItem, i: number) {
    return {
      // Button reset — these are <button role="menuitem"> for keyboard support, so strip the
      // native chrome and fill the row like the old <div> did.
      width: '100%', border: 'none', textAlign: 'left', fontFamily: 'inherit', appearance: 'none',
      display: 'flex', alignItems: 'center', gap: '10px', padding: '9px 10px', borderRadius: 'var(--radius-sm)',
      cursor: 'pointer', fontSize: '14px', color: item.danger ? 'var(--danger)' : 'var(--text-primary)',
      background: this.hover === i ? 'var(--surface-sunken)' : 'transparent',
    };
  }
}
