import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmailSettingsService } from '../../core/email-settings.service';
import { EmailProvider, EmailSettingsResponse, UpdateEmailSettingsRequest } from '../../core/models';
import { formatDate } from '../../core/file-type';
import { problemDetail, problemStatus, validationErrors } from '../../core/problem-details';
import { ButtonComponent } from '../../cove/lib/button/button.component';
import { IconComponent } from '../../cove/lib/icon/icon.component';
import { InputComponent } from '../../cove/lib/input/input.component';

/** Providers the admin can pick, with their labels. 'none' turns hosted email off (an env SMTP
 *  fallback, if configured, still applies — see the design §2.2). */
const PROVIDERS: { value: EmailProvider; label: string }[] = [
  { value: 'none', label: 'None (off)' },
  { value: 'resend', label: 'Resend' },
  { value: 'brevo', label: 'Brevo' },
  { value: 'mailgun', label: 'Mailgun' },
];

/**
 * Admin email-provider settings (#36, §7). Pick a hosted provider (Resend/Brevo/Mailgun) and paste
 * its API key, set the From address, and send a test — all at runtime, no redeploy. Reachable only
 * by admins via adminGuard; every call is server-gated by the "Admin" policy regardless. The API key
 * is write-only: it's never shown, only whether one is stored. See docs/feature-36-email-providers.md.
 */
@Component({
  selector: 'app-email-settings',
  imports: [RouterLink, ButtonComponent, IconComponent, InputComponent],
  templateUrl: './email-settings.html',
  styleUrl: './email-settings.scss',
})
export class EmailSettings {
  private readonly api = inject(EmailSettingsService);

  protected readonly providers = PROVIDERS;
  protected readonly formatDate = formatDate;

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  /** The last-saved settings, to diff against for the dirty check and to render last-test status. */
  protected readonly snapshot = signal<EmailSettingsResponse | null>(null);

  // Form fields.
  protected readonly provider = signal<EmailProvider>('none');
  protected readonly fromAddress = signal('');
  protected readonly fromName = signal('');
  protected readonly mailgunDomain = signal('');
  protected readonly mailgunRegion = signal('us');
  protected readonly publicBaseUrl = signal('');
  protected readonly inviteExpiryDays = signal('7');

  // API key is write-only: entered fresh, never read back.
  protected readonly apiKey = signal('');
  protected readonly showApiKey = signal(false);
  /** True once the admin chooses to replace a key that's already stored. */
  protected readonly replacingKey = signal(false);

  protected readonly saving = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Record<string, string[]>>({});

  protected readonly testing = signal(false);

  protected readonly isHosted = computed(() => this.provider() !== 'none');
  protected readonly isMailgun = computed(() => this.provider() === 'mailgun');

  /** A stored key exists for the *currently-selected* provider (only meaningful when unchanged). */
  protected readonly hasStoredKey = computed(() => {
    const s = this.snapshot();
    return !!s?.hasApiKey && this.provider() === s.provider;
  });

  /** A new key must be entered: a hosted provider with no reusable stored key (switched provider, or
   *  none was ever stored). */
  protected readonly keyRequired = computed(() => this.isHosted() && !this.hasStoredKey());

  /** Show the key input vs. the "configured" chip. */
  protected readonly showKeyInput = computed(
    () => this.isHosted() && (this.keyRequired() || this.replacingKey()));

  /** Unsaved edits — drives the Save enablement and disables Test (which uses saved settings). */
  protected readonly dirty = computed(() => {
    const s = this.snapshot();
    if (!s) return false;
    if (this.apiKey().length > 0) return true;
    return (
      this.provider() !== s.provider ||
      this.fromAddress() !== s.fromAddress ||
      this.fromName() !== s.fromName ||
      (this.mailgunDomain() || '') !== (s.mailgunDomain ?? '') ||
      (this.isMailgun() && this.mailgunRegion() !== (s.mailgunRegion ?? 'us')) ||
      this.publicBaseUrl() !== s.publicBaseUrl ||
      Number(this.inviteExpiryDays()) !== s.inviteExpiryDays
    );
  });

