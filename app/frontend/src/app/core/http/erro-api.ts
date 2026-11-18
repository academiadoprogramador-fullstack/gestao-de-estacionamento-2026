import { HttpErrorResponse } from '@angular/common/http';

/** Corpo Problem Details mínimo devolvido pela API. */
export interface ProblemaApi {
  readonly status?: number;
  readonly title?: string;
  readonly detail?: string;
  readonly code?: string;
  readonly correlationId?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

/** Página padrão da API. */
export interface Pagina<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** Extrai o código estável do erro sem exibir dados sensíveis. */
export function codigoDoErro(erro: unknown): string | null {
  if (erro instanceof HttpErrorResponse && typeof erro.error === 'object' && erro.error !== null) {
    const problema = erro.error as ProblemaApi;
    return problema.code ?? null;
  }

  return null;
}

const MENSAGENS: Readonly<Record<string, string>> = {
  acesso_negado: 'Seu perfil não permite executar esta operação.',
  stale_access_version:
    'O acesso desta conta foi alterado. Recarregue a lista antes de tentar novamente.',
  last_active_administrator: 'A unidade precisa de ao menos um Administrador ativo.',
  space_code_conflict: 'Já existe uma vaga com o código informado.',
  space_unavailable: 'A vaga não está mais disponível. Selecione outra vaga livre.',
  stale_rate_version: 'A tarifa corrente foi alterada. Recarregue os valores antes de publicar.',
  rate_not_published: 'Publique uma tarifa antes de registrar entradas.',
  idempotency_conflict: 'Esta tentativa já foi usada com outro conteúdo. Revise os dados enviados.',
  field_protection_failure: 'Não foi possível ler o campo protegido.',
  email_em_uso: 'Já existe uma conta com este e-mail.',
  valor_tarifa_invalido: 'Informe um valor positivo, com no máximo duas casas decimais.',
  paginacao_invalida: 'Página ou tamanho de página inválidos.',
  placa_invalida: 'Informe uma placa com até 16 caracteres alfanuméricos.',
  entrada_invalida: 'Revise os campos obrigatórios da entrada.',
  idempotency_key_invalida: 'A chave de idempotência é obrigatória e deve ser um UUID.',
};

/** Mensagem acessível para falhas da API, priorizando o código estável. */
export function mensagemDoErro(erro: unknown, fallback?: string): string {
  if (erro instanceof HttpErrorResponse) {
    if (erro.status === 0) {
      return 'Não foi possível alcançar a API. Verifique a disponibilidade do serviço e tente novamente.';
    }

    if (erro.status === 401) {
      return 'Sua sessão expirou. Entre novamente.';
    }

    if (erro.status === 429) {
      return 'Muitas tentativas. Aguarde um minuto antes de tentar novamente.';
    }

    const codigo = codigoDoErro(erro);
    if (codigo && MENSAGENS[codigo]) {
      return MENSAGENS[codigo];
    }

    if (erro.status === 403) {
      return 'Acesso negado para o seu perfil.';
    }
  }

  return fallback ?? 'Não foi possível concluir a operação. Tente novamente.';
}

/** Mantém a compatibilidade com o texto genérico de autenticação. */
export function mensagemDeFalha(erro: unknown): string {
  if (erro instanceof HttpErrorResponse && erro.status === 401) {
    return 'E-mail ou senha inválidos.';
  }

  return mensagemDoErro(erro);
}
