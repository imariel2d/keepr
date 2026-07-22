import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { FolderService } from '../../core/folder.service';
import { MediaService } from '../../core/media.service';
import { UploadService } from '../../core/upload.service';
import { UsageStore } from '../../core/usage.store';
import { BytesPipe } from '../../core/bytes.pipe';
import { formatDate, fileTypeOf } from '../../core/file-type';
import { DragPayload, FolderContents, FolderItem, MediaListItem, UploadTask } from '../../core/models';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { ModalComponent } from '../../cove/lib/modal/modal.component';
import { ProgressBarComponent } from '../../cove/lib/progress-bar/progress-bar.component';
import { ContextMenuComponent, ContextMenuItem } from '../../cove/lib/context-menu/context-menu.component';
import { FileCardComponent } from '../../cove/lib/files/file-card.component';
import { FolderCardComponent } from '../../cove/lib/files/folder-card.component';
import { FileType } from '../../cove/lib/files/file-type-meta';
import { MoveDialog } from './move-dialog';

type Tone = 'accent' | 'success' | 'warning' | 'danger';

/** Marks a drag as ours, so an OS file-drop and an internal move are never confused. */
const DRAG_TYPE = 'application/x-keepr-item';

@Component({
  selector: 'app-files',
  imports: [
    BytesPipe,
    ButtonComponent,
    IconComponent,
    InputComponent,
    ModalComponent,
    ProgressBarComponent,
    ContextMenuComponent,
    FileCardComponent,
    FolderCardComponent,
    MoveDialog,
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

  protected readonly moveOpen = signal(false);
  protected readonly moveTarget = signal<DragPayload | null>(null);

  protected readonly confirmOpen = signal(false);
  protected readonly confirmTarget = signal<DragPayload | null>(null);

  protected readonly folders = computed(() => this.contents()?.folders ?? []);
  protected readonly files = computed(() => this.contents()?.files ?? []);
  protected readonly breadcrumbs = computed(() => this.contents()?.breadcrumbs ?? []);
  protected readonly isEmpty = computed(() => !this.folders().length && !this.files().length);

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
    this.moveTarget.set(target);
    this.moveOpen.set(true);
    void this.moveDialog()?.reset();
  }

  protected async onMoveConfirmed(destination: string | null): Promise<void> {
    const target = this.moveTarget();
    this.moveOpen.set(false);
    if (target) await this.moveItem(target, destination);
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
    this.confirmTarget.set(target);
    this.confirmOpen.set(true);
  }

  protected async confirmDelete(): Promise<void> {
    const target = this.confirmTarget();
    this.confirmOpen.set(false);
    if (!target) return;
    try {
      if (target.kind === 'folder') await this.folderApi.delete(target.id);
      else await this.media.delete(target.id);
      await this.refresh();
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not delete that item.'));
    }
  }

  // ---- download -----------------------------------------------------------

  protected async download(item: MediaListItem): Promise<void> {
    try {
      window.open(await this.media.downloadUrl(item.id), '_blank');
    } catch (e) {
      this.error.set(this.messageOf(e, 'Could not get a download link.'));
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

  protected fileMenu(event: MouseEvent, f: MediaListItem): void {
    const payload: DragPayload = { kind: 'file', id: f.id, name: f.originalName };
    this.showMenu(event, [
      { label: 'Download', icon: 'download', onSelect: () => void this.download(f) },
      { label: 'Rename', icon: 'edit-2', onSelect: () => this.openRename(payload) },
      { label: 'Move to…', icon: 'move', onSelect: () => this.openMove(payload) },
      { divider: true },
      { label: 'Delete', icon: 'trash-2', danger: true, onSelect: () => this.askDelete(payload) },
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

  protected onFolderDragOver(event: DragEvent, folderId: string): void {
    if (!this.isInternal(event)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    this.dropTarget.set(folderId);
  }

  protected onFolderDragLeave(folderId: string): void {
    if (this.dropTarget() === folderId) this.dropTarget.set(null);
  }

  protected async onFolderDrop(event: DragEvent, folderId: string): Promise<void> {
    if (!this.isInternal(event)) return;
    event.preventDefault();
    event.stopPropagation();
    this.dropTarget.set(null);
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

  protected onDragOver(event: DragEvent): void {
    if (this.isInternal(event)) return; // a card being moved, not an incoming upload
    event.preventDefault();
    this.draggingFiles.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.draggingFiles.set(false);
  }

  protected async onDrop(event: DragEvent): Promise<void> {
    if (this.isInternal(event)) return;
    event.preventDefault();
    this.draggingFiles.set(false);
    await this.uploadAll(Array.from(event.dataTransfer?.files ?? []));
  }

  private async uploadAll(files: File[]): Promise<void> {
    for (const file of files) {
      try {
        // Uploads land in the folder being viewed, atomically — no upload-then-move dance.
        await this.uploads.upload(file, this.folderId());
      } catch {
        /* the task row carries its own error; keep going with the rest */
      }
    }
    if (files.length) await this.refresh();
  }

  // ---- presentation -------------------------------------------------------

  protected fileType(contentType: string | null): FileType {
    return fileTypeOf(contentType);
  }

  protected formatDate(iso: string): string {
    return formatDate(iso);
  }

  protected taskTone(task: UploadTask): Tone {
    if (task.status === 'error' || task.status === 'aborted') return 'danger';
    if (task.status === 'done') return 'success';
    return 'accent';
  }

  protected taskPercent(task: UploadTask): number {
    return task.totalBytes ? Math.round((task.uploadedBytes / task.totalBytes) * 100) : 0;
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
