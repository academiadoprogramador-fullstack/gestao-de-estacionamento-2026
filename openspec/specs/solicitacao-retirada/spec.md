# solicitacao-retirada Specification

## Purpose

Define o registro do pedido de retirada pelo caixa ou administrador, com conferência presencial do titular, bloqueio de terceiro, fixação imutável da cobrança, atomicidade, auditoria e repetição segura de tentativa incerta.

## Requirements

### Requirement: Pedido único para atendimento ativo

O sistema SHALL registrar o pedido de retirada somente para atendimento ativo e sem pedido anterior, por Caixa ou Administrador; horário, ator e vínculo com o atendimento SHALL ser do servidor, e um segundo pedido no mesmo ciclo SHALL ser recusado como conflito, salvo replay da mesma ação.

#### Scenario: Pedido confirmado

- **WHEN** Caixa ou Administrador registra o pedido de um atendimento ativo, sem pedido anterior, com conferência e `Idempotency-Key`
- **THEN** o atendimento passa ao estado aguardando, o pedido é vinculado com horário e ator do servidor, e a auditoria é confirmada na mesma transação

#### Scenario: Atendimento inelegível

- **WHEN** o atendimento não existe, não está ativo, já possui pedido ou já foi entregue
- **THEN** a API recusa o pedido com conflito ou regra não satisfeita e não altera o estado nem cria cobrança

#### Scenario: Segundo pedido no mesmo ciclo

- **WHEN** uma nova chave é usada para registrar outro pedido do mesmo atendimento
- **THEN** a API responde com conflito e preserva integralmente o pedido e o cálculo originais

#### Scenario: Replay do mesmo pedido

- **WHEN** a mesma chave e o mesmo conteúdo são reenviados por sessão autorizada
- **THEN** a API devolve o resultado mínimo original sem criar segundo pedido, cobrança ou evento duplicado

### Requirement: Conferência presencial do titular

O sistema SHALL exigir declaração de presença do titular e resultado aprovado ou divergente da conferência entre o documento com foto apresentado e o nome/número registrados, e SHALL NOT fotografar o documento nem coletar novo número.

#### Scenario: Conferência aprovada

- **WHEN** o conferente declara o titular presente e registra conferência aprovada
- **THEN** o pedido é confirmado com o resultado aprovado, sem foto do documento e sem novo número de documento

#### Scenario: Conferência divergente

- **WHEN** o conferente registra divergência entre o documento e os dados da entrada
- **THEN** o pedido e o cálculo são preservados com o resultado divergente, e a entrega normal fica bloqueada até autorização documental vigente

#### Scenario: Presença não declarada

- **WHEN** o pedido não declara a presença do titular ou omite o resultado da conferência
- **THEN** a API recusa o pedido com erro de validação e não altera o atendimento

#### Scenario: Documento não fotografado

- **WHEN** o cliente envia imagem, novo número de documento ou dados de terceiro no pedido
- **THEN** a API rejeita as propriedades inesperadas e não persiste conteúdo documental adicional

### Requirement: Retirada somente pelo titular presente

O sistema SHALL recusar retirada por terceiro ou com titular ausente, inclusive quando solicitada por Administrador ou acompanhada de autorização de exceção, sem coletar documento de terceiro e sem transferir o direito de retirada.

#### Scenario: Terceiro tenta retirar

- **WHEN** o conferente informa que o solicitante não é o titular registrado ou que o titular está ausente
- **THEN** a API recusa o pedido e orienta que a retirada por terceiro não é permitida no MVP

#### Scenario: Administrador tenta autorizar terceiro

- **WHEN** o Administrador tenta registrar ou autorizar retirada por terceiro
- **THEN** a API nega a operação e não cria autorização que transfira o direito de retirada

#### Scenario: Exceção não transfere o direito

- **WHEN** existe autorização documental ou financeira para o pedido
- **THEN** ela não permite retirada por terceiro nem dispensa a presença do titular

### Requirement: Fixação da cobrança no pedido

O pedido SHALL fixar o tempo decorrido, as diárias e o total calculados pelo servidor a partir do instante de entrada e do snapshot de tarifa, e esses valores SHALL permanecer imutáveis após o pedido.

