# autorizacoes-retirada Specification

## Purpose

Define a concessão, revogação, substituição e consumo de autorizações administrativas para divergência documental e saldo pendente, vinculadas à revisão vigente e preservadas no histórico, sem quitar o saldo, aprovar a conferência ou transferir o direito de retirada.

## Requirements

### Requirement: Concessão separada por tipo

O sistema SHALL permitir somente ao Administrador conceder autorização documental ou financeira, com justificativa e vínculo ao pedido e à versão relevante da conferência ou da razão, e a concessão SHALL NOT alterar conferência, saldo, pagamento ou estado do pedido.

#### Scenario: Autorização documental

- **WHEN** o Administrador concede autorização documental para um pedido com conferência divergente
- **THEN** a autorização é registrada com justificativa, ator, horário UTC e vínculo à conferência do pedido

#### Scenario: Autorização financeira

- **WHEN** o Administrador concede autorização financeira para um pedido com saldo pendente
- **THEN** a autorização é registrada com justificativa, ator, horário UTC e vínculo à revisão corrente da razão

#### Scenario: Concessão não altera o pedido

- **WHEN** uma autorização é concedida
- **THEN** conferência, saldo, pagamentos e estado do pedido permanecem inalterados

#### Scenario: Justificativa ausente

- **WHEN** a concessão não informa justificativa ou tipo válido
- **THEN** a API recusa a operação sem criar autorização

#### Scenario: Perfil sem permissão

- **WHEN** Caixa ou Manobrista tenta conceder autorização
- **THEN** a API nega a operação independentemente da interface

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

### Requirement: Substituição auditada do mesmo tipo

Outra concessão do mesmo tipo SHALL substituir a autorização vigente anterior de forma auditada, e o sistema SHALL NOT permitir duas autorizações vigentes do mesmo tipo para o mesmo pedido.

#### Scenario: Substituição confirmada

- **WHEN** o Administrador concede nova autorização do mesmo tipo de uma vigente
- **THEN** a nova passa a vigente, a anterior passa a substituída e ambas permanecem no histórico com seus autores e horários

#### Scenario: Tipos independentes

- **WHEN** existem autorização documental e financeira para o mesmo pedido
- **THEN** cada uma mantém vigência própria e a substituição de uma não altera a outra

#### Scenario: Duas vigentes do mesmo tipo

- **WHEN** duas concessões do mesmo tipo disputam a mesma versão
- **THEN** apenas uma permanece vigente e a outra é registrada como substituída ou recebe conflito

### Requirement: Justificativa protegida e projeções por perfil

O sistema SHALL persistir a justificativa da autorização cifrada com propósito próprio, registrando apenas referências opacas na auditoria e permitindo leitura explícita e auditada somente ao Administrador.

#### Scenario: Leitura administrativa

- **WHEN** o Administrador solicita explicitamente a justificativa de uma autorização
- **THEN** a API confirma a auditoria da leitura e então devolve o texto decifrado

#### Scenario: Caixa sem justificativa

- **WHEN** o Caixa consulta a situação da autorização no pedido
- **THEN** vê o tipo, a vigência e o efeito sobre a entrega, sem o texto da justificativa

#### Scenario: Manobrista sem autorização

- **WHEN** um Manobrista consulta fila ou detalhe
- **THEN** a resposta não contém autorizações, justificativas ou seus estados

#### Scenario: Falha de decifragem

- **WHEN** a chave ou o envelope da justificativa é inválido
- **THEN** a leitura falha fechado e não retorna conteúdo vazio, puro ou parcial

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

### Requirement: Tentativa incerta da autorização

A SPA SHALL manter concessão e revogação de autorização somente em memória, bloquear navegação interna e logout enquanto o comando estiver pendente ou incerto, avisar antes de sair do navegador e repetir explicitamente apenas com a mesma chave e o mesmo conteúdo.

#### Scenario: Comando pendente bloqueia saída

- **WHEN** a concessão ou revogação foi enviada e o resultado é desconhecido
- **THEN** a SPA impede navegação interna e logout e mostra aviso antes de fechar ou recarregar o navegador

#### Scenario: Resposta perdida

- **WHEN** a resposta da autorização se perde por falha de rede
- **THEN** a tentativa permanece em memória e a repetição usa exatamente a mesma chave e o mesmo conteúdo, sem criar concessão ou revogação duplicada

#### Scenario: Rejeição definitiva

- **WHEN** a API recusa a autorização por justificativa ausente, versão desatualizada ou perfil sem permissão
- **THEN** a tela permite correção sem reenvio automático e a próxima submissão explícita usa nova chave

#### Scenario: Sucesso recarrega a vigência

- **WHEN** a concessão ou revogação é confirmada
- **THEN** a tela recarrega o estado derivado do servidor e não reapresenta o status antigo como vigente
