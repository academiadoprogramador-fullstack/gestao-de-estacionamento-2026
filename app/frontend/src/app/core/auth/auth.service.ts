import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AntiforgeryToken, EmployeeSession, LoginCredentials } from './auth.models';

const sessionApi = '/api/v1/session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly antiforgeryToken = signal<AntiforgeryToken | null>(null);
  readonly session = signal<EmployeeSession | null>(null);
  readonly sessionResolved = signal(false);
  readonly accessDenied = signal(false);
  private sessionRequest: Promise<EmployeeSession | null> | null = null;

  async login(credentials: LoginCredentials): Promise<EmployeeSession> {
    const session = await firstValueFrom(
      this.http.post<EmployeeSession>(`${sessionApi}/login`, credentials, {
        withCredentials: true,
      }),
    );
    this.session.set(session);
    this.sessionResolved.set(true);
    this.accessDenied.set(false);
    this.antiforgeryToken.set(null);
    return session;
  }

  async refreshSession(): Promise<EmployeeSession | null> {
    try {
      const session = await firstValueFrom(
        this.http.get<EmployeeSession>(sessionApi, { withCredentials: true }),
      );
      this.session.set(session);
      this.sessionResolved.set(true);
      return session;
    } catch {
      this.session.set(null);
      this.sessionResolved.set(true);
      this.antiforgeryToken.set(null);
      return null;
    }
  }

  async ensureSession(): Promise<EmployeeSession | null> {
    if (this.sessionResolved()) return this.session();
    this.sessionRequest ??= this.refreshSession().finally(() => {
      this.sessionRequest = null;
    });
    return this.sessionRequest;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(
        this.http.post<void>(`${sessionApi}/logout`, {}, { withCredentials: true }),
      );
    } finally {
      this.session.set(null);
      this.sessionResolved.set(true);
      this.antiforgeryToken.set(null);
    }
  }

  async getAntiforgeryToken(): Promise<AntiforgeryToken> {
    const cached = this.antiforgeryToken();
    if (cached) return cached;
    const token = await firstValueFrom(
      this.http.get<AntiforgeryToken>(`${sessionApi}/antiforgery`, { withCredentials: true }),
    );
    this.antiforgeryToken.set(token);
    return token;
  }

  clearSession(): void {
    this.session.set(null);
    this.sessionResolved.set(true);
    this.antiforgeryToken.set(null);
  }
}
