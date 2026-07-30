import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AdminUserListItem, PagedResponse } from './models';

/**
 * The admin account-administration API (`/api/admin`). Every call is server-gated by the "Admin"
 * policy, so a non-admin reaching these would get 403. See docs/feature-34-admin-console.md.
 */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  /** A page of accounts, newest first. */
  listUsers(page: number, pageSize: number): Promise<PagedResponse<AdminUserListItem>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return firstValueFrom(
      this.http.get<PagedResponse<AdminUserListItem>>('/api/admin/users', { params })
    );
  }

  /** Set a user's storage quota, in bytes. Returns the updated account. */
  updateQuota(id: string, quotaBytes: number): Promise<AdminUserListItem> {
    return firstValueFrom(
      this.http.patch<AdminUserListItem>(`/api/admin/users/${id}/quota`, { quotaBytes })
    );
  }

  /**
   * Kicks a user: revokes their sessions now and queues the account (and its files) for permanent
   * deletion. 202 on success; 400 kicking yourself; 409 removing the last admin.
   */
  kickUser(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/admin/users/${id}`));
  }
}
