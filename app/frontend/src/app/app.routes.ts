import { Routes } from '@angular/router';
import { guardaComandoPendente, guardaPerfil, guardaSessao } from './core/guards/guards';

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
    path: 'employees',
    title: 'Funcionários — Gestão de Estacionamento',
    canActivate: [guardaSessao, guardaPerfil(['Administrador'])],
    loadComponent: () =>
      import('./features/funcionarios/funcionarios').then((modulo) => modulo.Funcionarios),
  },
  {
    path: 'spaces',
    title: 'Vagas — Gestão de Estacionamento',
    canActivate: [guardaSessao],
    loadComponent: () => import('./features/vagas/vagas').then((modulo) => modulo.Vagas),
  },
  {
    path: 'daily-rates',
    title: 'Tarifas — Gestão de Estacionamento',
    canActivate: [guardaSessao, guardaPerfil(['Administrador'])],
    loadComponent: () => import('./features/tarifas/tarifas').then((modulo) => modulo.Tarifas),
  },
  {
    path: 'check-in',
    title: 'Check-in — Gestão de Estacionamento',
    canActivate: [guardaSessao, guardaPerfil(['Administrador', 'Caixa'])],
    canDeactivate: [guardaComandoPendente],
    loadComponent: () => import('./features/check-in/check-in').then((modulo) => modulo.CheckIn),
  },
  {
    path: 'attendances',
    title: 'Atendimentos — Gestão de Estacionamento',
    canActivate: [guardaSessao],
    loadComponent: () =>
      import('./features/atendimentos/atendimentos').then((modulo) => modulo.Atendimentos),
  },
  {
    path: 'withdrawals/new',
    title: 'Registrar pedido de retirada — Gestão de Estacionamento',
    canActivate: [guardaSessao, guardaPerfil(['Administrador', 'Caixa'])],
    canDeactivate: [guardaComandoPendente],
    loadComponent: () =>
      import('./features/retiradas/pedido-retirada').then((modulo) => modulo.PedidoRetirada),
  },
  {
    path: 'withdrawals',
    title: 'Fila de retiradas — Gestão de Estacionamento',
    canActivate: [guardaSessao],
    loadComponent: () =>
      import('./features/retiradas/fila-retiradas').then((modulo) => modulo.FilaRetiradas),
  },
  {
    path: 'cashier/:attendanceId',
    title: 'Caixa do atendimento — Gestão de Estacionamento',
    canActivate: [guardaSessao, guardaPerfil(['Administrador', 'Caixa'])],
    canDeactivate: [guardaComandoPendente],
    loadComponent: () => import('./features/caixa/caixa').then((modulo) => modulo.Caixa),
  },
  {
    path: 'delivery/:withdrawalId',
    title: 'Entrega do veículo — Gestão de Estacionamento',
    canActivate: [guardaSessao],
    canDeactivate: [guardaComandoPendente],
    loadComponent: () => import('./features/delivery/delivery').then((modulo) => modulo.Delivery),
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
