import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import {
  AdminApiService,
  AttendanceDetail,
  AttendanceHistoryPage,
  AttendancePage,
} from '../admin/admin-api.service';
import { AuthService } from '../auth/auth.service';
import { AttendancesPage } from './attendances-page';

describe('AttendancesPage', () => {
  function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>((done) => {
      resolve = done;
    });
    return { promise, resolve };
  }
  function createPage(api: object) {
    TestBed.configureTestingModule({
      imports: [AttendancesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        { provide: AuthService, useValue: { session: signal({ roles: ['caixa'] }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
      ],
    });
    return TestBed.createComponent(AttendancesPage);
  }
  const item = {
    id: 'attendance-1',
    publicIdentifier: 'PUB-001',
    plate: 'AB-123-CD',
    model: 'Car',
    enteredAtUtc: '2026-11-13T10:00:00Z',
    state: 'active',
    parkingSpaceId: 'space-1',
    parkingSpaceCode: 'A-01',
  };
  const detail = {
    ...item,
    ownerName: 'Owner Name',
    operatorId: 'operator-1',
  };
  const historyPage = {
    items: [
      {
        actorId: 'operator-1',
        action: 'attendance.checked_in',
        targetId: item.id,
        occurredAtUtc: '2026-11-13T10:00:00Z',
        metadata: {},
      },
    ],
    page: 1,
    pageSize: 20,
    total: 1,
  };

  it('requires one exact criterion and renders an explicit empty result', async () => {
    const api = {
      searchAttendances: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
    };
    TestBed.configureTestingModule({
      imports: [AttendancesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        { provide: AuthService, useValue: { session: signal({ roles: ['caixa'] }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
      ],
    });
    const fixture = TestBed.createComponent(AttendancesPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    await page.search({ preventDefault: vi.fn() } as unknown as Event);
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(page.queryErrors()).toContain('Informe uma placa ou um identificador público.');
    expect(
      fixture.nativeElement.querySelector('#attendance-error-summary[role="alert"]'),
    ).not.toBeNull();
    expect(document.activeElement).toBe(
      fixture.nativeElement.querySelector('#attendance-error-summary'),
    );
    expect(api.searchAttendances).not.toHaveBeenCalled();
    page.query.set({ plate: item.plate, identifier: item.publicIdentifier });
    await page.search({ preventDefault: vi.fn() } as unknown as Event);
    expect(page.queryErrors()).toContain('Informe apenas um critério de busca por vez.');
    page.query.set({ plate: item.plate, identifier: '' });
    await page.search({ preventDefault: vi.fn() } as unknown as Event);
    fixture.detectChanges();
    expect(api.searchAttendances).toHaveBeenCalledWith(item.plate, '', 1);
    expect(fixture.nativeElement.textContent).toContain('Nenhum atendimento encontrado.');
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('shows details, paginates history, and reads sensitive values only after explicit actions', async () => {
    const api = {
      searchAttendances: vi
        .fn()
        .mockResolvedValue({ items: [item], page: 1, pageSize: 20, total: 21 }),
      getAttendance: vi
        .fn()
        .mockResolvedValue({ ...detail, dailyRateSnapshot: 12.34, dailyRateId: 'rate-1' }),
      getAttendanceHistory: vi.fn().mockResolvedValue(historyPage),
      readAttendanceDocument: vi.fn().mockResolvedValue({ value: 'document-secret' }),
      readEntryInspection: vi.fn().mockResolvedValue({ value: 'inspection-secret' }),
    };
    TestBed.configureTestingModule({
      imports: [AttendancesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        { provide: AuthService, useValue: { session: signal({ roles: ['caixa'] }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
      ],
    });
    const fixture = TestBed.createComponent(AttendancesPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    page.query.set({ plate: item.plate, identifier: '' });
    await page.search({ preventDefault: vi.fn() } as unknown as Event);
    expect(page.total()).toBe(21);
    await page.open(item);
    fixture.detectChanges();
    expect(api.getAttendanceHistory).toHaveBeenCalledWith(item.id, 1);
    expect(fixture.nativeElement.textContent).toContain('Tarifa diária registrada');
    expect(fixture.nativeElement.textContent).toContain('Ler documento do titular');
    expect(fixture.nativeElement.textContent).not.toContain('document-secret');
    await page.readInspection();
    expect(api.readEntryInspection).toHaveBeenCalledWith(item.id);
    expect(page.sensitiveValue()).toBe('inspection-secret');
    await page.readDocument();
    expect(api.readAttendanceDocument).toHaveBeenCalledWith(item.id);
    expect(page.sensitiveValue()).toBe('document-secret');
    await page.loadHistory(2);
    expect(api.getAttendanceHistory).toHaveBeenLastCalledWith(item.id, 2);
    await page.open(item);
    expect(page.sensitiveValue()).toBe('');
    expect(api.getAttendance).toHaveBeenCalledTimes(2);
    expect(api.getAttendanceHistory).toHaveBeenCalledTimes(3);
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('does not expose financial fields or offer document reading to valet users', async () => {
    const valetDetail = { ...detail };
    const api = {
      getAttendance: vi.fn().mockResolvedValue(valetDetail),
      getAttendanceHistory: vi
        .fn()
        .mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
      readAttendanceDocument: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [AttendancesPage],
      providers: [
        { provide: AdminApiService, useValue: api },
        { provide: AuthService, useValue: { session: signal({ roles: ['manobrista'] }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
      ],
    });
    const fixture = TestBed.createComponent(AttendancesPage);
    fixture.detectChanges();
    const page = fixture.componentInstance;
    await page.open(item);
    fixture.detectChanges();
    expect(page.canReadDocument()).toBe(false);
    expect(fixture.nativeElement.textContent).not.toContain('Tarifa diária registrada');
    expect(fixture.nativeElement.textContent).not.toContain('Ler documento do titular');
    await page.readDocument();
    expect(api.readAttendanceDocument).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Ler vistoria de entrada');
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it.each(['document', 'inspection'])(
    'discards a late %s response after selecting another attendance',
    async (field) => {
      const pending = deferred<{ value: string }>();
      const api = {
        getAttendance: vi.fn(async (id: string) => ({ ...detail, id })),
        getAttendanceHistory: vi.fn().mockResolvedValue(historyPage),
        readAttendanceDocument: vi.fn(() => pending.promise),
        readEntryInspection: vi.fn(() => pending.promise),
      };
      const fixture = createPage(api);
      const page = fixture.componentInstance;
      await page.open(item);
      const read = field === 'document' ? page.readDocument() : page.readInspection();
      await page.open({ ...item, id: 'B' });
      pending.resolve({ value: 'private-content-of-A' });
      await read;
      expect(page.detail()?.id).toBe('B');
      expect(page.sensitiveValue()).toBe('');
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('private-content-of-A');
      fixture.destroy();
      TestBed.resetTestingModule();
    },
  );

  it('keeps the latest selection when older detail and history requests finish later', async () => {
    const oldDetail = deferred<AttendanceDetail>();
    const oldHistory = deferred<AttendanceHistoryPage>();
    const api = {
      getAttendance: vi
        .fn()
        .mockReturnValueOnce(oldDetail.promise)
        .mockResolvedValue({ ...detail, id: 'B' }),
      getAttendanceHistory: vi
        .fn()
        .mockReturnValueOnce(oldHistory.promise)
        .mockResolvedValue({ ...historyPage, items: [] }),
    };
    const fixture = createPage(api);
    const page = fixture.componentInstance;
    const openA = page.open(item);
    await page.open({ ...item, id: 'B' });
    oldDetail.resolve(detail);
    oldHistory.resolve(historyPage);
    await openA;
    expect(page.detail()?.id).toBe('B');
    expect(page.history()).toEqual([]);
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('discards old history pages and responses after closing or destroying the page', async () => {
    const history = deferred<AttendanceHistoryPage>();
    const sensitive = deferred<{ value: string }>();
    const api = {
      getAttendance: vi.fn().mockResolvedValue(detail),
      getAttendanceHistory: vi
        .fn()
        .mockResolvedValueOnce(historyPage)
        .mockReturnValue(history.promise),
      readAttendanceDocument: vi.fn(() => sensitive.promise),
    };
    const fixture = createPage(api);
    const page = fixture.componentInstance;
    await page.open(item);
    const load = page.loadHistory(2);
    const read = page.readDocument();
    page.closeDetail();
    fixture.destroy();
    history.resolve({ ...historyPage, page: 2 });
    sensitive.resolve({ value: 'private' });
    await Promise.all([load, read]);
    expect(page.detail()).toBeNull();
    expect(page.history()).toEqual([]);
    expect(page.sensitiveValue()).toBe('');
    TestBed.resetTestingModule();
  });

  it('does not replace new search results with an older response', async () => {
    const old = deferred<AttendancePage>();
    const api = {
      searchAttendances: vi
        .fn()
        .mockReturnValueOnce(old.promise)
        .mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 }),
    };
    const fixture = createPage(api);
    const page = fixture.componentInstance;
    page.query.set({ plate: item.plate, identifier: '' });
    const first = page.searchPage(1);
    page.query.set({ plate: 'NEW', identifier: '' });
    await page.searchPage(1);
    old.resolve({ items: [item], page: 1, pageSize: 20, total: 1 });
    await first;
    expect(page.items()).toEqual([]);
    fixture.destroy();
    TestBed.resetTestingModule();
  });
});
