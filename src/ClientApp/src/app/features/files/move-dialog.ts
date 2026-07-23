import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { FolderService } from '../../core/folder.service';
import { Breadcrumb, DragPayload, FolderItem } from '../../core/models';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { ModalComponent } from '../../cove/lib/modal/modal.component';

/**
 * "Move to…" destination picker. Browses the tree one level at a time with the same
 * `contents` endpoint the main view uses, so there is no separate tree API to maintain.
 *
 * A folder cannot be moved inside itself; the server rejects that with 409, but the entry is
 * also disabled here so the user never walks into a dead end.
 */
@Component({
  selector: 'app-move-dialog',
  imports: [ModalComponent, ButtonComponent, IconComponent],
  template: `
    <cove-modal [open]="open" [title]="title()" [width]="520" (close)="cancel.emit()">
      <div class="picker">
        <div class="trail">
          <button type="button" class="crumb" (click)="browse(null)">
            <cove-icon name="hard-drive" [size]="14" />
            <span>My Files</span>
          </button>
          @for (c of trail(); track c.id) {
            <cove-icon name="chevron-right" [size]="13" color="var(--text-tertiary)" />
            <button type="button" class="crumb" (click)="browse(c.id)">{{ c.name }}</button>
          }
        </div>

        <div class="list">
          @if (loading()) {
            <p class="muted">Loading…</p>
          } @else if (folders().length === 0) {
            <p class="muted">No subfolders here.</p>
          } @else {
            @for (f of folders(); track f.id) {
              <button
                type="button"
                class="row"
                [disabled]="isSelf(f)"
                [title]="isSelf(f) ? 'A folder cannot be moved into itself' : ''"
                (click)="browse(f.id)">
                <cove-icon name="folder" [size]="18" color="var(--teal-500)" />
                <span class="rname">{{ f.name }}</span>
                @if (!isSelf(f)) { <cove-icon name="chevron-right" [size]="15" color="var(--text-tertiary)" /> }
              </button>
            }
          }
        </div>

        <p class="dest">
          Moving to <strong>{{ destinationName() }}</strong>
          @if (isCurrent()) { <span class="muted"> — already here</span> }
        </p>
      </div>

      <div class="foot">
        <cove-button variant="ghost" (click)="cancel.emit()">Cancel</cove-button>
        <cove-button variant="primary" [disabled]="isCurrent()" (click)="confirm.emit(here())">Move here</cove-button>
      </div>
    </cove-modal>
  `,
  styleUrl: './move-dialog.scss',
})
export class MoveDialog {
  private readonly folderApi = inject(FolderService);

  @Input() open = false;
  /** The items being moved — one card, or a whole selection. A folder can't go inside itself. */
  @Input() items: DragPayload[] = [];
  /** Where the items live now, so "Move here" can be disabled for a no-op. */
  @Input() sourceFolderId: string | null = null;

  @Output() confirm = new EventEmitter<string | null>();
  @Output() cancel = new EventEmitter<void>();

  protected readonly here = signal<string | null>(null);
  protected readonly trail = signal<Breadcrumb[]>([]);
  protected readonly folders = signal<FolderItem[]>([]);
  protected readonly loading = signal(false);

  /** Called by the parent each time the dialog is opened, to reset to the root. */
  async reset(): Promise<void> {
    await this.browse(null);
  }

  protected async browse(folderId: string | null): Promise<void> {
    this.loading.set(true);
    try {
      const contents = await this.folderApi.contents(folderId);
      this.here.set(folderId);
      this.trail.set(contents.breadcrumbs);
      this.folders.set(contents.folders);
    } finally {
      this.loading.set(false);
    }
  }

  protected title(): string {
    if (this.items.length === 1) return `Move ${this.items[0].name}`;
    return `Move ${this.items.length} items`;
  }

  protected isSelf(f: FolderItem): boolean {
    return this.items.some((i) => i.kind === 'folder' && i.id === f.id);
  }

  protected isCurrent(): boolean {
    return this.here() === this.sourceFolderId;
  }

  protected destinationName(): string {
    const t = this.trail();
    return t.length ? t[t.length - 1].name : 'My Files';
  }
}
