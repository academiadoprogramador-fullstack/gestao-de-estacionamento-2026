import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  LucideCircleAlert,
  LucideCircleCheck,
  LucideLoaderCircle,
  LucideRotateCcw,
} from '@lucide/angular';
import { codigoDoErro, mensagemDoErro } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';
import { formatarDataHora, formatarValor } from '../../shared/formato';
import { CoordenadorComandoService } from '../check-in/coordenador-comando';
import { RetiradasApiService } from '../retiradas/retiradas.service';
import {
  AtendimentoBusca,
  AutorizacaoResumo,
  EventoHistorico,
  LancamentoResumo,
  RetiradaDetalheFinanceira,
} from '../retiradas/retiradas.tipos';
import { CaixaApiService } from './caixa.service';

type TipoAutorizacao = 'documental' | 'financeira';

interface ConteudoComando {
  readonly tipo: string;
  readonly dados: unknown;
}

@Component({
  selector: 'app-caixa',
  imports: [
    RouterLink,
    LucideCircleAlert,
    LucideCircleCheck,
    LucideLoaderCircle,
    LucideRotateCcw,
  ],
  templateUrl: './caixa.html',
})
export class Caixa implements OnInit, OnDestroy {
  private readonly api = inject(CaixaApiService);
  private readonly retiradas = inject(RetiradasApiService);
  private readonly sessao = inject(SessaoService);
  private readonly rota = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly coordenador = inject(CoordenadorComandoService);

  readonly formatarDataHora = formatarDataHora;
  readonly formatarValor = formatarValor;
  readonly ehAdministrador = computed(() => this.sessao.possuiPerfil(['Administrador']));
  readonly ehOperador = computed(() => this.sessao.possuiPerfil(['Administrador', 'Caixa']));
  readonly acessoNegado = computed(() => !this.ehOperador());

