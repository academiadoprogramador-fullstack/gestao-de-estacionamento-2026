# Delta — Consulta de atendimentos

## MODIFIED Requirements

### Requirement: Detalhe do atendimento com retirada e caixa

O detalhe do atendimento SHALL informar existência e estado do pedido, horários de pedido e busca e, após entrega, ID, autor e horário da entrega e horário de liberação. Caixa e Administrador SHALL receber cobrança, saldo e resumo financeiro; Manobrista SHALL NOT receber finanças, conferência, autorizações, justificativas, documento completo ou texto de vistoria (F4-R06).

#### Scenario: Atendimento ativo sem pedido

- **WHEN** um dos três perfis consulta o detalhe de um atendimento ativo ainda sem pedido
- **THEN** a resposta indica ausência de retirada e mantém as projeções já definidas para entrada e vaga

#### Scenario: Atendimento com pedido

- **WHEN** Caixa ou Administrador consulta um atendimento com pedido
- **THEN** a resposta inclui estado da retirada, horários, diárias, total, descontos, pagamentos, estornos e saldo permitidos

#### Scenario: Manobrista sem finanças

- **WHEN** um Manobrista consulta o mesmo detalhe
- **THEN** a resposta não contém valores, saldo, conferência, autorizações, justificativas, documento completo ou texto de vistoria

#### Scenario: Conteúdo sensível permanece explícito

- **WHEN** Caixa ou Administrador precisa do documento, ou algum dos três perfis precisa da vistoria
- **THEN** a leitura continua exigindo ação explícita e auditoria confirmada antes de retornar o conteúdo

#### Scenario: Detalhe de atendimento entregue

- **WHEN** o funcionário localiza por busca exata e abre o detalhe de um atendimento entregue
- **THEN** a tela exibe entregue, identificação, horário e responsável pela entrega e a liberação da vaga vinculada, sem confundir histórico da visita com disponibilidade corrente

### Requirement: Histórico de retirada e caixa por perfil

O histórico SHALL registrar pedido, conferência, cálculo, estados da fila, pagamentos, descontos, estornos, autorizações, vistoria de saída, consumo, entrega e liberação com ator e horário UTC. SHALL filtrar por perfil antes de paginar, sem incluir texto de vistoria, documento ou justificativa em eventos (F4-R06).

#### Scenario: Histórico financeiro do caixa

- **WHEN** Caixa ou Administrador consulta o histórico de um atendimento com movimentação financeira
- **THEN** os eventos permitidos aparecem em ordem estável com ator, horário e valores, sem justificativas

#### Scenario: Histórico operacional do manobrista

- **WHEN** um Manobrista consulta o histórico
- **THEN** recebe somente eventos operacionais permitidos, sem finanças, conferência, autorizações ou justificativas

#### Scenario: Justificativa somente para administrador

- **WHEN** o Administrador solicita explicitamente a justificativa de desconto, estorno ou autorização
- **THEN** a API audita a leitura antes de devolver o texto decifrado

#### Scenario: Paginação filtrada

- **WHEN** o histórico possui mais eventos do que a página solicitada
- **THEN** a paginação considera apenas os eventos permitidos ao perfil, sem lacunas que revelem eventos omitidos

#### Scenario: Auditoria sem conteúdo

- **WHEN** um evento de retirada, caixa, autorização, vistoria de saída, entrega ou liberação é gravado
- **THEN** a auditoria guarda ator, alvo, tipo e UTC, sem documento, justificativa ou texto de vistoria

#### Scenario: Histórico de saída por perfil

- **WHEN** o funcionário abre o histórico de uma entrega com exceções
- **THEN** os três perfis veem vistoria registrada, entrega e liberação com ator e horário; somente Caixa e Administrador veem os consumos permitidos, sem texto de justificativa
