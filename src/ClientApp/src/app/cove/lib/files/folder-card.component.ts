import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';
import { IconButtonComponent } from '../icon-button/icon-button.component';

@Component({
  selector: 'cove-folder-card',
  standalone: true,
  imports: [CommonModule, IconComponent, IconButtonComponent],
  template: `
    <div (mouseenter)="hover = true" (mouseleave)="hover = false" (dblclick)="openItem.emit()"
         (contextmenu)="$event.preventDefault(); menu.emit($event)" [ngStyle]="cardStyle()">
      <cove-icon name="folder" [size]="28" color="var(--teal-500)"></cove-icon>
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
  /** Highlighted as the active drop destination. */
  @Input() selected = false;
  @Output() openItem = new EventEmitter<void>();
  @Output() menu = new EventEmitter<MouseEvent>();
  hover = false;
  cardStyle() {
    return {
      display: 'flex', alignItems: 'center', gap: '12px', padding: '14px 16px', borderRadius: 'var(--radius-lg)',
      background: this.selected ? 'var(--accent-subtle)' : (this.hover ? 'var(--surface-card-hover)' : 'var(--surface-card)'),
      border: '1px solid ' + (this.selected ? 'var(--accent)' : 'var(--border-subtle)'),
      cursor: 'pointer', fontFamily: 'var(--font-body)', minWidth: '220px',
      boxShadow: this.selected || this.hover ? 'var(--shadow-sm)' : 'none',
      transition: 'all var(--duration-fast) var(--ease-standard)',
    };
  }
}
