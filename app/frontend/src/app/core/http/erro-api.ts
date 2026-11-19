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
  withdrawal_requires_holder:
    'Declare a presença do titular. Retirada por terceiro ou com titular ausente não é permitida no MVP.',
  withdrawal_invalid_conference: 'Informe a conferência aprovada ou divergente.',
  withdrawal_invalid_attendance: 'Selecione um atendimento para o pedido de retirada.',
  withdrawal_already_exists:
    'Este atendimento já possui pedido. Um segundo pedido no mesmo ciclo é recusado.',
  withdrawal_not_found: 'Pedido de retirada não encontrado.',
  withdrawal_not_waiting:
    'O pedido não está mais aguardando busca. Recarregue a fila para ver o estado atual.',
  stale_withdrawal_version:
    'O pedido foi atualizado por outra operação. Recarregue o estado atual antes de tentar novamente.',
  attendance_not_active: 'O atendimento não está ativo para receber um pedido de retirada.',
  ledger_revision_stale:
    'A razão do atendimento foi alterada por outra operação. Recarregue o saldo e a revisão atuais.',
  payment_must_equal_balance: 'O acerto deve ser integral: o valor precisa ser exatamente o saldo.',
  balance_already_settled: 'A cobrança já está zerada e não exige pagamento.',
  payment_invalid_method: 'A forma de pagamento deve ser dinheiro, cartão ou Pix.',
  pagamento_invalido: 'Informe um valor positivo com no máximo duas casas decimais.',
  discount_requires_no_payment: 'Desconto bloqueado: existe pagamento líquido maior que zero.',
  discount_exceeds_balance: 'O desconto não pode exceder o valor ainda descontável do atendimento.',
  discount_requires_justification: 'Informe a justificativa obrigatória do desconto.',
  desconto_invalido: 'O desconto deve ser um valor positivo com no máximo duas casas decimais.',
  refund_must_be_full: 'O estorno deve ser integral e igual ao pagamento original.',
  payment_already_refunded: 'Este pagamento já foi estornado. O estorno original permanece preservado.',
  payment_not_found: 'Pagamento não encontrado para este atendimento.',
  estorno_invalido: 'Informe o valor integral positivo do pagamento a estornar.',
  estorno_requires_justification: 'Informe a justificativa obrigatória do estorno.',
  authorization_not_current:
    'A autorização não está vigente para a versão atual. Recarregue o pedido.',
  authorization_already_revoked:
    'Esta autorização já foi revogada. A concessão e a revogação permanecem no histórico.',
  authorization_requires_justification: 'Informe a justificativa obrigatória da autorização.',
  authorization_invalid_type: 'O tipo de autorização deve ser documental ou financeira.',
  authorization_justification_denied: 'A leitura da justificativa é exclusiva do Administrador.',
  revocation_requires_justification: 'Informe a justificativa obrigatória da revogação.',
  launch_not_found: 'Lançamento financeiro não encontrado.',
  launch_without_justification: 'Este lançamento não possui justificativa administrativa.',
  ledger_not_found: 'O atendimento não possui razão financeira.',
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
