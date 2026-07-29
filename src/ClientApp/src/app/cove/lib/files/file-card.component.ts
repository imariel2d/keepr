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
    <div tabindex="0" role="button" [attr.aria-label]="ariaLabel"
         (mouseenter)="hover = true" (mouseleave)="hover = false"
         (focusin)="focused = true" (focusout)="onFocusOut($event)"
         (keydown)="onKeydown($event)"
         (click)="cardClick.emit($event)" (dblclick)="openItem.emit()"
         (contextmenu)="$event.preventDefault(); menu.emit($event)" [ngStyle]="cardStyle()">
      <span *ngIf="revealed || selected" draggable="false"
        [ngStyle]="{ position: 'absolute', top: '10px', left: '10px', zIndex: 2, padding: '4px', margin: '-4px', cursor: 'pointer' }"
        (click)="$event.stopPropagation(); toggleSelect.emit()"
        (dblclick)="$event.stopPropagation()"
        (mousedown)="$event.stopPropagation()">
        <cove-checkbox [checked]="selected"></cove-checkbox>
      </span>
      <cove-icon-button *ngIf="revealed || selected" icon="more-vertical" label="More actions"
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
  @Output() toggleSelect = new EventEmitter<void>();
  /** Raw click on the card body; the host decides whether it selects, extends, or does nothing. */
  @Output() cardClick = new EventEmitter<MouseEvent>();
  hover = false;
  focused = false;
  get meta() { return TYPE_META[this.type] || TYPE_META.default; }

  /** Controls (checkbox, more-actions) show on hover OR keyboard focus, so they are reachable
      without a mouse. */
  get revealed() { return this.hover || this.focused; }

  /** e.g. "budget.xlsx, file, modified 2 days ago" — read out when the card gets focus. */
  get ariaLabel() {
    const when = this.modified ? `, modified ${this.modified}` : '';
    return `${this.name}, file${when}`;
  }

  /** Enter opens the file; Space toggles its selection — matching file-manager conventions.
      Ignored when a nested control (the more-actions button) is focused, so it keeps its own keys. */
  onKeydown(event: KeyboardEvent): void {
    if (event.target !== event.currentTarget) return;
    if (event.key === 'Enter') {
      event.preventDefault();
      this.openItem.emit();
    } else if (event.key === ' ' || event.key === 'Spacebar') {
      event.preventDefault();
      this.toggleSelect.emit();
    }
  }

  /** Keep controls revealed while focus is anywhere inside the card; hide once it leaves. */
  onFocusOut(event: FocusEvent): void {
    const card = event.currentTarget as HTMLElement;
    if (!card.contains(event.relatedTarget as Node)) this.focused = false;
  }
  cardStyle() {
    return {
      width: '190px', borderRadius: 'var(--radius-lg)',
      background: this.selected ? 'var(--accent-subtle)' : (this.hover ? 'var(--surface-card-hover)' : 'var(--surface-card)'),
      border: '1px solid ' + (this.selected ? 'var(--accent)' : 'var(--border-subtle)'), cursor: 'pointer',
      boxShadow: this.hover ? 'var(--shadow-sm)' : 'none', transition: 'all var(--duration-fast) var(--ease-standard)',
      fontFamily: 'var(--font-body)', overflow: 'hidden', position: 'relative',
      // Shift-clicking across cards would otherwise paint a text selection over the grid.
      userSelect: 'none',
    };
  }
}
