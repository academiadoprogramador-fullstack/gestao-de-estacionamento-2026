import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { urlApi } from '../../../environments/environment';
import { Pagina } from '../../core/http/erro-api';
import {
  AtendimentoBusca,
  AtendimentoResumo,
  EventoHistorico,
  LeituraSensivel,
  ResultadoPedidoRetirada,
  RetiradaDetalheFinanceira,
  RetiradaFila,
} from './retiradas.tipos';

/** Transporte HTTP tipado da fila e do pedido de retirada. */
@Injectable({ providedIn: 'root' })
export class RetiradasApiService {
  private readonly http = inject(HttpClient);

  listar(
    estado: string | null,
    pagina: number,
    tamanho: number,
  ): Observable<Pagina<RetiradaFila>> {
    const parametros = new URLSearchParams({
      page: String(pagina),
      pageSize: String(tamanho),
    });

    if (estado) {
      parametros.set('state', estado);
    }

    return this.http.get<Pagina<RetiradaFila>>(urlApi(`/api/v1/withdrawals?${parametros}`));
  }

  detalhe(pedidoId: string): Observable<RetiradaDetalheFinanceira> {
    return this.http.get<RetiradaDetalheFinanceira>(urlApi(`/api/v1/withdrawals/${pedidoId}`));
  }

  iniciarBusca(pedidoId: string, versaoEsperada: string): Observable<RetiradaDetalheFinanceira> {
    return this.http.post<RetiradaDetalheFinanceira>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/search`),
      { versaoEsperada },
    );
  }

  registrarPedido(
    atendimentoId: string,
    conteudo: { titularPresente: boolean; conferencia: 'aprovada' | 'divergente' },
    chave: string,
  ): Observable<ResultadoPedidoRetirada> {
    return this.http.post<ResultadoPedidoRetirada>(
      urlApi(`/api/v1/attendances/${atendimentoId}/withdrawal`),
      conteudo,
      { headers: { 'Idempotency-Key': chave } },
    );
  }

  listarAtendimentosAbertos(
    pagina: number,
    tamanho: number,
  ): Observable<Pagina<AtendimentoResumo>> {
    return this.http.get<Pagina<AtendimentoResumo>>(
      urlApi(`/api/v1/attendances?status=aberto&page=${pagina}&pageSize=${tamanho}`),
    );
  }

  buscarAtendimentos(
    criterio: 'plate' | 'identifier',
    termo: string,
  ): Observable<Pagina<AtendimentoBusca>> {
    const parametros = new URLSearchParams({ page: '1', pageSize: '20' });
    parametros.set(criterio, termo);
    return this.http.get<Pagina<AtendimentoBusca>>(urlApi(`/api/v1/attendances?${parametros}`));
  }

  detalheAtendimento(atendimentoId: string): Observable<AtendimentoBusca> {
    return this.http.get<AtendimentoBusca>(urlApi(`/api/v1/attendances/${atendimentoId}`));
  }

  lerDocumento(atendimentoId: string): Observable<LeituraSensivel> {
    return this.http.get<LeituraSensivel>(urlApi(`/api/v1/attendances/${atendimentoId}/document`));
  }

  historico(atendimentoId: string): Observable<Pagina<EventoHistorico>> {
    return this.http.get<Pagina<EventoHistorico>>(
      urlApi(`/api/v1/attendances/${atendimentoId}/history?page=1&pageSize=50`),
    );
  }
}
