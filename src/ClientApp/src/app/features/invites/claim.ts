import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { InviteService } from '../../core/invite.service';
import { ProfileStore } from '../../core/profile.store';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';

const MIN_PASSWORD_LENGTH = 12;

/**
 * Public claim page for an admin-provisioned account. The token in the URL is the authorization
 * (no session yet) — mirrors the share viewer. Validates the token, lets the invitee set a
 * password, and signs them in. See docs/feature-36-account-provisioning.md §8.4.
 */
@Component({
  selector: 'app-claim',
  imports: [ButtonComponent, InputComponent, IconComponent],
  templateUrl: './claim.html',
  styleUrl: './claim.scss',
})
export class Claim {
  private readonly invites = inject(InviteService);
  private readonly auth = inject(AuthService);
  private readonly profile = inject(ProfileStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly token = this.route.snapshot.paramMap.get('token') ?? '';

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;
  protected readonly loading = signal(true);
  /** True once the token is known bad (expired, claimed, or unknown) — a dead end. */
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
      const preview = await this.invites.preview(this.token);
      this.email.set(preview.email);
    } catch {
      // 410 (expired/claimed/unknown) or any failure: one dead end, no oracle.
      this.invalid.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected goToLogin(): void {
    void this.router.navigate(['/login']);
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
      await this.auth.claim(this.token, this.password());
      await this.profile.refresh();
      await this.router.navigate(['/files']);
    } catch (e) {
      const status = (e as { status?: number })?.status;
      if (status === 410) {
        // The invite lapsed between loading and submitting.
        this.invalid.set(true);
        return;
      }
      const fieldErrors = this.validationErrorsOf(e);
      if (Object.keys(fieldErrors).length > 0) {
        this.fieldErrors.set(fieldErrors);
      } else {
        this.error.set(this.detailOf(e, 'Could not set your password. Try again.'));
      }
    } finally {
      this.busy.set(false);
    }
  }

  private detailOf(e: unknown, fallback: string): string {
    const d = (e as { error?: { detail?: string } })?.error?.detail;
    return typeof d === 'string' && d ? d : fallback;
  }

  private validationErrorsOf(e: unknown): Record<string, string[]> {
    const errors = (e as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
    return errors && typeof errors === 'object' ? errors : {};
  }
}
