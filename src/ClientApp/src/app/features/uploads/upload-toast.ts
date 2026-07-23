import { Component, computed, effect, inject } from '@angular/core';
import { UploadService } from '../../core/upload.service';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { ProgressBarComponent } from '../../cove/lib/progress-bar/progress-bar.component';

/** How long a fully successful batch stays on screen before it clears itself. */
const AUTO_DISMISS_MS = 5000;

/**
 * Bottom-right progress notification for uploads. Replaces the inline per-file progress rows
 * that used to sit above the grid: uploads keep running while you navigate, so their progress
 * belongs in a fixed surface rather than inside one folder's page.
 *
 * A clean batch disappears on its own; anything that failed stays until dismissed, so an error
 * is never swallowed by a timer.
 */
@Component({
  selector: 'app-upload-toast',
  imports: [IconComponent, ProgressBarComponent],
  templateUrl: './upload-toast.html',
  styleUrl: './upload-toast.scss',
})
export class UploadToast {
  private readonly uploads = inject(UploadService);

  protected readonly tasks = this.uploads.tasks;

  protected readonly total = computed(() => this.tasks().length);
  protected readonly settled = computed(
    () => this.tasks().filter((t) => UploadService.isSettled(t)).length
  );
  protected readonly failed = computed(
    () => this.tasks().filter((t) => t.status === 'error' || t.status === 'aborted').length
  );
  protected readonly succeeded = computed(
    () => this.tasks().filter((t) => t.status === 'done').length
  );

  protected readonly finished = computed(() => this.total() > 0 && this.settled() === this.total());

  /** 1-based position of the file currently in flight, e.g. the "2" in "2 of 6". */
  protected readonly current = computed(() => Math.min(this.settled() + 1, this.total()));

  /** Name of the file in flight, or the last one to settle once the batch is done. */
  protected readonly currentName = computed(() => {
    const tasks = this.tasks();
    const active = tasks.find((t) => !UploadService.isSettled(t));
    return (active ?? tasks[tasks.length - 1])?.fileName ?? '';
  });

  protected readonly failedNames = computed(() =>
    this.tasks()
      .filter((t) => t.status === 'error' || t.status === 'aborted')
      .map((t) => t.fileName)
  );

  /** Overall byte progress across the batch, so the bar reflects the whole job. */
  protected readonly percent = computed(() => {
    const tasks = this.tasks();
    const total = tasks.reduce((sum, t) => sum + t.totalBytes, 0);
    if (!total) return 0;
    const done = tasks.reduce((sum, t) => sum + t.uploadedBytes, 0);
    return Math.min(100, Math.round((done / total) * 100));
  });

  protected readonly title = computed(() => {
    const total = this.total();
    if (!this.finished()) {
      return total > 1 ? `Uploading ${this.current()} of ${total}` : 'Uploading';
    }
    const failed = this.failed();
    if (!failed) return total > 1 ? `${total} files uploaded` : 'Upload complete';
    if (failed === total) return total > 1 ? `All ${total} uploads failed` : 'Upload failed';
    return `${this.succeeded()} of ${total} uploaded · ${failed} failed`;
  });

  constructor() {
    // Only a clean batch self-dismisses. The cleanup cancels the timer if a new upload starts
    // (or the user closes it first), so a fresh batch never inherits the old countdown.
    effect((onCleanup) => {
      if (!this.finished() || this.failed() > 0) return;
      const timer = setTimeout(() => this.uploads.clear(), AUTO_DISMISS_MS);
      onCleanup(() => clearTimeout(timer));
    });
  }

  protected dismiss(): void {
    this.uploads.clear();
  }
}
