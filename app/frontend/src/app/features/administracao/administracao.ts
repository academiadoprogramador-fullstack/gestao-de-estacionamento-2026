import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RouterLink } from '@angular/router';
import {
  LucideArrowLeft,
  LucideCircleAlert,
  LucideLoaderCircle,
  LucideShieldCheck,
} from '@lucide/angular';
import { urlApi } from '../../../environments/environment';
import { mensagemDeFalha } from '../../core/http/erro-api';

interface VerificacaoAdministrativa {
  readonly escopo: string;
  readonly descricao: string;
}

@Component({
  selector: 'app-administracao',
  imports: [RouterLink, LucideArrowLeft, LucideCircleAlert, LucideLoaderCircle, LucideShieldCheck],
  template: `
    <main class="mx-auto w-full max-w-2xl px-4 py-10">
      <a
        routerLink="/dashboard"
        class="inline-flex items-center gap-2 text-sm text-slate-300 transition hover:text-sky-300 focus:outline-none focus-visible:ring-2 focus-visible:ring-sky-400"
      >
        <svg lucideArrowLeft class="h-4 w-4" aria-hidden="true"></svg>
        Voltar à área de trabalho
      </a>

      <h1 class="mt-4 text-2xl font-semibold text-white">Verificação administrativa</h1>
      <p class="mt-1 text-sm text-slate-400">
        Esta rota e o endpoint correspondente exigem o perfil Administrador.
      </p>

      @if (carregando()) {
        <p class="mt-6 flex items-center gap-2 text-slate-300">
          <svg lucideLoaderCircle class="h-5 w-5 animate-spin" aria-hidden="true"></svg>
          Consultando o servidor…
        </p>
      } @else if (erro(); as mensagem) {
        <p class="mt-6 flex items-start gap-2 rounded-lg border border-rose-500/40 bg-rose-500/10 p-3 text-sm text-rose-100" role="alert">
          <svg lucideCircleAlert class="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true"></svg>
          {{ mensagem }}
        </p>
      } @else if (dados(); as verificacao) {
        <article class="mt-6 rounded-xl border border-emerald-500/30 bg-emerald-500/5 p-5">
          <h2 class="flex items-center gap-2 font-medium text-emerald-300">
            <svg lucideShieldCheck class="h-5 w-5" aria-hidden="true"></svg>
            Acesso confirmado
          </h2>
          <p class="mt-2 text-slate-200">{{ verificacao.descricao }}</p>
          <p class="mt-1 font-mono text-xs text-slate-400">escopo: {{ verificacao.escopo }}</p>
        </article>
      }
    </main>
  `,
})
export class Administracao implements OnInit {
  private readonly http = inject(HttpClient);

  readonly dados = signal<VerificacaoAdministrativa | null>(null);
  readonly carregando = signal(true);
  readonly erro = signal<string | null>(null);

  ngOnInit(): void {
    void this.carregar();
  }

  private async carregar(): Promise<void> {
    try {
      const resposta = await firstValueFrom(
        this.http.get<VerificacaoAdministrativa>(urlApi('/api/v1/administracao/verificacao')),
      );
      this.dados.set(resposta);
    } catch (erro) {
      this.erro.set(mensagemDeFalha(erro));
    } finally {
      this.carregando.set(false);
    }
  }
}
