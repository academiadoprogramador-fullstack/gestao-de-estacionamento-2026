import { Component } from '@angular/core';

@Component({
  standalone: true,
  selector: 'app-dashboard-page',
  template:
    '<section aria-labelledby="page-title"><h1 id="page-title" class="text-2xl font-semibold text-slate-950">Visão geral</h1><p class="mt-2 text-slate-600">Área de trabalho do estacionamento.</p></section>',
})
export class DashboardPage {}

@Component({
  standalone: true,
  selector: 'app-login-page',
  template:
    '<section aria-labelledby="page-title"><h1 id="page-title" class="text-2xl font-semibold text-slate-950">Acesso de funcionário</h1><p class="mt-2 text-slate-600">Entre com sua conta provisionada para continuar.</p></section>',
})
export class LoginPage {}

@Component({
  standalone: true,
  selector: 'app-forbidden-page',
  template:
    '<section aria-labelledby="page-title"><h1 id="page-title" class="text-2xl font-semibold text-slate-950">Acesso não permitido</h1><p class="mt-2 text-slate-600">Seu perfil não tem acesso a esta área.</p></section>',
})
export class ForbiddenPage {}
