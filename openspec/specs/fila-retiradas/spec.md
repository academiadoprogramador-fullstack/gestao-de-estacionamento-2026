# fila-retiradas Specification

## Purpose

Define a fila de pedidos de retirada consultável pelos três perfis, com estados aguardando, em busca e entregue, início de busca auditado, atualização periódica sem sobreposição, projeções por perfil e acesso à operação de entrega sem liberar vaga nem consumir autorização.

## Requirements

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

### Requirement: Acesso à operação de entrega pela fila

A fila SHALL oferecer aos três perfis acesso à operação de entrega do pedido, com as ações permitidas pelo perfil e estado. Consultar fila ou iniciar busca SHALL NOT liberar vaga nem consumir autorização (F4-R02/06).

#### Scenario: Abertura do pedido pela fila

- **WHEN** o funcionário abre a operação de um pedido na fila
- **THEN** a tela de entrega abre no pedido correto, com vistoria para os três perfis e confirmação somente para Caixa ou Administrador quando elegível

#### Scenario: Fila não libera vaga

- **WHEN** um pedido em aberto entra em busca ou sua linha é atualizada na fila sem confirmação de entrega
- **THEN** a ocupação permanece aberta e a vaga continua indisponível para nova entrada

### Requirement: Início de busca registrado

O sistema SHALL permitir aos três perfis iniciar a busca de um pedido aguardando, registrando ator e horário do servidor, e SHALL recusar a transição para pedido em busca, inexistente ou concorrente sem duplicar a mudança de estado.

#### Scenario: Início de busca confirmado

- **WHEN** um funcionário autorizado inicia a busca de um pedido aguardando
- **THEN** o pedido passa a em busca com responsável e horário do servidor e a mudança é auditada

#### Scenario: Início repetido ou concorrente

- **WHEN** dois funcionários iniciam a busca do mesmo pedido ou o pedido já está em busca
- **THEN** apenas uma transição é confirmada, a outra recebe conflito e o estado corrente pode ser recarregado

#### Scenario: Pedido inelegível

- **WHEN** o pedido não existe ou não está aguardando
- **THEN** a API recusa o início de busca sem alterar estado, responsável ou histórico

### Requirement: Atualização periódica sem sobreposição

A fila SHALL atualizar por polling com intervalo padrão de 30 segundos e configurável, pausar em aba oculta, atualizar ao recuperar foco e após comando, cancelar consultas ao sair e nunca manter requisições sobrepostas.

#### Scenario: Atualização ao recuperar foco

- **WHEN** a aba oculta volta a ficar visível
- **THEN** a tela consulta o estado atual antes de apresentar a fila

#### Scenario: Aba oculta pausa o polling

- **WHEN** a aba permanece oculta
- **THEN** nenhuma nova consulta periódica é disparada até o retorno do foco

#### Scenario: Falha de atualização preserva contexto

- **WHEN** uma atualização periódica falha
- **THEN** a tela informa a falha, mantém filtros e página atuais e tenta novamente no próximo ciclo sem empilhar requisições

#### Scenario: Sem sobreposição de consultas

- **WHEN** uma consulta ainda está em andamento ao chegar o próximo ciclo
- **THEN** uma nova requisição não é disparada até a anterior terminar ou ser cancelada

### Requirement: Projeções por perfil na fila

O sistema SHALL omitir do Manobrista finanças, conferência, autorizações, justificativas, documento completo e vistoria, e SHALL entregar a Caixa e Administrador os valores e o saldo permitidos, com documento completo somente por leitura explícita e auditada.

#### Scenario: Visão do Manobrista

- **WHEN** um Manobrista consulta a fila ou o detalhe operacional de um pedido
- **THEN** a resposta contém identificação, veículo, estado e horários operacionais, sem valores, saldo, conferência, autorização, justificativa, documento completo ou texto de vistoria

#### Scenario: Visão do caixa

- **WHEN** Caixa ou Administrador consulta o pedido
- **THEN** a resposta inclui valores fixados, pagamentos, descontos, estornos, saldo e situação das autorizações aplicáveis à entrega

#### Scenario: Documento permanece sob leitura explícita

- **WHEN** Caixa ou Administrador precisa do documento completo para a conferência presencial
- **THEN** a leitura continua sendo uma ação explícita, auditada antes de retornar o conteúdo

#### Scenario: Omissão no contrato

- **WHEN** o JSON de um perfil proibido é inspecionado
- **THEN** os campos omitidos não existem no DTO, e não apenas estão escondidos na tela

