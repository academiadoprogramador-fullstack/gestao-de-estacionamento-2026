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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  LucideCircleAlert,
  LucideFileText,
  LucideLoaderCircle,
  LucideRotateCcw,
  LucideSearch,
} from '@lucide/angular';
import { codigoDoErro, mensagemDoErro } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';
import { formatarDataHora, formatarValor } from '../../shared/formato';
import { CoordenadorComandoService } from '../check-in/coordenador-comando';
import { RetiradasApiService } from './retiradas.service';
import { AtendimentoBusca, AtendimentoResumo, ResultadoPedidoRetirada } from './retiradas.tipos';

type CriterioBusca = 'plate' | 'identifier';
type Conferencia = '' | 'aprovada' | 'divergente';

interface ConteudoPedido {
  readonly titularPresente: boolean;
  readonly conferencia: 'aprovada' | 'divergente';
}

@Component({
  selector: 'app-pedido-retirada',
  imports: [
    RouterLink,
    LucideCircleAlert,
    LucideFileText,
    LucideLoaderCircle,
    LucideRotateCcw,
    LucideSearch,
  ],
  templateUrl: './pedido-retirada.html',
})
export class PedidoRetirada implements OnInit, OnDestroy {
  private readonly api = inject(RetiradasApiService);
  private readonly sessao = inject(SessaoService);
  private readonly rota = inject(ActivatedRoute);
  readonly coordenador = inject(CoordenadorComandoService);

  readonly formatarDataHora = formatarDataHora;
  readonly formatarValor = formatarValor;
  readonly ehOperador = computed(() => this.sessao.possuiPerfil(['Administrador', 'Caixa']));
  readonly acessoNegado = computed(() => !this.ehOperador());

  readonly criterio = signal<CriterioBusca>('plate');
  readonly termo = signal('');
  readonly resultados = signal<readonly AtendimentoBusca[]>([]);
  readonly buscandoAtendimentos = signal(false);
  readonly buscou = signal(false);
  readonly erroBusca = signal<string | null>(null);
  readonly atendimento = signal<AtendimentoBusca | null>(null);
  readonly carregandoAtendimento = signal(false);

  readonly abertos = signal<readonly AtendimentoResumo[]>([]);
  readonly totalAbertos = signal(0);
  readonly paginaAbertos = signal(1);
  readonly tamanhoAbertos = 20;
  readonly carregandoAbertos = signal(true);
  readonly erroAbertos = signal<string | null>(null);

  readonly titularPresente = signal(false);
  readonly conferencia = signal<Conferencia>('');
  readonly erros = signal<readonly string[]>([]);
  readonly resumoErro = viewChild<ElementRef<HTMLDivElement>>('resumoErro');
  readonly resumoVisivel = signal(false);

