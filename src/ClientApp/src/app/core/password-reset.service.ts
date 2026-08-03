import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ResetCapabilities, ResetPreview } from './models';

/**
 * The public password-reset endpoints (`/api/auth`). No session — the token in the URL is the
 * authorization, like the invite/claim flow. Completing a reset issues a session and lives in
 * AuthService.resetPassword. See docs/feature-26-password-reset.md §5/§7.
 */
@Injectable({ providedIn: 'root' })
export class PasswordResetService {
  private readonly http = inject(HttpClient);

  /** Whether this deployment can send self-service reset links (mail is configured). A global fact,
   *  safe to fetch anonymously — it carries no account information. */
  capabilities(): Promise<ResetCapabilities> {
    return firstValueFrom(this.http.get<ResetCapabilities>('/api/auth/capabilities'));
  }

  /**
   * Requests a reset link. Always resolves (the server answers a neutral 202 whether or not the
   * address can be reset), so callers show the same confirmation on success — never revealing
   * whether the account exists.
   */
  async request(email: string): Promise<void> {
    await firstValueFrom(this.http.post('/api/auth/forgot-password', { email }));
  }

  /** Validates a reset token and returns the account email. 410 if unknown/expired/used. */
  preview(token: string): Promise<ResetPreview> {
    return firstValueFrom(
      this.http.get<ResetPreview>(`/api/auth/reset-password/${encodeURIComponent(token)}`)
    );
  }
}
