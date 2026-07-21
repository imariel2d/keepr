import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'cove-checkbox',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <label [ngStyle]="{ display: 'inline-flex', alignItems: 'center', gap: '8px', cursor: 'pointer' }">
      <span (click)="checkedChange.emit(!checked)" [ngStyle]="boxStyle()">
        <cove-icon *ngIf="checked" name="check" [size]="12" color="var(--text-on-brand)"></cove-icon>
      </span>
      <span *ngIf="label" [ngStyle]="{ fontFamily: 'var(--font-body)', fontSize: '14px', color: 'var(--text-primary)' }">{{ label }}</span>
    </label>`,
})
export class CheckboxComponent {
  @Input() checked = false;
  @Input() label?: string;
  @Output() checkedChange = new EventEmitter<boolean>();
  boxStyle() {
    return {
      width: '18px', height: '18px', borderRadius: '5px', display: 'inline-flex',
      alignItems: 'center', justifyContent: 'center',
      border: this.checked ? 'none' : '1.5px solid var(--border-strong)',
      background: this.checked ? 'var(--accent)' : 'var(--surface-card)',
      transition: 'background var(--duration-fast)',
    };
  }
}
