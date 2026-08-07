import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { EmailChangePending, EmailChangePreview, ProfileResponse, ResetCapabilities } from './models';

/** The outcome of a change-email request: applied immediately (mail off) or staged awaiting
 *  confirmation (mail on). A discriminated union so the caller shows the right copy. */
export type ChangeEmailResult =
  | { kind: 'applied'; profile: ProfileResponse }
  | { kind: 'pending'; pendingEmail: string };

/**
 * The change-email flow (#27). The request/cancel half is authenticated (`/api/me/email`); the
 * confirm half is anonymous and token-authorized (`/api/auth/confirm-email/{token}`), like the
 * reset/claim links — the token in the URL is the authorization. See docs/feature-27-change-email.md
 * §5/§12.
 */
@Injectable({ providedIn: 'root' })
export class EmailChangeService {
  private readonly http = inject(HttpClient);

  /** Whether a mail provider is configured — decides verify-before-commit vs. an immediate change.
   *  The same global `/api/auth/capabilities` fact the login screen reads; it carries no account
   *  information, so it's safe to fetch from an authenticated screen too. */
  async mailEnabled(): Promise<boolean> {
    const caps = await firstValueFrom(this.http.get<ResetCapabilities>('/api/auth/capabilities'));
    return caps.selfServiceReset;
  }

  /**
   * Requests an email change, re-authenticated by the current password. Resolves to `pending` (202 —
   * a confirmation link was emailed to the new address, nothing changed yet) or `applied` (200 —
   * changed immediately because mail isn't configured). Rejects 400 (wrong password / invalid email)
   * or 409 (`email_in_use` / `email_change_pending`).
   */
  async request(newEmail: string, currentPassword: string): Promise<ChangeEmailResult> {
    const res = await firstValueFrom(
      this.http.post<EmailChangePending | ProfileResponse>(
        '/api/me/email',
        { newEmail, currentPassword },
        { observe: 'response' }
      )
    );
    return res.status === 202
      ? { kind: 'pending', pendingEmail: (res.body as EmailChangePending).pendingEmail }
      : { kind: 'applied', profile: res.body as ProfileResponse };
  }

  /** Cancels a pending (unconfirmed) change so its emailed link dies. 404 when nothing is pending. */
  async cancel(): Promise<void> {
    await firstValueFrom(this.http.delete<void>('/api/me/email'));
  }

  /** Validates a confirmation token and returns the target address. 410 if unknown/expired/used. */
  preview(token: string): Promise<EmailChangePreview> {
    return firstValueFrom(
      this.http.get<EmailChangePreview>(`/api/auth/confirm-email/${encodeURIComponent(token)}`)
    );
  }

  /** Applies the change: swaps the account email and marks it verified, returning the new email. 410
   *  if the link lapsed, 409 if the address was taken meanwhile. Touches no session. */
  confirm(token: string): Promise<{ email: string }> {
    return firstValueFrom(
      this.http.post<{ email: string }>(`/api/auth/confirm-email/${encodeURIComponent(token)}`, {})
    );
  }
}
