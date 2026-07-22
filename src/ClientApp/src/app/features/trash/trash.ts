import { Component, computed, inject, signal } from '@angular/core';
import { TrashService } from '../../core/trash.service';
import { UsageStore } from '../../core/usage.store';
import { BytesPipe } from '../../core/bytes.pipe';
import { formatDate, typeMetaOf } from '../../core/file-type';
import { TrashItem } from '../../core/models';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { ModalComponent } from '../../cove/lib/modal/modal.component';
import { BadgeComponent } from '../../cove/lib/badge/badge.component';

@Component({
  selector: 'app-trash',
  imports: [BytesPipe, ButtonComponent, IconComponent, ModalComponent, BadgeComponent],
  templateUrl: './trash.html',
  styleUrl: './trash.scss',
})
export class Trash {
  private readonly trash = inject(TrashService);
  protected readonly usage = inject(UsageStore);

  protected readonly items = signal<TrashItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly purgeTarget = signal<TrashItem | null>(null);
  protected readonly emptyOpen = signal(false);
  protected readonly busy = signal(false);

  protected readonly totalBytes = computed(() =>
    this.items().reduce((sum, i) => sum + i.sizeBytes, 0)
  );

  constructor() {
    void this.refresh();
  }

  private async refresh(): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set(await this.trash.list());
      this.error.set(null);
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not load the Trash.'));
    } finally {
      this.loading.set(false);
    }
    void this.usage.refresh();
  }

  protected async restore(item: TrashItem): Promise<void> {
    this.busy.set(true);
    try {
      const res = await this.trash.restore(item.id);
      // The name can change on the way back: something may have taken it while this was away.
      this.flash(
        res.name === item.name
          ? `Restored "${res.name}".`
          : `Restored as "${res.name}" — "${item.name}" was taken.`
      );
      await this.refresh();
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not restore that item.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async confirmPurge(): Promise<void> {
    const item = this.purgeTarget();
    this.purgeTarget.set(null);
    if (!item) return;
    this.busy.set(true);
    try {
      await this.trash.purge(item.id);
      await this.refresh();
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not delete that item.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async confirmEmpty(): Promise<void> {
    this.emptyOpen.set(false);
    this.busy.set(true);
    try {
      await this.trash.empty();
      await this.refresh();
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not empty the Trash.'));
    } finally {
      this.busy.set(false);
    }
  }

  /** Whole days until the server purges this item; 0 means it goes in the next sweep. */
  protected daysLeft(item: TrashItem): number {
    const ms = new Date(item.purgesAt).getTime() - Date.now();
    return Math.max(0, Math.ceil(ms / 86_400_000));
  }

  protected purgeLabel(item: TrashItem): string {
    const days = this.daysLeft(item);
    if (days === 0) return 'Deletes today';
    return days === 1 ? 'Deletes tomorrow' : `Deletes in ${days} days`;
  }

  protected iconOf(item: TrashItem): { icon: string; color: string } {
    return item.kind === 'folder'
      ? { icon: 'folder', color: 'var(--teal-500)' }
      : typeMetaOf(this.guessType(item.name));
  }

  /** The trash listing carries no content type, so fall back to the extension. */
  private guessType(name: string): string {
    const ext = name.slice(name.lastIndexOf('.') + 1).toLowerCase();
    if (['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'].includes(ext)) return 'image/';
    if (['mp4', 'mov', 'webm', 'mkv'].includes(ext)) return 'video/';
    if (['mp3', 'wav', 'flac', 'aac'].includes(ext)) return 'audio/';
    if (ext === 'pdf') return 'application/pdf';
    if (['zip', 'tar', 'gz', 'rar', '7z'].includes(ext)) return 'application/zip';
    if (['doc', 'docx'].includes(ext)) return 'application/msword';
    if (['xls', 'xlsx', 'csv'].includes(ext)) return 'application/vnd.ms-excel';
    return '';
  }

  protected formatDate(iso: string): string {
    return formatDate(iso);
  }

  private flash(message: string): void {
    this.notice.set(message);
    setTimeout(() => this.notice.set(null), 6000);
  }

  private messageOf(e: unknown, fallback: string): string {
    const detail = (e as { error?: { detail?: string } })?.error?.detail;
    return typeof detail === 'string' && detail ? detail : fallback;
  }
}
