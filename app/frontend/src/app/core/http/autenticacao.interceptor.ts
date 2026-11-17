import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { ambiente } from '../../../environments/environment';
import { SessaoService } from '../sessao/sessao.service';

const METODOS_SEGUROS = new Set(['GET', 'HEAD', 'OPTIONS']);

/**
 * Anexa o bearer somente a URLs da API explicitamente configurada, nunca usa
 * `withCredentials` e nunca envia token a destinos arbitrários.
 *
 * Em `401`, dispara renovação single-flight. Apenas requisições seguras são
 * repetidas automaticamente; comandos de negócio não são reenviados.
 */
export const autenticacaoInterceptor: HttpInterceptorFn = (requisicao, proximo) => {
  const sessao = inject(SessaoService);
  const router = inject(Router);

  const pertenceAApi = requisicao.url.startsWith(ambiente.apiUrl);
  const token = sessao.tokenAcesso;

  const requisicaoAutenticada =
    pertenceAApi && token
      ? requisicao.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : requisicao;

  return proximo(requisicaoAutenticada).pipe(
    catchError((erro: unknown) => {
      if (!(erro instanceof HttpErrorResponse) || erro.status !== 401 || !pertenceAApi) {
        return throwError(() => erro);
      }

      if (
        requisicao.url.endsWith('/session/login') ||
        requisicao.url.endsWith('/session/refresh')
      ) {
        return throwError(() => erro);
      }

      return from(sessao.renovar()).pipe(
        switchMap((renovado) => {
          if (!renovado) {
            void router.navigate(['/login']);
            return throwError(() => erro);
          }

          if (!METODOS_SEGUROS.has(requisicao.method)) {
            return throwError(() => erro);
          }

          return proximo(
            requisicao.clone({
              setHeaders: { Authorization: `Bearer ${sessao.tokenAcesso ?? ''}` },
            }),
          );
        }),
      );
    }),
  );
};
