import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionResponse } from './models';

/**
 * Auth state for a session that lives in an HttpOnly cookie.
 *
 * The client holds no credential and cannot read one — the cookie is invisible to JavaScript,
 * which is the point (XSS can't steal what it can't see). What it holds instead is a *belief*
 * about whether the cookie is valid, resolved once from the server on startup.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly user = signal<SessionResponse | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly email = computed(() => this.user()?.email ?? null);
  /** Gates admin-only UI. The server enforces every /api/admin call regardless — this is for
   *  presentation, so a non-admin simply never sees the console. */
  readonly isAdmin = computed(() => this.user()?.role === 'Admin');

  /**
   * Cached so the probe runs once per app load however many guards await it. Without the cache
   * a first navigation that hits two guarded routes would fire two identical requests.
   */
  private probe: Promise<void> | null = null;

  /** Resolves auth state from the server. The guard awaits this before deciding to redirect. */
  async ensureResolved(): Promise<void> {
    this.probe ??= this.loadSession();
    return this.probe;
  }

  /**
   * Dormant since #36: public self-registration is closed server-side (the endpoint now 403s), so
   * nothing in the UI calls this. Kept — like the backend's InviteCodeRegistrationGate — so
   * re-opening signup is a small change rather than a rewrite.
   */
  async register(email: string, password: string, inviteCode: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<SessionResponse>('/api/auth/register', { email, password, inviteCode })
    );
    this.accept(res);
  }

  /**
   * Claims an admin-provisioned account: sets the chosen password and signs in. The token in the
   * URL is the authorization. See docs/feature-36-account-provisioning.md §8.4.
   */
  async claim(token: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<SessionResponse>(`/api/invites/${encodeURIComponent(token)}/claim`, { password })
    );
    this.accept(res);
  }

  async login(email: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<SessionResponse>('/api/auth/login', { email, password })
    );
    this.accept(res);
  }

  /**
   * Completes a password reset: sets the new password and signs in on this browser (the server
   * revoked every prior session). The token in the URL is the authorization. 410 if the link is no
   * longer valid, 400 on a policy failure. See docs/feature-26-password-reset.md §5.3.
   */
  async resetPassword(token: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<SessionResponse>(
        `/api/auth/reset-password/${encodeURIComponent(token)}`,
        { password }
      )
    );
    this.accept(res);
  }

  /**
   * Logout has to reach the server: the cookie is HttpOnly, so the browser will keep sending a
   * fully valid session no matter what this client forgets. Local state is cleared either way —
   * a failed request must not strand the user in a signed-in-looking app.
   */
  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
    } finally {
      this.clear();
    }
  }

  /** Called by the interceptor on a 401: the session died server-side, so drop the belief. */
  clear(): void {
    this.user.set(null);
    this.probe = Promise.resolve();
  }

  private accept(res: SessionResponse): void {
    this.user.set(res);
    this.probe = Promise.resolve();
  }

  private async loadSession(): Promise<void> {
    try {
      this.user.set(await firstValueFrom(this.http.get<SessionResponse>('/api/auth/session')));
    } catch {
      // 401 is the ordinary "not signed in" answer, not an error worth surfacing.
      this.user.set(null);
    }
  }
}
