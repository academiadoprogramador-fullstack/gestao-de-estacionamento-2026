import { ResumoEntrega } from '../retiradas/retiradas.tipos';
import { ResumoFotosVistoria } from '../../shared/fotos-vistoria/fotos-vistoria.tipos';

/** Condição avaliada pelo servidor para a prontidão da entrega. */
export interface CondicaoEntrega {
  readonly codigo: string;
  readonly descricao: string;
  readonly situacao: string;
  readonly atendida: boolean;
  readonly codigoBloqueio: string | null;
  readonly concessaoId: string | null;
  readonly revisaoReferencia: number | null;
}

/** Versões-base opacas devolvidas pela prontidão. */
export interface VersoesEntrega {
  readonly versaoAtendimento: string;
  readonly versaoPedido: string;
  readonly versaoRazao: string;
  readonly versaoControleDocumental: string;
  readonly versaoControleFinanceira: string;
}

export interface ProntidaoEntrega {
  readonly pedidoId: string;
  readonly atendimentoId: string;
  readonly identificador: string;
  readonly estado: string;
  readonly vagaCodigo: string;
  readonly vistoriaRegistrada: boolean;
  readonly conferencia: 'aprovada' | 'divergente';
  readonly saldo: number;
  readonly condicoes: readonly CondicaoEntrega[];
  readonly bloqueios: readonly string[];
  readonly pronta: boolean;
  readonly entrega: ResumoEntrega | null;
  readonly versoes: VersoesEntrega;
  readonly fotos?: ResumoFotosVistoria | null;
}

export interface ResultadoVistoriaSaida {
  readonly vistoriaId: string;
  readonly atendimentoId: string;
  readonly pedidoId: string;
  readonly ator: string;
  readonly registradaEmUtc: string;
  readonly location: string;
}

export interface LeituraVistoriaSaida {
  readonly conteudo: string;
  readonly ator: string;
  readonly registradaEmUtc: string;
}

export interface ResultadoEntrega {
  readonly entregaId: string;
  readonly pedidoId: string;
  readonly atendimentoId: string;
  readonly estado: string;
  readonly entregueEmUtc: string;
  readonly ator: string;
  readonly liberacao: ResumoEntrega;
  readonly location: string;
}