#### Scenario: Cobrança fixada

- **WHEN** o pedido é confirmado
- **THEN** tempo decorrido, quantidade de diárias, tarifa aplicada e total ficam registrados e passam a compor o saldo do atendimento

#### Scenario: Publicação posterior de tarifa

- **WHEN** uma nova tarifa é publicada após o pedido
- **THEN** o total do atendimento permanece igual ao calculado com a tarifa capturada na entrada

#### Scenario: Cálculo não confirmado

- **WHEN** o cálculo falha ou o pedido é revertido
- **THEN** nenhum valor parcial, cobrança órfã ou estado de retirada permanece confirmado

### Requirement: Atomicidade e auditoria do pedido

O sistema SHALL confirmar pedido, conferência, cobrança, mudança de estado do atendimento, eventos de auditoria e resultado idempotente na mesma transação e SHALL desfazer integralmente a operação em qualquer falha.

#### Scenario: Confirmação conjunta

- **WHEN** o pedido é aceito
- **THEN** todos os registros da operação e a auditoria ficam visíveis juntos após o commit

#### Scenario: Falha no meio da operação

- **WHEN** qualquer etapa do pedido falha após gravação parcial
- **THEN** a transação é revertida e não permanecem pedido, cobrança, mudança de estado ou evento confirmados

#### Scenario: Falha de auditoria

- **WHEN** a gravação da auditoria do pedido falha
- **THEN** o pedido não é confirmado e o atendimento permanece no estado anterior

### Requirement: Tentativa incerta e replay do pedido

A SPA SHALL manter a tentativa de pedido somente em memória, bloquear navegação interna e logout enquanto o comando estiver pendente ou incerto, avisar antes de sair do navegador e permitir repetição explícita apenas com a mesma chave e o mesmo conteúdo.

#### Scenario: Comando pendente bloqueia saída

- **WHEN** o pedido foi enviado e o resultado ainda é desconhecido
- **THEN** a SPA impede navegação interna e logout e mostra aviso antes de fechar ou recarregar o navegador

#### Scenario: Resposta perdida

- **WHEN** a resposta do pedido se perde por falha de rede
- **THEN** a tentativa permanece em memória e a repetição usa exatamente a mesma `Idempotency-Key` e o mesmo conteúdo, sem gerar nova chave

#### Scenario: Rejeição definitiva

- **WHEN** a API rejeita o pedido por validação, conflito ou regra de negócio
- **THEN** a tela permite correção sem reenvio automático e a próxima submissão explícita usa uma nova chave

#### Scenario: Sucesso limpa dados da conferência

- **WHEN** o pedido é confirmado
- **THEN** a tela apresenta estado, horário e valores fixados, limpa os dados sensíveis da conferência mantidos em memória e não mantém resumo de validação

### Requirement: Seleção por lista de atendimentos em aberto

A SPA SHALL listar os atendimentos em aberto para visualização e seleção do pedido, com paginação, sem exigir placa ou identificador; a busca exata SHALL permanecer como filtro opcional, e atendimento que já possua retirada SHALL ser apresentado com o estado existente e sem opção de novo pedido.

#### Scenario: Lista padrão no pedido

- **WHEN** Caixa ou Administrador abre a tela de pedido de retirada
- **THEN** vê os atendimentos em aberto com identificação, veículo, vaga, entrada e situação da retirada, sem precisar informar um identificador

#### Scenario: Seleção de atendimento sem pedido

- **WHEN** o operador seleciona um atendimento em aberto sem pedido
- **THEN** o contexto e a prévia do cálculo são carregados para a conferência presencial

#### Scenario: Atendimento com pedido existente

- **WHEN** a lista contém atendimento que já possui pedido
- **THEN** a tela indica o estado existente, não oferece registrar outro pedido e permite acompanhar o pedido no caixa

#### Scenario: Busca como filtro opcional

- **WHEN** o operador informa placa ou identificador
- **THEN** a lista é filtrada pela busca exata, sem substituir a seleção por lista quando o filtro é limpo
