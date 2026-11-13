import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, tap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const apiPrefix = '/api/';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const isApiRequest = request.url.startsWith(apiPrefix);
  const isMutation = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method);
  const prepareRequest =
    isApiRequest && isMutation
      ? from(auth.getAntiforgeryToken()).pipe(
          switchMap((token) =>
            next(
              request.clone({
                withCredentials: true,
                headers: request.headers.set(token.headerName, token.requestToken),
              }),
            ),
          ),
        )
      : next(isApiRequest ? request.clone({ withCredentials: true }) : request);

  return prepareRequest.pipe(
    tap(() => auth.accessDenied.set(false)),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        auth.clearSession();
        if (router.url !== '/' && !router.url.startsWith('/login')) {
          void router.navigateByUrl('/');
        }
      } else if (error instanceof HttpErrorResponse && error.status === 403) {
        auth.accessDenied.set(true);
        if (!router.url.startsWith('/forbidden')) {
          void router.navigateByUrl('/forbidden');
        }
      }
      return throwError(() => error);
    }),
  );
};
