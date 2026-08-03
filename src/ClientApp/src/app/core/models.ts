// API contract types. Mirror the DTOs in src/Api/Features/*.
// Full contract + behaviour notes: docs/api-changes-frontend.md.

export type Role = 'User' | 'Admin';

/** Who the caller is. Carries no token: the session is an HttpOnly cookie the client can't read.
 *  `role` gates admin-only UI; the server still enforces every /api/admin call. */
export interface SessionResponse {
  email: string;
  role: Role;
}

/** One account in the admin users table. `activeSessions` = live (not revoked, not expired).
 *  `pending` = invited but not yet claimed (no password) — can't sign in; its invite can be resent. */
export interface AdminUserListItem {
  id: string;
  email: string;
  role: Role;
  quotaBytes: number;
  usedBytes: number;
  remainingBytes: number;
  createdAt: string;
  activeSessions: number;
  pending: boolean;
}

/** One account in full (admin drawer / after a create or role change). Adds `trashedBytes`. */
export interface AdminUserDetail extends AdminUserListItem {
  trashedBytes: number;
}

/** Create an account (#36). `sendInvite` true → email a claim link (needs a configured mailer);
 *  false → the account is usable now with `password`. */
export interface CreateUserRequest {
  email: string;
  role: Role;
  sendInvite: boolean;
  password?: string;
}

/** Result of creating an account. `inviteEmailSent` is false for direct-password accounts and for
 *  invites whose email failed to send (the account still exists — it can be resent). */
export interface CreateUserResponse {
  account: AdminUserDetail;
  invited: boolean;
  inviteEmailSent: boolean;
}

/** The outbound-email provider (#36). 'none' = no hosted provider configured (an env SMTP fallback
 *  may still apply). See docs/feature-36-email-providers.md §2. */
export type EmailProvider = 'none' | 'resend' | 'brevo' | 'mailgun';

/** App-wide email settings as the admin screen sees them. The API key is write-only, so the read
 *  side carries only `hasApiKey`, never the key. `lastTest*` is the most recent test-send result. */
export interface EmailSettingsResponse {
  provider: EmailProvider;
  fromAddress: string;
  fromName: string;
  hasApiKey: boolean;
  mailgunDomain: string | null;
  mailgunRegion: string | null;
  publicBaseUrl: string;
  inviteExpiryDays: number;
  lastTestAt: string | null;
  lastTestOk: boolean | null;
  lastTestError: string | null;
}

/** Save the email settings. `apiKey` is write-only: blank/omitted keeps the stored key only when the
 *  provider is unchanged; changing provider requires a new key; 'none' clears it. */
export interface UpdateEmailSettingsRequest {
  provider: EmailProvider;
  fromAddress?: string;
  fromName?: string;
  apiKey?: string;
  mailgunDomain?: string;
  mailgunRegion?: string;
  publicBaseUrl?: string;
  inviteExpiryDays?: number;
}

/** Result of a test send: whether it went out, and a fixed safe category if not (never raw text). */
export interface EmailTestResult {
  ok: boolean;
  error: string | null;
}

/** A page of results plus the total, so the table can show "1–50 of 213". */
export interface PagedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** The signed-in account's profile (#29). `mustChangePassword` forces the set-password step. */
export interface ProfileResponse {
  email: string;
  firstName: string | null;
  lastName: string | null;
  role: Role;
  mustChangePassword: boolean;
}

/** The invited email behind a claim token, to prime the claim form. */
export interface InvitePreview {
  email: string;
}

/** Whether the deployment offers self-service password reset (a mail provider is configured). A
 *  global fact — never per-account — so the login screen can safely choose between a "Forgot
 *  password?" link and "contact your admin" copy. See docs/feature-26-password-reset.md §7. */
export interface ResetCapabilities {
  selfServiceReset: boolean;
}

/** The address a reset link is for, to prime the reset form. 410 if unknown/expired/used. */
export interface ResetPreview {
  email: string;
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

  /**
   * Search-only: the containing folder's path as ancestor names, root first (empty at the root).
   * Undefined outside a search result — the browse listing doesn't carry it.
   */
  location?: string[];
}

export interface DownloadUrlResponse {
  url: string;
  /** When the signature stops working, so clients can cache the URL until then. */
  expiresAt: string;
}

/** Public viewer metadata for a shared file (GET /api/share/{token}). No owner/internal ids. */
export interface SharePublicResponse {
  fileName: string;
  contentType: string | null;
  sizeBytes: number;
  previewKind: PreviewKind | null;
  /** null when the link never expires. */
  expiresAt: string | null;
}

/** A newly created share link. `url` is shown once — the server stores only the token's hash. */
export interface CreatedShareResponse {
  linkId: string;
  url: string;
  /** null when the link never expires. */
  expiresAt: string | null;
}

/** A share link for management, including the URL so an active link can be re-copied. */
export interface ShareLinkResponse {
  linkId: string;
  url: string;
  createdAt: string;
  /** null when the link never expires. */
  expiresAt: string | null;
  revoked: boolean;
  lastAccessedAt: string | null;
}

export interface FolderItem {
  id: string;
  name: string;
  /** null = top level. */
  parentId: string | null;
  createdAt: string;
  updatedAt: string;

  /**
   * Search-only: the parent folder's path as ancestor names, root first (empty at the root).
   * Undefined outside a search result.
   */
  location?: string[];
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

/**
 * Results of a name search across the whole tree (GET /api/search). Every item carries a
 * `location` (its containing folder's path) since results span folders.
 */
export interface SearchResults {
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
  /** `queued` exists so a batch can show "2 of 6" from the first frame, before file 2 starts. */
  status: 'queued' | 'uploading' | 'completing' | 'done' | 'error' | 'aborted';
  error?: string;
}

/** What a drag is carrying, so a drop target knows which move endpoint to call. */
export interface DragPayload {
  kind: 'file' | 'folder';
  id: string;
  name: string;
}
