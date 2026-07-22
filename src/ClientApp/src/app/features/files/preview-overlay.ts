import {
  Component,
  EventEmitter,
  HostListener,
  Input,
  Output,
  inject,
  signal,
} from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { MediaService } from '../../core/media.service';
import { saveFile } from '../../core/save-file';
import { BytesPipe } from '../../core/bytes.pipe';
import { MediaListItem } from '../../core/models';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { IconButtonComponent } from '../../cove/lib/icon-button/icon-button.component';
import { ButtonComponent } from '../../cove/lib/button/button.component';

/**
 * Full-screen file preview.
 *
 * Deliberately not built on cove-modal: that one is sized for forms (fixed padding, header and
 * footer bars, a width input), whereas a preview wants the viewport.
 *
 * How a file is rendered comes from the server's `previewKind`, never from sniffing the content
 * type here — see PreviewPolicy. Two rules that matter:
 *  - images, **including SVG**, go in an `<img>`, which never executes embedded script;
 *  - only PDFs use an `<iframe>`, and the server forces that response's content type so a file
 *    lying about being a PDF cannot become active content.
 */
@Component({
  selector: 'app-preview-overlay',
  imports: [BytesPipe, IconComponent, IconButtonComponent, ButtonComponent],
  templateUrl: './preview-overlay.html',
  styleUrl: './preview-overlay.scss',
})
export class PreviewOverlay {
  private readonly media = inject(MediaService);
  private readonly sanitizer = inject(DomSanitizer);

  /** Previewable files in the current folder, in display order. */
  @Input() items: MediaListItem[] = [];

  /**
   * Which item to open on. A setter rather than a method the parent calls: the overlay is created
   * by an @if, so a viewChild reference is still undefined at the moment the parent decides to
   * open it. Inputs are set before the first render, so loading starts immediately.
   */
  @Input() set startIndex(value: number) {
    this.index.set(value);
    void this.load();
  }

  @Output() closed = new EventEmitter<void>();

  protected readonly index = signal(0);
  protected readonly url = signal<string | null>(null);
  protected readonly pdfUrl = signal<SafeResourceUrl | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected current(): MediaListItem | null {
    return this.items[this.index()] ?? null;
  }

  protected hasPrev(): boolean {
    return this.index() > 0;
  }

  protected hasNext(): boolean {
    return this.index() < this.items.length - 1;
  }

  protected async go(delta: number): Promise<void> {
    const next = this.index() + delta;
    if (next < 0 || next >= this.items.length) return;
    this.index.set(next);
    await this.load();
  }

  private async load(): Promise<void> {
    const item = this.current();
    if (!item) return;

    this.loading.set(true);
    this.error.set(null);
    this.url.set(null);
    this.pdfUrl.set(null);

    try {
      const url = await this.media.previewUrl(item.id);
      this.url.set(url);
      // Angular treats an iframe src as a resource URL and blocks plain string binding. This is
      // our own short-lived presigned URL, and the PDF is served cross-origin with a forced
      // content type, so the iframe cannot reach back into the app.
      if (item.previewKind === 'pdf') {
        this.pdfUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
      }
    } catch {
      this.error.set('Could not load a preview for this file.');
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Presigned URLs are short-lived, so an overlay left open can outlive its URL. Drop the cached
   * one first — retrying with the same dead URL would just fail again.
   */
  protected retry(): void {
    const item = this.current();
    if (item) this.media.invalidateUrls(item.id);
    void this.load();
  }

  protected async download(): Promise<void> {
    const item = this.current();
    if (!item) return;
    try {
      saveFile(await this.media.downloadUrl(item.id));
    } catch {
      this.error.set('Could not start the download.');
    }
  }

  protected onImageError(): void {
    // Most likely an expired signature; make sure a retry doesn't reuse it.
    const item = this.current();
    if (item) this.media.invalidateUrls(item.id);
    this.error.set('That preview link expired. Try again.');
  }

  @HostListener('document:keydown', ['$event'])
  protected onKey(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.closed.emit();
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      void this.go(-1);
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      void this.go(1);
    }
  }
}
