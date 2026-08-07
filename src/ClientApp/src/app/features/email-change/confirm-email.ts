import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EmailChangeService } from '../../core/email-change.service';
import { problemStatus } from '../../core/problem-details';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';

/**
 * Public confirm-email page (#27). The token in the URL is the authorization (no session) — mirrors
 * the reset/claim pages. Validates the token to show which address it's for, then a single Confirm
 * applies the change and marks the new address verified. Touches no session — the user may be signed
 * in elsewhere. See docs/feature-27-change-email.md §5.2/§5.3.
 */
@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink, ButtonComponent, IconComponent],
  templateUrl: './confirm-email.html',
  styleUrl: './email-change.scss',
})
export class ConfirmEmail {
  private readonly emailChanges = inject(EmailChangeService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly token = this.route.snapshot.paramMap.get('token') ?? '';

  protected readonly loading = signal(true);
  /** True once the token is known bad (expired, used, unknown) — a dead end. */
  protected readonly invalid = signal(false);
  /** True once the new address has been confirmed. */
  protected readonly done = signal(false);
  protected readonly newEmail = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

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
      const preview = await this.emailChanges.preview(this.token);
      this.newEmail.set(preview.newEmail);
    } catch {
      // 410 (expired/used/unknown) or any failure: one dead end, no oracle.
      this.invalid.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected goToFiles(): void {
    void this.router.navigate(['/files']);
  }

  protected async confirm(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      const res = await this.emailChanges.confirm(this.token);
      this.newEmail.set(res.email);
      this.done.set(true);
    } catch (e) {
      const status = problemStatus(e);
      if (status === 410) {
        // The link lapsed between loading and confirming.
        this.invalid.set(true);
      } else if (status === 409) {
        this.error.set('That address is now in use. Start the change again from your profile.');
      } else {
        this.error.set('Could not confirm your new email. Try the link again.');
      }
    } finally {
      this.busy.set(false);
    }
  }
}
