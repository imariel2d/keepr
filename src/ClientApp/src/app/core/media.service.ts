import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DownloadUrlResponse, MediaListItem, Usage } from './models';

@Injectable({ providedIn: 'root' })
export class MediaService {
  private readonly http = inject(HttpClient);

  list(): Promise<MediaListItem[]> {
    return firstValueFrom(this.http.get<MediaListItem[]>('/api/media'));
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

  delete(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/media/${id}`));
  }
}
