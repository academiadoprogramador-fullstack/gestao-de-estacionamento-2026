import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  LucideCheck,
  LucideCircleCheck,
  LucideCircleX,
  LucideClipboardCheck,
  LucideEye,
  LucideLoaderCircle,
  LucideRotateCcw,
} from '@lucide/angular';
import { codigoDoErro, mensagemDoErro } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';
import { formatarDataHora, formatarValor } from '../../shared/formato';
import { FotosVistoria } from '../../shared/fotos-vistoria/fotos-vistoria';
import { CoordenadorComandoService } from '../check-in/coordenador-comando';
import { AtendimentoBusca, RetiradaDetalheFinanceira } from '../retiradas/retiradas.tipos';
import { CorpoConfirmacaoEntrega, DeliveryApiService } from './delivery.service';
import { ProntidaoEntrega } from './delivery.tipos';

type ConteudoComando =
  | {
      readonly tipo: 'vistoria';
      readonly atendimentoId: string;
      readonly texto: string;
      readonly versaoAtendimento: string;
    }
  | {
      readonly tipo: 'entrega';
      readonly pedidoId: string;
      readonly corpo: CorpoConfirmacaoEntrega;
    };

@Component({
  selector: 'app-delivery',
  imports: [
    RouterLink,
    FotosVistoria,
    LucideCheck,
    LucideCircleCheck,
    LucideCircleX,
    LucideClipboardCheck,
    LucideEye,
    LucideLoaderCircle,
    LucideRotateCcw,
  ],
  templateUrl: './delivery.html',
})
export class Delivery implements OnInit, OnDestroy {
  private readonly api = inject(DeliveryApiService);
  private readonly sessao = inject(SessaoService);
  private readonly rota = inject(ActivatedRoute);
  readonly coordenador = inject(CoordenadorComandoService);

  readonly formatarDataHora = formatarDataHora;
  readonly formatarValor = formatarValor;
  readonly ehOperador = computed(() => this.sessao.possuiPerfil(['Administrador', 'Caixa']));

