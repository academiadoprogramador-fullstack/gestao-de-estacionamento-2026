import { Routes } from '@angular/router';
import { guardaPerfil, guardaSessao } from './core/guards/guards';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Entrar — Gestão de Estacionamento',
    loadComponent: () => import('./features/auth/login/login').then((modulo) => modulo.Login),
  },
  {
    path: 'dashboard',
    title: 'Área de trabalho — Gestão de Estacionamento',
    canActivate: [guardaSessao],
    loadComponent: () =>
      import('./features/dashboard/dashboard').then((modulo) => modulo.Dashboard),
  },
  {
    path: 'administracao',
    title: 'Administração — Gestão de Estacionamento',
    canActivate: [guardaSessao, guardaPerfil(['Administrador'])],
    loadComponent: () =>
      import('./features/administracao/administracao').then((modulo) => modulo.Administracao),
  },
  {
    path: 'forbidden',
    title: 'Acesso negado — Gestão de Estacionamento',
    canActivate: [guardaSessao],
    loadComponent: () =>
      import('./features/auth/forbidden/forbidden').then((modulo) => modulo.Forbidden),
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
