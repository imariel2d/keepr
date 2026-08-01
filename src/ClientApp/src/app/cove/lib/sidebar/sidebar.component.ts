import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IconComponent } from '../icon/icon.component';
import { ButtonComponent } from '../button/button.component';
import { ProgressBarComponent } from '../progress-bar/progress-bar.component';

/** A nav entry. With `children`, it renders as an expandable group whose parent toggles rather than
 *  navigates; the children navigate. */
export interface NavItem { key: string; label: string; icon: string; children?: NavItem[]; }

@Component({
  selector: 'cove-sidebar',
  standalone: true,
  imports: [CommonModule, IconComponent, ButtonComponent, ProgressBarComponent],
  template: `
    <div [ngStyle]="{ width: '100%', display: 'flex', flexDirection: 'column', gap: '20px', padding: '20px 12px', fontFamily: 'var(--font-body)', height: '100%' }">
      <div [ngStyle]="{ display: 'flex', alignItems: 'center', gap: '8px', padding: '0 8px' }">
        <img *ngIf="brandMark; else glyph" [src]="brandMark" alt="" width="24" height="24" [ngStyle]="{ display: 'block' }" />
        <ng-template #glyph><cove-icon name="cloud" [size]="22" color="var(--accent)"></cove-icon></ng-template>
        <span [ngStyle]="{ fontFamily: 'var(--font-display)', fontWeight: 800, fontSize: '20px', color: 'var(--text-primary)' }">{{ brand }}</span>
      </div>
      <cove-button *ngIf="showUpload" icon="upload-cloud" [ngStyle]="{ margin: '0 8px', display: 'block' }" (click)="upload.emit()">Upload</cove-button>
      <nav aria-label="Primary" [ngStyle]="{ display: 'flex', flexDirection: 'column', gap: '2px' }">
        <ng-container *ngFor="let item of items">
          <!-- Leaf -->
          <button *ngIf="!item.children" type="button"
                  (click)="navigate.emit(item.key)"
                  [attr.aria-current]="active === item.key ? 'page' : null"
                  [ngStyle]="navStyle(item)">
            <cove-icon [name]="item.icon" [size]="18"></cove-icon>{{ item.label }}
          </button>

          <!-- Expandable group: parent toggles, children navigate -->
          <ng-container *ngIf="item.children">
            <button type="button"
                    (click)="toggle(item.key)"
                    [attr.aria-expanded]="isExpanded(item)"
                    [ngStyle]="groupStyle(item)">
              <cove-icon [name]="item.icon" [size]="18"></cove-icon>
              <span [ngStyle]="{ flex: 1 }">{{ item.label }}</span>
              <cove-icon name="chevron-right" [size]="16"
                         [ngStyle]="{ transform: isExpanded(item) ? 'rotate(90deg)' : 'none', transition: 'transform var(--duration-fast)' }"></cove-icon>
            </button>
            <div *ngIf="isExpanded(item)" [ngStyle]="{ display: 'flex', flexDirection: 'column', gap: '2px' }">
              <button *ngFor="let child of item.children" type="button"
                      (click)="navigate.emit(child.key)"
                      [attr.aria-current]="active === child.key ? 'page' : null"
                      [ngStyle]="childStyle(child)">
                <cove-icon [name]="child.icon" [size]="16"></cove-icon>{{ child.label }}
              </button>
            </div>
          </ng-container>
        </ng-container>
      </nav>
      <div [ngStyle]="{ marginTop: 'auto', padding: '0 8px', display: 'flex', flexDirection: 'column', gap: '8px' }">
        <cove-progress-bar [value]="pct" [tone]="pct > 85 ? 'warning' : 'accent'"></cove-progress-bar>
        <div [ngStyle]="{ fontSize: '12px', color: 'var(--text-tertiary)' }">{{ quotaLabel || defaultQuotaLabel }}</div>
        <div *ngIf="quotaNote" [ngStyle]="{ fontSize: '12px', color: 'var(--text-tertiary)' }">{{ quotaNote }}</div>
      </div>
    </div>`,
})
export class SidebarComponent implements OnChanges {
  @Input() active = 'mine';
  @Input() brand = 'Cove';
  /** Optional brand mark image; falls back to the generic cloud glyph when unset. */
  @Input() brandMark = '';
  @Input() showUpload = true;
  /** Raw numbers only used for the bar; the caption comes from quotaLabel. */
  @Input() quotaUsed = 0;
  @Input() quotaTotal = 100;
  /** Pre-formatted caption, e.g. "1.2 GB of 5 GB used". Falls back to a GB rendering. */
  @Input() quotaLabel = '';
  /** Optional second line, e.g. "800 MB in Trash". */
  @Input() quotaNote = '';
  @Output() navigate = new EventEmitter<string>();
  @Output() upload = new EventEmitter<void>();