  readonly atendimentoId = signal<string | null>(null);
  readonly atendimento = signal<AtendimentoBusca | null>(null);
  readonly pedido = signal<RetiradaDetalheFinanceira | null>(null);
  readonly historico = signal<readonly EventoHistorico[]>([]);
  readonly carregando = signal(true);
  readonly erro = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);
  readonly conflito = signal<string | null>(null);

  readonly valorPagamento = signal('');
  readonly formaPagamento = signal('');
  readonly errosPagamento = signal<readonly string[]>([]);
  readonly resumoPagamento = viewChild<ElementRef<HTMLDivElement>>('resumoPagamento');

  readonly valorDesconto = signal('');
  readonly justificativaDesconto = signal('');
  readonly errosDesconto = signal<readonly string[]>([]);
  readonly resumoDesconto = viewChild<ElementRef<HTMLDivElement>>('resumoDesconto');

  readonly pagamentoEstorno = signal('');
  readonly justificativaEstorno = signal('');
  readonly errosEstorno = signal<readonly string[]>([]);
  readonly resumoEstorno = viewChild<ElementRef<HTMLDivElement>>('resumoEstorno');

  readonly justificativasAutorizacao = signal<Record<TipoAutorizacao, string>>({
    documental: '',
    financeira: '',
  });
  readonly errosAutorizacao = signal<string | null>(null);

  readonly operacaoPendente = signal('Registrando operação…');
  readonly processando = signal(false);

  readonly confirmacaoAberta = signal(false);
  readonly confirmacaoTitulo = signal('Operação confirmada');
  readonly confirmacaoMensagem = signal('');
  readonly confirmacaoDetalhe = signal<string | null>(null);
  readonly botaoConfirmacao = viewChild<ElementRef<HTMLButtonElement>>('botaoConfirmacao');
  private focoAntesDaConfirmacao: HTMLElement | null = null;

  readonly justificativaAberta = signal(false);
  readonly justificativaMeta = signal('');
  readonly justificativaTexto = signal('');
  readonly erroJustificativa = signal<string | null>(null);
  readonly botaoFecharJustificativa = viewChild<ElementRef<HTMLButtonElement>>(
    'fecharJustificativa',
  );

  readonly saldo = computed(() => this.pedido()?.saldo ?? 0);
  readonly cobrado = computed(() => this.pedido()?.cobrado ?? 0);
  readonly pagoLiquido = computed(() => this.pedido()?.pagoLiquido ?? 0);
  readonly pagamentoBloqueado = computed(() => this.saldo() <= 0);
  readonly descontoBloqueado = computed(
    () => this.pagoLiquido() > 0 || this.cobrado() <= 0,
  );
  readonly pagamentosEstornaveis = computed(() => {
    const pedido = this.pedido();
    if (!pedido) {
      return [];
    }

    const estornados = new Set(
      pedido.lancamentos
        .filter((lancamento) => lancamento.tipo === 'estorno')
        .map((lancamento) => lancamento.pagamentoOriginalId),
    );

    return pedido.lancamentos.filter(
      (lancamento) => lancamento.tipo === 'pagamento' && !estornados.has(lancamento.id),
    );
  });
  readonly podeVerDesconto = computed(
    () => this.ehAdministrador() && !this.descontoBloqueado(),
  );

  ngOnInit(): void {
    const id = this.rota.snapshot.paramMap.get('attendanceId');
    this.atendimentoId.set(id);
    void this.carregar();
  }

  ngOnDestroy(): void {
    if (this.coordenador.bloqueado()) {
      this.coordenador.descartar();
    }
  }

  async carregar(): Promise<void> {
    const id = this.atendimentoId();
    if (!this.ehOperador()) {
      this.carregando.set(false);
      return;
    }

    if (!id) {
      this.erro.set('Atendimento inválido.');
      this.carregando.set(false);
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);
    this.conflito.set(null);

    try {
      const atendimento = await firstValueFrom(this.retiradas.detalheAtendimento(id));
      this.atendimento.set(atendimento);
      const pedidoId = atendimento.retirada?.pedidoId;

      if (!pedidoId) {
        this.erro.set('Este atendimento ainda não possui pedido de retirada.');
        this.pedido.set(null);
        this.carregando.set(false);
        return;
      }

      const [pedido, historico] = await Promise.all([
        firstValueFrom(this.api.obter(pedidoId)),
        firstValueFrom(this.retiradas.historico(id)),
      ]);

      this.pedido.set(pedido);
      this.historico.set(historico.items);
      this.inicializarFormularios(pedido);
    } catch (erro) {
      this.erro.set(mensagemDoErro(erro, 'Não foi possível carregar o caixa do atendimento.'));
    } finally {
      this.carregando.set(false);
    }
  }

  async submeterPagamento(evento: Event): Promise<void> {
    evento.preventDefault();
    this.errosPagamento.set([]);

    const pedido = this.pedido();
    if (!pedido) {
      return;
    }

    const valor = this.lerValor(this.valorPagamento());
    const forma = this.formaPagamento();
    const mensagens: string[] = [];

    if (!Number.isFinite(valor) || valor <= 0) {
      mensagens.push('Informe um valor positivo com duas casas decimais.');
    } else if (Math.abs(valor - pedido.saldo) > 0.004) {
      mensagens.push(`O acerto deve ser integral: exatamente ${formatarValor(pedido.saldo)}.`);
    }

    if (!forma) {
      mensagens.push('Selecione a forma: dinheiro, cartão ou Pix.');
    }

    if (mensagens.length > 0) {
      this.errosPagamento.set(mensagens);
      queueMicrotask(() => this.resumoPagamento()?.nativeElement.focus());
      return;
    }

    await this.executar(
      'Registrando pagamento…',
      {
        tipo: 'pagamento',
        dados: { valor, forma, revisaoEsperada: pedido.revisaoRazao },
      },
      'Pagamento integral confirmado. O saldo foi recarregado do servidor.',
    );
  }

  async submeterDesconto(evento: Event): Promise<void> {
    evento.preventDefault();
    this.errosDesconto.set([]);

    const pedido = this.pedido();
    if (!pedido) {
      return;
    }

    const valor = this.lerValor(this.valorDesconto());
    const justificativa = this.justificativaDesconto().trim();
    const mensagens: string[] = [];

    if (pedido.pagoLiquido > 0) {
      mensagens.push('Desconto bloqueado enquanto existir pagamento líquido maior que zero.');
    }

    if (!Number.isFinite(valor) || valor <= 0) {
      mensagens.push('O desconto deve ser um valor monetário positivo.');
    } else if (valor > pedido.cobrado + 0.004) {
      mensagens.push(`O desconto não pode exceder o valor descontável de ${formatarValor(pedido.cobrado)}.`);
    }

    if (!justificativa) {
      mensagens.push('Informe a justificativa obrigatória.');
    }

    if (mensagens.length > 0) {
      this.errosDesconto.set(mensagens);
      queueMicrotask(() => this.resumoDesconto()?.nativeElement.focus());
      return;
    }

    await this.executar(
      'Concedendo desconto…',
      {
        tipo: 'desconto',
        dados: { valor, justificativa, revisaoEsperada: pedido.revisaoRazao },
      },
      'Desconto registrado. O lançamento é append-only e o saldo foi recalculado.',
    );
  }

  async submeterEstorno(evento: Event): Promise<void> {
    evento.preventDefault();
    this.errosEstorno.set([]);

    const pedido = this.pedido();
    if (!pedido) {
      return;
    }

    const pagamento = pedido.lancamentos.find(
      (lancamento) => lancamento.id === this.pagamentoEstorno(),
    );
    const justificativa = this.justificativaEstorno().trim();
    const mensagens: string[] = [];

    if (!pagamento || pagamento.tipo !== 'pagamento') {
      mensagens.push('Selecione um pagamento ainda não estornado.');
    }

    if (!justificativa) {
      mensagens.push('Informe a justificativa obrigatória.');
    }

    if (mensagens.length > 0 || !pagamento) {
      this.errosEstorno.set(mensagens);
      queueMicrotask(() => this.resumoEstorno()?.nativeElement.focus());
      return;
    }

    await this.executar(
      'Registrando estorno…',
      {
        tipo: 'estorno',
        dados: {
          pagamentoId: pagamento.id,
          valor: pagamento.valor,
          justificativa,
          revisaoEsperada: pedido.revisaoRazao,
        },
      },
      'Estorno integral confirmado. O saldo foi reaberto e o pagamento original permanece.',
    );
  }

  async concederAutorizacao(
    evento: Event,
    tipoInformado: string,
    campo: HTMLTextAreaElement,
  ): Promise<void> {
    evento.preventDefault();
    this.errosAutorizacao.set(null);

    const pedido = this.pedido();
    if (!pedido) {
      return;
    }

    const tipo: TipoAutorizacao = tipoInformado === 'financeira' ? 'financeira' : 'documental';

    const justificativa = campo.value.trim();
    if (!justificativa) {
      this.errosAutorizacao.set('Informe a justificativa da autorização.');
      return;
    }

    await this.executar(
      'Concedendo autorização…',
      {
        tipo: `autorizacao-${tipo}`,
        dados: { tipo, justificativa, versaoEsperada: pedido.versaoPedido },
      },
      'Autorização registrada. A entrega futura avaliará a vigência; nada foi consumido.',
      () => campo.focus(),
    );
  }

  async revogarAutorizacao(
    evento: Event,
    tipoInformado: string,
    autorizacao: AutorizacaoResumo,
    campo: HTMLTextAreaElement,
  ): Promise<void> {
    evento.preventDefault();
    this.errosAutorizacao.set(null);

    const pedido = this.pedido();
    if (!pedido || !autorizacao.id) {
      return;
    }

    const tipo: TipoAutorizacao = tipoInformado === 'financeira' ? 'financeira' : 'documental';

    const justificativa = campo.value.trim();
    if (!justificativa) {
      this.errosAutorizacao.set('Informe a justificativa da revogação.');
      return;
    }

    await this.executar(
      'Revogando autorização…',
      {
        tipo: `revogacao-${tipo}`,
        dados: { autorizacaoId: autorizacao.id, justificativa },
      },
      'Autorização revogada. A concessão e a revogação permanecem no histórico.',
      () => campo.focus(),
    );
  }

  async lerJustificativaLancamento(lancamento: LancamentoResumo): Promise<void> {
    const pedido = this.pedido();
    if (!pedido || !this.ehAdministrador()) {
      return;
    }

    this.erroJustificativa.set(null);

    try {
      const leitura = await firstValueFrom(
        this.api.lerJustificativaLancamento(pedido.id, lancamento.id),
      );
      this.abrirJustificativa(
        `${lancamento.id} · ${lancamento.ator} · ${formatarDataHora(lancamento.ocorridoEmUtc)} · leitura registrada`,
        leitura.conteudo,
      );
    } catch (erro) {
      this.erroJustificativa.set(mensagemDoErro(erro, 'Não foi possível ler a justificativa.'));
    }
  }

  async lerJustificativaAutorizacao(autorizacao: AutorizacaoResumo): Promise<void> {
    const pedido = this.pedido();
    if (!pedido || !autorizacao.id || !this.ehAdministrador()) {
      return;
    }

    this.erroJustificativa.set(null);

    try {
      const leitura = await firstValueFrom(
        this.api.lerJustificativaAutorizacao(pedido.id, autorizacao.id),
      );
      this.abrirJustificativa(
        `${autorizacao.id} · ${autorizacao.concedidaPor ?? '—'} · ${formatarDataHora(autorizacao.concedidaEmUtc)} · leitura registrada`,
        leitura.conteudo,
      );
    } catch (erro) {
      this.erroJustificativa.set(mensagemDoErro(erro, 'Não foi possível ler a justificativa.'));
    }
  }

  fecharJustificativa(): void {
    this.justificativaAberta.set(false);
    this.justificativaTexto.set('');
    this.justificativaMeta.set('');
  }

  async repetir(): Promise<void> {
    if (this.coordenador.fase() !== 'incerto') {
      return;
    }

    this.processando.set(true);

    try {
      const resposta = await this.coordenador.repetir<ConteudoComando, unknown>((chave, conteudo) =>
        this.enviar(conteudo.tipo, chave, conteudo),
      );
      await this.concluirSucesso();
      const mensagem = 'Operação confirmada após repetição. O saldo foi recarregado do servidor.';
      this.aviso.set(mensagem);
      this.abrirConfirmacao(null, mensagem);
    } catch (erro) {
      this.tratarFalha(erro);
    } finally {
      this.processando.set(false);
    }
  }

  descartarTentativa(): void {
    this.coordenador.descartar();
    this.aviso.set('Tentativa descartada; o resultado no servidor permanece desconhecido.');
  }

  valorEstornoSelecionado(): string {
    const pagamento = this.pedido()?.lancamentos.find(
      (lancamento) => lancamento.id === this.pagamentoEstorno(),
    );
    return pagamento ? formatarValor(pagamento.valor) : '—';
  }

  rotuloVigencia(vigencia: AutorizacaoResumo['vigencia']): string {
    return {
      vigente: 'Vigente',
      revogada: 'Revogada',
      substituida: 'Substituída',
      obsoleta: 'Obsoleta',
      nao_concedida: 'Não concedida',
    }[vigencia];
  }

  classeVigencia(vigencia: AutorizacaoResumo['vigencia']): string {
    if (vigencia === 'vigente') {
      return 'bg-emerald-500/10 text-emerald-300';
    }

    if (vigencia === 'nao_concedida') {
      return 'bg-slate-800 text-slate-400';
    }

    if (vigencia === 'obsoleta') {
      return 'bg-amber-500/10 text-amber-300';
    }

    return 'bg-rose-500/10 text-rose-300';
  }

  rotuloTipoLancamento(tipo: LancamentoResumo['tipo']): string {
    return { desconto: 'Desconto', pagamento: 'Pagamento', estorno: 'Estorno' }[tipo];
  }

  rotuloForma(forma: string | null): string {
    return { dinheiro: 'Dinheiro', cartao: 'Cartão', pix: 'Pix' }[forma ?? ''] ?? '—';
  }

  atualizarJustificativa(tipo: string, valor: string): void {
    const chave: TipoAutorizacao = tipo === 'financeira' ? 'financeira' : 'documental';
    this.justificativasAutorizacao.update((atual) => ({ ...atual, [chave]: valor }));
  }

  justificativaDe(tipo: string): string {
    return this.justificativasAutorizacao()[tipo === 'financeira' ? 'financeira' : 'documental'];
  }

  autorizacao(tipo: string): AutorizacaoResumo | null {
    const pedido = this.pedido();
    if (!pedido) {
      return null;
    }

    return tipo === 'financeira'
      ? pedido.autorizacaoFinanceira
      : pedido.autorizacaoDocumental;
  }

  private async executar(
    descricao: string,
    conteudo: ConteudoComando,
    sucesso: string,
    aoFalhar?: () => void,
  ): Promise<void> {
    if (this.coordenador.bloqueado()) {
      return;
    }

    this.operacaoPendente.set(descricao);
    this.processando.set(true);

    try {
      await this.coordenador.executar(conteudo, (chave, dados) =>
        this.enviar(dados.tipo, chave, dados),
      );
      this.aviso.set(sucesso);
      await this.concluirSucesso();
      this.abrirConfirmacao(conteudo, sucesso);
    } catch (erro) {
      this.tratarFalha(erro);
      aoFalhar?.();
    } finally {
      this.processando.set(false);
    }
  }

  private enviar(
    tipo: string,
    chave: string,
    conteudo: ConteudoComando,
  ): Promise<unknown> {
    const pedido = this.pedido();
    if (!pedido) {
      return Promise.reject(new Error('sem_pedido'));
    }

    switch (tipo) {
      case 'pagamento':
        return firstValueFrom(
          this.api.pagar(
            pedido.id,
            conteudo.dados as { valor: number; forma: string; revisaoEsperada: number },
            chave,
          ),
        );
      case 'desconto':
        return firstValueFrom(
          this.api.descontar(
            pedido.id,
            conteudo.dados as {
              valor: number;
              justificativa: string;
              revisaoEsperada: number;
            },
            chave,
          ),
        );
      case 'estorno':
        return firstValueFrom(
          this.api.estornar(
            pedido.id,
            conteudo.dados as {
              pagamentoId: string;
              valor: number;
              justificativa: string;
              revisaoEsperada: number;
            },
            chave,
          ),
        );
      default:
        if (tipo.startsWith('revogacao-')) {
          const dados = conteudo.dados as { autorizacaoId: string; justificativa: string };
          return firstValueFrom(
            this.api.revogarAutorizacao(
              pedido.id,
              dados.autorizacaoId,
              dados.justificativa,
              chave,
            ),
          );
        }

        if (tipo.startsWith('autorizacao-')) {
          return firstValueFrom(
            this.api.concederAutorizacao(
              pedido.id,
              conteudo.dados as {
                tipo: TipoAutorizacao;
                justificativa: string;
                versaoEsperada: string;
              },
              chave,
            ),
          );
        }

        return Promise.reject(new Error('operacao_desconhecida'));
    }
  }

  private async concluirSucesso(): Promise<void> {
    await this.recarregar();
    this.limparFormularios();
  }

  private async recarregar(): Promise<void> {
    const id = this.atendimentoId();
    if (!id) {
      return;
    }

    const atendimento = await firstValueFrom(this.retiradas.detalheAtendimento(id));
    this.atendimento.set(atendimento);
    const pedidoId = atendimento.retirada?.pedidoId;

    if (!pedidoId) {
      return;
    }

    const [pedido, historico] = await Promise.all([
      firstValueFrom(this.api.obter(pedidoId)),
      firstValueFrom(this.retiradas.historico(id)),
    ]);

    this.pedido.set(pedido);
    this.historico.set(historico.items);
    this.inicializarFormularios(pedido);
  }

  private tratarFalha(erro: unknown): void {
    const codigo = codigoDoErro(erro);

    if (codigo === 'ledger_revision_stale' || codigo === 'stale_withdrawal_version') {
      this.conflito.set(
        'Conflito de revisão (409): a razão foi alterada por outra operação. Saldo e autorizações recarregados.',
      );
      void this.recarregar();
      return;
    }

    if (this.coordenador.fase() === 'incerto') {
      this.aviso.set(
        'O resultado do caixa é incerto. Repita a tentativa para reenviar exatamente a mesma chave e o mesmo conteúdo.',
      );
      return;
    }

    if (erro instanceof HttpErrorResponse && erro.status === 0) {
      this.aviso.set(
        'A resposta se perdeu. Repita a operação para confirmar o resultado com a mesma chave.',
      );
      return;
    }

    this.aviso.set(mensagemDoErro(erro, 'Não foi possível concluir a operação do caixa.'));
  }

  private inicializarFormularios(pedido: RetiradaDetalheFinanceira): void {
    if (pedido.saldo > 0) {
      this.valorPagamento.set(pedido.saldo.toFixed(2).replace('.', ','));
    } else {
      this.valorPagamento.set('');
    }

    const estornavel = pedido.lancamentos.find((lancamento) => {
      const estornados = new Set(
        pedido.lancamentos
          .filter((item) => item.tipo === 'estorno')
          .map((item) => item.pagamentoOriginalId),
      );
      return lancamento.tipo === 'pagamento' && !estornados.has(lancamento.id);
    });

    this.pagamentoEstorno.set(estornavel?.id ?? '');
  }

  private limparFormularios(): void {
    this.formaPagamento.set('');
    this.valorDesconto.set('');
    this.justificativaDesconto.set('');
    this.justificativaEstorno.set('');
    this.justificativasAutorizacao.set({ documental: '', financeira: '' });
    this.errosPagamento.set([]);
    this.errosDesconto.set([]);
    this.errosEstorno.set([]);
    this.errosAutorizacao.set(null);
  }

  private lerValor(texto: string): number {
    const normalizado = texto.trim().replace(/\./g, '').replace(',', '.');
    return Number(normalizado);
  }

  fecharConfirmacao(): void {
    this.confirmacaoAberta.set(false);
    this.confirmacaoDetalhe.set(null);
    this.focoAntesDaConfirmacao?.focus();
    this.focoAntesDaConfirmacao = null;
  }

  private abrirConfirmacao(conteudo: ConteudoComando | null, mensagem: string): void {
    const pedido = this.pedido();
    let detalhe = `Saldo atual: ${formatarValor(pedido?.saldo)} · revisão ${pedido?.revisaoRazao ?? '—'}.`;

    if (conteudo?.tipo === 'pagamento') {
      const dados = conteudo.dados as { valor: number; forma: string };
      detalhe =
        `Pagamento de ${formatarValor(dados.valor)} em ${this.rotuloForma(dados.forma)}. ` +
        detalhe;
    } else if (conteudo?.tipo === 'desconto') {
      const dados = conteudo.dados as { valor: number };
      detalhe = `Desconto de ${formatarValor(dados.valor)} registrado. ` + detalhe;
    } else if (conteudo?.tipo === 'estorno') {
      const dados = conteudo.dados as { valor: number };
      detalhe = `Estorno de ${formatarValor(dados.valor)} vinculado ao pagamento original. ` + detalhe;
    } else if (conteudo?.tipo.startsWith('autorizacao-')) {
      detalhe =
        'A autorização não altera saldo, pagamento ou estado do pedido, e nada foi consumido nesta fase.';
    } else if (conteudo?.tipo.startsWith('revogacao-')) {
      detalhe =
        'A concessão e a revogação permanecem no histórico; nada foi consumido nesta fase.';
    }

    this.confirmacaoTitulo.set('Operação confirmada');
    this.confirmacaoMensagem.set(mensagem);
    this.confirmacaoDetalhe.set(detalhe);
    this.focoAntesDaConfirmacao =
      typeof document !== 'undefined' ? (document.activeElement as HTMLElement | null) : null;
    this.confirmacaoAberta.set(true);
    queueMicrotask(() => this.botaoConfirmacao()?.nativeElement.focus());
  }

  private abrirJustificativa(meta: string, texto: string): void {
    this.justificativaMeta.set(meta);
    this.justificativaTexto.set(texto);
    this.justificativaAberta.set(true);
    queueMicrotask(() => this.botaoFecharJustificativa()?.nativeElement.focus());
  }
}
