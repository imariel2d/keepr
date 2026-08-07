import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ProfileService } from '../../core/profile.service';
import { ProfileStore } from '../../core/profile.store';
import { EmailChangeService } from '../../core/email-change.service';
import { LocaleService } from '../../core/locale.service';
import { Locale } from '../../core/locale';
import { MIN_PASSWORD_LENGTH } from '../../core/password-policy';
import { errorMessage, problemDetail, validationErrors } from '../../core/problem-details';
import { LanguageSwitcher } from '../i18n/language-switcher';
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
  imports: [ButtonComponent, InputComponent, IconComponent, AvatarComponent, LanguageSwitcher],
  templateUrl: './profile.html',
  styleUrl: './profile.scss',
})
export class Profile {
  private readonly api = inject(ProfileService);
  private readonly store = inject(ProfileStore);
  private readonly emailChanges = inject(EmailChangeService);
  protected readonly locale = inject(LocaleService);
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

  // Email form (#27). Its own current-password field, separate from the password card's.
  protected readonly mailEnabled = signal(true);
  protected readonly newEmail = signal('');
  protected readonly emailPassword = signal('');
  protected readonly showEmailPassword = signal(false);
  protected readonly changingEmail = signal(false);
  protected readonly emailNotice = signal<string | null>(null);
  protected readonly emailError = signal<string | null>(null);
  protected readonly emailFieldErrors = signal<Record<string, string[]>>({});

  protected readonly emailVerified = computed(() => this.profile()?.emailVerified ?? false);
  /** Any in-flight change awaiting confirmation (mail-on), or null. */
  protected readonly pendingEmail = computed(() => this.profile()?.pendingEmail ?? null);
  protected readonly canChangeEmail = computed(
    () => this.newEmail().trim().length > 0 && this.emailPassword().length > 0);

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    await this.store.ensureLoaded();
    const p = this.profile();
    this.firstName.set(p?.firstName ?? '');
    this.lastName.set(p?.lastName ?? '');
    // Whether the deployment can send a confirmation link decides the card's copy (verify-before-
    // commit vs. immediate). Fall back to "on" so a probe hiccup doesn't wrongly show the no-mail note.
    this.emailChanges.mailEnabled().then((on) => this.mailEnabled.set(on)).catch(() => {});
  }

  protected errorsFor(field: string): string[] {
    return this.passwordFieldErrors()[field] ?? [];
  }

  protected async saveName(): Promise<void> {
    this.savingName.set(true);
    this.nameNotice.set(null);
    this.nameError.set(null);
    try {
      const updated = await this.api.update(
        this.firstName().trim() || null,
        this.lastName().trim() || null,
        this.profile()?.preferredLanguage ?? null // full-replace: keep the stored language untouched
      );
      this.store.set(updated);
      this.nameNotice.set($localize`:@@profile.name.saved:Profile saved.`);
    } catch (e) {
      this.nameError.set(errorMessage(e));
    } finally {
      this.savingName.set(false);
    }
  }

  // Language (#30). The switcher persists the choice to the account, then reloads into that locale's
  // build (compile-time i18n has no in-place swap). Uses the stored names so it never disturbs them.
  protected readonly savingLanguage = signal(false);
  protected readonly languageError = signal<string | null>(null);

  protected async changeLanguage(locale: Locale): Promise<void> {
    const p = this.profile();
    if (!p || this.savingLanguage() || locale === this.locale.current) return;
    this.savingLanguage.set(true);
    this.languageError.set(null);
    try {
      await this.api.update(p.firstName, p.lastName, locale);
      this.locale.switchTo(locale); // persists cookie + reloads into /{locale}/
    } catch (e) {
      this.languageError.set(errorMessage(e));
      this.savingLanguage.set(false);
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

  protected emailErrorsFor(field: string): string[] {
    return this.emailFieldErrors()[field] ?? [];
  }

  protected async changeEmail(): Promise<void> {
    if (!this.canChangeEmail() || this.changingEmail()) return;
    this.changingEmail.set(true);
    this.emailNotice.set(null);
    this.emailError.set(null);
    this.emailFieldErrors.set({});
    try {
      const result = await this.emailChanges.request(this.newEmail().trim(), this.emailPassword());
      this.emailPassword.set('');
      this.newEmail.set('');
      if (result.kind === 'applied') {
        // Mail off: the change is live now. Adopt the returned profile (email + verified badge update).
        this.store.set(result.profile);
        this.emailNotice.set(`Your email is now ${result.profile.email}.`);
      } else {
        // Mail on: staged. Refresh so the pending line appears from the server's truth.
        await this.store.refresh();
        this.emailNotice.set(
          `Confirm the link we sent to ${result.pendingEmail} to finish the change.`);
      }
    } catch (e) {
      const fieldErrors = validationErrors(e);
      if (Object.keys(fieldErrors).length > 0) {
        this.emailFieldErrors.set(fieldErrors);
      } else {
        this.emailError.set(problemDetail(e, 'Could not change your email.'));
      }
    } finally {
      this.changingEmail.set(false);
    }
  }

  /** Re-sends the confirmation to the pending address; re-authenticated, so the password field must
   *  be filled (the button is disabled otherwise). Supersedes the previous link. */
  protected async resendEmailChange(): Promise<void> {
    const target = this.pendingEmail();
    if (!target || this.emailPassword().length === 0 || this.changingEmail()) return;
    this.changingEmail.set(true);
    this.emailNotice.set(null);
    this.emailError.set(null);
    try {
      await this.emailChanges.request(target, this.emailPassword());
      this.emailPassword.set('');
      this.emailNotice.set(`Confirmation resent to ${target}.`);
    } catch (e) {
      this.emailError.set(problemDetail(e, 'Could not resend the confirmation.'));
    } finally {
      this.changingEmail.set(false);
    }
  }

  protected async cancelEmailChange(): Promise<void> {
    if (this.changingEmail()) return;
    this.changingEmail.set(true);
    this.emailNotice.set(null);
    this.emailError.set(null);
    try {
      await this.emailChanges.cancel();
      await this.store.refresh();
      this.emailNotice.set('Pending email change cancelled.');
    } catch (e) {
      this.emailError.set(problemDetail(e, 'Could not cancel the change.'));
    } finally {
      this.changingEmail.set(false);
    }
  }
}
