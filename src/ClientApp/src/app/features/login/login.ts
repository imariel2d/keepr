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
        await this.auth.register(this.email(), this.password());
      } else {
        await this.auth.login(this.email(), this.password());
      }
      await this.router.navigate(['']);
    } catch {
      this.error.set(
        this.mode() === 'register'
          ? 'Registration failed. The email may already be in use.'
          : 'Login failed. Check your email and password.'
      );
    } finally {
      this.busy.set(false);
    }
  }
}
