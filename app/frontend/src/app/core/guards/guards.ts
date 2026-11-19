import { inject } from '@angular/core';
import { CanActivateFn, CanDeactivateFn, Router } from '@angular/router';
import { ComandoPendenteService } from '../comando/comando-pendente.service';
import { SessaoService } from '../sessao/sessao.service';

/**
 * Resolve o estado da sessão em memória antes de permitir a navegação.
 * Como não há persistência, recarregar a página conduz ao login.
 */
export const guardaSessao: CanActivateFn = () => {
  const sessao = inject(SessaoService);
  const router = inject(Router);

  return sessao.autenticado() ? true : router.createUrlTree(['/login']);
};

/**
 * Complementa a autorização da API: sem o perfil exigido, encaminha para a
 * área de acesso negado. A API continua sendo a autoridade final.
 */
export const guardaPerfil =
  (perfisPermitidos: readonly string[]): CanActivateFn =>
  () => {
    const sessao = inject(SessaoService);
    const router = inject(Router);

    if (!sessao.autenticado()) {
      return router.createUrlTree(['/login']);
    }

    return sessao.possuiPerfil(perfisPermitidos) ? true : router.createUrlTree(['/forbidden']);
  };

/**
 * Impede a saída de uma tela enquanto um comando crítico está pendente ou
 * incerto. O aviso antes de fechar/recarregar o navegador é armado pelo
 * coordenador do comando.
 */
export const guardaComandoPendente: CanDeactivateFn<unknown> = () => {
  const comando = inject(ComandoPendenteService);

  if (!comando.bloqueado()) {
    return true;
  }

  return window.confirm(
    'Existe uma tentativa de comando pendente ou incerta. Saia somente se aceitar perder o resultado desta tentativa.',
  );
};
