# Spec Delta — Consulta de atendimentos com fotos

## MODIFIED Requirements

### Requirement: Projeções por perfil no servidor

O sistema SHALL omitir documento completo, texto de vistoria e conteúdo/chave de foto das listagens e do detalhe comum, SHALL informar somente presença e quantidade de fotos quando permitido e SHALL omitir versão e valor de tarifa e demais dados financeiros do JSON entregue ao Manobrista, negando também no servidor o que o perfil não pode ver.

#### Scenario: Listagem sem conteúdo sensível

- **WHEN** qualquer perfil consulta a busca de atendimentos
- **THEN** os itens incluem identificação operacional e estado, sem documento completo, texto de vistoria, binário, chave ou URL de foto

#### Scenario: Dados financeiros fora do perfil

- **WHEN** um Manobrista consulta detalhe ou histórico
- **THEN** a resposta não contém snapshot de tarifa, valores ou eventos financeiros, e a omissão ocorre no DTO, não apenas na tela

#### Scenario: Acesso negado no servidor

- **WHEN** um perfil requisita diretamente um recurso que não lhe é permitido
- **THEN** a API responde com acesso negado sem depender do guard da SPA

#### Scenario: Fotos apenas por vínculo

- **WHEN** qualquer projeção comum inclui um atendimento com fotos
- **THEN** informa apenas presença e quantidade permitidas; conteúdo e metadados de armazenamento exigem a consulta específica da vistoria

### Requirement: Detalhe do atendimento com retirada e caixa

O detalhe do atendimento SHALL informar existência e estado do pedido, horários de pedido e busca e, após entrega, ID, autor e horário da entrega e horário de liberação. Caixa e Administrador SHALL receber cobrança, saldo e resumo financeiro; todos os perfis SHALL receber presença e quantidade de fotos das vistorias, sem o conteúdo; Manobrista SHALL NOT receber finanças, conferência, autorizações, justificativas, documento completo ou texto de vistoria (F4-R06; RF-13).

#### Scenario: Atendimento ativo sem pedido

- **WHEN** um dos três perfis consulta o detalhe de um atendimento ativo ainda sem pedido
- **THEN** a resposta indica ausência de retirada, a janela de fotos de entrada ainda aberta e mantém as projeções já definidas para entrada e vaga

#### Scenario: Atendimento com pedido

- **WHEN** Caixa ou Administrador consulta um atendimento com pedido
- **THEN** a resposta inclui estado da retirada, horários, diárias, total, descontos, pagamentos, estornos, saldo permitidos e a janela de fotos de entrada encerrada

#### Scenario: Manobrista sem finanças

- **WHEN** um Manobrista consulta o mesmo detalhe
- **THEN** a resposta não contém valores, saldo, conferência, autorizações, justificativas, documento completo ou texto de vistoria, mas informa presença e quantidade de fotos das duas vistorias

#### Scenario: Conteúdo sensível permanece explícito

- **WHEN** Caixa ou Administrador precisa do documento, ou algum dos três perfis precisa da vistoria ou das fotos
- **THEN** a leitura continua exigindo ação explícita e auditoria confirmada antes de retornar o conteúdo

#### Scenario: Detalhe de atendimento entregue

- **WHEN** o funcionário localiza por busca exata e abre o detalhe de um atendimento entregue
- **THEN** a tela exibe entregue, identificação, horário e responsável pela entrega, a liberação da vaga vinculada e que a janela de fotos de saída está encerrada, sem confundir histórico da visita com disponibilidade corrente

### Requirement: Histórico de retirada e caixa por perfil

O histórico SHALL registrar pedido, conferência, cálculo, estados da fila, pagamentos, descontos, estornos, autorizações, vistoria de saída, anexos de foto confirmados, descarte, consumo, entrega e liberação com ator e horário UTC. SHALL filtrar por perfil antes de paginar, sem incluir texto de vistoria, documento, justificativa, binário ou chave de objeto em eventos (F4-R06; RF-13).

#### Scenario: Histórico financeiro do caixa

- **WHEN** Caixa ou Administrador consulta o histórico de um atendimento com movimentação financeira
- **THEN** os eventos permitidos aparecem em ordem estável com ator, horário e valores, sem justificativas

#### Scenario: Histórico operacional do manobrista

- **WHEN** um Manobrista consulta o histórico
- **THEN** recebe somente eventos operacionais permitidos, incluindo anexo e descarte de foto sem conteúdo, sem finanças, conferência, autorizações ou justificativas

#### Scenario: Justificativa somente para administrador

- **WHEN** o Administrador solicita explicitamente a justificativa de desconto, estorno ou autorização
- **THEN** a API audita a leitura antes de devolver o texto decifrado

#### Scenario: Paginação filtrada

- **WHEN** o histórico possui mais eventos do que a página solicitada
- **THEN** a paginação considera apenas os eventos permitidos ao perfil, sem lacunas que revelem eventos omitidos

#### Scenario: Auditoria sem conteúdo

- **WHEN** um evento de retirada, caixa, autorização, vistoria de saída, anexo de foto, entrega ou liberação é gravado
- **THEN** a auditoria guarda ator, alvo, tipo e UTC, sem documento, justificativa, texto de vistoria, binário ou chave de objeto

#### Scenario: Histórico de saída por perfil

- **WHEN** o funcionário abre o histórico de uma entrega com exceções e fotos
- **THEN** os três perfis veem vistoria registrada, fotos confirmadas, entrega e liberação com ator e horário; somente Caixa e Administrador veem os consumos permitidos, sem texto de justificativa ou conteúdo de imagem
