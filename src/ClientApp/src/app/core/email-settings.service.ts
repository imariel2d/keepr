import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { EmailSettingsResponse, EmailTestResult, UpdateEmailSettingsRequest } from './models';

/**
 * The admin email-settings API (`/api/admin/email-settings`). Server-gated by the "Admin" policy.
 * The API key is write-only: `get` never returns it (only `hasApiKey`), and `update` sends it only
 * when the admin enters a new one. See docs/feature-36-email-providers.md §6.
 */
@Injectable({ providedIn: 'root' })
export class EmailSettingsService {
  private readonly http = inject(HttpClient);

  /** The current settings for the screen (no secret). */
  get(): Promise<EmailSettingsResponse> {
    return firstValueFrom(this.http.get<EmailSettingsResponse>('/api/admin/email-settings'));
  }

  /** Save the settings. 400 with a per-field `errors` map on validation failure. */
  update(req: UpdateEmailSettingsRequest): Promise<EmailSettingsResponse> {
    return firstValueFrom(this.http.put<EmailSettingsResponse>('/api/admin/email-settings', req));
  }

  /** Send a test email to the acting admin using the saved settings. 409 if email isn't configured. */
  sendTest(): Promise<EmailTestResult> {
    return firstValueFrom(this.http.post<EmailTestResult>('/api/admin/email-settings/test', {}));
  }
}
