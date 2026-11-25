import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { ComandoPendenteService } from '../../core/comando/comando-pendente.service';

export type FaseComando = 'ocioso' | 'pendente' | 'incerto' | 'sucesso' | 'rejeitado';

export interface TentativaComando<T> {
  readonly chave: string;
  readonly conteudo: T;
}

/**
 * Atraso antes de exibir o bloqueio visual de um comando pendente. Operações
 * locais rápidas não devem piscar uma sobreposição amarela na tela; o bloqueio
 * lógico de navegação/logout continua imediato.
 */
const AtrasoExibicaoPendenteMs = 400;

/**
 * Coordenador de comando crítico em memória. Mantém chave e conteúdo apenas
 * até sucesso, rejeição definitiva ou descarte explícito; nunca usa storage.
 *
 * - `pendente`/`incerto` bloqueiam rota/logout e armam o aviso de saída.
 * - Rejeição definitiva (4xx) libera correção e gera nova chave na próxima
 *   submissão explícita.
 * - Falha de rede/5xx mantém a tentativa para replay com a mesma chave e o
 *   mesmo conteúdo.
 */
@Injectable({ providedIn: 'root' })
export class CoordenadorComandoService implements OnDestroy {
  private readonly pendente = inject(ComandoPendenteService);
  private readonly faseInterna = signal<FaseComando>('ocioso');
  private readonly exibicaoInterna = signal(false);
  private tentativa: TentativaComando<unknown> | null = null;
  private temporizadorExibicao: ReturnType<typeof setTimeout> | null = null;

  readonly fase = this.faseInterna.asReadonly();

  /**
   * Indica quando a sobreposição de bloqueio deve aparecer. Um comando rápido
   * conclui sem exibi-la; um comando demorado a exibe após o atraso, e um
   * resultado incerto a exibe imediatamente.
   */
  readonly exibirBloqueio = this.exibicaoInterna.asReadonly();

  readonly bloqueado = computed(
    () => this.faseInterna() === 'pendente' || this.faseInterna() === 'incerto',
  );

  private readonly aoSairDoNavegador = (evento: BeforeUnloadEvent): void => {
    if (!this.bloqueado()) {
      return;
    }

    evento.preventDefault();
    evento.returnValue = '';
  };

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener('beforeunload', this.aoSairDoNavegador);
    }
  }

  ngOnDestroy(): void {
    if (typeof window !== 'undefined') {
      window.removeEventListener('beforeunload', this.aoSairDoNavegador);
    }

    this.limparTemporizadorExibicao();
  }

  /**
   * Executa um conteúdo novo. Enquanto houver tentativa pendente ou incerta,
   * exige replay explícito em vez de gerar uma nova chave.
   */
  async executar<T, R>(
    conteudo: T,
    enviar: (chave: string, conteudo: T) => Promise<R>,
  ): Promise<R> {
    if (this.bloqueado()) {
      throw new Error('comando_pendente');
    }

    const chave = this.gerarChave();
    this.tentativa = { chave, conteudo };
    return this.enviar((chaveAtual, conteudoAtual) => enviar(chaveAtual, conteudoAtual as T));
  }

  /** Reenvia exatamente a mesma chave e conteúdo da tentativa incerta. */
  async repetir<T, R>(enviar: (chave: string, conteudo: T) => Promise<R>): Promise<R> {
    if (!this.tentativa || this.faseInterna() !== 'incerto') {
      throw new Error('sem_tentativa_incerta');
    }

    return this.enviar((chaveAtual, conteudoAtual) => enviar(chaveAtual, conteudoAtual as T));
  }

  /** Descarta a tentativa em memória (saída confirmada da tela). */
  descartar(): void {
    this.tentativa = null;
    this.definirFase('ocioso');
  }

  private async enviar<R>(enviar: (chave: string, conteudo: unknown) => Promise<R>): Promise<R> {
    const tentativa = this.tentativa;
    if (!tentativa) {
      throw new Error('sem_tentativa');
    }

    this.definirFase('pendente');

    try {
      const resultado = await enviar(tentativa.chave, tentativa.conteudo);
      this.tentativa = null;
      this.definirFase('sucesso');
      return resultado;
    } catch (erro) {
      if (this.falhaIncerta(erro)) {
        this.definirFase('incerto');
      } else {
        // Rejeição definitiva: a próxima submissão explícita usa nova chave.
        this.tentativa = null;
        this.definirFase('rejeitado');
      }

      throw erro;
    }
  }

  private falhaIncerta(erro: unknown): boolean {
    if (erro instanceof HttpErrorResponse) {
      return erro.status === 0 || erro.status >= 500;
    }

    return true;
  }

  private gerarChave(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (caractere) => {
      const aleatorio = Math.floor(Math.random() * 16);
      const valor = caractere === 'x' ? aleatorio : (aleatorio & 0x3) | 0x8;
      return valor.toString(16);
    });
  }

  private definirFase(fase: FaseComando): void {
    this.faseInterna.set(fase);
    this.pendente.definirBloqueado(fase === 'pendente' || fase === 'incerto');
    this.agendarExibicao(fase);
  }

  private agendarExibicao(fase: FaseComando): void {
    this.limparTemporizadorExibicao();

    if (fase === 'incerto') {
      this.exibicaoInterna.set(true);
      return;
    }

    if (fase !== 'pendente') {
      this.exibicaoInterna.set(false);
      return;
    }

    this.exibicaoInterna.set(false);
    this.temporizadorExibicao = setTimeout(() => {
      this.temporizadorExibicao = null;

      if (this.faseInterna() === 'pendente') {
        this.exibicaoInterna.set(true);
      }
    }, AtrasoExibicaoPendenteMs);
  }

  private limparTemporizadorExibicao(): void {
    if (this.temporizadorExibicao) {
      clearTimeout(this.temporizadorExibicao);
      this.temporizadorExibicao = null;
    }
  }
}
