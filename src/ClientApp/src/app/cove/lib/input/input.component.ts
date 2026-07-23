import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

type Size = 'sm' | 'md' | 'lg';

@Component({
  selector: 'cove-input',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div [ngStyle]="wrapStyle()">
      <cove-icon *ngIf="icon" [name]="icon" [size]="16" color="var(--text-tertiary)"></cove-icon>
      <input [placeholder]="placeholder" [value]="value" [type]="type"
             [attr.autocomplete]="autocomplete" [attr.name]="name" [attr.required]="required || null"
             (input)="valueChange.emit(input.value)" #input
             (focus)="focus = true" (blur)="focus = false" [ngStyle]="inputStyle" />
      <ng-content select="[trailing]"></ng-content>
    </div>`,
})
export class InputComponent {
  @Input() icon?: string;
  @Input() size: Size = 'md';
  @Input() type = 'text';
  @Input() placeholder = '';
  @Input() value = '';
  @Input() autocomplete?: string;
  @Input() name?: string;
  @Input() required = false;
  @Output() valueChange = new EventEmitter<string>();
  focus = false;
  private heights: Record<Size, string> = { sm: '32px', md: '40px', lg: '48px' };
  // boxShadow:none overrides the global `*:focus-visible` ring. The wrapper already shows focus
  // (accent border + shadow), so without this the inner input draws a second ring inside it.
  inputStyle = { flex: 1, border: 'none', outline: 'none', boxShadow: 'none', background: 'transparent', fontFamily: 'var(--font-body)', fontSize: '14px', color: 'var(--text-primary)' };
  wrapStyle() {
    return {
      display: 'flex', alignItems: 'center', gap: '8px', height: this.heights[this.size],
      padding: '0 14px', borderRadius: 'var(--radius-md)', background: 'var(--surface-card)',
      border: '1px solid ' + (this.focus ? 'var(--focus-ring)' : 'var(--border-default)'),
      boxShadow: this.focus ? 'var(--shadow-focus)' : 'none',
      transition: 'border-color var(--duration-fast), box-shadow var(--duration-fast)',
    };
  }
}
