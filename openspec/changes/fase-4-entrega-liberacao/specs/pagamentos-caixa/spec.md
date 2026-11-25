# Delta — Pagamentos de caixa

## MODIFIED Requirements

### Requirement: Novo acerto após reabertura ou entrega excepcional

Após estorno integral ou entrega excepcional com saldo, o sistema SHALL admitir novo pagamento integral do saldo atual, sem reabrir atendimento, pedido, ocupação ou vaga, e SHALL NOT tratá-lo como duplicidade do pagamento anterior (F4-R05).

#### Scenario: Acerto após estorno

- **WHEN** um pagamento é integralmente estornado e o saldo é reaberto
- **THEN** um novo pagamento integral do saldo atual é aceito e o pago líquido volta a igualar o cobrado

#### Scenario: Acerto após entrega excepcional

- **WHEN** Caixa ou Administrador abre o caixa de um atendimento efetivamente entregue com saldo pendente e registra pagamento integral
- **THEN** a tela mostra o pendente antes do acerto e depois recarrega o saldo zerado e o histórico; entrega, consumo, lançamentos anteriores e ocupação são preservados, inclusive se a vaga já foi reutilizada

#### Scenario: Entrega não é antecipada

- **WHEN** o pagamento é registrado para atendimento ainda não entregue
- **THEN** o atendimento permanece aguardando ou em busca, sem transição para entregue nem liberação de vaga

## ADDED Requirements

### Requirement: Acesso à operação de entrega pelo caixa

O caixa SHALL oferecer a Caixa e Administrador acesso à operação de entrega do pedido, sem executar ou antecipar a entrega ao navegar ou registrar acerto (F4-R02/05/07).

#### Scenario: Abertura da entrega no caixa

- **WHEN** Caixa ou Administrador abre a operação de entrega a partir do caixa de um pedido
- **THEN** a tela de entrega abre para o mesmo pedido; autorização e saldo continuam dependendo do estado retornado pelo servidor e a navegação não altera a ocupação
