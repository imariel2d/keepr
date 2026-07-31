import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  AdminUserDetail,
  AdminUserListItem,
  CreateUserRequest,
  CreateUserResponse,
  PagedResponse,
  Role,
} from './models';

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

  /**
   * Provisions a new account. Direct mode (`sendInvite:false`) needs a password and the account is
   * usable at once; invite mode emails a claim link (needs a configured mailer). 400 on validation,
   * 409 on a duplicate email or invite-mode-without-a-mailer. See docs/feature-36-account-provisioning.md §4.
   */
  createUser(req: CreateUserRequest): Promise<CreateUserResponse> {
    return firstValueFrom(this.http.post<CreateUserResponse>('/api/admin/users', req));
  }

  /** Set a user's storage quota, in bytes. Returns the updated account. */
  updateQuota(id: string, quotaBytes: number): Promise<AdminUserDetail> {
    return firstValueFrom(
      this.http.patch<AdminUserDetail>(`/api/admin/users/${id}/quota`, { quotaBytes })
    );
  }

  /** Change a user's role. 400 self-demote / bad role; 409 removing the last admin. */
  updateRole(id: string, role: Role): Promise<AdminUserDetail> {
    return firstValueFrom(this.http.patch<AdminUserDetail>(`/api/admin/users/${id}/role`, { role }));
  }

  /** Resend the claim invite for a pending (unclaimed) account. 409 already claimed / no mailer. */
  resendInvite(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/admin/users/${id}/invite`, {}));
  }

  /**
   * Kicks a user: revokes their sessions now and queues the account (and its files) for permanent
   * deletion. 202 on success; 400 kicking yourself; 409 removing the last admin.
   */
  kickUser(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/admin/users/${id}`));
  }
}
