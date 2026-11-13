import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AdminApiService } from '../admin/admin-api.service';
import { CheckInPage } from './check-in-page';

describe('CheckInPage', () => {
  const space = { id: 'space-1', code: 'A-01', isOccupied: false, version: 'v1' };
  const request = {
    plate: 'AB-123-CD',
    model: 'Car',
    ownerName: 'Owner Name',
    documentNumber: 'document-private',
    entryInspection: 'inspection-private',
    parkingSpaceId: space.id,
  };

  it('submits once, displays only the minimal result, clears PII and refreshes availability', async () => {
    let resolveCheckIn!: (value: {
      attendanceId: string;
      publicIdentifier: string;
      parkingSpaceId: string;
      state: string;
      enteredAtUtc: string;
    }) => void;
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 })),
      createCheckIn: vi.fn().mockImplementation(
        () =>
          new Promise((resolve) => {
            resolveCheckIn = resolve;
          }),
      ),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.model.set(request);
    const event = { preventDefault: vi.fn() } as unknown as Event;
    const firstSubmit = page.create(event);
    const duplicateSubmit = page.create(event);
    expect(api.createCheckIn).toHaveBeenCalledTimes(1);
    expect(page.pendingAttempt()).not.toBeNull();
    expect(page.confirmNavigationAway()).toBe(false);
    const unloadPending = new Event('beforeunload', { cancelable: true });
    globalThis.dispatchEvent(unloadPending);
    expect(unloadPending.defaultPrevented).toBe(true);
    const [content, key] = api.createCheckIn.mock.calls[0];
    expect(content).toEqual(request);
    expect(key).toMatch(/^[0-9a-f-]{36}$/i);
    resolveCheckIn({
      attendanceId: 'attendance-1',
      publicIdentifier: 'PUB-001',
      parkingSpaceId: space.id,
      state: 'active',
      enteredAtUtc: '2026-11-13T10:00:00Z',
    });
    await Promise.all([firstSubmit, duplicateSubmit]);
    expect(page.result()?.publicIdentifier).toBe('PUB-001');
    expect(page.pendingAttempt()).toBeNull();
    expect(page.selectedSpaceCode()).toBe('A-01');
    expect(page.model()).toEqual({
      plate: '',
      model: '',
      ownerName: '',
      documentNumber: '',
      entryInspection: '',
      parkingSpaceId: '',
    });
    expect(api.listSpaces).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.textContent).not.toContain('document-private');
    expect(fixture.nativeElement.textContent).not.toContain('inspection-private');
    expect(globalThis.localStorage.length).toBe(0);
    expect(globalThis.sessionStorage.length).toBe(0);
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('blocks submission when there is no free space and reports a missing tariff response', async () => {
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [], page: 1, pageSize: 100, total: 0 })),
      createCheckIn: vi
        .fn()
        .mockRejectedValue(
          new HttpErrorResponse({ status: 422, error: { code: 'daily_rate_not_configured' } }),
        ),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.model.set(request);
    await page.create({ preventDefault: vi.fn() } as unknown as Event);
    expect(api.createCheckIn).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Não há vagas livres');

    api.listSpaces.mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 }));
    page.loadSpaces();
    await page.create({ preventDefault: vi.fn() } as unknown as Event);
    expect(page.error()).toContain('tarifa diária ainda não foi configurada');
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('validates required fields before making a check-in request', async () => {
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 })),
      createCheckIn: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    await page.create({ preventDefault: vi.fn() } as unknown as Event);
    expect(api.createCheckIn).not.toHaveBeenCalled();
    expect(page.fieldErrors().length).toBeGreaterThan(0);
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(
      fixture.nativeElement.querySelector('#check-in-error-summary[role="alert"]'),
    ).not.toBeNull();
    expect(document.activeElement).toBe(
      fixture.nativeElement.querySelector('#check-in-error-summary'),
    );
    expect(fixture.nativeElement.querySelector('input[aria-required="true"]')).not.toBeNull();
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('keeps the same content and idempotency key in memory after an unknown result and replays explicitly', async () => {
    const created = {
      attendanceId: 'attendance-1',
      publicIdentifier: 'PUB-001',
      parkingSpaceId: space.id,
      state: 'active',
      enteredAtUtc: '2026-11-13T10:00:00Z',
    };
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 })),
      createCheckIn: vi
        .fn()
        .mockRejectedValueOnce(new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' }))
        .mockResolvedValueOnce(created),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.model.set(request);
    await page.create({ preventDefault: vi.fn() } as unknown as Event);

    expect(page.unknownAttempt()).not.toBeNull();
    expect(page.model()).toEqual(request);
    fixture.detectChanges();
    const [firstContent, firstKey] = api.createCheckIn.mock.calls[0];
    expect(fixture.nativeElement.textContent).toContain('Resultado ainda não confirmado');
    expect(fixture.nativeElement.querySelector('input')?.matches(':disabled')).toBe(true);
    const unload = new Event('beforeunload', { cancelable: true });
    globalThis.dispatchEvent(unload);
    expect(unload.defaultPrevented).toBe(true);

    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(false);
    expect(page.confirmNavigationAway()).toBe(false);
    expect(confirmSpy).toHaveBeenCalled();
    confirmSpy.mockRestore();

    await page.retryUnknownAttempt();
    expect(api.createCheckIn).toHaveBeenCalledTimes(2);
    expect(api.createCheckIn.mock.calls[1][0]).toEqual(firstContent);
    expect(api.createCheckIn.mock.calls[1][1]).toBe(firstKey);
    expect(page.unknownAttempt()).toBeNull();
    expect(page.result()?.publicIdentifier).toBe('PUB-001');
    expect(page.model().documentNumber).toBe('');
    expect(page.model().entryInspection).toBe('');
    expect(globalThis.localStorage.length).toBe(0);
    expect(globalThis.sessionStorage.length).toBe(0);
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it.each([
    [400, 'invalid_check_in'],
    [404, 'space_not_found'],
    [409, 'space_occupied'],
    [422, 'daily_rate_not_configured'],
  ])(
    'unlocks the form after a definitive replay rejection %s without automatically submitting a new key',
    async (status, code) => {
      const api = {
        listSpaces: vi
          .fn()
          .mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 })),
        createCheckIn: vi
          .fn()
          .mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }))
          .mockRejectedValueOnce(
            new HttpErrorResponse({ status: Number(status), error: { code } }),
          ),
      };
      TestBed.configureTestingModule({
        imports: [CheckInPage],
        providers: [{ provide: AdminApiService, useValue: api }],
      });
      const fixture = TestBed.createComponent(CheckInPage);
      fixture.detectChanges();
      const page = fixture.componentInstance;
      page.model.set(request);
      await page.create({ preventDefault: vi.fn() } as unknown as Event);
      await page.retryUnknownAttempt();
      expect(page.unknownAttempt()).toBeNull();
      expect(page.pendingAttempt()).toBeNull();
      expect(page.model()).toEqual(request);
      expect(api.createCheckIn).toHaveBeenCalledTimes(2);
      expect(api.createCheckIn.mock.calls[1]).toEqual(api.createCheckIn.mock.calls[0]);
      expect(page.confirmNavigationAway()).toBe(true);
      fixture.destroy();
      TestBed.resetTestingModule();
    },
  );

  it('keeps the original attempt when a replay is rejected before idempotency lookup', async () => {
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 })),
      createCheckIn: vi
        .fn()
        .mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }))
        .mockRejectedValueOnce(
          new HttpErrorResponse({ status: 400, error: { code: 'invalid_antiforgery' } }),
        ),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.model.set(request);
    await page.create({ preventDefault: vi.fn() } as unknown as Event);
    await page.retryUnknownAttempt();
    expect(page.unknownAttempt()).not.toBeNull();
    expect(api.createCheckIn.mock.calls[1]).toEqual(api.createCheckIn.mock.calls[0]);
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('does not reload availability or restore PII when a POST resolves after destruction', async () => {
    let resolve!: (value: object) => void;
    const api = {
      listSpaces: vi.fn().mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 })),
      createCheckIn: vi.fn(
        () =>
          new Promise((done) => {
            resolve = done;
          }),
      ),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.model.set(request);
    const pending = page.create({ preventDefault: vi.fn() } as unknown as Event);
    fixture.destroy();
    resolve({ attendanceId: 'late' });
    await pending;
    expect(api.listSpaces).toHaveBeenCalledTimes(1);
    expect(page.result()).toBeNull();
    expect(page.model().documentNumber).toBe('');
    TestBed.resetTestingModule();
  });

  it('pages through all free spaces, refreshes explicitly, and retains the rest of the form', () => {
    const first = Array.from({ length: 100 }, (_, index) => ({
      ...space,
      id: `space-${index}`,
      code: `A-${index}`,
    }));
    const second = [{ ...space, id: 'space-101', code: 'B-01' }];
    const api = {
      listSpaces: vi.fn((_: string, __: string, page: number) =>
        of({ items: page === 1 ? first : second, page, pageSize: 100, total: 101 }),
      ),
      createCheckIn: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    expect(page.spaces()).toHaveLength(100);
    page.model.set({ ...request, parkingSpaceId: 'space-0' });
    page.loadSpaces(2);
    expect(api.listSpaces).toHaveBeenLastCalledWith('', 'free', 2, 100);
    expect(page.spaces()).toEqual(second);
    expect(page.selectedSpaceSnapshot()?.id).toBe('space-0');
    page.loadSpaces();
    expect(api.listSpaces).toHaveBeenLastCalledWith('', 'free', 2, 100);
    expect(page.model()).toEqual({ ...request, parkingSpaceId: 'space-0' });
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('recovers from an initial availability failure through the explicit refresh control', () => {
    const api = {
      listSpaces: vi
        .fn()
        .mockReturnValueOnce(throwError(() => new Error('offline')))
        .mockReturnValue(of({ items: [space], page: 1, pageSize: 100, total: 1 })),
      createCheckIn: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [CheckInPage],
      providers: [{ provide: AdminApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(CheckInPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    expect(page.availabilityError()).toContain('Não foi possível carregar');
    page.model.set({ ...request, ownerName: 'Preservado' });
    page.loadSpaces();
    expect(page.availabilityError()).toBe('');
    expect(page.spaces()).toEqual([space]);
    expect(page.model().ownerName).toBe('Preservado');
    fixture.destroy();
    TestBed.resetTestingModule();
  });
});
