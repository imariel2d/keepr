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

  update(firstName: string | null, lastName: string | null): Promise<ProfileResponse> {
    return firstValueFrom(
      this.http.patch<ProfileResponse>('/api/me/profile', { firstName, lastName })
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
