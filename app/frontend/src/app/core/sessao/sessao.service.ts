import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { urlApi } from '../../../environments/environment';

/** Dados mínimos do funcionário devolvidos pela API. */
export interface FuncionarioSessao {
  readonly id: string;
  readonly nome: string;
  readonly email: string;
  readonly perfis: readonly string[];
}

/** Estado de sessão exposto somente por Signals de leitura. */
export interface EstadoSessao {
  readonly funcionario: FuncionarioSessao;
  readonly expiraSessaoEmUtc: string;
}

interface RespostaLogin {
  readonly tokenAcesso: string;
  readonly refresh: string;
  readonly expiraAcessoEmUtc: string;
  readonly expiraSessaoEmUtc: string;
  readonly funcionario: FuncionarioSessao;
}

interface RespostaConsultaSessao {
  readonly sessaoId: string;
  readonly expiraEmUtc: string;
  readonly versaoAcesso: number;
  readonly funcionario: FuncionarioSessao;
}

export type ResultadoLogin =
  | { readonly sucesso: true }
  | { readonly sucesso: false; readonly motivo: 'credenciais' | 'indisponivel' };

/**
 * Mantém JWT, refresh e dados do funcionário exclusivamente em memória.
 * Não usa localStorage, sessionStorage, IndexedDB, cookie ou compartilhamento
 * entre abas; recarregar a página exige novo login.
 */
@Injectable({ providedIn: 'root' })
export class SessaoService {
  private readonly http = inject(HttpClient);
  private readonly estadoInterno = signal<EstadoSessao | null>(null);
  private renovacaoEmAndamento: Promise<boolean> | null = null;

  #tokenAcesso: string | null = null;
  #refresh: string | null = null;

  readonly estado = this.estadoInterno.asReadonly();
  readonly autenticado = computed(() => this.estadoInterno() !== null);
  readonly perfis = computed<readonly string[]>(() => this.estadoInterno()?.funcionario.perfis ?? []);
  readonly possuidorDeRefresh = computed(() => this.#refresh !== null);

  /** Usado somente pelo interceptor; não é persistido. */
  get tokenAcesso(): string | null {
    return this.#tokenAcesso;
  }

  possuiPerfil(perfis: readonly string[]): boolean {
    if (perfis.length === 0) {
      return true;
    }

    const atuais = this.perfis();
    return perfis.some((perfil) => atuais.includes(perfil));
  }

  async login(email: string, senha: string): Promise<ResultadoLogin> {
    try {
      const resposta = await firstValueFrom(
        this.http.post<RespostaLogin>(urlApi('/api/v1/session/login'), { email, senha }),
      );

      this.aplicarSessao(resposta);
      return { sucesso: true };
    } catch (erro: unknown) {
      if (typeof erro === 'object' && erro !== null && 'status' in erro) {
        const status = (erro as { status: number }).status;
        if (status === 401) {
          return { sucesso: false, motivo: 'credenciais' };
        }
      }

      return { sucesso: false, motivo: 'indisponivel' };
    }
  }

  /**
   * Renovação single-flight: chamadas concorrentes compartilham a mesma
   * requisição de refresh. Falha limpa a memória e exige novo login.
   */
  async renovar(): Promise<boolean> {
    if (this.renovacaoEmAndamento) {
      return this.renovacaoEmAndamento;
    }

    this.renovacaoEmAndamento = this.executarRenovacao();

    try {
      return await this.renovacaoEmAndamento;
    } finally {
      this.renovacaoEmAndamento = null;
    }
  }

  /**
   * Revoga a sessão no servidor antes de limpar a memória. Retorna falso
   * quando a revogação remota não pôde ser confirmada.
   */
  async logout(): Promise<boolean> {
    let revogacaoConfirmada = false;

    try {
      if (this.#tokenAcesso) {
        await firstValueFrom(this.http.post<void>(urlApi('/api/v1/session/logout'), null));
        revogacaoConfirmada = true;
      }
    } catch {
      revogacaoConfirmada = false;
    } finally {
      this.limpar();
    }

    return revogacaoConfirmada;
  }

  limpar(): void {
    this.#tokenAcesso = null;
    this.#refresh = null;
    this.estadoInterno.set(null);
  }

  /** Confirma no servidor que a sessão em memória continua válida. */
  async confirmarSessao(): Promise<boolean> {
    if (!this.#tokenAcesso) {
      return false;
    }

    try {
      const resposta = await firstValueFrom(
        this.http.get<RespostaConsultaSessao>(urlApi('/api/v1/session')),
      );

      this.estadoInterno.set({
        funcionario: resposta.funcionario,
        expiraSessaoEmUtc: resposta.expiraEmUtc,
      });
      return true;
    } catch {
      this.limpar();
      return false;
    }
  }

  private async executarRenovacao(): Promise<boolean> {
    if (!this.#refresh) {
      this.limpar();
      return false;
    }

    try {
      const resposta = await firstValueFrom(
        this.http.post<RespostaLogin>(urlApi('/api/v1/session/refresh'), {
          refresh: this.#refresh,
        }),
      );

      this.aplicarSessao(resposta);
      return true;
    } catch {
      this.limpar();
      return false;
    }
  }

  private aplicarSessao(resposta: RespostaLogin): void {
    this.#tokenAcesso = resposta.tokenAcesso;
    this.#refresh = resposta.refresh;
    this.estadoInterno.set({
      funcionario: resposta.funcionario,
      expiraSessaoEmUtc: resposta.expiraSessaoEmUtc,
    });
  }
}
