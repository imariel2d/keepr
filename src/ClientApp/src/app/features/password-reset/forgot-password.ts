import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PasswordResetService } from '../../core/password-reset.service';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { InputComponent } from '../../cove/lib/input/input.component';

/**
 * Public "forgot password" page. Asks for an email and always shows the same neutral confirmation —
 * the server answers a neutral 202 whether or not the address can be reset, so this screen never
 * reveals whether an account exists. If the deployment has no mail provider, self-service reset
 * isn't offered and the screen points the user at their admin instead.
 * See docs/feature-26-password-reset.md §5/§7.
 */
@Component({
  selector: 'app-forgot-password',
  imports: [RouterLink, ButtonComponent, InputComponent],
  templateUrl: './forgot-password.html',
  styleUrl: './password-reset.scss',
})
export class ForgotPassword {
  private readonly resets = inject(PasswordResetService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  /** True when the deployment can send reset links; false → show the "contact your admin" copy. */
  protected readonly selfService = signal(false);
  protected readonly email = signal('');
  protected readonly busy = signal(false);
  /** True once a request has been submitted — the confirmation replaces the form. */
  protected readonly sent = signal(false);

  protected readonly canSubmit = computed(() => this.email().trim().length > 0);

  protected readonly submitLabel = computed(() =>
    this.busy() ? $localize`:@@forgot.sending:Sending…` : $localize`:@@forgot.send:Send reset link`);

  protected goToLogin(): void {
    void this.router.navigate(['/login']);
  }

  constructor() {
    void this.checkCapability();
  }

  private async checkCapability(): Promise<void> {
    try {
      const caps = await this.resets.capabilities();
      this.selfService.set(caps.selfServiceReset);
    } catch {
      // If the probe fails, fall back to the safe copy (contact your admin) rather than a form that
      // might silently drop the request on a no-mail deployment.
      this.selfService.set(false);
    } finally {
      this.loading.set(false);
    }
  }

  protected async submit(event?: Event): Promise<void> {
    event?.preventDefault();
    if (!this.canSubmit() || this.busy()) return;
    this.busy.set(true);
    try {
      await this.resets.request(this.email().trim());
    } catch {
      // The endpoint is always-neutral; even a transport hiccup must not reveal anything. Show the
      // same confirmation regardless — a genuine failure just means no email arrives and the user
      // can retry.
    } finally {
      this.busy.set(false);
      this.sent.set(true);
    }
  }
}
