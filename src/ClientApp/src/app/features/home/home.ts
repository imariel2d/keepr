import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MediaService } from '../../core/media.service';
import { UploadService } from '../../core/upload.service';
import { BytesPipe } from '../../core/bytes.pipe';
import { MediaListItem, Usage, UploadTask } from '../../core/models';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { IconButtonComponent } from '../../cove/lib/icon-button/icon-button.component';
import { ProgressBarComponent } from '../../cove/lib/progress-bar/progress-bar.component';
import { FileType, TYPE_META } from '../../cove/lib/files/file-type-meta';

type Tone = 'accent' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'app-home',
  imports: [BytesPipe, ButtonComponent, IconComponent, IconButtonComponent, ProgressBarComponent],
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

  protected onBrowse(): void {
    document.getElementById('file-input')?.click();
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

  /** Map a MIME type to a Cove file-type, then to its icon + accent colour. */
  protected fileType(contentType: string | null): FileType {
    const t = contentType ?? '';
    if (t.startsWith('image/')) return 'image';
    if (t.startsWith('video/')) return 'video';
    if (t.startsWith('audio/')) return 'audio';
    if (t === 'application/pdf') return 'pdf';
    if (t.includes('zip') || t.includes('compressed') || t.includes('tar')) return 'archive';
    if (t.includes('word') || t.includes('document')) return 'doc';
    if (t.includes('sheet') || t.includes('excel') || t.includes('csv')) return 'sheet';
    return 'default';
  }

  protected typeMeta(contentType: string | null): { icon: string; color: string } {
    return TYPE_META[this.fileType(contentType)];
  }

  /** e.g. "Jul 12, 2026" for the modified column. */
  protected formatDate(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '';
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  }

  protected taskTone(task: UploadTask): Tone {
    if (task.status === 'error' || task.status === 'aborted') return 'danger';
    if (task.status === 'done') return 'success';
    return 'accent';
  }

  protected taskPercent(task: UploadTask): number {
    return task.totalBytes ? Math.round((task.uploadedBytes / task.totalBytes) * 100) : 0;
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
