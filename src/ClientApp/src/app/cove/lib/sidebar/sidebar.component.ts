import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';
import { ButtonComponent } from '../button/button.component';
import { ProgressBarComponent } from '../progress-bar/progress-bar.component';

interface NavItem { key: string; label: string; icon: string; }

@Component({
  selector: 'cove-sidebar',
  standalone: true,
  imports: [CommonModule, IconComponent, ButtonComponent, ProgressBarComponent],
  template: `
    <div [ngStyle]="{ width: '240px', display: 'flex', flexDirection: 'column', gap: '20px', padding: '20px 12px', fontFamily: 'var(--font-body)', height: '100%' }">
      <div [ngStyle]="{ display: 'flex', alignItems: 'center', gap: '8px', padding: '0 8px' }">
        <cove-icon name="cloud" [size]="22" color="var(--accent)"></cove-icon>
        <span [ngStyle]="{ fontFamily: 'var(--font-display)', fontWeight: 800, fontSize: '20px', color: 'var(--text-primary)' }">Cove</span>
      </div>
      <cove-button icon="upload-cloud" [ngStyle]="{ margin: '0 8px', display: 'block' }">Upload</cove-button>
      <div [ngStyle]="{ display: 'flex', flexDirection: 'column', gap: '2px' }">
        <div *ngFor="let item of items" (click)="navigate.emit(item.key)" [ngStyle]="navStyle(item)">
          <cove-icon [name]="item.icon" [size]="18"></cove-icon>{{ item.label }}
        </div>
      </div>
      <div [ngStyle]="{ marginTop: 'auto', padding: '0 8px', display: 'flex', flexDirection: 'column', gap: '8px' }">
        <cove-progress-bar [value]="pct" [tone]="pct > 85 ? 'warning' : 'accent'"></cove-progress-bar>
        <div [ngStyle]="{ fontSize: '12px', color: 'var(--text-tertiary)' }">{{ quotaUsed }} GB of {{ quotaTotal }} GB used</div>
      </div>
    </div>`,
})
export class SidebarComponent {
  @Input() active = 'mine';
  @Input() quotaUsed = 0;
  @Input() quotaTotal = 100;
  @Output() navigate = new EventEmitter<string>();
  items: NavItem[] = [
    { key: 'mine', label: 'My Drive', icon: 'folder' },
    { key: 'shared', label: 'Shared with me', icon: 'users' },
    { key: 'recent', label: 'Recent', icon: 'clock' },
    { key: 'starred', label: 'Starred', icon: 'star' },
    { key: 'trash', label: 'Trash', icon: 'trash-2' },
  ];
  get pct() { return Math.round((this.quotaUsed / this.quotaTotal) * 100); }
  navStyle(item: NavItem) {
    const on = this.active === item.key;
    return {
      display: 'flex', alignItems: 'center', gap: '12px', padding: '10px 12px', borderRadius: 'var(--radius-md)',
      cursor: 'pointer', fontSize: '14px', fontWeight: 600,
      background: on ? 'var(--accent-subtle)' : 'transparent',
      color: on ? 'var(--accent-subtle-text)' : 'var(--text-secondary)',
    };
  }
}
