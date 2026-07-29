import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, computed, inject, signal } from '@angular/core';
import { ShareService } from '../../core/share.service';
import { ShareLinkResponse } from '../../core/models';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconButtonComponent } from '../../cove/lib/icon-button/icon-button.component';
import { ContextMenuComponent, ContextMenuItem } from '../../cove/lib/context-menu/context-menu.component';
import { menuAnchor } from '../../core/menu-anchor';
import { ModalComponent } from '../../cove/lib/modal/modal.component';

interface ExpiryOption {
  readonly label: string;
  /** null = the link never expires. */
  readonly days: number | null;
}

/** Non-empty sentinel for the "Never" option so it's distinct from the empty placeholder value. */
const NEVER = 'never';

const EXPIRY_OPTIONS: ExpiryOption[] = [
  { label: '1 day', days: 1 },
  { label: '7 days', days: 7 },
  { label: '30 days', days: 30 },
  { label: 'Never', days: null },
];

/**
 * "Share" dialog for one file: mint an "anyone with the link" URL, copy any active link, change a
 * link's expiry, revoke one, or stop sharing the file entirely.
 *
 * An active link's URL can be re-copied at any time — the server stores the token so it can hand
 * the URL back (design Q-S5). Revoked links are hidden; they're dead and can't be re-shared.
 */
@Component({
  selector: 'app-share-dialog',
  imports: [ModalComponent, ButtonComponent, IconButtonComponent, ContextMenuComponent],
  template: `
    <cove-modal [open]="open" [title]="'Share ' + fileName" [width]="560" (close)="close.emit()">
      <div class="share-body">
        <!-- Create -->
        <div class="create">
          <label>
            Link expires in
            <select [value]="days() ?? NEVER" (change)="onDaysChange($event)">
              @for (o of options; track o.label) {
                <option [value]="o.days ?? NEVER">{{ o.label }}</option>
              }
            </select>
          </label>
          <cove-button variant="primary" icon="link" [disabled]="busy()" (click)="create()">
            Create link
          </cove-button>
        </div>

        <!-- Existing links (revoked ones are hidden) -->
        <div class="links">
          @if (loading()) {
            <p class="muted">Loading links…</p>
          } @else if (visibleLinks().length === 0) {
            <p class="muted">This file isn't shared yet.</p>
          } @else {
            @for (link of visibleLinks(); track link.linkId) {
              <div class="link" [class.dead]="status(link) !== 'Active'">
                <div class="link-info">
                  <span class="badge" [attr.data-state]="status(link)">{{ status(link) }}</span>
                  <span class="dates">
                    @if (link.expiresAt) { Expires {{ formatDate(link.expiresAt) }} }
                    @else { Never expires }
                  </span>
                </div>
                <div class="link-actions">
                  <select [value]="''" (change)="extend(link, $event)" title="Change expiry">
                    <option value="" disabled>Change expiry…</option>
                    @for (o of options; track o.label) {
                      <option [value]="o.days ?? NEVER">{{ o.label }}</option>
                    }
                  </select>
                  @if (copiedId() === link.linkId) {
                    <span class="copied">Copied</span>
                  }
                  <cove-icon-button icon="more-vertical" label="Link actions" [size]="32"
                                    (click)="openLinkMenu(link, $event)" />
                </div>
              </div>
            }
          }
        </div>

        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
      </div>

      <div class="foot">
        @if (hasActive()) {
          <cove-button variant="danger" icon="link-2-off" [disabled]="busy()" (click)="stopAll()">
            Stop sharing
          </cove-button>
        }
        <cove-button variant="ghost" (click)="close.emit()">Done</cove-button>
      </div>
    </cove-modal>

    <cove-context-menu
      [open]="menuOpen()"
      [x]="menuX()"
      [y]="menuY()"
      [items]="menuItems()"
      (closed)="menuOpen.set(false)" />
  `,
  styleUrl: './share-dialog.scss',
})
export class ShareDialog implements OnChanges {
  private readonly api = inject(ShareService);

  @Input() open = false;
  @Input() fileId = '';
  @Input() fileName = '';

  @Output() close = new EventEmitter<void>();
  /** Emitted whenever the file's share state changes, so the card can update a "shared" marker. */
  @Output() changed = new EventEmitter<void>();

  protected readonly options = EXPIRY_OPTIONS;
  protected readonly NEVER = NEVER;
  protected readonly days = signal(EXPIRY_OPTIONS[1].days); // default 7
  protected readonly links = signal<ShareLinkResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** The link just copied, so its row briefly shows a "Copied" chip. */
  protected readonly copiedId = signal<string | null>(null);

