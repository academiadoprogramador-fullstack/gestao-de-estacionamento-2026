import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  LucideCircleAlert,
  LucideClipboardList,
  LucideLoaderCircle,
  LucideRefreshCw,
} from '@lucide/angular';
import { ambiente } from '../../../environments/environment';
import { codigoDoErro, mensagemDoErro } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';
import { formatarDataHora, formatarValor } from '../../shared/formato';
import { PollingRetiradasService } from './polling-retiradas.service';
import { RetiradasApiService } from './retiradas.service';
import { RetiradaFila } from './retiradas.tipos';

type FiltroEstado = '' | 'aguardando' | 'em busca' | 'entregue';

@Component({
  selector: 'app-fila-retiradas',
  imports: [
    RouterLink,
    LucideCircleAlert,
    LucideClipboardList,
    LucideLoaderCircle,
    LucideRefreshCw,
  ],
  templateUrl: './fila-retiradas.html',
})
export class FilaRetiradas implements OnInit, OnDestroy {
  private readonly api = inject(RetiradasApiService);
  private readonly sessao = inject(SessaoService);
  private readonly polling = inject(PollingRetiradasService);

  readonly formatarDataHora = formatarDataHora;
  readonly formatarValor = formatarValor;
  readonly intervaloSegundos = ambiente.intervaloPollingRetiradasSegundos;
  readonly ehOperador = computed(() => this.sessao.possuiPerfil(['Administrador', 'Caixa']));

  readonly filtro = signal<FiltroEstado>('');
  readonly pagina = signal(1);
  readonly tamanhoPagina = signal(20);

  readonly itens = signal<readonly RetiradaFila[]>([]);
  readonly total = signal(0);
  readonly carregandoInicial = signal(true);
  readonly atualizando = signal(false);
  readonly erro = signal<string | null>(null);
  readonly conflito = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);
  readonly ultimaAtualizacao = signal<string>('—');
  readonly buscando = signal<string | null>(null);

  private contexto = 0;
  private consultaEmAndamento = false;
  private avisoTimer: ReturnType<typeof setTimeout> | null = null;
  private ativo = false;

  ngOnInit(): void {
    this.ativo = true;
    this.polling.iniciar(
      () => this.carregar(true),
      this.intervaloSegundos * 1000,
    );
    void this.carregar(false);
  }

  ngOnDestroy(): void {
    this.ativo = false;
    this.contexto += 1;
    this.polling.parar();

    if (this.avisoTimer) {
      clearTimeout(this.avisoTimer);
    }
  }

  readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.tamanhoPagina())),
  );

  contar(estado: 'aguardando' | 'em busca' | 'entregue'): number {
    return this.itens().filter((item) => item.estado === estado).length;
  }

  alterarFiltro(valor: string): void {
    this.filtro.set(valor as FiltroEstado);
    this.pagina.set(1);
    this.conflito.set(null);
    void this.polling.atualizarAgora();
  }

  alterarTamanho(valor: string): void {
    this.tamanhoPagina.set(Number(valor) || 20);
    this.pagina.set(1);
    void this.polling.atualizarAgora();
  }

  irParaPagina(novaPagina: number): void {
    if (novaPagina < 1 || novaPagina > this.totalPaginas()) {
      return;
    }

    this.pagina.set(novaPagina);
    void this.polling.atualizarAgora();
  }

  async carregar(silencioso: boolean): Promise<void> {
    if (this.consultaEmAndamento) {
      return;
    }

    this.consultaEmAndamento = true;
    const token = ++this.contexto;

    if (!silencioso) {
      this.carregandoInicial.set(this.itens().length === 0);
      this.atualizando.set(true);
    }

    try {
      const resposta = await firstValueFrom(
        this.api.listar(this.filtro() || null, this.pagina(), this.tamanhoPagina()),
      );

      if (!this.ativo || token !== this.contexto || !resposta) {
        return;
      }

      this.itens.set(resposta.items);
      this.total.set(resposta.total);
      this.erro.set(null);
      this.ultimaAtualizacao.set(new Date().toLocaleTimeString('pt-BR'));
    } catch (erro) {
      if (this.ativo && token === this.contexto) {
        this.erro.set(
          mensagemDoErro(erro, 'Não foi possível atualizar a fila de retiradas.'),
        );
      }
    } finally {
      this.consultaEmAndamento = false;

      if (this.ativo && token === this.contexto) {
        this.carregandoInicial.set(false);
        this.atualizando.set(false);
      }
    }
  }

  async atualizarAgora(): Promise<void> {
    await this.polling.atualizarAgora();
  }

  async iniciarBusca(item: RetiradaFila): Promise<void> {
    if (this.buscando() !== null || item.estado !== 'aguardando') {
      return;
    }

    this.buscando.set(item.id);
    this.conflito.set(null);

    try {
      await firstValueFrom(this.api.iniciarBusca(item.id, item.versaoPedido));
      this.mostrarAviso('Busca iniciada. Responsável e horário são do servidor.');
      await this.polling.notificarComando();
    } catch (erro) {
      const codigo = codigoDoErro(erro);

      if (codigo === 'stale_withdrawal_version' || codigo === 'withdrawal_not_waiting') {
        this.conflito.set(
          'Conflito: outro funcionário iniciou a busca. O estado atual foi recarregado.',
        );
      } else if (erro instanceof HttpErrorResponse && erro.status === 409) {
        this.conflito.set(
          'Conflito: o pedido foi alterado por outra operação. O estado atual foi recarregado.',
        );
      } else {
        this.erro.set(mensagemDoErro(erro, 'Não foi possível iniciar a busca.'));
      }

      await this.polling.atualizarAgora();
    } finally {
      this.buscando.set(null);
    }
  }

  private mostrarAviso(texto: string): void {
    this.aviso.set(texto);

    if (this.avisoTimer) {
      clearTimeout(this.avisoTimer);
    }

    this.avisoTimer = setTimeout(() => this.aviso.set(null), 6500);
  }
}
