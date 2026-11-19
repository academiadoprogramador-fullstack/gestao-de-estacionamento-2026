# Spec Delta

## ADDED Requirements

### Requirement: Detalhe do atendimento com retirada e caixa

O detalhe do atendimento SHALL informar a existência e o estado do pedido de retirada e os horários de pedido e início de busca, e SHALL entregar a Caixa e Administrador a cobrança, o saldo e o resumo financeiro permitidos, mantendo o Manobrista sem finanças, conferência, autorizações, justificativas, documento completo e vistoria.

#### Scenario: Atendimento ativo sem pedido

- **WHEN** um dos três perfis consulta o detalhe de um atendimento ativo ainda sem pedido
- **THEN** a resposta indica ausência de retirada e mantém as projeções já definidas para entrada e vaga

#### Scenario: Atendimento com pedido

- **WHEN** Caixa ou Administrador consulta um atendimento com pedido
- **THEN** a resposta inclui estado da retirada, horários, diárias, total, descontos, pagamentos, estornos e saldo permitidos

#### Scenario: Manobrista sem finanças

- **WHEN** um Manobrista consulta o mesmo detalhe
- **THEN** a resposta não contém valores, saldo, conferência, autorizações, justificativas, documento completo ou vistoria

#### Scenario: Conteúdo sensível permanece explícito

- **WHEN** Caixa ou Administrador precisa do documento ou da vistoria
- **THEN** a leitura continua exigindo ação explícita e auditoria confirmada antes de retornar o conteúdo

### Requirement: Histórico de retirada e caixa por perfil

O histórico do atendimento SHALL registrar pedido, conferência, cálculo, mudanças de estado da fila, pagamentos, descontos, estornos e autorizações com ator e horário UTC e SHALL ser filtrado por perfil antes da paginação, sem expor conteúdo sensível ao Manobrista nem justificativas fora do Administrador.

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

- **WHEN** um evento de retirada, caixa ou autorização é gravado
- **THEN** a auditoria guarda ator, alvo, tipo e UTC, sem documento, justificativa ou texto de vistoria

### Requirement: Listagem de atendimentos em aberto

O sistema SHALL permitir aos três perfis listar, com paginação, os atendimentos em aberto da unidade (ativo, aguardando ou em busca), em ordem estável de entrada, informando identificação, veículo, vaga, entrada e a existência/estado da retirada, e SHALL NOT incluir finanças, conferência, autorizações, justificativas, documento completo ou vistoria na projeção de lista.

#### Scenario: Lista padrão de abertos

- **WHEN** Caixa ou Administrador abre a lista de atendimentos em aberto sem informar placa ou identificador
- **THEN** recebe uma página de atendimentos ainda não entregues, ordenada por entrada, com a situação da retirada de cada um

#### Scenario: Tela de atendimentos por omissão

- **WHEN** um dos três perfis abre a tela de atendimentos
- **THEN** a lista de abertos é carregada automaticamente com paginação, sem exigir placa ou identificador

#### Scenario: Busca exata opcional na tela

- **WHEN** o usuário informa placa ou identificador
- **THEN** a lista é filtrada pela busca exata e a tela oferece limpar o filtro para voltar aos atendimentos em aberto

#### Scenario: Atendimento com pedido na lista

- **WHEN** um atendimento listado já possui pedido de retirada
- **THEN** a lista informa o estado da retirada e o identificador do pedido, sem permitir um novo pedido

#### Scenario: Visão do Manobrista

- **WHEN** um Manobrista consulta a lista de atendimentos em aberto
- **THEN** recebe a mesma projeção operacional, sem valores, saldo, conferência, autorizações, justificativas, documento completo ou vistoria

#### Scenario: Filtro exclusivo

- **WHEN** a consulta combina `status=aberto` com placa, identificador ou outro status
- **THEN** a API recusa a consulta como filtro inválido, sem listar todos por omissão

#### Scenario: Paginação completa

- **WHEN** existem mais atendimentos em aberto do que o tamanho de página
- **THEN** é possível navegar por todas as páginas mantendo a ordem estável e a projeção por perfil
