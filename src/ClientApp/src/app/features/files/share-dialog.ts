import { Component, EventEmitter, Input, Output, computed, inject, signal } from '@angular/core';
import { ShareService } from '../../core/share.service';
import { ShareLinkResponse } from '../../core/models';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { ModalComponent } from '../../cove/lib/modal/modal.component';

interface ExpiryOption {
  readonly label: string;
  readonly days: number;
}

const EXPIRY_OPTIONS: ExpiryOption[] = [
  { label: '1 day', days: 1 },
  { label: '7 days', days: 7 },
  { label: '30 days', days: 30 },
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
  imports: [ModalComponent, ButtonComponent, IconComponent],
  template: `
    <cove-modal [open]="open" [title]="'Share ' + fileName" [width]="560" (close)="close.emit()">
      <div class="share-body">
        <!-- Create -->
        <div class="create">
          <label>
            Link expires in
            <select [value]="days()" (change)="onDaysChange($event)">
              @for (o of options; track o.days) {
                <option [value]="o.days">{{ o.label }}</option>
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
                  <span class="dates">Expires {{ formatDate(link.expiresAt) }}</span>
                </div>
                <div class="link-actions">
                  @if (status(link) === 'Active') {
                    <cove-button variant="secondary" size="sm"
                                 [icon]="copiedId() === link.linkId ? 'check' : 'copy'"
                                 (click)="copy(link)">
                      {{ copiedId() === link.linkId ? 'Copied' : 'Copy link' }}
                    </cove-button>
                  }
                  <select [value]="''" (change)="extend(link, $event)" title="Change expiry">
                    <option value="" disabled>Change expiry…</option>
                    @for (o of options; track o.days) {
                      <option [value]="o.days">{{ o.label }}</option>
                    }
                  </select>
                  <cove-button variant="ghost" size="sm" icon="x" [disabled]="busy()"
                               (click)="revoke(link)">
                    Revoke
                  </cove-button>
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
  `,
  styleUrl: './share-dialog.scss',
})
export class ShareDialog {
  private readonly api = inject(ShareService);

  @Input() open = false;
  @Input() fileId = '';
  @Input() fileName = '';

  @Output() close = new EventEmitter<void>();
  /** Emitted whenever the file's share state changes, so the card can update a "shared" marker. */
  @Output() changed = new EventEmitter<void>();

  protected readonly options = EXPIRY_OPTIONS;
  protected readonly days = signal(EXPIRY_OPTIONS[1].days); // default 7
  protected readonly links = signal<ShareLinkResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  /** The link whose "Copy link" was just clicked, so only that button reads "Copied". */
  protected readonly copiedId = signal<string | null>(null);

  /** Revoked links are dead and can't be re-shared, so they're hidden from the list. */
  protected readonly visibleLinks = computed(() => this.links().filter((l) => !l.revoked));

  /** Called by the parent each time the dialog opens, to load the file's links fresh. */
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
    this.days.set(Number((event.target as HTMLSelectElement).value));
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const res = await this.api.create(this.fileId, this.days());
      this.links.set(await this.api.list(this.fileId));
      // Copy the new link straight to the clipboard — it's the thing the user just asked for.
      await this.copyUrl(res.url, res.linkId);
      this.changed.emit();
    } catch {
      this.error.set('Could not create the link.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async extend(link: ShareLinkResponse, event: Event): Promise<void> {
    const select = event.target as HTMLSelectElement;
    const days = Number(select.value);
    select.value = ''; // reset the picker back to its placeholder
    if (!days) return;

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

  private async copyUrl(url: string, linkId: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      this.copiedId.set(linkId);
    } catch {
      // Clipboard blocked (e.g. an insecure context); surface it rather than fail silently.
      this.error.set('Copying isn’t available here — select and copy the link manually.');
    }
  }

  protected status(link: ShareLinkResponse): 'Active' | 'Expired' | 'Revoked' {
    if (link.revoked) return 'Revoked';
    return new Date(link.expiresAt).getTime() <= Date.now() ? 'Expired' : 'Active';
  }

  protected hasActive(): boolean {
    return this.links().some((l) => this.status(l) === 'Active');
  }

  protected formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
