import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { AdminApiService } from './admin-api.service';
import {
  DailyRatesPage,
  EmployeesPage,
  SPACE_AVAILABILITY_POLL_INTERVAL_MS,
  SpacesPage,
} from './admin-pages';

describe('administrative pages', () => {
  const employee = {
    id: 'employee-1',
    email: 'staff@example.invalid',
    isActive: true,
    roles: ['caixa' as const],
    concurrencyStamp: 'version-1',
  };

  it('clears the initial password after employee creation and refreshes the list', async () => {
    const api = {
      listEmployees: vi
        .fn()
        .mockResolvedValue({ items: [employee], page: 1, pageSize: 20, total: 1 }),
      createEmployee: vi.fn().mockResolvedValue(employee),
    };
    TestBed.configureTestingModule({
      imports: [EmployeesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(EmployeesPage);
    const page = fixture.componentInstance;
    page.employeeModel.set({
      email: 'staff@example.invalid',
      initialPassword: 'A-Strong-Initial-Pass-2026',
    });
    fixture.detectChanges();
    await page.create({ preventDefault: vi.fn() } as unknown as Event);

    expect(api.createEmployee).toHaveBeenCalledWith(
      'staff@example.invalid',
      'A-Strong-Initial-Pass-2026',
      ['caixa'],
    );
    expect(page.employeeModel().initialPassword).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain('A-Strong-Initial-Pass-2026');
    expect(api.listEmployees).toHaveBeenCalled();
    TestBed.resetTestingModule();
  });

  it('does not issue an employee creation request for invalid form values', async () => {
    const api = { createEmployee: vi.fn(), listEmployees: vi.fn() };
    TestBed.configureTestingModule({
      imports: [EmployeesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(EmployeesPage);
    const page = fixture.componentInstance;
    page.employeeModel.set({ email: 'not-an-email', initialPassword: 'short' });
    await page.create({ preventDefault: vi.fn() } as unknown as Event);
    expect(api.createEmployee).not.toHaveBeenCalled();
    expect(page.validationErrors().length).toBeGreaterThan(0);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#employee-error-summary')?.textContent).toContain(
      'e-mail',
    );
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('shows and focuses validation feedback for invalid space creation', async () => {
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [], page: 1, pageSize: 20, total: 0 })),
      createSpace: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [SpacesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        {
          provide: AuthService,
          useValue: { session: signal({ userId: 'a', userName: 'admin', roles: ['admin'] }) },
        },
        { provide: SPACE_AVAILABILITY_POLL_INTERVAL_MS, useValue: 60_000 },
      ],
    });
    const fixture = TestBed.createComponent(SpacesPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.spaceModel.set({ code: 'invalid space' });
    await page.create({ preventDefault: vi.fn() } as unknown as Event);
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(api.createSpace).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('#space-error-summary')?.textContent).toContain(
      'hífen',
    );
    expect(document.activeElement?.id).toBe('space-error-summary');
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('shows and focuses validation feedback for invalid tariff publication', async () => {
    const api = {
      currentRate: vi.fn().mockResolvedValue({ currentRate: null, version: 'v1' }),
      listRates: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
      publishRate: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [DailyRatesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(DailyRatesPage);
    await fixture.componentInstance.load();
    fixture.componentInstance.rateModel.set({ amount: '0.00' });
    await fixture.componentInstance.publish({ preventDefault: vi.fn() } as unknown as Event);
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(api.publishRate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.validationErrors().length).toBeGreaterThan(0);
    expect(fixture.nativeElement.querySelector('#rate-error-summary')?.textContent).toContain(
      'positivo',
    );
    expect(document.activeElement?.id).toBe('rate-error-summary');
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('shows role-gated space creation only for administrators', () => {
    const authMock = { session: signal({ userId: 'u', userName: 'staff', roles: ['manobrista'] }) };
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [], page: 1, pageSize: 20, total: 0 })),
    };
    TestBed.configureTestingModule({
      imports: [SpacesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        { provide: AuthService, useValue: authMock },
      ],
    });
    const fixture = TestBed.createComponent(SpacesPage);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Cadastrar vaga');
    expect(fixture.nativeElement.textContent).toContain('A disponibilidade exibida é informativa');
    TestBed.resetTestingModule();
  });

  it('refetches availability after an administrator creates a space', async () => {
    vi.useFakeTimers();
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [], page: 1, pageSize: 20, total: 0 })),
      createSpace: vi
        .fn()
        .mockResolvedValue({ id: 'space-1', code: 'A-01', isOccupied: false, version: 'v1' }),
    };
    const authMock = { session: signal({ userId: 'u', userName: 'admin', roles: ['admin'] }) };
    TestBed.configureTestingModule({
      imports: [SpacesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        { provide: AuthService, useValue: authMock },
        { provide: SPACE_AVAILABILITY_POLL_INTERVAL_MS, useValue: 15_000 },
      ],
    });
    const fixture = TestBed.createComponent(SpacesPage);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    const page = fixture.componentInstance;
    page.spaceModel.set({ code: 'A-01' });
    await page.create({ preventDefault: vi.fn() } as unknown as Event);
    expect(api.createSpace).toHaveBeenCalledWith('A-01');
    expect(api.listSpaces).toHaveBeenCalledTimes(2);
    fixture.destroy();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('polls without overlap, pauses while hidden, refreshes on focus, and tears down on navigation', async () => {
    vi.useFakeTimers();
    const originalVisibility = Object.getOwnPropertyDescriptor(document, 'visibilityState');
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    const pending: Subject<{ items: never[]; page: number; pageSize: number; total: number }>[] =
      [];
    const api = {
      listSpaces: vi.fn(() => {
        const response = new Subject<{
          items: never[];
          page: number;
          pageSize: number;
          total: number;
        }>();
        pending.push(response);
        return response;
      }),
      createSpace: vi.fn(),
    };
    const authMock = { session: signal({ userId: 'u', userName: 'admin', roles: ['admin'] }) };
    TestBed.configureTestingModule({
      imports: [SpacesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        { provide: AuthService, useValue: authMock },
        { provide: SPACE_AVAILABILITY_POLL_INTERVAL_MS, useValue: 1_000 },
      ],
    });
    const fixture = TestBed.createComponent(SpacesPage);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    expect(api.listSpaces).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(5_000);
    expect(api.listSpaces).toHaveBeenCalledTimes(1);

    pending[0].next({ items: [], page: 1, pageSize: 20, total: 0 });
    pending[0].complete();
    globalThis.dispatchEvent(new Event('focus'));
    expect(api.listSpaces).toHaveBeenCalledTimes(2);

    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
    pending[1].next({ items: [], page: 1, pageSize: 20, total: 0 });
    pending[1].complete();
    await vi.advanceTimersByTimeAsync(5_000);
    expect(api.listSpaces).toHaveBeenCalledTimes(2);

    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);
    expect(api.listSpaces).toHaveBeenCalledTimes(3);
    expect(pending[2].observed).toBe(true);
    fixture.destroy();
    expect(pending[2].observed).toBe(false);
    await vi.advanceTimersByTimeAsync(5_000);
    expect(api.listSpaces).toHaveBeenCalledTimes(3);
    TestBed.resetTestingModule();
    vi.useRealTimers();
    if (originalVisibility) Object.defineProperty(document, 'visibilityState', originalVisibility);
    else Reflect.deleteProperty(document, 'visibilityState');
  });

  it('reloads employee data after an optimistic-concurrency conflict', async () => {
    const api = {
      listEmployees: vi
        .fn()
        .mockResolvedValue({ items: [employee], page: 1, pageSize: 20, total: 1 }),
      updateEmployee: vi
        .fn()
        .mockRejectedValue(new HttpErrorResponse({ status: 409, statusText: 'Conflict' })),
    };
    TestBed.configureTestingModule({
      imports: [EmployeesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const page = TestBed.createComponent(EmployeesPage).componentInstance;
    await page.load(1);
    page.editRoles.set({ [employee.id]: ['admin'] });
    await page.saveAccess(employee);
    expect(page.error()).toContain('mudou em outra sessão');
    expect(api.listEmployees).toHaveBeenCalledTimes(2);
    TestBed.resetTestingModule();
  });

  it('refreshes employee state after a successful profile and activation update', async () => {
    const updated = {
      ...employee,
      isActive: false,
      roles: ['manobrista' as const],
      concurrencyStamp: 'version-2',
    };
    const api = {
      listEmployees: vi
        .fn()
        .mockResolvedValue({ items: [updated], page: 1, pageSize: 20, total: 1 }),
      updateEmployee: vi
        .fn()
        .mockResolvedValue({ employee: updated, concurrencyStamp: 'version-2' }),
    };
    TestBed.configureTestingModule({
      imports: [EmployeesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const page = TestBed.createComponent(EmployeesPage).componentInstance;
    await page.load(1);
    page.editRoles.set({ [employee.id]: ['manobrista'] });
    await page.setActive(employee, false);
    expect(api.updateEmployee).toHaveBeenCalledWith(employee, false, ['manobrista']);
    expect(page.employees()[0].isActive).toBe(false);
    expect(page.employees()[0].concurrencyStamp).toBe('version-2');
    expect(page.message()).toContain('Acesso atualizado');
    TestBed.resetTestingModule();
  });

  it('refreshes the tariff version after a publication conflict', async () => {
    const config1 = { currentRate: null, version: 'version-1' };
    const config2 = { currentRate: null, version: 'version-2' };
    const api = {
      currentRate: vi.fn().mockResolvedValueOnce(config1).mockResolvedValueOnce(config2),
      listRates: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
      publishRate: vi
        .fn()
        .mockRejectedValue(new HttpErrorResponse({ status: 409, statusText: 'Conflict' })),
    };
    TestBed.configureTestingModule({
      imports: [DailyRatesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const page = TestBed.createComponent(DailyRatesPage).componentInstance;
    await page.load();
    page.rateModel.set({ amount: '0.50' });
    await page.publish({ preventDefault: vi.fn() } as unknown as Event);
    expect(api.publishRate).toHaveBeenCalledWith(0.5, 'version-1');
    expect(page.configuration()?.version).toBe('version-2');
    expect(page.error()).toContain('versão vigente mudou');
    TestBed.resetTestingModule();
  });

  it('rejects a non-positive tariff before calling the API', async () => {
    const api = {
      currentRate: vi.fn().mockResolvedValue({ currentRate: null, version: 'version-1' }),
      listRates: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
      publishRate: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [DailyRatesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const page = TestBed.createComponent(DailyRatesPage).componentInstance;
    await page.load();
    page.rateModel.set({ amount: '0.00' });
    await page.publish({ preventDefault: vi.fn() } as unknown as Event);
    expect(api.publishRate).not.toHaveBeenCalled();
    TestBed.resetTestingModule();
  });

  it('loads the requested tariff history page and exposes pagination', async () => {
    const history = (page: number) => ({
      items: [
        {
          id: `rate-${page}`,
          amount: page,
          publishedAtUtc: '2026-01-01T00:00:00Z',
          publishedById: 'admin',
        },
      ],
      page,
      pageSize: 20,
      total: 45,
    });
    const api = {
      currentRate: vi.fn().mockResolvedValue({ currentRate: null, version: 'version' }),
      listRates: vi.fn().mockImplementation((page = 1) => Promise.resolve(history(page))),
    };
    TestBed.configureTestingModule({
      imports: [DailyRatesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(DailyRatesPage);
    fixture.detectChanges();
    await fixture.componentInstance.load();
    expect(fixture.componentInstance.rateTotal()).toBe(45);
    await fixture.componentInstance.load(2);
    expect(api.listRates).toHaveBeenLastCalledWith(2, 20);
    expect(fixture.componentInstance.rates()[0].id).toBe('rate-2');
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('recovers from an initial tariff load failure using the explicit refresh action', async () => {
    const api = {
      currentRate: vi
        .fn()
        .mockRejectedValueOnce(new Error('offline'))
        .mockResolvedValue({ currentRate: null, version: 'v1' }),
      listRates: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
    };
    TestBed.configureTestingModule({
      imports: [DailyRatesPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(DailyRatesPage);
    await fixture.componentInstance.load();
    expect(fixture.componentInstance.configuration()).toBeNull();
    expect(fixture.componentInstance.error()).toContain('Não foi possível carregar');
    await fixture.componentInstance.load();
    expect(fixture.componentInstance.configuration()?.version).toBe('v1');
    expect(api.currentRate).toHaveBeenCalledTimes(2);
    fixture.destroy();
    TestBed.resetTestingModule();
  });
});
