import { Injectable, computed, inject, signal } from '@angular/core';
import { ProfileService } from './profile.service';
import { ProfileResponse } from './models';

/**
 * Caches the signed-in account's profile so the forced-change guard can read `mustChangePassword`
 * once per app load rather than re-fetching on every guarded navigation. Mirrors how AuthService
 * caches the session probe. See docs/feature-36-account-provisioning.md §7.3.
 */
@Injectable({ providedIn: 'root' })
export class ProfileStore {
  private readonly api = inject(ProfileService);

  readonly profile = signal<ProfileResponse | null>(null);
  readonly mustChangePassword = computed(() => this.profile()?.mustChangePassword ?? false);

  private probe: Promise<void> | null = null;

  /** Loads the profile once; subsequent callers share the same promise. */
  async ensureLoaded(): Promise<void> {
    this.probe ??= this.load();
    return this.probe;
  }

  /** Forces a fresh load — after login (a different account) or a password change (flag clears). */
  async refresh(): Promise<void> {
    this.probe = this.load();
    return this.probe;
  }

  /** Adopt a profile the caller already has (e.g. a PATCH response), skipping a round-trip. */
  set(profile: ProfileResponse): void {
    this.profile.set(profile);
    this.probe = Promise.resolve();
  }

  /** Drop cached state on logout so the next account starts clean. */
  clear(): void {
    this.profile.set(null);
    this.probe = null;
  }

  private async load(): Promise<void> {
    try {
      this.profile.set(await this.api.get());
    } catch {
      // Do NOT cache a failed load. Leave profile null AND clear the probe so the next
      // ensureLoaded retries. Otherwise a transient failure right after login would be cached as a
      // resolved probe, and passwordChangeGuard would read mustChangePassword=false and wave a
      // newly provisioned account through without the forced change. See feature-36 §7.3.
      this.profile.set(null);
      this.probe = null;
    }
  }
}
