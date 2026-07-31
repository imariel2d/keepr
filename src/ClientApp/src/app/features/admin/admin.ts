import { Component, computed, inject, signal } from '@angular/core';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { AdminUserListItem, Role } from '../../core/models';
import { BytesPipe } from '../../core/bytes.pipe';
import { formatDate } from '../../core/file-type';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { ModalComponent } from '../../cove/lib/modal/modal.component';
import { InputComponent } from '../../cove/lib/input/input.component';

const PAGE_SIZE = 50;
const GB = 1024 ** 3;
const MIN_PASSWORD_LENGTH = 12;

/**
 * Admin console — account administration (#34, extended by #36). A paged table of every account.
 * Admins can create accounts (with a password or an email invite), change a role, set a quota,
 * resend a pending invite, and remove (kick) an account. Reachable only by admins via adminGuard;
 * every call is server-gated by the "Admin" policy regardless.
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
  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  // Create-account modal.
  protected readonly createOpen = signal(false);
  protected readonly newEmail = signal('');
  protected readonly newRole = signal<Role>('User');
  protected readonly newSendInvite = signal(false);
  protected readonly newPassword = signal('');
  protected readonly creating = signal(false);
  protected readonly createFieldErrors = signal<Record<string, string[]>>({});
  /** True when invite mode is unavailable because no mailer is configured (server said 409). */
  protected readonly emailUnavailable = signal(false);
  protected readonly canCreate = computed(() => {
    if (!this.newEmail().trim()) return false;
    // Direct mode needs a password of at least the min length; invite mode needs none.
    return this.newSendInvite() || [...this.newPassword()].length >= MIN_PASSWORD_LENGTH;
  });

  // Quota-edit modal.
  protected readonly quotaTarget = signal<AdminUserListItem | null>(null);
  protected readonly quotaGb = signal('');
  protected readonly saving = signal(false);

  // Role-change modal.
  protected readonly roleTarget = signal<AdminUserListItem | null>(null);
  protected readonly roleValue = signal<Role>('User');
  protected readonly savingRole = signal(false);

  // Remove (kick) modal. The admin must retype the target email — a guardrail for an action that
  // permanently deletes the account and all its files.
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

  /** The current admin can't remove or demote their own account — the server also refuses. */
  protected isSelf(u: AdminUserListItem): boolean {
    return u.email === this.auth.email();
  }

  protected errorsFor(field: string): string[] {
    return this.createFieldErrors()[field] ?? [];
  }

  // ---- create --------------------------------------------------------------

  protected openCreate(): void {
    this.newEmail.set('');
    this.newRole.set('User');
    this.newSendInvite.set(false);
    this.newPassword.set('');
    this.createFieldErrors.set({});
    this.emailUnavailable.set(false);
    this.dialogError.set(null);
    this.createOpen.set(true);
  }

  protected setSendInvite(send: boolean): void {
    this.newSendInvite.set(send);
    this.dialogError.set(null);
    // Clear the stale "password too short" hint when switching to invite mode.
    if (send) this.createFieldErrors.update((e) => ({ ...e, password: [] }));
  }

  protected async createAccount(): Promise<void> {
    if (!this.canCreate()) return;
    this.creating.set(true);
    this.dialogError.set(null);
    this.createFieldErrors.set({});
    try {
      const res = await this.api.createUser({
        email: this.newEmail().trim(),
        role: this.newRole(),
        sendInvite: this.newSendInvite(),
        password: this.newSendInvite() ? undefined : this.newPassword(),
      });
      this.createOpen.set(false);

      if (res.invited && !res.inviteEmailSent) {
        this.notice.set(
          `${res.account.email} was created, but the invite email couldn't be sent. ` +
          `Use "Resend invite" on their row to try again.`);
      } else if (res.invited) {
        this.notice.set(`Invite sent to ${res.account.email}.`);
      } else {
        this.notice.set(`Account created for ${res.account.email}.`);
      }
      await this.load();
    } catch (e) {
      const fieldErrors = this.validationErrorsOf(e);
      if (Object.keys(fieldErrors).length > 0) {
        this.createFieldErrors.set(fieldErrors);
      } else {
        const detail = this.detailOf(e, 'Could not create the account.');
        // A 409 in invite mode means no mailer is configured — nudge toward the password path.
        this.emailUnavailable.set(this.newSendInvite() && this.statusOf(e) === 409);
        this.dialogError.set(detail);
      }
    } finally {
      this.creating.set(false);
    }
  }

  // ---- role ----------------------------------------------------------------

  protected openRole(u: AdminUserListItem): void {
    this.roleTarget.set(u);
    this.roleValue.set(u.role);
    this.dialogError.set(null);
  }

  protected async saveRole(): Promise<void> {
    const u = this.roleTarget();
    if (!u) return;
    if (this.roleValue() === u.role) {
      this.roleTarget.set(null);
      return;
    }

    this.savingRole.set(true);
    this.dialogError.set(null);
    try {
      const updated = await this.api.updateRole(u.id, this.roleValue());
      this.patchRow(u.id, { role: updated.role });
      this.roleTarget.set(null);
      this.notice.set(`${u.email} is now ${updated.role}.`);
    } catch (e) {
      // 400 self-demote / 409 last admin carry a user-facing detail.
      this.dialogError.set(this.detailOf(e, 'Could not change the role.'));
    } finally {
      this.savingRole.set(false);
    }
  }

  // ---- resend invite -------------------------------------------------------

  protected async resend(u: AdminUserListItem): Promise<void> {
    this.notice.set(null);
    try {
      await this.api.resendInvite(u.id);
      this.notice.set(`Invite re-sent to ${u.email}.`);
    } catch (e) {
      this.error.set(this.detailOf(e, `Could not resend the invite to ${u.email}.`));
    }
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
      this.patchRow(u.id, { quotaBytes: updated.quotaBytes, remainingBytes: updated.remainingBytes });
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
      this.dialogError.set(this.detailOf(e, 'Could not remove this account.'));
    } finally {
      this.kicking.set(false);
    }
  }

  private patchRow(id: string, patch: Partial<AdminUserListItem>): void {
    this.users.update((list) => list.map((x) => (x.id === id ? { ...x, ...patch } : x)));
  }

  private detailOf(e: unknown, fallback: string): string {
    const d = (e as { error?: { detail?: string } })?.error?.detail;
    return typeof d === 'string' && d ? d : fallback;
  }

  private statusOf(e: unknown): number | undefined {
    return (e as { status?: number })?.status;
  }

  private validationErrorsOf(e: unknown): Record<string, string[]> {
    const errors = (e as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
    return errors && typeof errors === 'object' ? errors : {};
  }
}