  readonly pedidoId = signal<string | null>(null);
  readonly pedido = signal<RetiradaDetalheFinanceira | null>(null);
  readonly atendimento = signal<AtendimentoBusca | null>(null);
  readonly prontidao = signal<ProntidaoEntrega | null>(null);
  readonly carregando = signal(true);
  readonly atualizando = signal(false);
  readonly erro = signal<string | null>(null);
  readonly conflito = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);

  readonly textoVistoria = signal('');
  readonly errosVistoria = signal<readonly string[]>([]);
  readonly resumoVistoria = viewChild<ElementRef<HTMLDivElement>>('resumoVistoria');
  readonly botaoLerVistoria = viewChild<ElementRef<HTMLButtonElement>>('botaoLerVistoria');

  readonly lendoVistoria = signal(false);
  readonly leituraVistoria = signal<{
    conteudo: string;
    ator: string;
    registradaEmUtc: string;
  } | null>(null);
  readonly erroLeitura = signal<string | null>(null);

  readonly confirmacaoAberta = signal(false);
  readonly processando = signal(false);
  readonly operacaoPendente = signal('Registrando operação…');
  readonly botaoConfirmacao = viewChild<ElementRef<HTMLButtonElement>>('botaoConfirmacao');
  readonly tituloRecibo = viewChild<ElementRef<HTMLHeadingElement>>('tituloRecibo');
  private focoAntesDaConfirmacao: HTMLElement | null = null;

  private ativo = true;
  private contexto = 0;

  readonly entregue = computed(() => {
    const pedido = this.pedido();
    return pedido?.estado === 'entregue' || Boolean(pedido?.entrega);
  });

  readonly vistoriaRegistrada = computed(
    () =>
      Boolean(this.pedido()?.vistoriaSaidaRegistrada) ||
      Boolean(this.atendimento()?.vistoriaSaidaRegistrada),
  );

  readonly podeRegistrarVistoria = computed(() => !this.vistoriaRegistrada() && !this.entregue());

  readonly pronta = computed(() => Boolean(this.prontidao()?.pronta));

  constructor() {
    effect(() => {
      if (!this.sessao.autenticado()) {
        this.contexto += 1;
        this.pedido.set(null);
        this.atendimento.set(null);
        this.prontidao.set(null);
        this.leituraVistoria.set(null);
      }
    });
  }

  ngOnInit(): void {
    const id = this.rota.snapshot.paramMap.get('withdrawalId');
    this.pedidoId.set(id);

    if (!id) {
      this.erro.set('Pedido de retirada inválido.');
      this.carregando.set(false);
      return;
    }

    void this.carregar();
  }

  ngOnDestroy(): void {
    this.ativo = false;
    this.contexto += 1;
    this.pedido.set(null);
    this.atendimento.set(null);
    this.prontidao.set(null);
    this.leituraVistoria.set(null);

    if (this.coordenador.bloqueado()) {
      this.coordenador.descartar();
    }
  }

  async carregar(mostrarCarregando = true): Promise<void> {
    const id = this.pedidoId();
    if (!id) {
      this.carregando.set(false);
      return;
    }

    const token = ++this.contexto;

    if (mostrarCarregando) {
      this.carregando.set(true);
    } else {
      this.atualizando.set(true);
    }

    this.erro.set(null);

    try {
      const pedido = await firstValueFrom(this.api.obterPedido(id));
      if (!this.ativo || token !== this.contexto) {
        return;
      }

      this.pedido.set(pedido);
      const atendimento = await firstValueFrom(this.api.obterAtendimento(pedido.atendimentoId));
      if (!this.ativo || token !== this.contexto) {
        return;
      }

      this.atendimento.set(atendimento);

      if (this.ehOperador() && pedido.estado !== 'entregue' && !pedido.entrega) {
        const prontidao = await firstValueFrom(this.api.prontidao(id));
        if (!this.ativo || token !== this.contexto) {
          return;
        }

        this.prontidao.set(prontidao);
      } else {
        this.prontidao.set(null);
      }
    } catch (erro) {
      if (this.ativo && token === this.contexto) {
        this.erro.set(mensagemDoErro(erro, 'Não foi possível carregar a operação de entrega.'));
      }
    } finally {
      if (this.ativo && token === this.contexto) {
        this.carregando.set(false);
        this.atualizando.set(false);
      }
    }
  }

  async registrarVistoria(evento: Event): Promise<void> {
    evento.preventDefault();
    this.errosVistoria.set([]);

    const atendimento = this.atendimento();
    const texto = this.textoVistoria().trim();
    const mensagens: string[] = [];

    if (texto.length < 1 || texto.length > 4000) {
      mensagens.push('Informe de 1 a 4.000 caracteres nas condições de saída.');
    }

    if (!atendimento?.versaoAtendimento) {
      mensagens.push('Recarregue o atendimento antes de registrar a vistoria.');
    }

    if (mensagens.length > 0) {
      this.errosVistoria.set(mensagens);
      queueMicrotask(() => this.resumoVistoria()?.nativeElement.focus());
      return;
    }

    await this.executar(
      'Registrando vistoria…',
      {
        tipo: 'vistoria',
        atendimentoId: atendimento!.id,
        texto,
        versaoAtendimento: atendimento!.versaoAtendimento!,
      },
      'Vistoria registrada. O texto é imutável e a leitura é explícita e auditada.',
    );
  }

  async lerVistoria(): Promise<void> {
    const atendimento = this.atendimento();
    if (!atendimento || this.lendoVistoria()) {
      return;
    }

    this.lendoVistoria.set(true);
    this.erroLeitura.set(null);
    this.leituraVistoria.set(null);
    const token = ++this.contexto;

    try {
      const leitura = await firstValueFrom(this.api.lerVistoria(atendimento.id));
      if (this.ativo && token === this.contexto) {
        this.leituraVistoria.set({
          conteudo: leitura.conteudo,
          ator: leitura.ator,
          registradaEmUtc: leitura.registradaEmUtc,
        });
      }
    } catch (erro) {
      if (this.ativo && token === this.contexto) {
        this.erroLeitura.set(
          mensagemDoErro(
            erro,
            'Não foi possível consultar a vistoria. Nenhum texto foi retornado.',
          ),
        );
      }
    } finally {
      if (this.ativo && token === this.contexto) {
        this.lendoVistoria.set(false);
      }
    }
  }

  abrirConfirmacao(): void {
    if (!this.ehOperador() || !this.prontidao()?.pronta || this.coordenador.bloqueado()) {
      return;
    }

    this.focoAntesDaConfirmacao =
      typeof document !== 'undefined' ? (document.activeElement as HTMLElement | null) : null;
    this.confirmacaoAberta.set(true);
    queueMicrotask(() => this.botaoConfirmacao()?.nativeElement.focus());
  }

  fecharConfirmacao(): void {
    this.confirmacaoAberta.set(false);
    this.focoAntesDaConfirmacao?.focus();
    this.focoAntesDaConfirmacao = null;
  }

  async confirmarEntrega(): Promise<void> {
    const pedido = this.pedido();
    const prontidao = this.prontidao();

    if (!pedido || !prontidao?.pronta || this.coordenador.bloqueado()) {
      return;
    }

    this.fecharConfirmacao();
    const corpo: CorpoConfirmacaoEntrega = {
      confirmacao: true,
      versaoAtendimento: prontidao.versoes.versaoAtendimento,
      versaoPedido: prontidao.versoes.versaoPedido,
      versaoRazao: prontidao.versoes.versaoRazao,
      versaoControleDocumental: prontidao.versoes.versaoControleDocumental,
      versaoControleFinanceira: prontidao.versoes.versaoControleFinanceira,
    };

    await this.executar(
      'Confirmando entrega…',
      { tipo: 'entrega', pedidoId: pedido.id, corpo },
      'Entrega confirmada. A vaga foi liberada nesta visita e o estado atual foi recarregado.',
    );
  }

  async repetir(): Promise<void> {
    if (this.coordenador.fase() !== 'incerto') {
      return;
    }

    this.processando.set(true);

    try {
      await this.coordenador.repetir<ConteudoComando, unknown>((chave, dados) =>
        this.enviar(chave, dados),
      );
      await this.concluirSucesso(null);
    } catch (erro) {
      await this.tratarFalha(erro);
    } finally {
      this.processando.set(false);
    }
  }

  async descartarTentativa(): Promise<void> {
    this.coordenador.descartar();
    this.aviso.set('Tentativa descartada; o resultado no servidor permanece desconhecido.');
    await this.carregar(false);
  }

  async recarregarEstado(): Promise<void> {
    if (this.coordenador.bloqueado()) {
      this.coordenador.descartar();
    }

    this.conflito.set(null);
    this.aviso.set('Estado recarregado do servidor. Revise antes de confirmar novamente.');
    await this.carregar(false);
  }

  private async executar(
    descricao: string,
    conteudo: ConteudoComando,
    sucesso: string,
  ): Promise<void> {
    if (this.coordenador.bloqueado()) {
      return;
    }

    this.operacaoPendente.set(descricao);
    this.processando.set(true);
    this.erro.set(null);
    this.conflito.set(null);

    try {
      await this.coordenador.executar<ConteudoComando, unknown>(conteudo, (chave, dados) =>
        this.enviar(chave, dados),
      );
      await this.concluirSucesso(conteudo);
      this.aviso.set(sucesso);
    } catch (erro) {
      await this.tratarFalha(erro);
    } finally {
      this.processando.set(false);
    }
  }

  private enviar(chave: string, conteudo: ConteudoComando): Promise<unknown> {
    switch (conteudo.tipo) {
      case 'vistoria':
        return firstValueFrom(
          this.api.registrarVistoria(
            conteudo.atendimentoId,
            conteudo.texto,
            conteudo.versaoAtendimento,
            chave,
          ),
        );
      case 'entrega':
        return firstValueFrom(this.api.confirmarEntrega(conteudo.pedidoId, conteudo.corpo, chave));
      default:
        return Promise.reject(new Error('comando_desconhecido'));
    }
  }

  private async concluirSucesso(conteudo: ConteudoComando | null): Promise<void> {
    this.textoVistoria.set('');
    this.errosVistoria.set([]);
    await this.carregar(false);

    queueMicrotask(() => {
      if (conteudo?.tipo === 'entrega' || this.entregue()) {
        this.tituloRecibo()?.nativeElement.focus();
      } else {
        this.botaoLerVistoria()?.nativeElement.focus();
      }
    });
  }

  private async tratarFalha(erro: unknown): Promise<void> {
    const codigo = codigoDoErro(erro);

    if (erro instanceof HttpErrorResponse && erro.status === 401) {
      this.erro.set(
        'Sessão inválida ou expirada. Entre novamente para retomar a operação; nenhum comando foi repetido automaticamente.',
      );
      return;
    }

    if (this.coordenador.fase() === 'incerto') {
      this.aviso.set(
        'O resultado é incerto. Use “Repetir a mesma tentativa” para reenviar exatamente a mesma chave e o mesmo conteúdo.',
      );
      return;
    }

    if (
      codigo === 'concurrency_conflict' ||
      codigo === 'delivery_already_completed' ||
      codigo === 'delivery_authorization_stale' ||
      codigo === 'stale_attendance_version' ||
      codigo === 'stale_withdrawal_version' ||
      codigo === 'exit_inspection_already_exists'
    ) {
      this.conflito.set(
        'O estado mudou desde a prontidão. Recarregue o estado atual antes de tentar outra confirmação.',
      );
      await this.carregar(false);
      return;
    }

    this.erro.set(mensagemDoErro(erro, 'Não foi possível concluir a operação de entrega.'));
  }
}
