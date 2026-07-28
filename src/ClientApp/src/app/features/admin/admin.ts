import { Component, computed, inject, signal } from '@angular/core';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { AdminUserListItem } from '../../core/models';
import { BytesPipe } from '../../core/bytes.pipe';
import { formatDate } from '../../core/file-type';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { ModalComponent } from '../../cove/lib/modal/modal.component';
import { InputComponent } from '../../cove/lib/input/input.component';

const PAGE_SIZE = 50;
const GB = 1024 ** 3;

/**
 * Admin console — account administration (#34). A paged table of every account with two actions:
 * set a user's storage quota, and remove (kick) an account. Reachable only by admins via
 * adminGuard; every call is server-gated by the "Admin" policy regardless.
 */
@Component({
  selector: 'app-admin',
  imports: [BytesPipe, ButtonComponent, IconComponent, ModalComponent, InputComponent],
  templateUrl: './admin.html',
  styleUrl: './admin.scss',
})
export class Admin {
  private readonly api = inject(AdminService);
  private readonly auth = inject(AuthService);

  protected readonly users = signal<AdminUserListItem[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly pageSize = PAGE_SIZE;
  protected readonly formatDate = formatDate;

  // Quota-edit modal.
  protected readonly quotaTarget = signal<AdminUserListItem | null>(null);
  protected readonly quotaGb = signal('');
  protected readonly saving = signal(false);

  // Remove (kick) modal. The admin must retype the target email — a guardrail for an action that
  // permanently deletes the account and all its files (design §6).
  protected readonly kickTarget = signal<AdminUserListItem | null>(null);
  protected readonly kickConfirm = signal('');
  protected readonly kicking = signal(false);
  protected readonly kickReady = computed(() =>
    this.kickTarget()?.email.trim().toLowerCase() === this.kickConfirm().trim().toLowerCase());

  /** Shown inside whichever modal is open. */
  protected readonly dialogError = signal<string | null>(null);

  protected readonly rangeStart = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize + 1);
  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize, this.total()));
  protected readonly hasPrev = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() * this.pageSize < this.total());

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const res = await this.api.listUsers(this.page(), this.pageSize);
      this.users.set(res.items);
      this.total.set(res.total);
    } catch {
      this.error.set('Could not load accounts.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async go(delta: number): Promise<void> {
    const next = this.page() + delta;
    if (next < 1 || (next - 1) * this.pageSize >= this.total()) return;
    this.page.set(next);
    await this.load();
  }

  /** The current admin can't remove their own account — the server also refuses (400). */
  protected isSelf(u: AdminUserListItem): boolean {
    return u.email === this.auth.email();
  }

  // ---- quota ---------------------------------------------------------------

  protected openQuota(u: AdminUserListItem): void {
    this.quotaTarget.set(u);
    this.quotaGb.set(String(+(u.quotaBytes / GB).toFixed(2)));
    this.dialogError.set(null);
  }

  protected async saveQuota(): Promise<void> {
    const u = this.quotaTarget();
    if (!u) return;

    const gb = Number(this.quotaGb());
    if (!Number.isFinite(gb) || gb < 0) {
      this.dialogError.set('Enter a storage size of 0 GB or more.');
      return;
    }

    this.saving.set(true);
    this.dialogError.set(null);
    try {
      const updated = await this.api.updateQuota(u.id, Math.round(gb * GB));
      this.users.update((list) =>
        list.map((x) =>
          x.id === u.id
            ? { ...x, quotaBytes: updated.quotaBytes, remainingBytes: updated.remainingBytes }
            : x));
      this.quotaTarget.set(null);
      this.notice.set(`Quota updated for ${u.email}.`);
    } catch (e) {
      this.dialogError.set(this.detailOf(e, 'Could not update the quota.'));
    } finally {
      this.saving.set(false);
    }
  }

  // ---- remove (kick) -------------------------------------------------------

  protected openKick(u: AdminUserListItem): void {
    this.kickTarget.set(u);
    this.kickConfirm.set('');
    this.dialogError.set(null);
  }

  protected async confirmKick(): Promise<void> {
    const u = this.kickTarget();
    if (!u) return;

    this.kicking.set(true);
    this.dialogError.set(null);
    try {
      await this.api.kickUser(u.id);
      this.kickTarget.set(null);
      this.notice.set(`${u.email} has been removed.`);
      await this.load();
    } catch (e) {
      // 400 (kicking yourself) / 409 (last admin) carry a user-facing detail.
      this.dialogError.set(this.detailOf(e, 'Could not remove this account.'));
    } finally {
      this.kicking.set(false);
    }
  }

  private detailOf(e: unknown, fallback: string): string {
    const d = (e as { error?: { detail?: string } })?.error?.detail;
    return typeof d === 'string' && d ? d : fallback;
  }
}
