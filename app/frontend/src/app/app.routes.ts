import { Routes } from '@angular/router';
import { authenticatedGuard, roleGuard } from './core/auth/auth.guards';
import { DashboardPage, ForbiddenPage, LoginPage } from './core/pages/operational-pages';
import { DailyRatesPage, EmployeesPage, SpacesPage } from './core/admin/admin-pages';
import { CheckInPage, pendingCheckInCanDeactivate } from './core/check-in/check-in-page';
import { AttendancesPage } from './core/attendances/attendances-page';

export const routes: Routes = [
  { path: 'login', component: LoginPage },
  { path: 'forbidden', component: ForbiddenPage },
  { path: 'dashboard', component: DashboardPage, canActivate: [authenticatedGuard] },
  {
    path: 'employees',
    component: EmployeesPage,
    canActivate: [authenticatedGuard, roleGuard],
    data: { roles: ['admin'] },
  },
  {
    path: 'spaces',
    component: SpacesPage,
    canActivate: [authenticatedGuard, roleGuard],
    data: { roles: ['manobrista', 'caixa', 'admin'] },
  },
  {
    path: 'daily-rates',
    component: DailyRatesPage,
    canActivate: [authenticatedGuard, roleGuard],
    data: { roles: ['admin'] },
  },
  {
    path: 'check-in',
    component: CheckInPage,
    canActivate: [authenticatedGuard, roleGuard],
    canDeactivate: [pendingCheckInCanDeactivate],
    data: { roles: ['caixa', 'admin'] },
  },
  {
    path: 'attendances',
    component: AttendancesPage,
    canActivate: [authenticatedGuard, roleGuard],
    data: { roles: ['manobrista', 'caixa', 'admin'] },
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
