import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ProfileService } from '../../core/profile.service';
import { ProfileStore } from '../../core/profile.store';
import { MIN_PASSWORD_LENGTH } from '../../core/password-policy';
import { problemDetail, validationErrors } from '../../core/problem-details';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { AvatarComponent } from '../../cove/lib/avatar/avatar.component';

/**
 * The signed-in account's profile (#29): edit display name, see the read-only email and role, and
 * change the password (#28 core). When the account must change its password — an admin-set initial
 * password (§4.1) or the bootstrap admin — this screen enters a can't-skip "set a new password"
 * mode. See docs/feature-36-account-provisioning.md §7.
 */
@Component({
  selector: 'app-profile',
  imports: [ButtonComponent, InputComponent, IconComponent, AvatarComponent],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
})
export class Profile {
  private readonly api = inject(ProfileService);
  private readonly store = inject(ProfileStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;
  protected readonly profile = this.store.profile;

  /** Forced when the account carries the must-change flag, or arrived via the guard's redirect. */
  protected readonly forced = computed(
    () => this.store.mustChangePassword() || this.route.snapshot.queryParamMap.has('changePassword'));

  protected readonly displayName = computed(() => {
    const p = this.profile();
    const name = `${p?.firstName ?? ''} ${p?.lastName ?? ''}`.trim();
    return name || p?.email || '';
  });

  // Name form.
  protected readonly firstName = signal('');
  protected readonly lastName = signal('');
  protected readonly savingName = signal(false);
  protected readonly nameNotice = signal<string | null>(null);
  protected readonly nameError = signal<string | null>(null);

  // Password form.
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  // A reveal toggle per field, so showing one doesn't expose the others.
  protected readonly showCurrent = signal(false);
  protected readonly showNew = signal(false);
  protected readonly showConfirm = signal(false);
  protected readonly savingPassword = signal(false);
  protected readonly passwordNotice = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordFieldErrors = signal<Record<string, string[]>>({});

  protected readonly newLongEnough = computed(() => [...this.newPassword()].length >= MIN_PASSWORD_LENGTH);
  protected readonly confirmMatches = computed(
    () => this.confirmPassword().length > 0 && this.newPassword() === this.confirmPassword());
  protected readonly canChangePassword = computed(
    () => this.currentPassword().length > 0 && this.newLongEnough() && this.confirmMatches());

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    await this.store.ensureLoaded();
    const p = this.profile();
    this.firstName.set(p?.firstName ?? '');
    this.lastName.set(p?.lastName ?? '');
  }

  protected errorsFor(field: string): string[] {
    return this.passwordFieldErrors()[field] ?? [];
  }

  protected async saveName(): Promise<void> {
    this.savingName.set(true);
    this.nameNotice.set(null);
    this.nameError.set(null);
    try {
      const updated = await this.api.update(this.firstName().trim() || null, this.lastName().trim() || null);
      this.store.set(updated);
      this.nameNotice.set('Profile saved.');
    } catch (e) {
      this.nameError.set(problemDetail(e, 'Could not save your profile.'));
    } finally {
      this.savingName.set(false);
    }
  }

  protected async changePassword(): Promise<void> {
    if (!this.canChangePassword()) return;
    this.savingPassword.set(true);
    this.passwordNotice.set(null);
    this.passwordError.set(null);
    this.passwordFieldErrors.set({});
    try {
      await this.api.changePassword(this.currentPassword(), this.newPassword());
      // Reload so mustChangePassword clears; the forced mode ends and the guard lets them through.
      await this.store.refresh();
      this.currentPassword.set('');
      this.newPassword.set('');
      this.confirmPassword.set('');

      if (this.forced()) {
        await this.router.navigate(['/files']);
      } else {
        this.passwordNotice.set('Password changed. Your other sessions were signed out.');
      }
    } catch (e) {
      const fieldErrors = validationErrors(e);
      if (Object.keys(fieldErrors).length > 0) {
        this.passwordFieldErrors.set(fieldErrors);
      } else {
        this.passwordError.set(problemDetail(e, 'Could not change your password.'));
      }
    } finally {
      this.savingPassword.set(false);
    }
  }
}
