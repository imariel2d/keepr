import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DownloadUrlResponse, MediaListItem, Usage } from './models';

@Injectable({ providedIn: 'root' })
export class MediaService {
  private readonly http = inject(HttpClient);

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

  async downloadUrl(id: string): Promise<string> {
    const res = await firstValueFrom(
      this.http.get<DownloadUrlResponse>(`/api/media/${id}/download-url`)
    );
    return res.url;
  }

  /** The stored name may be suffixed on collision — use the response, not the requested name. */
  rename(id: string, originalName: string): Promise<MediaListItem> {
    return firstValueFrom(
      this.http.patch<MediaListItem>(`/api/media/${id}`, { originalName })
    );
  }

  /** `folderId: null` moves the file to the root. May also suffix the name. */
  move(id: string, folderId: string | null): Promise<MediaListItem> {
    return firstValueFrom(
      this.http.post<MediaListItem>(`/api/media/${id}/move`, { folderId })
    );
  }

  /** Moves to the trash; recoverable for 10 days. Quota is not freed until purge. */
  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/media/${id}`));
  }
}
