import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { urlApi } from '../../../environments/environment';
import { AtendimentoBusca, RetiradaDetalheFinanceira } from '../retiradas/retiradas.tipos';
import {
  LeituraVistoriaSaida,
  ProntidaoEntrega,
  ResultadoEntrega,
  ResultadoVistoriaSaida,
  VersoesEntrega,
} from './delivery.tipos';

export interface CorpoConfirmacaoEntrega extends VersoesEntrega {
  readonly confirmacao: true;
}

/** Transporte HTTP tipado da vistoria de saída e da entrega. */
@Injectable({ providedIn: 'root' })
export class DeliveryApiService {
  private readonly http = inject(HttpClient);

  obterPedido(pedidoId: string): Observable<RetiradaDetalheFinanceira> {
    return this.http.get<RetiradaDetalheFinanceira>(urlApi(`/api/v1/withdrawals/${pedidoId}`));
  }

  obterAtendimento(atendimentoId: string): Observable<AtendimentoBusca> {
    return this.http.get<AtendimentoBusca>(urlApi(`/api/v1/attendances/${atendimentoId}`));
  }

  prontidao(pedidoId: string): Observable<ProntidaoEntrega> {
    return this.http.get<ProntidaoEntrega>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/delivery-readiness`),
    );
  }

  registrarVistoria(
    atendimentoId: string,
    texto: string,
    versaoAtendimento: string,
    chave: string,
  ): Observable<ResultadoVistoriaSaida> {
    return this.http.post<ResultadoVistoriaSaida>(
      urlApi(`/api/v1/attendances/${atendimentoId}/exit-inspection`),
      { texto, versaoAtendimentoEsperada: versaoAtendimento },
      { headers: { 'Idempotency-Key': chave } },
    );
  }

  lerVistoria(atendimentoId: string): Observable<LeituraVistoriaSaida> {
    return this.http.get<LeituraVistoriaSaida>(
      urlApi(`/api/v1/attendances/${atendimentoId}/exit-inspection`),
    );
  }

  confirmarEntrega(
    pedidoId: string,
    corpo: CorpoConfirmacaoEntrega,
    chave: string,
  ): Observable<ResultadoEntrega> {
    return this.http.post<ResultadoEntrega>(
      urlApi(`/api/v1/withdrawals/${pedidoId}/delivery`),
      corpo,
      { headers: { 'Idempotency-Key': chave } },
    );
  }
}
