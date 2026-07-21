import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';
import { IconButtonComponent } from '../icon-button/icon-button.component';
import { CheckboxComponent } from '../checkbox/checkbox.component';
import { AvatarComponent } from '../avatar/avatar.component';
import { FileType, TYPE_META } from './file-type-meta';

@Component({
  selector: 'cove-file-card',
  standalone: true,
  imports: [CommonModule, IconComponent, IconButtonComponent, CheckboxComponent, AvatarComponent],
  template: `
    <div (mouseenter)="hover = true" (mouseleave)="hover = false" (dblclick)="openItem.emit()" [ngStyle]="cardStyle()">
      <cove-checkbox *ngIf="hover || selected" [checked]="selected"
        [ngStyle]="{ position: 'absolute', top: '10px', left: '10px', zIndex: 2 }"></cove-checkbox>
      <cove-icon-button *ngIf="hover || selected" icon="more-vertical" label="More actions"
        (click)="$event.stopPropagation(); menu.emit($event)"
        [ngStyle]="{ position: 'absolute', top: '6px', right: '6px', zIndex: 2, background: 'var(--surface-card)' }"></cove-icon-button>
      <div [ngStyle]="{ height: '120px', background: 'var(--surface-sunken)', display: 'flex', alignItems: 'center', justifyContent: 'center', overflow: 'hidden' }">
        <img *ngIf="thumbnail" [src]="thumbnail" [alt]="name" [ngStyle]="{ width: '100%', height: '100%', objectFit: 'cover' }" />
        <cove-icon *ngIf="!thumbnail" [name]="meta.icon" [size]="36" [color]="meta.color"></cove-icon>
      </div>
      <div [ngStyle]="{ padding: '10px 12px 12px' }">
        <div [ngStyle]="{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }">{{ name }}</div>
        <div [ngStyle]="{ display: 'flex', alignItems: 'center', gap: '6px', marginTop: '6px' }">
          <cove-avatar *ngIf="owner" [name]="owner" [size]="18"></cove-avatar>
          <span [ngStyle]="{ fontSize: '12px', color: 'var(--text-tertiary)' }">{{ modified }}</span>
        </div>
      </div>
    </div>`,
})
export class FileCardComponent {
  @Input() name!: string;
  @Input() type: FileType = 'default';
  @Input() thumbnail?: string;
  @Input() owner?: string;
  @Input() modified?: string;
  @Input() selected = false;
  @Output() openItem = new EventEmitter<void>();
  @Output() menu = new EventEmitter<MouseEvent>();
  hover = false;
  get meta() { return TYPE_META[this.type] || TYPE_META.default; }
  cardStyle() {
    return {
      width: '190px', borderRadius: 'var(--radius-lg)',
      background: this.selected ? 'var(--accent-subtle)' : (this.hover ? 'var(--surface-card-hover)' : 'var(--surface-card)'),
      border: '1px solid ' + (this.selected ? 'var(--accent)' : 'var(--border-subtle)'), cursor: 'pointer',
      boxShadow: this.hover ? 'var(--shadow-sm)' : 'none', transition: 'all var(--duration-fast) var(--ease-standard)',
      fontFamily: 'var(--font-body)', overflow: 'hidden', position: 'relative',
    };
  }
}
