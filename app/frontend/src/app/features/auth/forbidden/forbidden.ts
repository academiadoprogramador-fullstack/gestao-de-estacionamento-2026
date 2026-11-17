import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideArrowLeft, LucideShieldAlert } from '@lucide/angular';
import { SessaoService } from '../../../core/sessao/sessao.service';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, LucideArrowLeft, LucideShieldAlert],
  template: `
    <main class="mx-auto flex w-full max-w-xl flex-col items-center px-4 py-16 text-center">
      <svg lucideShieldAlert class="h-14 w-14 text-amber-400" aria-hidden="true"></svg>
      <h1 class="mt-4 text-2xl font-semibold text-white">Acesso negado</h1>
      <p class="mt-2 text-slate-300">
        Seu perfil não permite abrir esta área. A autorização é aplicada pelo servidor; esta
        mensagem não substitui a verificação da API.
      </p>
      <p class="mt-2 text-sm text-slate-400">
        Perfis atuais: {{ sessao.perfis().join(', ') || 'nenhum' }}
      </p>
      <a
        routerLink="/dashboard"
        class="mt-6 inline-flex items-center gap-2 rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200 transition hover:border-sky-400 hover:text-sky-300 focus:outline-none focus-visible:ring-2 focus-visible:ring-sky-400"
      >
        <svg lucideArrowLeft class="h-4 w-4" aria-hidden="true"></svg>
        Voltar à área de trabalho
      </a>
    </main>
  `,
})
export class Forbidden {
  readonly sessao = inject(SessaoService);
}
