import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ProfileStore } from '../../core/profile.store';
import { problemDetail } from '../../core/problem-details';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';

/**
 * Sign-in only. Since #36 public self-registration is closed — accounts are provisioned by an admin
 * (with a password or an email invite), so there is no register mode or invite-code field here.
 * See docs/feature-36-account-provisioning.md §3.2.
 */
@Component({
  selector: 'app-login',
  imports: [ButtonComponent, InputComponent, IconComponent],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly profile = inject(ProfileStore);
  private readonly router = inject(Router);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected async submit(event?: Event): Promise<void> {
    event?.preventDefault();
    this.error.set(null);
    this.busy.set(true);
    try {
      await this.auth.login(this.email(), this.password());
      // Prime the profile so the forced-change guard can act immediately after sign-in. Non-fatal:
      // a failed prefetch must not report the (successful) login as failed — the guard re-loads,
      // since ProfileStore doesn't cache a failed load.
      await this.profile.refresh().catch(() => {});
      await this.router.navigate(['']);
    } catch (e) {
      this.error.set(problemDetail(e, 'Login failed. Check your email and password.'));
    } finally {
      this.busy.set(false);
    }
  }
}
