import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AntiforgeryToken, EmployeeSession, LoginCredentials } from './auth.models';

const sessionApi = '/api/v1/session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly antiforgeryToken = signal<AntiforgeryToken | null>(null);
  readonly session = signal<EmployeeSession | null>(null);

  async login(credentials: LoginCredentials): Promise<EmployeeSession> {
    const token = await this.getAntiforgeryToken();
    const session = await firstValueFrom(this.http.post<EmployeeSession>(
      `${sessionApi}/login`,
      credentials,
      {
        headers: new HttpHeaders({ [token.headerName]: token.requestToken }),
        withCredentials: true,
      },
    ));
    this.session.set(session);
    return session;
  }

  async refreshSession(): Promise<EmployeeSession | null> {
    try {
      const session = await firstValueFrom(this.http.get<EmployeeSession>(sessionApi, { withCredentials: true }));
      this.session.set(session);
      return session;
    } catch {
      this.session.set(null);
      return null;
    }
  }

  async logout(): Promise<void> {
    const token = await this.getAntiforgeryToken();
    await firstValueFrom(this.http.post<void>(
      `${sessionApi}/logout`,
      {},
      {
        headers: new HttpHeaders({ [token.headerName]: token.requestToken }),
        withCredentials: true,
      },
    ));
    this.session.set(null);
  }

  private async getAntiforgeryToken(): Promise<AntiforgeryToken> {
    const token = await firstValueFrom(this.http.get<AntiforgeryToken>(
      `${sessionApi}/antiforgery`,
      { withCredentials: true },
    ));
    this.antiforgeryToken.set(token);
    return token;
  }
}
