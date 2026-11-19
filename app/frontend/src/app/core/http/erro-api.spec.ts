import { HttpErrorResponse } from '@angular/common/http';
import { mensagemDoErro } from './erro-api';

function erro(status: number, code?: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: code ? { code } : null });
}

describe('mensagemDoErro', () => {
  it('traduz os códigos da retirada e do caixa sem expor detalhe técnico', () => {
    expect(mensagemDoErro(erro(400, 'withdrawal_requires_holder'))).toContain('presença do titular');
    expect(mensagemDoErro(erro(409, 'withdrawal_already_exists'))).toContain('segundo pedido');
    expect(mensagemDoErro(erro(409, 'ledger_revision_stale'))).toContain('Recarregue o saldo');
    expect(mensagemDoErro(erro(422, 'payment_must_equal_balance'))).toContain('integral');
    expect(mensagemDoErro(erro(422, 'discount_requires_no_payment'))).toContain('pagamento líquido');
    expect(mensagemDoErro(erro(409, 'payment_already_refunded'))).toContain('estornado');
    expect(mensagemDoErro(erro(403, 'authorization_justification_denied'))).toContain(
      'Administrador',
    );
  });

  it('mantém as mensagens genéricas de rede, sessão e perfil', () => {
    expect(mensagemDoErro(erro(0))).toContain('alcançar a API');
    expect(mensagemDoErro(erro(401))).toContain('sessão expirou');
    expect(mensagemDoErro(erro(403))).toContain('Acesso negado');
  });
});
