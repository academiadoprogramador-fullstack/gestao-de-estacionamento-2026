import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { AuthService } from './core/auth/auth.service';
import { App } from './app';
import { vi } from 'vitest';
import { provideRouter, Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { EmployeeSession } from './core/auth/auth.models';
import { routes } from './app.routes';

@Component({ standalone: true, template: '' })
class PendingPage {
  readonly confirmNavigationAway = vi.fn(() => false);
}

describe('App', () => {
  const authMock = {
    session: signal<EmployeeSession | null>(null),
    refreshSession: vi.fn().mockResolvedValue(null),
    ensureSession: vi.fn().mockResolvedValue(null),
    accessDenied: signal(false),
    login: vi.fn(),
    logout: vi.fn(),
  };

  beforeEach(async () => {
    authMock.session.set(null);
    authMock.refreshSession.mockClear();
    authMock.ensureSession.mockClear();
    authMock.login.mockReset();
    authMock.logout.mockReset();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [{ provide: AuthService, useValue: authMock }, provideRouter(routes)],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the accessible local foundation page without ZoneJS', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('main h1')?.textContent).toContain(
      'Operação segura começa aqui.',
    );
    expect(compiled.querySelector('a[href="#main-content"]')?.textContent).toContain(
      'Pular para o conteúdo',
    );
    expect(compiled.querySelector('main')?.getAttribute('tabindex')).toBe('-1');
  });

  it('shows accessible field errors after an empty keyboard-submittable form is submitted', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const email = compiled.querySelector<HTMLInputElement>('#email');
    const password = compiled.querySelector<HTMLInputElement>('#password');
    const form = compiled.querySelector<HTMLFormElement>('form');
    expect(email?.getAttribute('aria-label')).toBeNull();
    expect(compiled.querySelector('label[for="email"]')?.textContent).toContain('E-mail');
    expect(compiled.querySelector('label[for="password"]')?.textContent).toContain('Senha');
    expect(email?.tabIndex).toBe(0);
    expect(password?.tabIndex).toBe(0);

    form?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled.textContent).toContain('Informe seu e-mail.');
    expect(compiled.textContent).toContain('Informe sua senha.');
    expect(authMock.login).not.toHaveBeenCalled();
  });

  it('shows a generic text error when the API rejects the login', async () => {
    authMock.login.mockRejectedValue({ status: 401 });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const email = compiled.querySelector<HTMLInputElement>('#email')!;
    const password = compiled.querySelector<HTMLInputElement>('#password')!;
    email.value = 'operator@example.invalid';
    email.dispatchEvent(new Event('input', { bubbles: true }));
    password.value = 'incorrect-password';
    password.dispatchEvent(new Event('input', { bubbles: true }));
    compiled
      .querySelector('form')
      ?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(authMock.login).toHaveBeenCalledWith({
      email: 'operator@example.invalid',
      password: 'incorrect-password',
    });
    expect(compiled.querySelector('[role="alert"]')?.textContent).toContain(
      'Confira suas credenciais',
    );
  });

  it('checks the active page before logging out, preserving a pending check-in', async () => {
    TestBed.resetTestingModule();
    authMock.session.set({ userId: 'u1', userName: 'cashier', roles: ['caixa'] });
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        { provide: AuthService, useValue: authMock },
        provideRouter([
          { path: 'pending', component: PendingPage },
          { path: 'login', component: PendingPage },
        ]),
      ],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/pending');
    fixture.detectChanges();
    const page = fixture.debugElement.query(By.directive(PendingPage))
      .componentInstance as PendingPage;
    await fixture.componentInstance.logout();
    expect(page.confirmNavigationAway).toHaveBeenCalled();
    expect(authMock.logout).not.toHaveBeenCalled();
    page.confirmNavigationAway.mockReturnValue(true);
    await fixture.componentInstance.logout();
    expect(authMock.logout).toHaveBeenCalledTimes(1);
    fixture.destroy();
  });
});
