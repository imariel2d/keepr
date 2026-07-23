import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthResponse } from './models';

const TOKEN_KEY = 'media.jwt';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly token = signal<string | null>(localStorage.getItem(TOKEN_KEY));
  readonly isAuthenticated = computed(() => this.token() !== null);

  currentToken(): string | null {
    return this.token();
  }

  /** Signups are gated: the server rejects a missing or wrong invite code with 403. */
  async register(email: string, password: string, inviteCode: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/register', { email, password, inviteCode })
    );
    this.setToken(res.accessToken);
  }

  async login(email: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>('/api/auth/login', { email, password })
    );
    this.setToken(res.accessToken);
  }

  logout(): void {
    this.token.set(null);
    localStorage.removeItem(TOKEN_KEY);
  }

  private setToken(value: string): void {
    this.token.set(value);
    localStorage.setItem(TOKEN_KEY, value);
  }
}
