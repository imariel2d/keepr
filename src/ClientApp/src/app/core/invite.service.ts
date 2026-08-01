import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { InvitePreview } from './models';

/**
 * The public claim endpoints (`/api/invites`). No session — the token in the URL is the
 * authorization, like the share viewer. Claiming itself issues a session and lives in AuthService.
 * See docs/feature-36-account-provisioning.md §8.4.
 */
@Injectable({ providedIn: 'root' })
export class InviteService {
  private readonly http = inject(HttpClient);

  /** Validates a claim token and returns the invited email. 410 if expired/claimed/unknown. */
  preview(token: string): Promise<InvitePreview> {
    return firstValueFrom(this.http.get<InvitePreview>(`/api/invites/${encodeURIComponent(token)}`));
  }
}
