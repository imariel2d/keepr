import { Component, inject, signal } from '@angular/core';
import { Router, RouterOutlet, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from './core/auth.service';
import { ThemeService } from './core/theme.service';
import { UsageStore } from './core/usage.store';
import { ButtonComponent } from './cove/lib/button/button.component';
import { IconButtonComponent } from './cove/lib/icon-button/icon-button.component';
import { NavItem, SidebarComponent } from './cove/lib/sidebar/sidebar.component';
import { BytesPipe } from './core/bytes.pipe';
import { UploadToast } from './features/uploads/upload-toast';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    ButtonComponent,
    IconButtonComponent,
    SidebarComponent,
    UploadToast,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly usage = inject(UsageStore);
  private readonly router = inject(Router);
  private readonly bytes = new BytesPipe();

  /** Which sidebar entry is highlighted, derived from the URL. */
  protected readonly section = signal<'files' | 'trash'>('files');

  protected readonly navItems: NavItem[] = [
    { key: 'files', label: 'My Files', icon: 'folder' },
    { key: 'trash', label: 'Trash', icon: 'trash-2' },
  ];

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => this.section.set(e.urlAfterRedirects.startsWith('/trash') ? 'trash' : 'files'));
  }

  protected quotaLabel(): string {
    const u = this.usage.usage();
    if (!u) return '';
    return `${this.bytes.transform(u.usedBytes)} of ${this.bytes.transform(u.quotaBytes)} used`;
  }

  /**
   * Trashed bytes still count against the quota until purged — without this line, "I deleted
   * everything and I'm still full" has no answer in the UI.
   */
  protected quotaNote(): string {
    const trashed = this.usage.usage()?.trashedBytes ?? 0;
    return trashed > 0 ? `${this.bytes.transform(trashed)} in Trash` : '';
  }

  protected usedPercent(): number {
    const u = this.usage.usage();
    if (!u || !u.quotaBytes) return 0;
    return Math.min(100, Math.round((u.usedBytes / u.quotaBytes) * 100));
  }

  protected navigate(key: string): void {
    this.router.navigate([key === 'trash' ? '/trash' : '/files']);
  }

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
