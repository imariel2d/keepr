import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DownloadUrlResponse, MediaListItem, Usage } from './models';

type Disposition = 'inline' | 'attachment';

/**
 * Stop trusting a presigned URL slightly before it actually expires, so a request that starts
 * just under the wire doesn't fail in flight.
 */
const EXPIRY_SAFETY_MS = 60_000;

@Injectable({ providedIn: 'root' })
export class MediaService {
  private readonly http = inject(HttpClient);

  /**
   * Presigned URLs, keyed by `${id}:${disposition}`.
   *
   * Two things this buys, both of which show up in practice:
   *  - paging back and forth in the preview overlay re-requested a URL for a file that already
   *    had a perfectly good one;
   *  - a grid thumbnail and the preview of the same image ask for the identical inline URL, so
   *    opening an already-thumbnailed image needs no request at all.
   *
   * The *promise* is cached rather than the resolved value, so callers arriving while a request
   * is in flight share it instead of each firing their own — which is what happens when you hold
   * down an arrow key.
   */
  private readonly urlCache = new Map<string, Promise<DownloadUrlResponse>>();

  /**
   * Every file the user owns, from any folder — for an "all files" or search view. To list one
   * folder's files, prefer FolderService.contents(), which returns subfolders in the same call.
   */
  listAll(): Promise<MediaListItem[]> {
    return firstValueFrom(this.http.get<MediaListItem[]>('/api/media'));
  }

  /** Files in one folder; null lists the root. */
  listIn(folderId: string | null): Promise<MediaListItem[]> {
    let params = new HttpParams().set('scoped', true);
    if (folderId) params = params.set('folderId', folderId);
    return firstValueFrom(this.http.get<MediaListItem[]>('/api/media', { params }));
  }

  usage(): Promise<Usage> {
    return firstValueFrom(this.http.get<Usage>('/api/me/usage'));
  }

  /**
   * Short-lived URL that makes the browser *save* the file under its real name. The storage key
   * is an opaque UUID, so the correct filename only exists because the server sets
   * Content-Disposition on the presigned URL.
   */
  downloadUrl(id: string): Promise<string> {
    return this.presignedUrl(id, 'attachment');
  }

  /**
   * Short-lived URL that renders in the page. 415 if the type isn't on the server's preview
   * allowlist — check `previewKind` first rather than relying on the error.
   */
  previewUrl(id: string): Promise<string> {
    return this.presignedUrl(id, 'inline');
  }

  /**
   * Forget any cached URLs for a file.
   *
   * Needed after a rename or move: the URL itself stays valid, because the storage key never
   * changes, but the filename baked into its Content-Disposition would still be the old one — so
   * a download would quietly save under the previous name.
   */
  invalidateUrls(id: string): void {
    this.urlCache.delete(`${id}:inline`);
    this.urlCache.delete(`${id}:attachment`);
  }

  private async presignedUrl(id: string, disposition: Disposition): Promise<string> {
    const key = `${id}:${disposition}`;

    const cached = this.urlCache.get(key);
    if (cached) {
      try {
        const hit = await cached;
        if (this.stillUsable(hit)) return hit.url;
      } catch {
        // A failed request must not poison the cache; fall through and ask again.
      }
      this.urlCache.delete(key);
    }

    const pending = firstValueFrom(
      this.http.get<DownloadUrlResponse>(`/api/media/${id}/download-url`, {
        params: { disposition },
      })
    );
    this.urlCache.set(key, pending);

    try {
      return (await pending).url;
    } catch (e) {
      this.urlCache.delete(key);
      throw e;
    }
  }

  private stillUsable(res: DownloadUrlResponse): boolean {
    const expiry = new Date(res.expiresAt).getTime();
    return Number.isFinite(expiry) && expiry - EXPIRY_SAFETY_MS > Date.now();
  }

  /** The stored name may be suffixed on collision — use the response, not the requested name. */
  async rename(id: string, originalName: string): Promise<MediaListItem> {
    const item = await firstValueFrom(
      this.http.patch<MediaListItem>(`/api/media/${id}`, { originalName })
    );
    this.invalidateUrls(id);
    return item;
  }

  /** `folderId: null` moves the file to the root. May also suffix the name. */
  async move(id: string, folderId: string | null): Promise<MediaListItem> {
    const item = await firstValueFrom(
      this.http.post<MediaListItem>(`/api/media/${id}/move`, { folderId })
    );
    this.invalidateUrls(id);
    return item;
  }

  /** Moves to the trash; recoverable for 10 days. Quota is not freed until purge. */
  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`/api/media/${id}`));
    this.invalidateUrls(id);
  }
}
