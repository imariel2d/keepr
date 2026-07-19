import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MediaService } from '../../core/media.service';
import { UploadService } from '../../core/upload.service';
import { BytesPipe } from '../../core/bytes.pipe';
import { MediaListItem, Usage } from '../../core/models';

@Component({
  selector: 'app-home',
  imports: [BytesPipe],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home implements OnInit {
  private readonly media = inject(MediaService);
  protected readonly uploads = inject(UploadService);

  protected readonly usage = signal<Usage | null>(null);
  protected readonly files = signal<MediaListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly dragging = signal(false);

  protected readonly usedPercent = computed(() => {
    const u = this.usage();
    if (!u || u.quotaBytes === 0) return 0;
    return Math.min(100, Math.round((u.usedBytes / u.quotaBytes) * 100));
  });

  async ngOnInit(): Promise<void> {
    await this.refresh();
    this.loading.set(false);
  }

  protected async onFilesSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    await this.uploadAll(files);
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
  }

  protected async onDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    this.dragging.set(false);
    const files = Array.from(event.dataTransfer?.files ?? []);
    await this.uploadAll(files);
  }

  protected async download(item: MediaListItem): Promise<void> {
    const url = await this.media.downloadUrl(item.id);
    window.open(url, '_blank');
  }

  protected async remove(item: MediaListItem): Promise<void> {
    await this.media.delete(item.id);
    await this.refresh();
  }

  /** A category label + emoji glyph for a file, from its content type. */
  protected icon(contentType: string | null): string {
    const t = contentType ?? '';
    if (t.startsWith('image/')) return '🖼️';
    if (t.startsWith('video/')) return '🎬';
    if (t.startsWith('audio/')) return '🎵';
    if (t === 'application/pdf') return '📄';
    if (t.includes('zip') || t.includes('compressed')) return '🗜️';
    return '📁';
  }

  private async uploadAll(files: File[]): Promise<void> {
    for (const file of files) {
      try {
        await this.uploads.upload(file);
      } catch {
        /* the task carries its own error; continue with the next file */
      }
    }
    if (files.length) await this.refresh();
  }

  private async refresh(): Promise<void> {
    const [usage, files] = await Promise.all([
      this.media.usage(),
      this.media.list(),
    ]);
    this.usage.set(usage);
    this.files.set(files);
  }
}