  readonly enviando = signal(false);
  readonly resultado = signal<ResultadoPedidoRetirada | null>(null);
  readonly conferenciaRegistrada = signal<Conferencia>('');
  readonly recusa = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);

  readonly documento = signal<string | null>(null);
  readonly documentoAberto = signal(false);
  readonly erroDocumento = signal<string | null>(null);
  readonly lendoDocumento = signal(false);
  readonly botaoFecharDocumento = viewChild<ElementRef<HTMLButtonElement>>('fecharDocumento');

  readonly previa = computed(() => {
    const atendimento = this.atendimento();
    if (!atendimento || atendimento.retirada) {
      return null;
    }

    const segundos = Math.max(
      0,
      Math.floor((Date.now() - new Date(atendimento.entradaUtc).getTime()) / 1000),
    );
    const diarias = Math.max(1, Math.ceil(segundos / 86_400));
    const tarifa = atendimento.tarifaValorSnapshot ?? 0;
    return { diarias, total: diarias * tarifa, segundos };
  });

  ngOnInit(): void {
    void this.carregarAbertos();

    const atendimentoId = this.rota.snapshot.queryParamMap.get('attendanceId');
    if (atendimentoId) {
      void this.carregarAtendimento(atendimentoId);
    }
  }

  readonly totalPaginasAbertos = computed(() =>
    Math.max(1, Math.ceil(this.totalAbertos() / this.tamanhoAbertos)),
  );

  async carregarAbertos(): Promise<void> {
    this.carregandoAbertos.set(true);
    this.erroAbertos.set(null);

    try {
      const resposta = await firstValueFrom(
        this.api.listarAtendimentosAbertos(this.paginaAbertos(), this.tamanhoAbertos),
      );
      this.abertos.set(resposta.items);
      this.totalAbertos.set(resposta.total);
    } catch (erro) {
      this.erroAbertos.set(
        mensagemDoErro(erro, 'Não foi possível carregar os atendimentos em aberto.'),
      );
    } finally {
      this.carregandoAbertos.set(false);
    }
  }

  alterarPaginaAbertos(novaPagina: number): void {
    if (novaPagina < 1 || novaPagina > this.totalPaginasAbertos()) {
      return;
    }

    this.paginaAbertos.set(novaPagina);
    void this.carregarAbertos();
  }

  limparFiltro(): void {
    this.buscou.set(false);
    this.termo.set('');
    this.resultados.set([]);
    this.erroBusca.set(null);
  }

  async selecionarDaLista(item: AtendimentoResumo): Promise<void> {
    await this.carregarAtendimento(item.id);
  }

  estadoRetiradaDoItem(item: AtendimentoResumo): string | null {
    return item.estadoRetirada ?? (item as AtendimentoBusca).retirada?.estado ?? null;
  }

  pedidoDoItem(item: AtendimentoResumo): string | null {
    return item.pedidoId ?? (item as AtendimentoBusca).retirada?.pedidoId ?? null;
  }

  ngOnDestroy(): void {
    // A saída só chega aqui após o guard de comando pendente; a tentativa
    // apenas em memória é descartada.
    if (this.coordenador.bloqueado()) {
      this.coordenador.descartar();
    }
  }

  selecionarCriterio(criterio: CriterioBusca): void {
    this.criterio.set(criterio);
    this.termo.set('');
    this.resultados.set([]);
    this.erroBusca.set(null);
  }

  async buscar(evento?: Event): Promise<void> {
    evento?.preventDefault();
    const termo = this.termo().trim();
    this.erroBusca.set(null);
    this.resultados.set([]);
    this.buscou.set(true);

    if (!termo) {
      this.erroBusca.set('Informe uma placa ou um identificador para buscar.');
      return;
    }

    this.buscandoAtendimentos.set(true);

    try {
      const resposta = await firstValueFrom(this.api.buscarAtendimentos(this.criterio(), termo));
      this.resultados.set(resposta.items);

      if (resposta.items.length === 0) {
        this.erroBusca.set('Nenhum atendimento encontrado para o critério informado.');
      }
    } catch (erro) {
      this.erroBusca.set(mensagemDoErro(erro, 'Não foi possível buscar atendimentos.'));
    } finally {
      this.buscandoAtendimentos.set(false);
    }
  }

  async selecionar(resultado: AtendimentoBusca): Promise<void> {
    this.resultados.set([]);
    this.termo.set('');
    await this.carregarAtendimento(resultado.id);
  }

  async carregarAtendimento(atendimentoId: string): Promise<void> {
    this.carregandoAtendimento.set(true);
    this.erroBusca.set(null);
    this.recusa.set(null);
    this.resultado.set(null);
    this.limparErros();

    try {
      const atendimento = await firstValueFrom(this.api.detalheAtendimento(atendimentoId));
      this.atendimento.set(atendimento);
    } catch (erro) {
      this.erroBusca.set(mensagemDoErro(erro, 'Não foi possível carregar o atendimento.'));
    } finally {
      this.carregandoAtendimento.set(false);
    }
  }

  declararPresenca(presente: boolean): void {
    this.titularPresente.set(presente);
  }

  selecionarConferencia(conferencia: Conferencia): void {
    this.conferencia.set(conferencia);
  }

  async lerDocumento(): Promise<void> {
    const atendimento = this.atendimento();
    if (!atendimento) {
      return;
    }

    this.lendoDocumento.set(true);
    this.erroDocumento.set(null);

    try {
      const leitura = await firstValueFrom(this.api.lerDocumento(atendimento.id));
      this.documento.set(leitura.conteudo);
      this.documentoAberto.set(true);
      queueMicrotask(() => this.botaoFecharDocumento()?.nativeElement.focus());
    } catch (erro) {
      this.erroDocumento.set(mensagemDoErro(erro, 'Não foi possível ler o documento do titular.'));
    } finally {
      this.lendoDocumento.set(false);
    }
  }

  fecharDocumento(): void {
    this.documentoAberto.set(false);
    this.documento.set(null);
  }

  async submeter(evento: Event): Promise<void> {
    evento.preventDefault();
    this.recusa.set(null);
    this.aviso.set(null);

    const atendimento = this.atendimento();
    if (!atendimento || !this.validar(atendimento)) {
      return;
    }

    if (this.coordenador.bloqueado()) {
      return;
    }

    const conteudo: ConteudoPedido = {
      titularPresente: true,
      conferencia: this.conferencia() as 'aprovada' | 'divergente',
    };

    this.enviando.set(true);

    try {
      const resultado = await this.coordenador.executar(conteudo, (chave, dados) =>
        firstValueFrom(this.api.registrarPedido(atendimento.id, dados, chave)),
      );
      this.concluir(resultado);
    } catch (erro) {
      this.tratarFalha(erro);
    } finally {
      this.enviando.set(false);
    }
  }

  async repetir(): Promise<void> {
    if (this.coordenador.fase() !== 'incerto') {
      return;
    }

    const atendimento = this.atendimento();
    if (!atendimento) {
      return;
    }

    this.enviando.set(true);

    try {
      const resultado = await this.coordenador.repetir<ConteudoPedido, ResultadoPedidoRetirada>(
        (chave, dados) => firstValueFrom(this.api.registrarPedido(atendimento.id, dados, chave)),
      );
      this.concluir(resultado);
    } catch (erro) {
      this.tratarFalha(erro);
    } finally {
      this.enviando.set(false);
    }
  }

  descartarTentativa(): void {
    this.coordenador.descartar();
    this.aviso.set('Tentativa descartada; o resultado no servidor permanece desconhecido.');
  }

  limpar(): void {
    this.titularPresente.set(false);
    this.conferencia.set('');
    this.conferenciaRegistrada.set('');
    this.recusa.set(null);
    this.resultado.set(null);
    this.aviso.set(null);
    this.limparErros();
    this.coordenador.descartar();
  }

  async copiarIdentificador(): Promise<void> {
    const atendimento = this.atendimento();
    if (!atendimento) {
      return;
    }

    try {
      await navigator.clipboard.writeText(atendimento.identificador);
      this.aviso.set('Identificador copiado.');
    } catch {
      this.aviso.set('Área de transferência indisponível; o identificador permanece selecionável.');
    }
  }

  formatarDuracao(segundos: number): string {
    const horas = Math.floor(segundos / 3600);
    const minutos = Math.floor((segundos % 3600) / 60);
    return horas === 0
      ? `${minutos} min`
      : `${horas} h ${String(minutos).padStart(2, '0')} min`;
  }

  private validar(atendimento: AtendimentoBusca): boolean {
    this.limparErros();
    const mensagens: string[] = [];

    if (atendimento.retirada) {
      mensagens.push('Este atendimento já possui pedido; um segundo pedido é recusado.');
    }

    if (!this.titularPresente()) {
      mensagens.push(
        'Declare a presença do titular. Retirada por terceiro ou com titular ausente não é permitida no MVP.',
      );
    }

    if (!this.conferencia()) {
      mensagens.push('Informe o resultado da conferência: aprovada ou divergente.');
    }

    if (mensagens.length > 0) {
      this.erros.set(mensagens);
      this.resumoVisivel.set(true);
      queueMicrotask(() => this.resumoErro()?.nativeElement.focus());
      return false;
    }

    return true;
  }

  private concluir(resultado: ResultadoPedidoRetirada): void {
    this.resultado.set(resultado);
    this.conferenciaRegistrada.set(this.conferencia());
    this.titularPresente.set(false);
    this.conferencia.set('');
    this.documento.set(null);
    this.documentoAberto.set(false);
    this.recusa.set(null);
    this.aviso.set(null);
    this.limparErros();
    this.atendimento.update((atual) =>
      atual
        ? {
            ...atual,
            retirada: {
              pedidoId: resultado.pedidoId,
              estado: resultado.estado,
              solicitadoEmUtc: resultado.solicitadoEmUtc,
              buscaIniciadaEmUtc: null,
              buscaPor: null,
              diarias: resultado.diarias,
              totalOriginal: resultado.totalOriginal,
            },
          }
        : atual,
    );
    void this.carregarAbertos();
  }

  private tratarFalha(erro: unknown): void {
    const codigo = codigoDoErro(erro);

    if (codigo === 'withdrawal_already_exists') {
      this.recusa.set(
        'Segundo pedido recusado (409). O pedido e o cálculo originais permanecem inalterados. Nenhum novo valor foi fixado.',
      );
      return;
    }

    if (this.coordenador.fase() === 'incerto') {
      this.aviso.set(
        'O resultado do pedido é incerto. Repita a tentativa para reenviar exatamente a mesma chave e o mesmo conteúdo.',
      );
      return;
    }

    if (erro instanceof HttpErrorResponse && erro.status === 0) {
      this.aviso.set(
        'A resposta se perdeu. Repita o mesmo pedido para confirmar o resultado com a mesma chave.',
      );
      return;
    }

    this.erros.set([mensagemDoErro(erro, 'Não foi possível registrar o pedido de retirada.')]);
    this.resumoVisivel.set(true);
    queueMicrotask(() => this.resumoErro()?.nativeElement.focus());
  }

  private limparErros(): void {
    this.erros.set([]);
    this.resumoVisivel.set(false);
    this.erroDocumento.set(null);
  }
}
