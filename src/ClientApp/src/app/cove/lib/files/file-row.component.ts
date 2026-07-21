import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';
import { IconButtonComponent } from '../icon-button/icon-button.component';
import { CheckboxComponent } from '../checkbox/checkbox.component';
import { AvatarComponent } from '../avatar/avatar.component';
import { FileType, TYPE_META } from './file-type-meta';

@Component({
  selector: 'cove-file-row',
  standalone: true,
  imports: [CommonModule, IconComponent, IconButtonComponent, CheckboxComponent, AvatarComponent],
  template: `
    <div (mouseenter)="hover = true" (mouseleave)="hover = false" (dblclick)="openItem.emit()" [ngStyle]="rowStyle()">
      <cove-checkbox *ngIf="hover || selected" [checked]="selected"></cove-checkbox>
      <cove-icon *ngIf="!(hover || selected)" [name]="meta.icon" [size]="18" [color]="meta.color"></cove-icon>
      <div [ngStyle]="{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }">
        <cove-icon *ngIf="hover || selected" [name]="meta.icon" [size]="16" [color]="meta.color"></cove-icon>
        <span [ngStyle]="{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', fontWeight: 500 }">{{ name }}</span>
      </div>
      <div [ngStyle]="{ display: 'flex', alignItems: 'center', gap: '6px', color: 'var(--text-secondary)', fontSize: '13px' }">
        <cove-avatar *ngIf="owner" [name]="owner" [size]="18"></cove-avatar>{{ owner }}
      </div>
      <div [ngStyle]="{ color: 'var(--text-tertiary)', fontSize: '13px' }">{{ modified }}</div>
      <div [ngStyle]="{ color: 'var(--text-tertiary)', fontSize: '13px' }">{{ size }}</div>
      <cove-icon-button icon="star" [label]="starred ? 'Unstar' : 'Star'" (click)="$event.stopPropagation(); toggleStar.emit()"
        [ngStyle]="{ color: starred ? 'var(--amber-500)' : 'var(--text-tertiary)', opacity: (hover || starred) ? 1 : 0 }"></cove-icon-button>
      <cove-icon-button icon="more-vertical" label="More actions" (click)="$event.stopPropagation(); menu.emit($event)"
        [ngStyle]="{ opacity: hover ? 1 : 0 }"></cove-icon-button>
    </div>`,
})
export class FileRowComponent {
  @Input() name!: string;
  @Input() type: FileType = 'default';
  @Input() owner?: string;
  @Input() modified?: string;
  @Input() size?: string;
  @Input() starred = false;
  @Input() selected = false;
  @Output() openItem = new EventEmitter<void>();
  @Output() menu = new EventEmitter<MouseEvent>();
  @Output() toggleStar = new EventEmitter<void>();
  hover = false;
  get meta() { return TYPE_META[this.type] || TYPE_META.default; }
  rowStyle() {
    return {
      display: 'grid', gridTemplateColumns: '28px 1fr 140px 140px 90px 36px 36px', alignItems: 'center', gap: '12px',
      padding: '10px 12px', borderRadius: 'var(--radius-md)', cursor: 'pointer',
      background: this.selected ? 'var(--accent-subtle)' : this.hover ? 'var(--surface-card-hover)' : 'transparent',
      fontFamily: 'var(--font-body)', fontSize: '14px', color: 'var(--text-primary)', transition: 'background var(--duration-fast)',
    };
  }
}
