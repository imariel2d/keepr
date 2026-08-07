import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { PasswordResetService } from '../../core/password-reset.service';
import { ProfileStore } from '../../core/profile.store';
import { MIN_PASSWORD_LENGTH } from '../../core/password-policy';
import { errorMessage, problemStatus, validationErrors } from '../../core/problem-details';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';

/**
 * Public reset-password page. The token in the URL is the authorization (no session) — mirrors the
 * claim page. Validates the token, lets the user choose a new password, and signs them in (the
 * server revoked every prior session). See docs/feature-26-password-reset.md §5.
 */
@Component({
  selector: 'app-reset-password',
  imports: [RouterLink, ButtonComponent, InputComponent, IconComponent],
  templateUrl: './reset-password.html',
  styleUrl: './password-reset.scss',
})
export class ResetPassword {
  private readonly resets = inject(PasswordResetService);
  private readonly auth = inject(AuthService);
  private readonly profile = inject(ProfileStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly token = this.route.snapshot.paramMap.get('token') ?? '';

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;
  protected readonly loading = signal(true);
  /** True once the token is known bad (expired, used, or unknown) — a dead end. */
  protected readonly invalid = signal(false);
  protected readonly email = signal('');

  protected readonly password = signal('');
  protected readonly confirm = signal('');
  protected readonly showPassword = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]>>({});

  protected readonly longEnough = computed(() => [...this.password()].length >= MIN_PASSWORD_LENGTH);
  protected readonly matches = computed(() => this.confirm().length > 0 && this.password() === this.confirm());
  protected readonly canSubmit = computed(() => this.longEnough() && this.matches());

  protected readonly revealLabel = computed(() =>
    this.showPassword() ? $localize`:@@password.hide:Hide password` : $localize`:@@password.show:Show password`);
  protected readonly submitLabel = computed(() =>
    this.busy() ? $localize`:@@common.please_wait:Please wait…` : $localize`:@@reset.submit:Reset password & sign in`);

  constructor() {
    void this.validate();
  }

  private async validate(): Promise<void> {
    if (!this.token) {
      this.invalid.set(true);
      this.loading.set(false);
      return;
    }
    try {
      const preview = await this.resets.preview(this.token);
      this.email.set(preview.email);
    } catch {
      // 410 (expired/used/unknown) or any failure: one dead end, no oracle.
      this.invalid.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected goToForgot(): void {
    void this.router.navigate(['/forgot-password']);
  }

  protected errorsFor(field: string): string[] {
    return this.fieldErrors()[field] ?? [];
  }

  protected async submit(event?: Event): Promise<void> {
    event?.preventDefault();
    if (!this.canSubmit()) return;
    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set({});
    try {
      await this.auth.resetPassword(this.token, this.password());
    } catch (e) {
      const status = problemStatus(e);
      if (status === 410) {
        // The link lapsed between loading and submitting.
        this.invalid.set(true);
        return;
      }
      const fieldErrors = validationErrors(e);
      if (Object.keys(fieldErrors).length > 0) {
        this.fieldErrors.set(fieldErrors);
      } else {
        this.error.set(errorMessage(e));
      }
      return;
    } finally {
      this.busy.set(false);
    }

    // Reset succeeded (password stored, session issued). The profile prefetch is a convenience — if
    // it fails on a flaky connection, don't tell the user their reset failed (retrying would 410 the
    // now-used token). Just proceed. See claim.ts for the same reasoning.
    await this.profile.refresh().catch(() => {});
    await this.router.navigate(['/files']);
  }
}
