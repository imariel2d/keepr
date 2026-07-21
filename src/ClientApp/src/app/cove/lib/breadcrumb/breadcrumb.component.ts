import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export interface BreadcrumbItem { id: string; label: string; }

@Component({
  selector: 'cove-breadcrumb',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div [ngStyle]="{ display: 'flex', alignItems: 'center', gap: '6px', fontFamily: 'var(--font-body)', fontSize: '14px' }">
      <ng-container *ngFor="let item of items; let i = index">
        <cove-icon *ngIf="i > 0" name="chevron-right" [size]="14" color="var(--text-tertiary)"></cove-icon>
        <span (click)="navigate.emit(item)" [ngStyle]="itemStyle(i)">{{ item.label }}</span>
      </ng-container>
    </div>`,
})
export class BreadcrumbComponent {
  @Input() items: BreadcrumbItem[] = [];
  @Output() navigate = new EventEmitter<BreadcrumbItem>();
  itemStyle(i: number) {
    const last = i === this.items.length - 1;
    return {
      cursor: last ? 'default' : 'pointer', fontWeight: last ? 700 : 500,
      color: last ? 'var(--text-primary)' : 'var(--text-secondary)',
    };
  }
}
