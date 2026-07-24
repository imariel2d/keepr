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

  /** Signups are gated: the server rejects a missing or wrong invite code with 403. */
  async register(email: string, password: string, inviteCode: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<SessionResponse>('/api/auth/register', { email, password, inviteCode })
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
