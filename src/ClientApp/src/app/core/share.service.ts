import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  CreatedShareResponse,
  DownloadUrlResponse,
  ShareLinkResponse,
  SharePublicResponse,
} from './models';

type Disposition = 'inline' | 'attachment';

/**
 * Shareable links, both sides of the feature:
 *  - the **public** path (resolve a token, mint a presigned URL) is anonymous — the token in the
 *    URL is the whole authorization, so nothing here depends on a session;
 *  - the **owner** path (create/list/edit/revoke) is the authenticated management surface.
 *
 * See docs/shareable-links-design.md.
 */
@Injectable({ providedIn: 'root' })
export class ShareService {
  private readonly http = inject(HttpClient);

  // ---- Public (anonymous) --------------------------------------------------

  /** Resolve a token to render-only metadata. Throws 404 (unknown) or 410 (expired/revoked/gone). */
  resolve(token: string): Promise<SharePublicResponse> {
    return firstValueFrom(this.http.get<SharePublicResponse>(this.base(token)));
  }

  /** Short-lived URL that renders in the page. Only for previewable types. */
  previewUrl(token: string): Promise<string> {
    return this.publicUrl(token, 'inline');
  }

  /** Short-lived URL that saves the file under its real name. */
  downloadUrl(token: string): Promise<string> {
    return this.publicUrl(token, 'attachment');
  }

  private async publicUrl(token: string, disposition: Disposition): Promise<string> {
    const res = await firstValueFrom(
      this.http.get<DownloadUrlResponse>(`${this.base(token)}/download-url`, {
        params: { disposition },
      })
    );
    return res.url;
  }

  private base(token: string): string {
    return `/api/share/${encodeURIComponent(token)}`;
  }

  // ---- Owner (authenticated) -----------------------------------------------

  /** Mint a link for a file. `url` in the response is the one and only chance to copy it. */
  create(fileId: string, expiresInDays: number): Promise<CreatedShareResponse> {
    return firstValueFrom(
      this.http.post<CreatedShareResponse>(`/api/media/${fileId}/share`, { expiresInDays })
    );
  }

  /** The file's links, newest first. Never includes a URL. */
  list(fileId: string): Promise<ShareLinkResponse[]> {
    return firstValueFrom(this.http.get<ShareLinkResponse[]>(`/api/media/${fileId}/shares`));
  }

  /** Change a link's expiry. Rejected (409) on a revoked link. */
  updateExpiry(linkId: string, expiresInDays: number): Promise<ShareLinkResponse> {
    return firstValueFrom(
      this.http.patch<ShareLinkResponse>(`/api/shares/${linkId}`, { expiresInDays })
    );
  }

  /** Stop sharing one link. */
  revoke(linkId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/shares/${linkId}`));
  }

  /** Stop sharing the whole file — revoke every live link at once. Returns how many were revoked. */
  stopSharingFile(fileId: string): Promise<{ revoked: number }> {
    return firstValueFrom(this.http.delete<{ revoked: number }>(`/api/media/${fileId}/shares`));
  }
}
