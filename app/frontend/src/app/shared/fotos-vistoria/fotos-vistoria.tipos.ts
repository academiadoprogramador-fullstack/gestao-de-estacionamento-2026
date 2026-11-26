/** Tipos de foto e metadados retornados pela API sem chave do bucket. */
export interface FotoVistoriaResumo {
  readonly fotoId: string;
  readonly tipo: 'entrada' | 'saida';
  readonly mime: string;
  readonly tamanhoBytes: number;
  readonly largura: number;
  readonly altura: number;
  readonly criadoEmUtc: string;
}

export interface OrcamentoFotos {
  readonly percentualBytes: number;
  readonly percentualOperacoesClasseA: number;
  readonly percentualOperacoesClasseB: number;
  readonly aviso: boolean;
  readonly suspenso: boolean;
  readonly motivo: string | null;
  readonly limiteFotosPorVistoria: number;
}

export interface ListaFotosVistoria {
  readonly itens: readonly FotoVistoriaResumo[];
  readonly quantidade: number;
  readonly janela: 'aberta' | 'encerrada';
  readonly vencido: boolean;
  readonly orcamento: OrcamentoFotos;
}

/** Resumo embutido no detalhe do atendimento e na prontidão de entrega. */
export interface ResumoFotosVistoria {
  readonly quantidadeEntrada: number;
  readonly quantidadeSaida: number;
  readonly janelaEntrada: 'aberta' | 'encerrada';
  readonly janelaSaida: 'aberta' | 'encerrada';
  readonly orcamento: OrcamentoFotos;
}
