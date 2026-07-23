import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';
import { IconButtonComponent } from '../icon-button/icon-button.component';
import { CheckboxComponent } from '../checkbox/checkbox.component';

@Component({
  selector: 'cove-folder-card',
  standalone: true,
  imports: [CommonModule, IconComponent, IconButtonComponent, CheckboxComponent],
  template: `
    <div (mouseenter)="hover = true" (mouseleave)="hover = false"
         (click)="cardClick.emit($event)" (dblclick)="openItem.emit()"
         (contextmenu)="$event.preventDefault(); menu.emit($event)" [ngStyle]="cardStyle()">
      <!-- Fixed-width slot so swapping the icon for the checkbox never resizes the row. -->
      <span draggable="false"
        [ngStyle]="{ width: '28px', height: '28px', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, cursor: 'pointer' }"
        (click)="$event.stopPropagation(); toggleSelect.emit()"
        (dblclick)="$event.stopPropagation()"
        (mousedown)="$event.stopPropagation()">
        <cove-checkbox *ngIf="hover || selected" [checked]="selected"></cove-checkbox>
        <cove-icon *ngIf="!(hover || selected)" name="folder" [size]="28" color="var(--teal-500)"></cove-icon>
      </span>
      <div [ngStyle]="{ flex: 1, minWidth: 0 }">
        <div [ngStyle]="{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }">{{ name }}</div>
        <div *ngIf="itemCount != null" [ngStyle]="{ fontSize: '12px', color: 'var(--text-tertiary)' }">{{ itemCount }} items</div>
      </div>
      <cove-icon *ngIf="shared" name="users" [size]="15" color="var(--text-tertiary)"></cove-icon>
      <!-- Always rendered so it reserves its 36px: mounting it on hover grew the row and
           nudged the whole grid. Visibility is toggled instead of the element. -->
      <cove-icon-button icon="more-vertical" label="More actions"
        (click)="$event.stopPropagation(); menu.emit($event)"
        [ngStyle]="{ opacity: hover ? 1 : 0, pointerEvents: hover ? 'auto' : 'none',
                     transition: 'opacity var(--duration-fast) var(--ease-standard)' }"></cove-icon-button>
    </div>`,
})
export class FolderCardComponent {
  @Input() name!: string;
  @Input() itemCount?: number;
  @Input() shared = false;
  /** Part of the current selection: ticks the checkbox and tints the card. */
  @Input() selected = false;
  /** Highlighted as the active drop destination. Same tint, but not a selection. */
  @Input() active = false;
  @Output() openItem = new EventEmitter<void>();
  @Output() menu = new EventEmitter<MouseEvent>();
  @Output() toggleSelect = new EventEmitter<void>();
  /** Raw click on the card body; the host decides whether it selects or does nothing. */
  @Output() cardClick = new EventEmitter<MouseEvent>();
  hover = false;
  cardStyle() {
    const lit = this.selected || this.active;
    return {
      display: 'flex', alignItems: 'center', gap: '12px', padding: '14px 16px', borderRadius: 'var(--radius-lg)',
      background: lit ? 'var(--accent-subtle)' : (this.hover ? 'var(--surface-card-hover)' : 'var(--surface-card)'),
      border: '1px solid ' + (lit ? 'var(--accent)' : 'var(--border-subtle)'),
      cursor: 'pointer', fontFamily: 'var(--font-body)', minWidth: '220px',
      boxShadow: lit || this.hover ? 'var(--shadow-sm)' : 'none',
      transition: 'all var(--duration-fast) var(--ease-standard)',
      // Marquee-dragging across cards would otherwise paint a text selection.
      userSelect: 'none',
    };
  }
}
