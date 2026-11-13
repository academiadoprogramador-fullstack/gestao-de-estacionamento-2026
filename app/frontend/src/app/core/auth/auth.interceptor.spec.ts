import { provideHttpClient, withInterceptors, HttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { routes } from '../../app.routes';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let requests: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    });
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => {
    requests.verify();
    TestBed.resetTestingModule();
  });

  it('fetches antiforgery token and adds it to API mutations with cookie credentials', async () => {
    http.post('/api/v1/spaces', { code: 'A-01' }).subscribe();
    const tokenRequest = requests.expectOne('/api/v1/session/antiforgery');
    expect(tokenRequest.request.method).toBe('GET');
    tokenRequest.flush({ requestToken: 'request-token', headerName: 'X-CSRF-TOKEN' });
    await Promise.resolve();
    await Promise.resolve();

    const mutation = requests.expectOne('/api/v1/spaces');
    expect(mutation.request.headers.get('X-CSRF-TOKEN')).toBe('request-token');
    expect(mutation.request.withCredentials).toBe(true);
    mutation.flush({ id: 'space-id' });
  });

  it('uses antiforgery for login and logout, updating only in-memory session state', async () => {
    const loginPromise = auth.login({ email: 'staff@example.invalid', password: 'secret' });
    requests.expectOne('/api/v1/session/antiforgery').flush({
      requestToken: 'login-token',
      headerName: 'X-CSRF-TOKEN',
    });
    await Promise.resolve();
    await Promise.resolve();
    const loginRequest = requests.expectOne('/api/v1/session/login');
    expect(loginRequest.request.headers.get('X-CSRF-TOKEN')).toBe('login-token');
    expect(loginRequest.request.withCredentials).toBe(true);
    loginRequest.flush({ userId: 'u1', userName: 'staff', roles: ['caixa'] });
    await expect(loginPromise).resolves.toEqual({
      userId: 'u1',
      userName: 'staff',
      roles: ['caixa'],
    });
    expect(auth.session()?.userId).toBe('u1');

    const logoutPromise = auth.logout();
    const logoutTokenRequest = requests.expectOne('/api/v1/session/antiforgery');
    logoutTokenRequest.flush({ requestToken: 'logout-token', headerName: 'X-CSRF-TOKEN' });
    await Promise.resolve();
    await Promise.resolve();
    const logoutRequest = requests.expectOne('/api/v1/session/logout');
    expect(logoutRequest.request.headers.get('X-CSRF-TOKEN')).toBe('logout-token');
    expect(logoutRequest.request.withCredentials).toBe(true);
    logoutRequest.flush(null);
    await expect(logoutPromise).resolves.toBeUndefined();
    expect(auth.session()).toBeNull();

    http.post('/api/v1/spaces', { code: 'A-02' }).subscribe();
    requests.expectOne('/api/v1/session/antiforgery').flush({
      requestToken: 'post-logout-token',
      headerName: 'X-CSRF-TOKEN',
    });
    await Promise.resolve();
    await Promise.resolve();
    const postLogoutMutation = requests.expectOne('/api/v1/spaces');
    expect(postLogoutMutation.request.headers.get('X-CSRF-TOKEN')).toBe('post-logout-token');
    postLogoutMutation.flush({ id: 'space-after-logout' });

    expect(globalThis.localStorage.length).toBe(0);
    expect(globalThis.sessionStorage.length).toBe(0);
  });

  it('clears in-memory session after an API 401 without browser storage', () => {
    auth.session.set({ userId: 'user-id', userName: 'operator', roles: ['caixa'] });
    auth.sessionResolved.set(true);
    http.get('/api/v1/session').subscribe({ error: () => undefined });
    requests.expectOne('/api/v1/session').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.session()).toBeNull();
    expect(auth.sessionResolved()).toBe(true);
    expect(globalThis.localStorage.length).toBe(0);
    expect(globalThis.sessionStorage.length).toBe(0);
  });

  it('marks 403 responses as access denied', () => {
    http.get('/api/v1/employees').subscribe({ error: () => undefined });
    requests.expectOne('/api/v1/employees').flush({}, { status: 403, statusText: 'Forbidden' });
    expect(auth.accessDenied()).toBe(true);
  });
});