  @Input() items: NavItem[] = [
    { key: 'mine', label: 'My Drive', icon: 'folder' },
    { key: 'shared', label: 'Shared with me', icon: 'users' },
    { key: 'recent', label: 'Recent', icon: 'clock' },
    { key: 'starred', label: 'Starred', icon: 'star' },
    { key: 'trash', label: 'Trash', icon: 'trash-2' },
  ];

  get pct() { return this.quotaTotal ? Math.round((this.quotaUsed / this.quotaTotal) * 100) : 0; }
  get defaultQuotaLabel() { return `${this.quotaUsed} GB of ${this.quotaTotal} GB used`; }

  /** Groups the user has expanded. A group is auto-expanded when it holds the active child, so
   *  landing on a sub-route (e.g. /admin/email) opens its parent. */
  private readonly expanded = new Set<string>();

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['active'] || changes['items']) {
      for (const item of this.items) {
        if (this.containsActive(item)) this.expanded.add(item.key);
      }
    }
  }

  isExpanded(item: NavItem): boolean { return this.expanded.has(item.key); }

  toggle(key: string): void {
    this.expanded.has(key) ? this.expanded.delete(key) : this.expanded.add(key);
  }

  private containsActive(item: NavItem): boolean {
    return !!item.children?.some((c) => c.key === this.active);
  }

  private baseRow() {
    // Button reset — these render as <button> for keyboard/AT support, so strip the
    // native chrome and make them fill the row like the old <div> did.
    return {
      width: '100%', border: 'none', textAlign: 'left', fontFamily: 'inherit', appearance: 'none',
      display: 'flex', alignItems: 'center', gap: '12px', borderRadius: 'var(--radius-md)',
      cursor: 'pointer', fontWeight: 600,
    };
  }

  navStyle(item: NavItem) {
    const on = this.active === item.key;
    return {
      ...this.baseRow(), padding: '10px 12px', fontSize: '14px',
      background: on ? 'var(--accent-subtle)' : 'transparent',
      color: on ? 'var(--accent-subtle-text)' : 'var(--text-secondary)',
    };
  }

  /** A group parent: never the accent fill (that's reserved for the active child), just emphasized
   *  text when one of its children is active. */
  groupStyle(item: NavItem) {
    const on = this.containsActive(item);
    return {
      ...this.baseRow(), padding: '10px 12px', fontSize: '14px', background: 'transparent',
      color: on ? 'var(--text-primary)' : 'var(--text-secondary)',
    };
  }

  /** A child row: indented under the parent, slightly smaller, accent fill when active. */
  childStyle(child: NavItem) {
    const on = this.active === child.key;
    return {
      ...this.baseRow(), gap: '10px', padding: '8px 12px 8px 30px', fontSize: '13px',
      background: on ? 'var(--accent-subtle)' : 'transparent',
      color: on ? 'var(--accent-subtle-text)' : 'var(--text-secondary)',
    };
  }
}
