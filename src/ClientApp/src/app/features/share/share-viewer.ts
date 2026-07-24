import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ShareService } from '../../core/share.service';
import { saveFile } from '../../core/save-file';
import { BytesPipe } from '../../core/bytes.pipe';
import { PreviewKind, SharePublicResponse } from '../../core/models';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { ButtonComponent } from '../../cove/lib/button/button.component';

type ViewState = 'loading' | 'ok' | 'notfound' | 'gone' | 'error';

/**
 * The public page a share link opens to (`/s/:token`). Unauthenticated — the token in the URL is
 * the whole authorization. It resolves the token to metadata, renders a preview for previewable
 * types (reusing the server's `previewKind` and `PreviewPolicy`, never sniffing here), and offers
 * a download for everything. See docs/shareable-links-design.md §5.
 */
@Component({
  selector: 'app-share-viewer',
  imports: [BytesPipe, IconComponent, ButtonComponent],
  templateUrl: './share-viewer.html',
  styleUrl: './share-viewer.scss',
})
export class ShareViewer implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly share = inject(ShareService);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly state = signal<ViewState>('loading');
  protected readonly file = signal<SharePublicResponse | null>(null);
  /** The server's problem+json message for a 404/410 — shown verbatim. */
  protected readonly message = signal<string | null>(null);

  protected readonly previewUrl = signal<string | null>(null);
  protected readonly pdfUrl = signal<SafeResourceUrl | null>(null);
  /** A preview URL was requested but the media wouldn't load — fall back to a download prompt. */
  protected readonly previewFailed = signal(false);
  protected readonly downloadError = signal<string | null>(null);

  private token = '';

  async ngOnInit(): Promise<void> {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    if (!this.token) {
      this.state.set('notfound');
      return;
    }
    await this.load();
  }

  private async load(): Promise<void> {
    this.state.set('loading');
    try {
      const meta = await this.share.resolve(this.token);
      this.file.set(meta);
      this.state.set('ok');
      if (meta.previewKind) await this.loadPreview(meta.previewKind);
    } catch (e) {
      const status = (e as { status?: number })?.status;
      const detail = (e as { error?: { detail?: string } })?.error?.detail;
      if (status === 404) {
        this.state.set('notfound');
        this.message.set(detail ?? "This share link doesn't exist.");
      } else if (status === 410) {
        this.state.set('gone');
        this.message.set(detail ?? 'This share link is no longer available.');
      } else {
        this.state.set('error');
      }
    }
  }

  private async loadPreview(kind: PreviewKind): Promise<void> {
    try {
      const url = await this.share.previewUrl(this.token);
      this.previewUrl.set(url);
      // Angular blocks a plain string as an iframe src; this is our own short-lived presigned URL,
      // served cross-origin with a forced content type, so the PDF iframe can't reach the app.
      if (kind === 'pdf') this.pdfUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
    } catch {
      this.previewFailed.set(true);
    }
  }

  protected async download(): Promise<void> {
    this.downloadError.set(null);
    try {
      saveFile(await this.share.downloadUrl(this.token));
    } catch {
      this.downloadError.set('Could not start the download. The link may have just expired.');
    }
  }

  /** A previewable file whose media element errored — most likely an expired signature. */
  protected onMediaError(): void {
    this.previewFailed.set(true);
  }
}
