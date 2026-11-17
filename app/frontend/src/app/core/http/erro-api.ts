import { HttpErrorResponse } from '@angular/common/http';

/** Corpo Problem Details mínimo devolvido pela API. */
export interface ProblemaApi {
  readonly status?: number;
  readonly title?: string;
  readonly detail?: string;
  readonly code?: string;
  readonly correlationId?: string;
}

/** Extrai o código estável do erro sem exibir dados sensíveis. */
export function codigoDoErro(erro: unknown): string | null {
  if (erro instanceof HttpErrorResponse && typeof erro.error === 'object' && erro.error !== null) {
    const problema = erro.error as ProblemaApi;
    return problema.code ?? null;
  }

  return null;
}

/** Mensagem acessível e genérica para falhas de autenticação. */
export function mensagemDeFalha(erro: unknown): string {
  if (erro instanceof HttpErrorResponse) {
    if (erro.status === 0) {
      return 'Não foi possível alcançar a API. Verifique a disponibilidade do serviço e tente novamente.';
    }

    if (erro.status === 401) {
      return 'E-mail ou senha inválidos.';
    }

    if (erro.status === 403) {
      return 'Acesso negado para o seu perfil.';
    }

    if (erro.status === 429) {
      return 'Muitas tentativas. Aguarde um minuto antes de tentar novamente.';
    }
  }

  return 'Não foi possível concluir a operação. Tente novamente.';
}
