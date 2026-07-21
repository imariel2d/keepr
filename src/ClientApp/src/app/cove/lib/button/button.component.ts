import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
type Size = 'sm' | 'md' | 'lg';

@Component({
  selector: 'cove-button',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <button [disabled]="disabled" [ngStyle]="style()"
            (mouseenter)="hover = true" (mouseleave)="hover = false">
      <cove-icon *ngIf="icon && iconPosition === 'left'" [name]="icon" [size]="16"></cove-icon>
      <ng-content></ng-content>
      <cove-icon *ngIf="icon && iconPosition === 'right'" [name]="icon" [size]="16"></cove-icon>
    </button>`,
})
export class ButtonComponent {
  @Input() variant: Variant = 'primary';
  @Input() size: Size = 'md';
  @Input() icon?: string;
  @Input() iconPosition: 'left' | 'right' = 'left';
  @Input() disabled = false;
  hover = false;

  private sizes: Record<Size, any> = {
    sm: { height: '32px', padding: '0 12px', fontSize: '13px', gap: '6px' },
    md: { height: '40px', padding: '0 18px', fontSize: '14px', gap: '8px' },
    lg: { height: '48px', padding: '0 22px', fontSize: '16px', gap: '8px' },
  };
  private variants: Record<Variant, any> = {
    primary: { base: 'var(--accent)', hover: 'var(--accent-hover)', color: 'var(--text-on-brand)', border: '1px solid transparent' },
    secondary: { base: 'var(--surface-card)', hover: 'var(--surface-card-hover)', color: 'var(--text-primary)', border: '1px solid var(--border-default)' },
    ghost: { base: 'transparent', hover: 'var(--surface-sunken)', color: 'var(--text-primary)', border: '1px solid transparent' },
    danger: { base: 'var(--danger)', hover: 'var(--danger-hover)', color: '#fff', border: '1px solid transparent' },
  };

  style() {
    const s = this.sizes[this.size], v = this.variants[this.variant];
    return {
      display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: s.gap,
      height: s.height, padding: s.padding, fontSize: s.fontSize, fontFamily: 'var(--font-body)',
      fontWeight: 600, borderRadius: 'var(--radius-md)', border: v.border,
      background: this.hover && !this.disabled ? v.hover : v.base, color: v.color,
      cursor: this.disabled ? 'not-allowed' : 'pointer', opacity: this.disabled ? 0.5 : 1,
      transition: 'background var(--duration-fast) var(--ease-standard)',
    };
  }
}
