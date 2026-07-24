import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
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
 * "Share" dialog for one file: mint an "anyone with the link" URL, see the file's existing links,
 * change a link's expiry, revoke one, or stop sharing the file entirely.
 *
 * The created URL is shown once and cannot be re-displayed — the server stores only the token's
 * digest (design §3.1). Editing a link's expiry, rather than recreating it, is how a link already
 * handed out is kept alive.
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

        <!-- The one-time URL of a link just created -->
        @if (freshUrl(); as url) {
          <div class="fresh">
            <p class="fresh-note">
              <cove-icon name="check" [size]="14" /> Link created. Copy it now — for security it
              won't be shown again.
            </p>
            <div class="url-row">
              <input class="url" type="text" readonly [value]="url" (focus)="selectAll($event)" />
              <cove-button variant="secondary" [icon]="copied() ? 'check' : 'copy'" (click)="copy(url)">
                {{ copied() ? 'Copied' : 'Copy' }}
              </cove-button>
            </div>
          </div>
        }

        <!-- Existing links -->
        <div class="links">
          @if (loading()) {
            <p class="muted">Loading links…</p>
          } @else if (links().length === 0) {
            <p class="muted">This file isn't shared yet.</p>
          } @else {
            @for (link of links(); track link.linkId) {
              <div class="link" [class.dead]="status(link) !== 'Active'">
                <div class="link-info">
                  <span class="badge" [attr.data-state]="status(link)">{{ status(link) }}</span>
                  <span class="dates">
                    @if (status(link) === 'Revoked') {
                      Revoked
                    } @else {
                      Expires {{ formatDate(link.expiresAt) }}
                    }
                  </span>
                </div>
                @if (status(link) === 'Active') {
                  <div class="link-actions">
                    <select [value]="''" (change)="extend(link, $event)" title="Change expiry">
                      <option value="" disabled>Change expiry…</option>
                      @for (o of options; track o.days) {
                        <option [value]="o.days">{{ o.label }}</option>
                      }
                    </select>
                    <cove-button variant="ghost" icon="x" [disabled]="busy()" (click)="revoke(link)">
                      Revoke
                    </cove-button>
                  </div>
                }
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
  protected readonly freshUrl = signal<string | null>(null);
  protected readonly copied = signal(false);

  /** Called by the parent each time the dialog opens, to load the file's links fresh. */
  async load(): Promise<void> {
    this.freshUrl.set(null);
    this.copied.set(false);
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
    this.copied.set(false);
    try {
      const res = await this.api.create(this.fileId, this.days());
      this.freshUrl.set(res.url);
      this.links.set(await this.api.list(this.fileId));
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
      this.freshUrl.set(null);
      this.links.set(await this.api.list(this.fileId));
      this.changed.emit();
    } catch {
      this.error.set('Could not stop sharing this file.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(url: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(url);
      this.copied.set(true);
    } catch {
      // Clipboard blocked (e.g. insecure context) — the field is selectable as a fallback.
    }
  }

  protected selectAll(event: FocusEvent): void {
    (event.target as HTMLInputElement).select();
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
