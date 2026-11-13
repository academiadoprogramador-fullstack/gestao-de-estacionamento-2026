import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';

export type EmployeeRole = 'manobrista' | 'caixa' | 'admin';
export interface EmployeeItem {
  id: string;
  email: string;
  isActive: boolean;
  roles: EmployeeRole[];
  concurrencyStamp: string;
}
export interface EmployeePage {
  items: EmployeeItem[];
  page: number;
  pageSize: number;
  total: number;
}
export interface SpaceItem {
  id: string;
  code: string;
  isOccupied: boolean;
  version: string;
}
export interface SpacePage {
  items: SpaceItem[];
  page: number;
  pageSize: number;
  total: number;
}
export interface DailyRate {
  id: string;
  amount: number;
  publishedAtUtc: string;
  publishedById: string;
}
export interface RateConfiguration {
  currentRate: DailyRate | null;
  version: string;
}
export interface RatePage {
  items: DailyRate[];
  page: number;
  pageSize: number;
  total: number;
}
export interface CreateCheckInRequest {
  plate: string;
  model: string;
  ownerName: string;
  documentNumber: string;
  entryInspection: string;
  parkingSpaceId: string;
}
export interface CheckInCreated {
  attendanceId: string;
  publicIdentifier: string;
  parkingSpaceId: string;
  state: string;
  enteredAtUtc: string;
}
export interface AttendanceItem {
  id: string;
  publicIdentifier: string;
  plate: string;
  model: string;
  enteredAtUtc: string;
  state: string;
  parkingSpaceId: string;
  parkingSpaceCode: string;
}
export interface AttendancePage {
  items: AttendanceItem[];
  page: number;
  pageSize: number;
  total: number;
}
export interface AttendanceDetail extends AttendanceItem {
  ownerName: string;
  operatorId: string;
  dailyRateId?: string;
  dailyRateSnapshot?: number;
}
export interface AttendanceHistoryItem {
  actorId: string;
  action: string;
  targetId: string | null;
  occurredAtUtc: string;
  metadata: Record<string, string>;
}
export interface AttendanceHistoryPage {
  items: AttendanceHistoryItem[];
  page: number;
  pageSize: number;
  total: number;
}

@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);

  listEmployees(page = 1): Promise<EmployeePage> {
    return firstValueFrom(
      this.http.get<EmployeePage>(`/api/v1/employees?page=${page}&pageSize=20`),
    );
  }

  createEmployee(
    email: string,
    initialPassword: string,
    roles: EmployeeRole[],
  ): Promise<EmployeeItem> {
    return firstValueFrom(
      this.http.post<EmployeeItem>('/api/v1/employees', { email, initialPassword, roles }),
    );
  }

  updateEmployee(
    item: EmployeeItem,
    isActive: boolean,
    roles: EmployeeRole[],
  ): Promise<{ employee: EmployeeItem; concurrencyStamp: string }> {
    return firstValueFrom(
      this.http.put<{ employee: EmployeeItem; concurrencyStamp: string }>(
        `/api/v1/employees/${item.id}/access`,
        { concurrencyStamp: item.concurrencyStamp, isActive, roles },
      ),
    );
  }

  listSpaces(code = '', status = '', page = 1, pageSize = 20): Observable<SpacePage> {
    const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (code.trim()) query.set('code', code.trim());
    if (status) query.set('status', status);
    return this.http.get<SpacePage>(`/api/v1/spaces?${query}`);
  }

  createSpace(code: string): Promise<SpaceItem> {
    return firstValueFrom(this.http.post<SpaceItem>('/api/v1/spaces', { code }));
  }

  createCheckIn(content: CreateCheckInRequest, idempotencyKey: string): Promise<CheckInCreated> {
    return firstValueFrom(
      this.http.post<CheckInCreated>('/api/v1/attendances', content, {
        headers: { 'Idempotency-Key': idempotencyKey },
      }),
    );
  }

  searchAttendances(plate: string, identifier: string, page = 1): Promise<AttendancePage> {
    const query = new URLSearchParams({ page: String(page), pageSize: '20' });
    if (plate.trim()) query.set('plate', plate.trim());
    if (identifier.trim()) query.set('identifier', identifier.trim());
    return firstValueFrom(this.http.get<AttendancePage>(`/api/v1/attendances?${query}`));
  }

  getAttendance(id: string): Promise<AttendanceDetail> {
    return firstValueFrom(this.http.get<AttendanceDetail>(`/api/v1/attendances/${id}`));
  }

  getAttendanceHistory(id: string, page = 1): Promise<AttendanceHistoryPage> {
    return firstValueFrom(
      this.http.get<AttendanceHistoryPage>(
        `/api/v1/attendances/${id}/history?page=${page}&pageSize=20`,
      ),
    );
  }

  readAttendanceDocument(id: string): Promise<{ value: string }> {
    return firstValueFrom(this.http.get<{ value: string }>(`/api/v1/attendances/${id}/document`));
  }

  readEntryInspection(id: string): Promise<{ value: string }> {
    return firstValueFrom(
      this.http.get<{ value: string }>(`/api/v1/attendances/${id}/entry-inspection`),
    );
  }

  currentRate(): Promise<RateConfiguration> {
    return firstValueFrom(this.http.get<RateConfiguration>('/api/v1/daily-rates/current'));
  }

  listRates(page = 1, pageSize = 20): Promise<RatePage> {
    return firstValueFrom(
      this.http.get<RatePage>(`/api/v1/daily-rates?page=${page}&pageSize=${pageSize}`),
    );
  }

  publishRate(amount: number, baseVersion: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/v1/daily-rates', { amount, baseVersion }));
  }
}
