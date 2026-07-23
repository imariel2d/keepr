import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';

@Component({
  selector: 'app-login',
  imports: [ButtonComponent, InputComponent, IconComponent],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly email = signal('');
  protected readonly password = signal('');
  /** Required to register: the server gates signups behind a shared code. */
  protected readonly inviteCode = signal('');
  protected readonly showPassword = signal(false);
  protected readonly mode = signal<'login' | 'register'>('login');
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected toggleMode(): void {
    this.mode.update((m) => (m === 'login' ? 'register' : 'login'));
    this.error.set(null);
  }

  protected async submit(event?: Event): Promise<void> {
    event?.preventDefault();
    this.error.set(null);
    this.busy.set(true);
    try {
      if (this.mode() === 'register') {
        await this.auth.register(this.email(), this.password(), this.inviteCode());
      } else {
        await this.auth.login(this.email(), this.password());
      }
      await this.router.navigate(['']);
    } catch (e) {
      // The gate's reasons are written for end users ("That invite code is not valid."), so show
      // them rather than a generic failure that leaves the user guessing which field is wrong.
      this.error.set(
        this.messageOf(
          e,
          this.mode() === 'register'
            ? 'Registration failed. The email may already be in use.'
            : 'Login failed. Check your email and password.'
        )
      );
    } finally {
      this.busy.set(false);
    }
  }

  /** Errors are problem+json; `detail` carries a message meant for the user. */
  private messageOf(e: unknown, fallback: string): string {
    const detail = (e as { error?: { detail?: string } })?.error?.detail;
    return typeof detail === 'string' && detail ? detail : fallback;
  }
}
