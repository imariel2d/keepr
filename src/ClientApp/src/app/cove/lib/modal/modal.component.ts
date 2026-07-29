import {
  Component, Input, Output, EventEmitter, ViewChild, ElementRef, OnChanges, SimpleChanges,
  HostListener,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconButtonComponent } from '../icon-button/icon-button.component';

let modalSeq = 0;

@Component({
  selector: 'cove-modal',
  standalone: true,
  imports: [CommonModule, IconButtonComponent],
  template: `
    <div *ngIf="open" #scrim
         (mousedown)="onScrimDown($event, scrim)"
         (mouseup)="onScrimUp($event, scrim)"
         (click)="onScrimClick()"
         [ngStyle]="{ position: 'fixed', inset: 0, background: 'var(--surface-scrim)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000 }">
      <div #panel role="dialog" aria-modal="true" tabindex="-1" [attr.aria-labelledby]="titleId" [ngStyle]="panelStyle()">
        <div [ngStyle]="{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '18px 20px', borderBottom: '1px solid var(--border-subtle)' }">
          <div [id]="titleId" [ngStyle]="{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '18px', color: 'var(--text-primary)' }">{{ title }}</div>
          <cove-icon-button icon="x" label="Close" (click)="close.emit()"></cove-icon-button>
        </div>
        <!-- Body is a flex column with a gap so the trailing action row (the .foot div) is
             always separated from the content above it. Every modal in the app follows the
             [content block, .foot] shape, so one gap lands exactly between them. -->
        <div [ngStyle]="{ display: 'flex', flexDirection: 'column', gap: '20px', padding: '20px', overflowY: 'auto' }"><ng-content></ng-content></div>
      </div>
    </div>`,
})
export class ModalComponent implements OnChanges {
  @Input() open = false;
  @Input() title = '';
  @Input() width = 480;
  @Output() close = new EventEmitter<void>();

  @ViewChild('panel') private panelRef?: ElementRef<HTMLElement>;

  /** Unique id so aria-labelledby on the dialog can point at this instance's title. */
  protected readonly titleId = `cove-modal-title-${modalSeq++}`;

  /** The element focus should return to when the dialog closes (WCAG 2.4.3). */
  private previouslyFocused: HTMLElement | null = null;

  private static readonly focusableSelector = [
    'a[href]', 'button:not([disabled])', 'textarea:not([disabled])',
    'input:not([disabled])', 'select:not([disabled])', '[tabindex]:not([tabindex="-1"])',
  ].join(',');

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['open']) return;
    if (this.open) {
      // Remember where focus was so we can restore it on close, then move focus into
      // the dialog once it has rendered.
      this.previouslyFocused = document.activeElement as HTMLElement | null;
      queueMicrotask(() => this.focusFirst());
    } else if (changes['open'].previousValue) {
      this.previouslyFocused?.focus?.();
      this.previouslyFocused = null;
    }
  }

  /** ESC closes; Tab is kept inside the dialog (a modal must trap focus). */
  @HostListener('document:keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (!this.open) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close.emit();
      return;
    }
    if (event.key === 'Tab') this.trapTab(event);
  }

  private focusable(): HTMLElement[] {
    const panel = this.panelRef?.nativeElement;
    if (!panel) return [];
    return Array.from(panel.querySelectorAll<HTMLElement>(ModalComponent.focusableSelector))
      .filter((el) => el.offsetParent !== null || el === document.activeElement);
  }

  private focusFirst(): void {
    const items = this.focusable();
    (items[0] ?? this.panelRef?.nativeElement)?.focus();
  }

  private trapTab(event: KeyboardEvent): void {
    const items = this.focusable();
    if (items.length === 0) {
      event.preventDefault();
      this.panelRef?.nativeElement?.focus();
      return;
    }
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement as HTMLElement | null;
    const panel = this.panelRef?.nativeElement;

    // Wrap at the ends, and pull focus back in if it has escaped the dialog entirely.
    if (event.shiftKey && (active === first || !panel?.contains(active))) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  /**
   * A backdrop click closes the modal, but only when the whole gesture happened on the backdrop.
   *
   * Selecting text in a field and releasing outside the panel makes the browser dispatch `click`
   * on the common ancestor of press and release — the backdrop itself — so checking the click
   * target alone would still close the modal and throw away what the user typed. Both ends of the
   * gesture are tracked instead, which also stops a drag that starts on the backdrop and ends
   * inside the panel from closing it.
   */
  private pressedOnScrim = false;
  private releasedOnScrim = false;

  /**
   * These must be methods returning void, not inline assignments. Angular calls
   * preventDefault() whenever an event-binding expression evaluates to false, and
   * `pressed = $event.target === scrim` evaluates to false for every click inside the panel —
   * which suppressed the mousedown default and left the fields unfocusable.
   */
  protected onScrimDown(event: MouseEvent, scrim: HTMLElement): void {
    this.pressedOnScrim = event.target === scrim;
  }

  protected onScrimUp(event: MouseEvent, scrim: HTMLElement): void {
    this.releasedOnScrim = event.target === scrim;
  }

  protected onScrimClick(): void {
    const shouldClose = this.pressedOnScrim && this.releasedOnScrim;
    this.pressedOnScrim = false;
    this.releasedOnScrim = false;
    if (shouldClose) this.close.emit();
  }
  panelStyle() {
    return {
      width: this.width + 'px', maxWidth: '90vw', maxHeight: '85vh', background: 'var(--surface-overlay)',
      borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-lg)', display: 'flex', flexDirection: 'column',
      fontFamily: 'var(--font-body)', overflow: 'hidden', outline: 'none',
    };
  }
}
