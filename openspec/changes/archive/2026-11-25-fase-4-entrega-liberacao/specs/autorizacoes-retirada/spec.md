# Delta — Autorizações de retirada

## MODIFIED Requirements

### Requirement: Vigência derivada da versão

Uma autorização SHALL ser vigente somente enquanto o pedido não estiver entregue e a concessão não estiver revogada nem substituída e corresponder à revisão corrente da conferência ou da razão. Mudança financeira relevante SHALL invalidar a concessão financeira anterior; nenhuma autorização SHALL expirar por prazo arbitrado.

#### Scenario: Autorização vigente

- **WHEN** o pedido está aberto, a autorização não foi revogada nem substituída e a revisão vinculada continua corrente
- **THEN** o estado derivado é vigente e a entrega pode considerá-la para o tipo correspondente

#### Scenario: Alteração financeira invalida

- **WHEN** pagamento, desconto ou estorno altera a revisão da razão após a concessão financeira
- **THEN** a autorização anterior passa a obsoleta e não pode ser usada para entrega

#### Scenario: Revogação invalida

- **WHEN** a autorização é revogada
- **THEN** o estado derivado passa a revogada e a concessão original permanece no histórico

#### Scenario: Substituição invalida

- **WHEN** outra autorização do mesmo tipo é concedida
- **THEN** a anterior passa a substituída e deixa de ser vigente

#### Scenario: Sem expiração por tempo

- **WHEN** decorre tempo sem revogação, substituição, entrega ou alteração relevante
- **THEN** a autorização continua vigente, sem prazo arbitrado de expiração

### Requirement: Revogação antes do consumo

O Administrador SHALL poder revogar autorização vigente, antes do consumo, com justificativa, preservando concessão e revogação. O sistema SHALL NOT expor comando de consumo independente da entrega.

#### Scenario: Revogação confirmada

- **WHEN** o Administrador revoga uma autorização com justificativa
- **THEN** a revogação é registrada com ator e horário do servidor e a autorização deixa de ser vigente

#### Scenario: Revogação sem consumo

- **WHEN** uma autorização é revogada antes de qualquer entrega
- **THEN** nenhum consumo é registrado e o pedido permanece nos estados aguardando ou em busca

#### Scenario: Consumo indisponível

- **WHEN** um cliente tenta consumir autorização isoladamente pela API, fora do comando de entrega
- **THEN** a operação é negada sem alterar a autorização, o pedido, a vaga ou a ocupação

#### Scenario: Revogação não reativa

- **WHEN** uma autorização revogada é consultada ou uma nova revogação é tentada
- **THEN** ela continua revogada e não volta a ser vigente

### Requirement: Autorização não transfere o direito de retirada

A concessão de exceção SHALL NOT transferir o direito de retirada, dispensar a presença do titular, aprovar a conferência, quitar o saldo ou liberar a vaga; ela apenas registra uma condição excepcional a validar na entrega.

#### Scenario: Terceiro com autorização

- **WHEN** alguém tenta usar autorização para permitir retirada por terceiro ou com titular ausente
- **THEN** a API recusa a retirada mesmo que a autorização exista

#### Scenario: Autorização não quita

- **WHEN** existe autorização financeira vigente
- **THEN** o saldo continua exigível e o pagamento integral permanece necessário para a quitação normal

#### Scenario: Autorização não entrega

- **WHEN** existem as duas autorizações vigentes mas nenhuma entrega foi confirmada
- **THEN** o pedido continua aberto, sem liberação de vaga ou consumo

## ADDED Requirements

### Requirement: Consumo vinculado à entrega

A entrega SHALL consumir somente a autorização corrente e vigente de cada tipo indispensável, uma vez por entrega e tipo, preservando a concessão, a revisão e seu histórico, sem copiar a justificativa. O sistema SHALL NOT aceitar nova concessão, revogação ou consumo para pedido entregue (F4-R03).

#### Scenario: Consumo independente por tipo

- **WHEN** entrega exige autorização para conferência divergente, saldo positivo ou ambos
- **THEN** somente os tipos necessários são consumidos, referenciando a concessão vigente e sua revisão; a entrega normal não consome autorização

#### Scenario: Autorização anterior bloqueada

- **WHEN** o comando usa controle desatualizado ou autorização revogada, substituída ou obsoleta
- **THEN** a tentativa não consome a concessão anterior; a tela informa o bloqueio ou conflito e permite recarga, sem entrega parcial

#### Scenario: Consulta após entrega

- **WHEN** Caixa ou Administrador consulta autorizações do pedido entregue no caixa
- **THEN** vê consumo e estado histórico das autorizações permitidas, sem opções de conceder ou revogar, e solicitações diretas de novas mutações são recusadas
- **AND** consumos continuam registrados mesmo após acerto posterior; concessão não usada que ainda seria vigente é obsoleta; estados revogada e substituída não são reescritos
