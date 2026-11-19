import { Injectable, OnDestroy } from '@angular/core';

export type AcaoAtualizacao = () => Promise<void>;

/**
 * Polling da fila de retiradas com intervalo configurável, pausa em aba
 * oculta, atualização ao recuperar foco/após comando e trava contra
 * sobreposição. Não mantém requisição alguma depois de `parar`.
 */
@Injectable({ providedIn: 'root' })
export class PollingRetiradasService implements OnDestroy {
  private intervaloMs = 30_000;
  private acao: AcaoAtualizacao | null = null;
  private temporizador: ReturnType<typeof setTimeout> | null = null;
  private ativo = false;
  private emAndamento = false;

  private readonly aoMudarVisibilidade = (): void => {
    if (!this.ativo) {
      return;
    }

    if (this.documentoOculto()) {
      this.cancelarTemporizador();
      return;
    }

    void this.executarAgora();
  };

  constructor() {
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', this.aoMudarVisibilidade);
    }
  }

  iniciar(acao: AcaoAtualizacao, intervaloMs = 30_000): void {
    this.acao = acao;
    this.intervaloMs = intervaloMs > 0 ? intervaloMs : 30_000;
    this.ativo = true;
    this.agendar();
  }

  /** Dispara agora (foco, filtro ou após comando) sem sobrepor execução. */
  atualizarAgora(): Promise<void> {
    return this.executarAgora();
  }

  /** Atualização explícita após um comando confirmado. */
  notificarComando(): Promise<void> {
    return this.executarAgora();
  }

  parar(): void {
    this.ativo = false;
    this.acao = null;
    this.cancelarTemporizador();
  }

  ngOnDestroy(): void {
    this.parar();

    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.aoMudarVisibilidade);
    }
  }

  private agendar(): void {
    this.cancelarTemporizador();

    if (!this.ativo || this.documentoOculto() || this.emAndamento) {
      return;
    }

    this.temporizador = setTimeout(() => {
      void this.executarAgora();
    }, this.intervaloMs);
  }

  private async executarAgora(): Promise<void> {
    if (!this.acao || this.emAndamento) {
      return;
    }

    this.cancelarTemporizador();
    this.emAndamento = true;

    try {
      await this.acao();
    } finally {
      this.emAndamento = false;

      if (this.ativo) {
        this.agendar();
      }
    }
  }

  private cancelarTemporizador(): void {
    if (this.temporizador !== null) {
      clearTimeout(this.temporizador);
      this.temporizador = null;
    }
  }

  private documentoOculto(): boolean {
    return typeof document !== 'undefined' && document.hidden;
  }
}
