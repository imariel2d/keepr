import { Component, Input, Output, EventEmitter, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';

export interface ContextMenuItem { label?: string; icon?: string; danger?: boolean; divider?: boolean; onSelect?: () => void; }

@Component({
  selector: 'cove-context-menu',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <div *ngIf="open" [ngStyle]="menuStyle()">
      <ng-container *ngFor="let item of items; let i = index">
        <div *ngIf="item.divider" [ngStyle]="{ height: '1px', background: 'var(--border-subtle)', margin: '6px 4px' }"></div>
        <div *ngIf="!item.divider" (click)="select(item)" (mouseenter)="hover = i" (mouseleave)="hover = -1" [ngStyle]="rowStyle(item, i)">
          <cove-icon *ngIf="item.icon" [name]="item.icon" [size]="16" [color]="item.danger ? 'var(--danger)' : 'var(--text-secondary)'"></cove-icon>
          {{ item.label }}
        </div>
      </ng-container>
    </div>`,
})
export class ContextMenuComponent {
  @Input() open = false;
  @Input() x = 0;
  @Input() y = 0;
  @Input() items: ContextMenuItem[] = [];
  @Output() closed = new EventEmitter<void>();
  hover = -1;
  constructor(private el: ElementRef) {}
  @HostListener('document:mousedown', ['$event'])
  onDocDown(e: MouseEvent) {
    if (this.open && !this.el.nativeElement.contains(e.target)) this.closed.emit();
  }
  select(item: ContextMenuItem) { item.onSelect?.(); this.closed.emit(); }
  menuStyle() {
    return {
      position: 'fixed', top: this.y + 'px', left: this.x + 'px', minWidth: '200px',
      background: 'var(--surface-overlay)', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-md)',
      border: '1px solid var(--border-subtle)', padding: '6px', zIndex: 1100, fontFamily: 'var(--font-body)',
    };
  }
  rowStyle(item: ContextMenuItem, i: number) {
    return {
      display: 'flex', alignItems: 'center', gap: '10px', padding: '9px 10px', borderRadius: 'var(--radius-sm)',
      cursor: 'pointer', fontSize: '14px', color: item.danger ? 'var(--danger)' : 'var(--text-primary)',
      background: this.hover === i ? 'var(--surface-sunken)' : 'transparent',
    };
  }
}
