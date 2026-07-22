// API contract types. Mirror the DTOs in src/Api/Features/*.
// Full contract + behaviour notes: docs/api-changes-frontend.md.

export interface AuthResponse {
  accessToken: string;
}

export interface Usage {
  quotaBytes: number;
  usedBytes: number;
  remainingBytes: number;
  /** Part of usedBytes held by trashed files. Freed only when the trash is purged. */
  trashedBytes: number;
}

export interface InitUploadResponse {
  mediaId: string;
  key: string;
  uploadId: string;
  partSize: number;
  /**
   * The name the server actually stored, which differs from the requested one when the folder
   * already held that name (`report.pdf` -> `report (2).pdf`). Display this, never File.name.
   */
  originalName: string;
  folderId: string | null;
}

export interface PartUrlResponse {
  partNumber: number;
  url: string;
}

export interface CompletePart {
  partNumber: number;
  eTag: string;
}

/** How the browser may render a file inline. Decided server-side by an allowlist. */
export type PreviewKind = 'image' | 'pdf' | 'video' | 'audio';

export interface MediaListItem {
  id: string;
  originalName: string;
  contentType: string | null;
  sizeBytes: number;
  /** null = the owner's root, not a missing value. */
  folderId: string | null;
  createdAt: string;
  /**
   * null means download-only. Never infer this from contentType on the client: a file's stored
   * type is whatever the uploader declared when magic-byte sniffing came up empty, so the
   * allowlist lives on the server.
   */
  previewKind: PreviewKind | null;
}

export interface DownloadUrlResponse {
  url: string;
  /** When the signature stops working, so clients can cache the URL until then. */
  expiresAt: string;
}

export interface FolderItem {
  id: string;
  name: string;
  /** null = top level. */
  parentId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface Breadcrumb {
  id: string;
  name: string;
}

/** One folder's contents. `folder` is null and `breadcrumbs` empty at the root. */
export interface FolderContents {
  folder: FolderItem | null;
  breadcrumbs: Breadcrumb[];
  folders: FolderItem[];
  files: MediaListItem[];
}

export type TrashKind = 'file' | 'folder';

export interface TrashItem {
  id: string;
  kind: TrashKind;
  name: string;
  /** For a folder, the total bytes held inside it. */
  sizeBytes: number;
  deletedAt: string;
  /** Server-computed purge deadline — don't derive it from a retention constant here. */
  purgesAt: string;
}

export interface RestoreResponse {
  id: string;
  /** May differ from the deleted name if something took it while the item was in the trash. */
  name: string;
}

/** Client-side view of an in-progress or finished upload. */
export interface UploadTask {
  id: string;
  fileName: string;
  totalBytes: number;
  uploadedBytes: number;
  status: 'uploading' | 'completing' | 'done' | 'error' | 'aborted';
  error?: string;
}

/** What a drag is carrying, so a drop target knows which move endpoint to call. */
export interface DragPayload {
  kind: 'file' | 'folder';
  id: string;
  name: string;
}
