import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'cove-icon-button',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <button type="button" [attr.aria-label]="label" [title]="label" [ngStyle]="style()"
            [attr.aria-expanded]="ariaExpanded" [attr.aria-controls]="ariaControls"
            (mouseenter)="hover = true" (mouseleave)="hover = false">
      <cove-icon [name]="icon" [size]="round(size * 0.5)"></cove-icon>
    </button>`,
})
export class IconButtonComponent {
  @Input() icon!: string;
  @Input() label!: string;
  @Input() size = 36;
  @Input() active = false;
  /** For buttons that toggle a disclosure (e.g. a drawer); forwarded to the inner button. */
  @Input() ariaExpanded?: boolean;
  @Input() ariaControls?: string;
  hover = false;
  round(n: number) { return Math.round(n); }
  style() {
    return {
      width: this.size + 'px', height: this.size + 'px', display: 'inline-flex',
      alignItems: 'center', justifyContent: 'center', borderRadius: 'var(--radius-md)',
      border: 'none', cursor: 'pointer',
      background: this.active ? 'var(--accent-subtle)' : this.hover ? 'var(--surface-sunken)' : 'transparent',
      color: this.active ? 'var(--accent-subtle-text)' : 'var(--text-secondary)',
      transition: 'background var(--duration-fast) var(--ease-standard)',
    };
  }
}
