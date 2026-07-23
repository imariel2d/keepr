import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconButtonComponent } from '../icon-button/icon-button.component';

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
      <div [ngStyle]="panelStyle()">
        <div [ngStyle]="{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '18px 20px', borderBottom: '1px solid var(--border-subtle)' }">
          <div [ngStyle]="{ fontFamily: 'var(--font-display)', fontWeight: 700, fontSize: '18px', color: 'var(--text-primary)' }">{{ title }}</div>
          <cove-icon-button icon="x" label="Close" (click)="close.emit()"></cove-icon-button>
        </div>
        <div [ngStyle]="{ padding: '20px', overflowY: 'auto' }"><ng-content></ng-content></div>
        <div [ngStyle]="{ display: 'flex', justifyContent: 'flex-end', gap: '10px', padding: '14px 20px', borderTop: '1px solid var(--border-subtle)' }">
          <ng-content select="[footer]"></ng-content>
        </div>
      </div>
    </div>`,
})
export class ModalComponent {
  @Input() open = false;
  @Input() title = '';
  @Input() width = 480;
  @Output() close = new EventEmitter<void>();

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
      fontFamily: 'var(--font-body)', overflow: 'hidden',
    };
  }
}
