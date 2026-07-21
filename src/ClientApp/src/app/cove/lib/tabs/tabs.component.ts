import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export interface TabItem { value: string; label: string; icon?: string; }

@Component({
  selector: 'cove-tabs',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div [ngStyle]="{ display: 'flex', gap: '4px', borderBottom: '1px solid var(--border-subtle)' }">
      <button *ngFor="let item of items" (click)="valueChange.emit(item.value)" [ngStyle]="tabStyle(item)">
        <cove-icon *ngIf="item.icon" [name]="item.icon" [size]="15"></cove-icon>
        {{ item.label }}
      </button>
    </div>`,
})
export class TabsComponent {
  @Input() items: TabItem[] = [];
  @Input() value = '';
  @Output() valueChange = new EventEmitter<string>();
  tabStyle(item: TabItem) {
    const on = this.value === item.value;
    return {
      display: 'inline-flex', alignItems: 'center', gap: '6px', padding: '10px 4px', marginRight: '20px',
      background: 'transparent', border: 'none', cursor: 'pointer', fontFamily: 'var(--font-body)',
      fontWeight: 600, fontSize: '14px', color: on ? 'var(--text-primary)' : 'var(--text-tertiary)',
      borderBottom: on ? '2px solid var(--accent)' : '2px solid transparent',
      transition: 'color var(--duration-fast)',
    };
  }
}
