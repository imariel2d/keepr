import { Component, HostListener, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { FolderService } from '../../core/folder.service';
import { MediaService } from '../../core/media.service';
import { UploadService } from '../../core/upload.service';
import { UsageStore } from '../../core/usage.store';
import { BytesPipe } from '../../core/bytes.pipe';
import { formatDate, fileTypeOf } from '../../core/file-type';
import { DragPayload, FolderContents, FolderItem, MediaListItem } from '../../core/models';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { ModalComponent } from '../../cove/lib/modal/modal.component';
import { ContextMenuComponent, ContextMenuItem } from '../../cove/lib/context-menu/context-menu.component';
import { FileCardComponent } from '../../cove/lib/files/file-card.component';
import { FolderCardComponent } from '../../cove/lib/files/folder-card.component';
import { FileType } from '../../cove/lib/files/file-type-meta';
import { MoveDialog } from './move-dialog';
import { PreviewOverlay } from './preview-overlay';
import { InViewDirective } from '../../core/in-view.directive';
import { saveFile } from '../../core/save-file';

/** Marks a drag as ours, so an OS file-drop and an internal move are never confused. */
const DRAG_TYPE = 'application/x-keepr-item';

/**
 * Only images below this size double as their own grid thumbnail. Above it the type icon is
 * kept: a folder of phone photos would otherwise pull tens of megabytes to paint 190px cards.
 * Proper derivatives are feature #16.
 */
const THUMBNAIL_MAX_BYTES = 500 * 1024;

@Component({
  selector: 'app-files',
  imports: [
    BytesPipe,
    ButtonComponent,
    IconComponent,
    InputComponent,
    ModalComponent,
    ContextMenuComponent,
    FileCardComponent,
    FolderCardComponent,
    MoveDialog,
    PreviewOverlay,
    InViewDirective,
  ],
  templateUrl: './files.html',
  styleUrl: './files.scss',
})
export class Files {
  private readonly folderApi = inject(FolderService);
  private readonly media = inject(MediaService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly uploads = inject(UploadService);
  protected readonly usage = inject(UsageStore);

  private readonly moveDialog = viewChild(MoveDialog);

  /** Current folder from the URL; null at the root. */
  protected readonly folderId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('folderId'))),
    { initialValue: null }
  );

  protected readonly contents = signal<FolderContents | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  /** Transient banner, e.g. when the server stored a different name than we asked for. */
  protected readonly notice = signal<string | null>(null);

  protected readonly draggingFiles = signal(false);
  protected readonly dropTarget = signal<string | null>(null);

  protected readonly menuOpen = signal(false);
  protected readonly menuX = signal(0);
  protected readonly menuY = signal(0);
  protected readonly menuItems = signal<ContextMenuItem[]>([]);

  protected readonly createOpen = signal(false);
  protected readonly createName = signal('New folder');

  protected readonly renameOpen = signal(false);
  protected readonly renameTarget = signal<DragPayload | null>(null);
  protected readonly renameValue = signal('');

  // Both dialogs take a list: one entry when acting on a single card, many when acting on a
  // checkbox selection. Keeping one code path means bulk and single can never drift apart.
  protected readonly moveOpen = signal(false);
  protected readonly moveTargets = signal<DragPayload[]>([]);

  protected readonly confirmOpen = signal(false);
  protected readonly confirmTargets = signal<DragPayload[]>([]);

  /** Ids of checkbox-selected files in the current folder. */
  protected readonly selectedIds = signal<ReadonlySet<string>>(new Set());

  protected readonly previewOpen = signal(false);
  protected readonly previewIndex = signal(0);
  /** Presigned thumbnail URLs, keyed by media id; filled lazily as cards scroll into view. */
  protected readonly thumbnails = signal<Record<string, string>>({});

  protected readonly folders = computed(() => this.contents()?.folders ?? []);
  protected readonly files = computed(() => this.contents()?.files ?? []);
  protected readonly breadcrumbs = computed(() => this.contents()?.breadcrumbs ?? []);
  protected readonly isEmpty = computed(() => !this.folders().length && !this.files().length);

  /** Files the server says can be rendered; the overlay pages through exactly these. */
  protected readonly previewable = computed(() => this.files().filter((f) => f.previewKind !== null));

  protected readonly selectedCount = computed(() => this.selectedIds().size);
  protected readonly allSelected = computed(
    () => this.files().length > 0 && this.selectedCount() === this.files().length
  );

  /** The single target when deleting one card, or null when confirming a whole selection. */
  protected readonly confirmSingle = computed(() =>
    this.confirmTargets().length === 1 ? this.confirmTargets()[0] : null
  );

  protected readonly confirmTitle = computed(() => {
    const targets = this.confirmTargets();
    return targets.length === 1 ? `Delete ${targets[0].name}?` : `Delete ${targets.length} items?`;
  });

  /** Selection resolved against the current listing, in display order. */
  protected readonly selectedFiles = computed(() =>
    this.files().filter((f) => this.selectedIds().has(f.id))
  );

  private selectedPayloads(): DragPayload[] {
    return this.selectedFiles().map((f) => ({ kind: 'file', id: f.id, name: f.originalName }));
  }

  // ---- selection ----------------------------------------------------------

  protected isSelected(id: string): boolean {
    return this.selectedIds().has(id);
  }

  protected toggleSelect(id: string): void {
    this.selectedIds.update((current) => {
      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  protected selectAll(): void {
    this.selectedIds.set(new Set(this.files().map((f) => f.id)));
  }

  protected clearSelection(): void {
    this.selectedIds.set(new Set());
  }

  /**
   * Click on the card body. Once anything is selected the grid is in selection mode, so a plain
   * click picks files rather than doing nothing — reaching for the small checkbox every time is
   * the behaviour this replaces.
   *
   * Shift and Ctrl/Cmd act on the clicked file alone: deliberately not a range select, so a
   * modifier can never sweep in files the user did not click.
   */
  protected onCardClick(event: MouseEvent, file: MediaListItem): void {
    if (event.shiftKey || event.ctrlKey || event.metaKey) {
      event.preventDefault();
      this.toggleSelect(file.id);
      return;
    }
    // Outside selection mode a single click is inert; double-click still opens.
    if (this.selectedCount() > 0) this.toggleSelect(file.id);
  }

  constructor() {
    // Re-fetch whenever the route id changes, including back/forward navigation.
    this.route.paramMap.subscribe(() => void this.refresh());
  }

  // ---- navigation ---------------------------------------------------------

  protected openFolder(f: FolderItem): void {
    void this.router.navigate(['/files', f.id]);
  }

  protected navigateTo(folderId: string | null): void {
    void this.router.navigate(folderId ? ['/files', folderId] : ['/files']);
  }

  // ---- data ---------------------------------------------------------------

  private async refresh(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.thumbnails.set({});
      // Ids are folder-scoped; carrying them across a reload would keep phantom items selected.
      this.clearSelection();
      this.contents.set(await this.folderApi.contents(this.folderId()));
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not load this folder.'));
      this.contents.set(null);
    } finally {
      this.loading.set(false);
    }
    void this.usage.refresh();
  }

  // ---- create / rename ----------------------------------------------------

  protected openCreate(): void {
    this.createName.set('New folder');
    this.createOpen.set(true);
  }

  protected async submitCreate(): Promise<void> {
    const name = this.createName().trim();
    if (!name) return;
    this.createOpen.set(false);
    try {
      const created = await this.folderApi.create(name, this.folderId());
      // The server auto-suffixes a colliding name rather than failing, so report what it chose.
      if (created.name !== name) this.flash(`A folder named "${name}" already existed — created "${created.name}".`);
      await this.refresh();
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not create the folder.'));
    }
  }

  protected openRename(target: DragPayload): void {
    this.renameTarget.set(target);
    this.renameValue.set(target.name);
    this.renameOpen.set(true);
  }

  protected async submitRename(): Promise<void> {
    const target = this.renameTarget();
    const name = this.renameValue().trim();
    if (!target || !name) return;
    this.renameOpen.set(false);
    try {
      const stored =
        target.kind === 'folder'
          ? (await this.folderApi.rename(target.id, name)).name
          : (await this.media.rename(target.id, name)).originalName;
      if (stored !== name) this.flash(`"${name}" was taken — saved as "${stored}".`);
      await this.refresh();
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not rename.'));
    }
  }

  // ---- move ---------------------------------------------------------------

  protected openMove(target: DragPayload): void {
    this.moveTargets.set([target]);
    this.moveOpen.set(true);
    void this.moveDialog()?.reset();
  }

  protected openMoveSelected(): void {
    if (!this.selectedCount()) return;
    this.moveTargets.set(this.selectedPayloads());
    this.moveOpen.set(true);
    void this.moveDialog()?.reset();
  }

  protected async onMoveConfirmed(destination: string | null): Promise<void> {
    const targets = this.moveTargets();
    this.moveOpen.set(false);
    if (!targets.length) return;

    if (targets.length === 1) {
      await this.moveItem(targets[0], destination);
      return;
    }

    const failed = await this.forEachTarget(targets, (t) =>
      t.kind === 'folder' ? this.folderApi.move(t.id, destination) : this.media.move(t.id, destination)
    );

    await this.refresh();
    this.reportFailures(failed, targets.length, 'move');
  }

  // ---- bulk download ------------------------------------------------------

  /**
   * One presigned save per file, sequentially. There is no server-side zip yet, so a large
   * selection means a burst of downloads — browsers commonly prompt once for the batch.
   */
  protected async downloadSelected(): Promise<void> {
    const files = this.selectedFiles();
    if (!files.length) return;
    let failed = 0;
    for (const file of files) {
      try {
        saveFile(await this.media.downloadUrl(file.id));
      } catch {
        failed++;
      }
    }
    if (failed) {
      this.error.set(
        `Could not get a download link for ${failed} of ${files.length} ${this.plural(files.length, 'file')}.`
      );
    }
  }

  // ---- bulk plumbing ------------------------------------------------------

  /** Runs an operation over every target, collecting rather than aborting on the first failure. */
  private async forEachTarget(
    targets: DragPayload[],
    op: (target: DragPayload) => Promise<unknown>
  ): Promise<DragPayload[]> {
    const failed: DragPayload[] = [];
    for (const target of targets) {
      try {
        await op(target);
      } catch {
        failed.push(target);
      }
    }
    return failed;
  }

  private reportFailures(failed: DragPayload[], total: number, verb: string): void {
    if (!failed.length) return;
    const names = failed.map((f) => `"${f.name}"`).join(', ');
    this.error.set(
      failed.length === total
        ? `Could not ${verb} ${names}.`
        : `Could not ${verb} ${failed.length} of ${total} items: ${names}.`
    );
  }

  protected plural(n: number, word: string): string {
    return n === 1 ? word : `${word}s`;
  }

  /** Shared by the picker and drag-and-drop. */
  private async moveItem(item: DragPayload, destination: string | null): Promise<void> {
    try {
      const stored =
        item.kind === 'folder'
          ? (await this.folderApi.move(item.id, destination)).name
          : (await this.media.move(item.id, destination)).originalName;
      if (stored !== item.name) this.flash(`Moved — renamed to "${stored}" to avoid a clash.`);
      await this.refresh();
    } catch (e) {
      // 409 here is the "into its own subfolder" case; the server message says so plainly.
      this.error.set(this.messageOf(e, 'Could not move that item.'));
    }
  }

  // ---- delete -------------------------------------------------------------

  protected askDelete(target: DragPayload): void {
    this.confirmTargets.set([target]);
    this.confirmOpen.set(true);
  }

  protected askDeleteSelected(): void {
    if (!this.selectedCount()) return;
    this.confirmTargets.set(this.selectedPayloads());
    this.confirmOpen.set(true);
  }

  protected async confirmDelete(): Promise<void> {
    const targets = this.confirmTargets();
    this.confirmOpen.set(false);
    if (!targets.length) return;

    const failed = await this.forEachTarget(targets, (t) =>
      t.kind === 'folder' ? this.folderApi.delete(t.id) : this.media.delete(t.id)
    );

    await this.refresh();
    this.reportFailures(failed, targets.length, 'delete');
  }

  // ---- download -----------------------------------------------------------

  protected async download(item: MediaListItem): Promise<void> {
    try {
      // The URL carries Content-Disposition: attachment, so this saves under the real filename
      // instead of opening a tab that renders the file.
      saveFile(await this.media.downloadUrl(item.id));
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not get a download link.'));
    }
  }

  // ---- preview ------------------------------------------------------------

  /** Clicking a file previews it when possible, and otherwise falls back to downloading. */
  protected openFile(item: MediaListItem): void {
    // In selection mode the double-click's two clicks have already toggled this card twice
    // (a no-op); opening the previewer on top of that is not what the user is doing.
    if (this.selectedCount() > 0) return;
    if (item.previewKind === null) {
      void this.download(item);
      return;
    }
    const index = this.previewable().findIndex((f) => f.id === item.id);
    this.previewIndex.set(Math.max(0, index));
    this.previewOpen.set(true);
  }

  /**
   * Lazily fetch a thumbnail once a card scrolls near the viewport. Skipped for non-images and
   * for anything over the size cap, which keeps the icon instead.
   */
  protected async onCardInView(item: MediaListItem): Promise<void> {
    if (item.previewKind !== 'image' || item.sizeBytes > THUMBNAIL_MAX_BYTES) return;
    if (this.thumbnails()[item.id]) return;
    try {
      const url = await this.media.previewUrl(item.id);
      this.thumbnails.update((map) => ({ ...map, [item.id]: url }));
    } catch {
      // A missing thumbnail is cosmetic; the card keeps its type icon.
    }
  }

  // ---- context menus ------------------------------------------------------

  protected folderMenu(event: MouseEvent, f: FolderItem): void {
    const payload: DragPayload = { kind: 'folder', id: f.id, name: f.name };
    this.showMenu(event, [
      { label: 'Open', icon: 'folder-open', onSelect: () => this.openFolder(f) },
      { label: 'Rename', icon: 'edit-2', onSelect: () => this.openRename(payload) },
      { label: 'Move to…', icon: 'move', onSelect: () => this.openMove(payload) },
      { divider: true },
      { label: 'Delete', icon: 'trash-2', danger: true, onSelect: () => this.askDelete(payload) },
    ]);
  }

  /**
   * Opened by the ⋮ button and by right-click. Acting on a card that is part of a multi-selection
   * applies to the whole selection — the labels carry the count so the scope is never a guess.
   * Right-clicking a card outside the selection leaves the selection alone and acts on that card.
   */
  protected fileMenu(event: MouseEvent, f: MediaListItem): void {
    const payload: DragPayload = { kind: 'file', id: f.id, name: f.originalName };
    const count = this.selectedCount();
    const bulk = this.isSelected(f.id) && count > 1;
    const suffix = bulk ? ` ${count} ${this.plural(count, 'file')}` : '';

    this.showMenu(event, [
      {
        label: `Download${suffix}`,
        icon: 'download',
        onSelect: () => void (bulk ? this.downloadSelected() : this.download(f)),
      },
      // Renaming is inherently one-at-a-time, so it drops out of a bulk menu.
      ...(bulk ? [] : [{ label: 'Rename', icon: 'edit-2', onSelect: () => this.openRename(payload) }]),
      {
        label: `Move${suffix} to…`,
        icon: 'move',
        onSelect: () => (bulk ? this.openMoveSelected() : this.openMove(payload)),
      },
      { divider: true },
      {
        label: `Delete${suffix}`,
        icon: 'trash-2',
        danger: true,
        onSelect: () => (bulk ? this.askDeleteSelected() : this.askDelete(payload)),
      },
    ]);
  }

  private showMenu(event: MouseEvent, items: ContextMenuItem[]): void {
    this.menuX.set(event.clientX);
    this.menuY.set(event.clientY);
    this.menuItems.set(items);
    this.menuOpen.set(true);
  }

  // ---- drag and drop ------------------------------------------------------

  protected onItemDragStart(event: DragEvent, payload: DragPayload): void {
    event.dataTransfer?.setData(DRAG_TYPE, JSON.stringify(payload));
    event.dataTransfer?.setData('text/plain', payload.name);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  /** True when the drag carries one of our cards rather than files from the desktop. */
  private isInternal(event: DragEvent): boolean {
    return event.dataTransfer?.types.includes(DRAG_TYPE) ?? false;
  }

  /** Folder cards take both kinds of drag: an internal card to move, or OS files to upload in. */
  protected onFolderDragOver(event: DragEvent, folderId: string): void {
    const internal = this.isInternal(event);
    if (!internal && !this.isFileDrag(event)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = internal ? 'move' : 'copy';
    this.dropTarget.set(folderId);
  }

  protected onFolderDragLeave(folderId: string): void {
    if (this.dropTarget() === folderId) this.dropTarget.set(null);
  }

  protected async onFolderDrop(event: DragEvent, folderId: string): Promise<void> {
    const internal = this.isInternal(event);
    if (!internal && !this.isFileDrag(event)) return;
    event.preventDefault();
    // Stop the host handler from also claiming this drop and uploading into the current folder.
    event.stopPropagation();
    this.dropTarget.set(null);

    if (!internal) {
      this.dragDepth = 0;
      this.draggingFiles.set(false);
      await this.uploadAll(Array.from(event.dataTransfer?.files ?? []), folderId);
      return;
    }

    const payload = this.readPayload(event);
    if (!payload) return;
    if (payload.kind === 'folder' && payload.id === folderId) return; // into itself
    await this.moveItem(payload, folderId);
  }

  /** Dropping on a breadcrumb moves the item up to that ancestor (or the root). */
  protected onCrumbDragOver(event: DragEvent, key: string): void {
    if (!this.isInternal(event)) return;
    event.preventDefault();
    this.dropTarget.set(key);
  }

  protected async onCrumbDrop(event: DragEvent, folderId: string | null): Promise<void> {
    if (!this.isInternal(event)) return;
    event.preventDefault();
    this.dropTarget.set(null);
    const payload = this.readPayload(event);
    if (!payload) return;
    if (folderId === this.folderId()) return; // already here
    await this.moveItem(payload, folderId);
  }

  private readPayload(event: DragEvent): DragPayload | null {
    const raw = event.dataTransfer?.getData(DRAG_TYPE);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as DragPayload;
    } catch {
      return null;
    }
  }

  // ---- uploads (OS file drops) -------------------------------------------

  protected onBrowse(): void {
    document.getElementById('file-input')?.click();
  }

  protected async onFilesSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    await this.uploadAll(files);
  }

  /**
   * The whole folder view is the drop target — there is no separate dropzone strip any more.
   *
   * `dragenter`/`dragleave` bubble from every child the cursor crosses, so a naive boolean
   * flickers as you move across cards. Counting enter/leave pairs and only clearing at zero
   * keeps the overlay steady for the whole drag.
   */
  private dragDepth = 0;

  /** True only for a drag carrying OS files, so internal card moves never raise the overlay. */
  private isFileDrag(event: DragEvent): boolean {
    return !this.isInternal(event) && (event.dataTransfer?.types.includes('Files') ?? false);
  }

  @HostListener('dragenter', ['$event'])
  protected onDragEnter(event: DragEvent): void {
    if (!this.isFileDrag(event)) return;
    event.preventDefault();
    this.dragDepth++;
    this.draggingFiles.set(true);
  }

  @HostListener('dragover', ['$event'])
  protected onDragOver(event: DragEvent): void {
    if (!this.isFileDrag(event)) return;
    event.preventDefault(); // without this the browser refuses the drop
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  }

  @HostListener('dragleave', ['$event'])
  protected onDragLeave(event: DragEvent): void {
    if (!this.isFileDrag(event)) return;
    this.dragDepth = Math.max(0, this.dragDepth - 1);
    if (this.dragDepth === 0) this.draggingFiles.set(false);
  }

  @HostListener('drop', ['$event'])
  protected async onDrop(event: DragEvent): Promise<void> {
    if (!this.isFileDrag(event)) return;
    event.preventDefault();
    this.dragDepth = 0;
    this.draggingFiles.set(false);
    await this.uploadAll(Array.from(event.dataTransfer?.files ?? []));
  }

  /**
   * @param destinationId Defaults to the folder being viewed; a drop straight onto a folder card
   * passes that folder instead, so the files land inside it with no upload-then-move dance.
   */
  private async uploadAll(files: File[], destinationId?: string | null): Promise<void> {
    if (!files.length) return;
    const target = destinationId === undefined ? this.folderId() : destinationId;
    // Progress is reported by the global upload toast, not inline here.
    await this.uploads.uploadMany(files, target);
    await this.refresh();
  }

  // ---- presentation -------------------------------------------------------

  protected fileType(contentType: string | null): FileType {
    return fileTypeOf(contentType);
  }

  protected formatDate(iso: string): string {
    return formatDate(iso);
  }

  private flash(message: string): void {
    this.notice.set(message);
    setTimeout(() => this.notice.set(null), 6000);
  }

  /** Errors are problem+json; `detail` carries a message written for end users. */
  private messageOf(e: unknown, fallback: string): string {
    const detail = (e as { error?: { detail?: string } })?.error?.detail;
    return typeof detail === 'string' && detail ? detail : fallback;
  }
}