  protected readonly canSave = computed(() => {
    if (this.saving()) return false;
    if (!this.isHosted()) return this.dirty(); // 'none' just needs a change to be worth saving
    if (!this.fromAddress().trim()) return false;
    if (this.keyRequired() && !this.apiKey().trim()) return false;
    if (this.isMailgun() && !this.mailgunDomain().trim()) return false;
    return this.dirty();
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.apply(await this.api.get());
    } catch {
      this.loadError.set('Could not load email settings.');
    } finally {
      this.loading.set(false);
    }
  }

  /** Reset the form to a settings response (after load or a successful save). */
  private apply(s: EmailSettingsResponse): void {
    this.snapshot.set(s);
    this.provider.set(s.provider);
    this.fromAddress.set(s.fromAddress);
    this.fromName.set(s.fromName);
    this.mailgunDomain.set(s.mailgunDomain ?? '');
    this.mailgunRegion.set(s.mailgunRegion ?? 'us');
    this.publicBaseUrl.set(s.publicBaseUrl);
    this.inviteExpiryDays.set(String(s.inviteExpiryDays));
    this.apiKey.set('');
    this.showApiKey.set(false);
    this.replacingKey.set(false);
    this.fieldErrors.set({});
    this.formError.set(null);
  }

  protected errorsFor(field: string): string[] {
    return this.fieldErrors()[field] ?? [];
  }

  /** Changing the provider clears any typed key: a key is provider-specific and can't carry over. */
  protected onProviderChange(value: string): void {
    this.provider.set(value as EmailProvider);
    this.apiKey.set('');
    this.replacingKey.set(false);
    this.notice.set(null);
  }

  protected startReplacingKey(): void {
    this.replacingKey.set(true);
    this.apiKey.set('');
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;
    this.saving.set(true);
    this.notice.set(null);
    this.formError.set(null);
    this.fieldErrors.set({});

    const hosted = this.isHosted();
    const req: UpdateEmailSettingsRequest = {
      provider: this.provider(),
      fromAddress: hosted ? this.fromAddress().trim() : undefined,
      fromName: hosted ? this.fromName().trim() : undefined,
      // Send the key only when one was entered; otherwise the server keeps the stored key.
      apiKey: this.apiKey().trim() || undefined,
      mailgunDomain: this.isMailgun() ? this.mailgunDomain().trim() : undefined,
      mailgunRegion: this.isMailgun() ? this.mailgunRegion() : undefined,
      publicBaseUrl: this.publicBaseUrl().trim(),
      inviteExpiryDays: Number(this.inviteExpiryDays()),
    };

    try {
      this.apply(await this.api.update(req));
      this.notice.set('Email settings saved.');
    } catch (e) {
      const fieldErrors = validationErrors(e);
      if (Object.keys(fieldErrors).length > 0) {
        this.fieldErrors.set(fieldErrors);
      } else {
        this.formError.set(problemDetail(e, 'Could not save the settings.'));
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected async sendTest(): Promise<void> {
    if (this.testing() || this.dirty()) return;
    this.testing.set(true);
    this.notice.set(null);
    this.formError.set(null);
    try {
      const result = await this.api.sendTest();
      // Refresh so the persisted last-test status (time + ok/error) shows.
      this.apply(await this.api.get());
      this.notice.set(result.ok ? 'Test email sent — check your inbox.' : null);
    } catch (e) {
      // 409 = email not configured; anything else = an unexpected failure.
      if (problemStatus(e) === 409) {
        this.formError.set(problemDetail(e, 'Email is not configured.'));
      } else {
        this.formError.set(problemDetail(e, 'Could not send the test email.'));
      }
    } finally {
      this.testing.set(false);
    }
  }
}
