import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconButtonComponent } from '../icon-button/icon-button.component';

@Component({
  selector: 'cove-modal',
  standalone: true,
  imports: [CommonModule, IconButtonComponent],
  template: `
    <div *ngIf="open" (click)="close.emit()"
         [ngStyle]="{ position: 'fixed', inset: 0, background: 'var(--surface-scrim)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000 }">
      <div (click)="$event.stopPropagation()" [ngStyle]="panelStyle()">
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
  panelStyle() {
    return {
      width: this.width + 'px', maxWidth: '90vw', maxHeight: '85vh', background: 'var(--surface-overlay)',
      borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-lg)', display: 'flex', flexDirection: 'column',
      fontFamily: 'var(--font-body)', overflow: 'hidden',
    };
  }
}