  // Per-link actions (copy / revoke) live in a three-dot menu to keep each row uncluttered.
  protected readonly menuOpen = signal(false);
  protected readonly menuX = signal(0);
  protected readonly menuY = signal(0);
  protected readonly menuItems = signal<ContextMenuItem[]>([]);

  /** Revoked links are dead and can't be re-shared, so they're hidden from the list. */
  protected readonly visibleLinks = computed(() => this.links().filter((l) => !l.revoked));

  /**
   * Load the file's links whenever the dialog opens (or its target file changes while open).
   * Inputs are all assigned before ngOnChanges runs, so `fileId` is populated here — unlike the
   * old parent-driven `load()` call, which fired before the [fileId] binding flushed and hit
   * `/api/media//shares` with an empty id.
   */
  ngOnChanges(changes: SimpleChanges): void {
    if (changes['open']?.currentValue === true && this.fileId) {
      void this.load();
    }
  }

  /** Fetches the file's links fresh. */
  async load(): Promise<void> {
    this.copiedId.set(null);
    this.error.set(null);
    this.loading.set(true);
    try {
      this.links.set(await this.api.list(this.fileId));
    } catch {
      this.error.set('Could not load this file’s links.');
    } finally {
      this.loading.set(false);
    }
  }

  protected onDaysChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.days.set(value === NEVER ? null : Number(value));
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.create(this.fileId, this.days());
      this.links.set(await this.api.list(this.fileId));
      // The new link appears in the list; the owner copies it from the row's actions menu when
      // they want it (auto-copying on create was removed per product decision).
      this.changed.emit();
    } catch {
      this.error.set('Could not create the link.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async extend(link: ShareLinkResponse, event: Event): Promise<void> {
    const select = event.target as HTMLSelectElement;
    const value = select.value;
    select.value = ''; // reset the picker back to its placeholder
    if (value === '') return; // the placeholder was re-selected — no change
    const days = value === NEVER ? null : Number(value);

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.updateExpiry(link.linkId, days);
      this.links.set(await this.api.list(this.fileId));
      this.changed.emit();
    } catch {
      this.error.set('Could not change the expiry.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async revoke(link: ShareLinkResponse): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.revoke(link.linkId);
      this.links.set(await this.api.list(this.fileId));
      this.changed.emit();
    } catch {
      this.error.set('Could not revoke the link.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async stopAll(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.stopSharingFile(this.fileId);
      this.copiedId.set(null);
      this.links.set(await this.api.list(this.fileId));
      this.changed.emit();
    } catch {
      this.error.set('Could not stop sharing this file.');
    } finally {
      this.busy.set(false);
    }
  }

  protected copy(link: ShareLinkResponse): Promise<void> {
    return this.copyUrl(link.url, link.linkId);
  }

  /** Opens the per-link actions menu at the click position: copy (active links only) + revoke. */
  protected openLinkMenu(link: ShareLinkResponse, event: MouseEvent): void {
    event.stopPropagation();
    const items: ContextMenuItem[] = [];
    if (this.status(link) === 'Active') {
      items.push({ label: 'Copy link', icon: 'copy', onSelect: () => void this.copy(link) });
    }
    items.push({ label: 'Revoke', icon: 'link-2-off', danger: true, onSelect: () => void this.revoke(link) });
    this.menuItems.set(items);
    const { x, y } = menuAnchor(event);
    this.menuX.set(x);
    this.menuY.set(y);
    this.menuOpen.set(true);
  }

  private async copyUrl(url: string, linkId: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      this.copiedId.set(linkId);
      // Auto-clear the "Copied" chip; the menu has already closed, so there's no button to reset.
      setTimeout(() => this.copiedId() === linkId && this.copiedId.set(null), 1500);
    } catch {
      // Clipboard blocked (e.g. an insecure context); surface it rather than fail silently.
      this.error.set('Copying isn’t available here — select and copy the link manually.');
    }
  }

  protected status(link: ShareLinkResponse): 'Active' | 'Expired' | 'Revoked' {
    if (link.revoked) return 'Revoked';
    if (link.expiresAt === null) return 'Active'; // never expires
    return new Date(link.expiresAt).getTime() <= Date.now() ? 'Expired' : 'Active';
  }

  protected hasActive(): boolean {
    return this.links().some((l) => this.status(l) === 'Active');
  }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
