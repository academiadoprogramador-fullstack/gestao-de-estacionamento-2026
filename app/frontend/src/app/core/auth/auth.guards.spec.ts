import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { routes } from '../../app.routes';
import { AuthService } from './auth.service';

describe('role-protected routes', () => {
  it('redirects an expired cookie session to login', async () => {
    const authMock = { ensureSession: vi.fn().mockResolvedValue(null) };
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), { provide: AuthService, useValue: authMock }],
    });
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/employees');
    expect(router.url).toBe('/login?returnUrl=%2Femployees');
    expect(authMock.ensureSession).toHaveBeenCalled();
  });

  it('allows administrators and redirects profiles without permission', async () => {
    const authMock = {
      ensureSession: vi
        .fn()
        .mockResolvedValue({ userId: 'u', userName: 'employee', roles: ['admin'] }),
    };
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), { provide: AuthService, useValue: authMock }],
    });
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/employees');
    expect(router.url).toBe('/employees');

    authMock.ensureSession.mockResolvedValue({
      userId: 'u',
      userName: 'employee',
      roles: ['manobrista'],
    });
    await router.navigateByUrl('/daily-rates');
    expect(router.url).toBe('/forbidden');
  });
});
