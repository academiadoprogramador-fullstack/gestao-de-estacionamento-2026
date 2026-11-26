import { Pagina } from '../../core/http/erro-api';
import { ResumoFotosVistoria } from '../../shared/fotos-vistoria/fotos-vistoria.tipos';

/** Vínculo histórico da entrega com a vaga liberada naquela visita. */
export interface ResumoEntrega {
  readonly entregaId: string;
  readonly responsavel: string;
  readonly entregueEmUtc: string;
  readonly vagaId: string;
  readonly vagaCodigo: string;
  readonly liberadaEmUtc: string;
}

/** Item da fila de retiradas. O Manobrista não recebe os campos financeiros. */
export interface RetiradaFila {
  readonly id: string;
  readonly atendimentoId: string;
  readonly identificador: string;
  readonly placa: string;
  readonly modelo: string;
  readonly vagaCodigo: string;
  readonly estado: 'aguardando' | 'em busca' | 'entregue';
  readonly entradaUtc: string;
  readonly solicitadoEmUtc: string;
  readonly buscaIniciadaEmUtc: string | null;
  readonly responsavelBusca: string | null;
  readonly versaoPedido: string;
  readonly vistoriaSaidaRegistrada?: boolean;
  readonly entrega?: ResumoEntrega | null;
  readonly totalOriginal?: number;
  readonly pagoLiquido?: number;
  readonly saldo?: number;
  readonly autorizacaoDocumental?: string;
  readonly autorizacaoFinanceira?: string;
}

export interface LancamentoResumo {
  readonly id: string;
  readonly tipo: 'desconto' | 'pagamento' | 'estorno';
  readonly valor: number;
  readonly formaPagamento: string | null;
  readonly pagamentoOriginalId: string | null;
  readonly ator: string;
  readonly ocorridoEmUtc: string;
  readonly temJustificativa: boolean;
}

export interface AutorizacaoResumo {
  readonly tipo: 'documental' | 'financeira';
  readonly id: string | null;
  readonly vigencia:
    | 'vigente'
    | 'revogada'
    | 'substituida'
    | 'obsoleta'
    | 'consumida'
    | 'nao_concedida';
  readonly revisaoReferencia: number | null;
  readonly concedidaPor: string | null;
  readonly concedidaEmUtc: string | null;
  readonly temJustificativa: boolean;
  readonly consumidaEmUtc?: string | null;
}

/** Detalhe financeiro do pedido. */
export interface RetiradaDetalheFinanceira
  extends Omit<
    RetiradaFila,
    | 'totalOriginal'
    | 'pagoLiquido'
    | 'saldo'
    | 'autorizacaoDocumental'
    | 'autorizacaoFinanceira'
  > {
  readonly conferencia: 'aprovada' | 'divergente';
  readonly duracaoTicks: number;
  readonly diarias: number;
  readonly tarifaVersao: number;
  readonly tarifaValorSnapshot: number;
  readonly totalOriginal: number;
  readonly descontos: number;
  readonly pagamentos: number;
  readonly estornos: number;
  readonly cobrado: number;
  readonly pagoLiquido: number;
  readonly saldo: number;
  readonly revisaoRazao: number;
  readonly lancamentos: readonly LancamentoResumo[];
  readonly autorizacaoDocumental: AutorizacaoResumo;
  readonly autorizacaoFinanceira: AutorizacaoResumo;
  readonly vistoriaSaidaRegistrada?: boolean;
  readonly entrega?: ResumoEntrega | null;
}

export interface AtendimentoResumo {
  readonly id: string;
  readonly identificador: string;
  readonly placa: string;
  readonly modelo: string;
  readonly titular: string;
  readonly estado: string;
  readonly vagaCodigo: string;
  readonly entradaUtc: string;
  readonly pedidoId?: string | null;
  readonly estadoRetirada?: string | null;
}

export interface AtendimentoBusca extends AtendimentoResumo {
  readonly tarifaVersao?: number;
  readonly tarifaValorSnapshot?: number;
  readonly versaoAtendimento?: string;
  readonly vistoriaSaidaRegistrada?: boolean;
  readonly fotos?: ResumoFotosVistoria | null;
  readonly entrega?: ResumoEntrega | null;
  readonly retirada?: {
    readonly pedidoId: string;
    readonly estado: string;
    readonly solicitadoEmUtc: string;
    readonly buscaIniciadaEmUtc: string | null;
    readonly buscaPor: string | null;
    readonly diarias?: number;
    readonly totalOriginal?: number;
    readonly cobrado?: number;
    readonly pagoLiquido?: number;
    readonly saldo?: number;
  } | null;
}

export interface ResultadoPedidoRetirada {
  readonly pedidoId: string;
  readonly atendimentoId: string;
  readonly identificador: string;
  readonly estado: string;
  readonly solicitadoEmUtc: string;
  readonly diarias: number;
  readonly totalOriginal: number;
  readonly location: string;
}

export interface EventoHistorico {
  readonly tipo: string;
  readonly atorOpaco: string;
  readonly ocorridoEmUtc: string;
}

export interface LeituraSensivel {
  readonly conteudo: string;
}

export interface ResultadoLancamento {
  readonly pedidoId: string;
  readonly atendimentoId: string;
  readonly lancamentoId: string;
  readonly tipo: string;
  readonly valor: number;
  readonly revisaoRazao: number;
  readonly saldo: number;
  readonly location: string;
}

export interface ResultadoAutorizacao {
  readonly pedidoId: string;
  readonly autorizacaoId: string;
  readonly tipo: string;
  readonly vigencia: string;
  readonly revisaoReferencia: number;
  readonly concedidaEmUtc: string;
  readonly location: string;
}

export type PaginaRetiradas = Pagina<RetiradaFila>;
