# Delta — Fila de retiradas

## MODIFIED Requirements

### Requirement: Fila paginada de pedidos de retirada

O sistema SHALL permitir aos três perfis consultar pedidos de retirada com paginação padrão de 20 e máxima de 100, em ordem estável por horário do pedido e identificador, exibindo os estados aguardando, em busca e entregue (F4-R06).

#### Scenario: Consulta pelos três perfis

- **WHEN** Administrador, Caixa ou Manobrista consulta a fila com página e tamanho válidos
- **THEN** a resposta contém itens, página, tamanho e total, com identificação operacional, estado e horários permitidos ao perfil

#### Scenario: Paginação completa

- **WHEN** existem mais pedidos do que o tamanho de página
- **THEN** é possível navegar por todas as páginas e recarregar a página corrente sem perder filtros nem voltar à primeira página

#### Scenario: Estado entregue ausente

- **WHEN** o filtro entregue é aplicado sem pedidos entregues correspondentes
- **THEN** a fila apresenta um estado vazio textual, sem inventar entregas

#### Scenario: Entregues na fila

- **WHEN** um dos três perfis abre a fila após uma entrega ou seleciona o filtro entregue
- **THEN** os registros correspondentes mostram entregue, identificação da entrega, horário, responsável, vaga histórica e horário da liberação, sem ação de iniciar busca no registro entregue

## ADDED Requirements

### Requirement: Acesso à operação de entrega pela fila

A fila SHALL oferecer aos três perfis acesso à operação de entrega do pedido, com as ações permitidas pelo perfil e estado. Consultar fila ou iniciar busca SHALL NOT liberar vaga nem consumir autorização (F4-R02/06).

#### Scenario: Abertura do pedido pela fila

- **WHEN** o funcionário abre a operação de um pedido na fila
- **THEN** a tela de entrega abre no pedido correto, com vistoria para os três perfis e confirmação somente para Caixa ou Administrador quando elegível

#### Scenario: Fila não libera vaga

- **WHEN** um pedido em aberto entra em busca ou sua linha é atualizada na fila sem confirmação de entrega
- **THEN** a ocupação permanece aberta e a vaga continua indisponível para nova entrada

## REMOVED Requirements

### Requirement: Ausência de entrega antecipada

**Reason**: A entrega deixa de ser indisponível; a proteção agora é definida por `entrega-veiculo` e pelo acesso à operação sem efeitos na fila.

**Migration**: A confirmação ocorre exclusivamente no novo comando de entrega; iniciar busca e consultar a fila permanecem sem efeitos de liberação.
