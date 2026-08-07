import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ProfileResponse } from './models';

/**
 * The signed-in account's own profile (`/api/me`). Names (#29) and change-password (#28 core).
 * See docs/feature-36-account-provisioning.md §7.
 */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);

  get(): Promise<ProfileResponse> {
    return firstValueFrom(this.http.get<ProfileResponse>('/api/me/profile'));
  }

  /** Full-replace of the profile fields (#29/#30): every call sends the whole set, so a blank value
   *  clears it. `preferredLanguage` null → the default (English); an unsupported code → 400. */
  update(
    firstName: string | null,
    lastName: string | null,
    preferredLanguage: string | null
  ): Promise<ProfileResponse> {
    return firstValueFrom(
      this.http.patch<ProfileResponse>('/api/me/profile', { firstName, lastName, preferredLanguage })
    );
  }

  /** 400 if the current password is wrong or the new one fails the rules. On success the account's
   *  other sessions are revoked server-side. */
  changePassword(currentPassword: string, newPassword: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>('/api/me/password', { currentPassword, newPassword })
    );
  }
}
