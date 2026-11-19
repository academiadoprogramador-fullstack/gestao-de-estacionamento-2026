import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { urlApi } from '../../../environments/environment';
import { LeituraSensivel } from '../retiradas/retiradas.tipos';
import {
  ResultadoAutorizacao,
  ResultadoLancamento,
  RetiradaDetalheFinanceira,
} from '../retiradas/retiradas.tipos';

/** Transporte HTTP tipado do caixa, ajustes e autorizações. */
@Injectable({ providedIn: 'root' })
export class CaixaApiService {
  private readonly http = inject(HttpClient);

  obter(pedidoId: string): Observable<RetiradaDetalheFinanceira> {
    return this.http.get<RetiradaDetalheFinanceira>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/cashier`),
    );
  }

  pagar(
    pedidoId: string,
    conteudo: { valor: number; forma: string; revisaoEsperada: number },
    chave: string,
  ): Observable<ResultadoLancamento> {
    return this.http.post<ResultadoLancamento>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/payments`),
      conteudo,
      { headers: { 'Idempotency-Key': chave } },
    );
  }

  descontar(
    pedidoId: string,
    conteudo: { valor: number; justificativa: string; revisaoEsperada: number },
    chave: string,
  ): Observable<ResultadoLancamento> {
    return this.http.post<ResultadoLancamento>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/discounts`),
      conteudo,
      { headers: { 'Idempotency-Key': chave } },
    );
  }

  estornar(
    pedidoId: string,
    conteudo: {
      pagamentoId: string;
      valor: number;
      justificativa: string;
      revisaoEsperada: number;
    },
    chave: string,
  ): Observable<ResultadoLancamento> {
    return this.http.post<ResultadoLancamento>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/refunds`),
      conteudo,
      { headers: { 'Idempotency-Key': chave } },
    );
  }

  concederAutorizacao(
    pedidoId: string,
    conteudo: { tipo: 'documental' | 'financeira'; justificativa: string; versaoEsperada: string },
    chave: string,
  ): Observable<ResultadoAutorizacao> {
    return this.http.post<ResultadoAutorizacao>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/authorizations`),
      conteudo,
      { headers: { 'Idempotency-Key': chave } },
    );
  }

  revogarAutorizacao(
    pedidoId: string,
    autorizacaoId: string,
    justificativa: string,
    chave: string,
  ): Observable<unknown> {
    return this.http.post(
      urlApi(`/api/v1/withdrawals/${pedidoId}/authorizations/${autorizacaoId}/revoke`),
      { justificativa },
      { headers: { 'Idempotency-Key': chave } },
    );
  }

  lerJustificativaLancamento(pedidoId: string, lancamentoId: string): Observable<LeituraSensivel> {
    return this.http.get<LeituraSensivel>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/launches/${lancamentoId}/justification`),
    );
  }

  lerJustificativaAutorizacao(
    pedidoId: string,
    autorizacaoId: string,
  ): Observable<LeituraSensivel> {
    return this.http.get<LeituraSensivel>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/authorizations/${autorizacaoId}/justification`),
    );
  }
}
