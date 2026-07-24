import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { InputComponent } from '../../cove/lib/input/input.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';

/** Mirrors PasswordPolicy.MinLength. The server stays the authority; this is only guidance. */
const MIN_PASSWORD_LENGTH = 12;

/**
 * Mirrors PasswordPolicy.MaxBytes. Bytes rather than characters because BCrypt truncates at 72
 * bytes server-side, so a passphrase in a non-Latin script can cross the limit well under 72
 * visible characters.
 */
const MAX_PASSWORD_BYTES = 72;

interface Requirement {
  readonly label: string;
  readonly met: boolean;
}

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

  /** Per-field messages from the server's 400, keyed by field name. */
  protected readonly fieldErrors = signal<Record<string, string[]>>({});

  /**
   * The rules that can be checked in the browser, shown live while registering so the user isn't
   * told the requirements one failed submit at a time.
   *
   * Every rule the browser can evaluate is mirrored here, so `canSubmit` below never lets through
   * something the server is certain to reject.
   *
   * The breach check is the one deliberate omission: mirroring it would mean the browser sending
   * password hash prefixes to a third party. That one stays server-side and surfaces on submit.
   */
  protected readonly requirements = computed<Requirement[]>(() => {
    const password = this.password();
    const local = this.email().split('@')[0]?.toLowerCase() ?? '';

    return [
      {
        label: `At least ${MIN_PASSWORD_LENGTH} characters`,
        met: [...password].length >= MIN_PASSWORD_LENGTH,
      },
      {
        label: `At most ${MAX_PASSWORD_BYTES} bytes (about ${MAX_PASSWORD_BYTES} characters)`,
        met:
          password.length > 0 &&
          new TextEncoder().encode(password).length <= MAX_PASSWORD_BYTES,
      },
      {
        label: "Doesn't contain your email",
        met:
          password.length > 0 &&
          (local.length < 3 || !password.toLowerCase().includes(local)),
      },
    ];
  });

  protected readonly canSubmit = computed(
    () => this.mode() === 'login' || this.requirements().every((r) => r.met)
  );

  protected toggleMode(): void {
    this.mode.update((m) => (m === 'login' ? 'register' : 'login'));
    this.error.set(null);
    this.fieldErrors.set({});
  }

  protected errorsFor(field: string): string[] {
    return this.fieldErrors()[field] ?? [];
  }

  protected async submit(event?: Event): Promise<void> {
    event?.preventDefault();
    this.error.set(null);
    this.fieldErrors.set({});
    this.busy.set(true);
    try {
      if (this.mode() === 'register') {
        await this.auth.register(this.email(), this.password(), this.inviteCode());
      } else {
        await this.auth.login(this.email(), this.password());
      }
      await this.router.navigate(['']);
    } catch (e) {
      // A 400 carries a per-field `errors` map, which is more useful than one summary line.
      // Gate and credential failures only carry `detail`, so both shapes are handled.
      const fieldErrors = this.validationErrorsOf(e);
      this.fieldErrors.set(fieldErrors);

      // The server sends `detail` as well as `errors` (it is what the older single-line rendering
      // reads), so showing both would print every message twice. When the errors are already
      // attached to their fields, the banner has nothing left to add.
      this.error.set(
        Object.keys(fieldErrors).length > 0
          ? null
          : this.messageOf(
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

  private validationErrorsOf(e: unknown): Record<string, string[]> {
    const errors = (e as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
    return errors && typeof errors === 'object' ? errors : {};
  }
}
