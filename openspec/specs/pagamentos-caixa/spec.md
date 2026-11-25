# pagamentos-caixa Specification

## Purpose

Define o acerto integral do saldo e o estorno integral vinculado ao pagamento, preservando lançamentos originais, reabrindo o saldo quando aplicável e impedindo duplicidade sob concorrência ou resposta perdida.

## Requirements

### Requirement: Acerto integral do saldo

O sistema SHALL permitir a Caixa ou Administrador registrar pagamento exatamente igual ao saldo exigível positivo, em dinheiro, cartão ou Pix, com valor, forma, ator e horário UTC do servidor; pagamento parcial, excedente, com crédito ou troco e cobrança zerada SHALL ser recusados.

#### Scenario: Pagamento integral confirmado

- **WHEN** Caixa ou Administrador registra o valor exato do saldo positivo em uma forma aceita
- **THEN** o pagamento é confirmado com ator e horário do servidor, o pago líquido aumenta e o saldo passa a zero

#### Scenario: Valor parcial

- **WHEN** o valor informado é menor que o saldo exigível
- **THEN** a API recusa o acerto e mantém o saldo inalterado, sem registrar quitação parcial

#### Scenario: Valor excedente ou troco

- **WHEN** o valor informado excede o saldo ou o pedido menciona crédito ou troco
- **THEN** a API recusa a operação sem criar pagamento nem saldo a favor

#### Scenario: Cobrança zerada

- **WHEN** o saldo é zero por desconto ou quitação anterior
- **THEN** a API recusa novo pagamento e não exige valor fictício

#### Scenario: Forma de pagamento inválida

- **WHEN** a forma informada não é dinheiro, cartão ou Pix
- **THEN** a API recusa com erro de validação sem registrar o acerto

#### Scenario: Perfil sem permissão

- **WHEN** um Manobrista tenta registrar pagamento
- **THEN** a API nega a operação e não altera o saldo

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

### Requirement: Acesso à operação de entrega pelo caixa

O caixa SHALL oferecer a Caixa e Administrador acesso à operação de entrega do pedido, sem executar ou antecipar a entrega ao navegar ou registrar acerto (F4-R02/05/07).

#### Scenario: Abertura da entrega no caixa

- **WHEN** Caixa ou Administrador abre a operação de entrega a partir do caixa de um pedido
- **THEN** a tela de entrega abre para o mesmo pedido; autorização e saldo continuam dependendo do estado retornado pelo servidor e a navegação não altera a ocupação

### Requirement: Estorno integral e único por pagamento

O sistema SHALL permitir somente ao Administrador estornar integralmente um pagamento ainda não estornado, com justificativa, vínculo ao lançamento original e ator/UTC do servidor; o estorno SHALL reabrir o saldo, SHALL NOT reduzir o cobrado e SHALL NOT executar operação bancária.

#### Scenario: Estorno confirmado

- **WHEN** o Administrador estorna integralmente um pagamento não estornado, com justificativa
- **THEN** o estorno é vinculado ao pagamento original, o pago líquido diminui e o saldo é reaberto

#### Scenario: Estorno parcial

- **WHEN** o valor do estorno difere do pagamento original
- **THEN** a API recusa a operação e não reabre o saldo parcialmente

#### Scenario: Segundo estorno do mesmo pagamento

- **WHEN** o Administrador tenta estornar novamente um pagamento já estornado
- **THEN** a API responde com conflito e preserva o estorno original

#### Scenario: Original preservado

- **WHEN** um estorno é confirmado
- **THEN** o pagamento original continua visível e o cobrado permanece igual ao total original menos os descontos

#### Scenario: Perfil sem permissão

- **WHEN** Caixa ou Manobrista tenta estornar pagamento
- **THEN** a API nega a operação independentemente da interface

### Requirement: Justificativa do estorno protegida

O sistema SHALL persistir a justificativa do estorno cifrada com propósito próprio, registrando apenas referências opacas na auditoria e permitindo leitura explícita e auditada somente ao Administrador.

#### Scenario: Leitura administrativa

- **WHEN** o Administrador solicita explicitamente a justificativa de um estorno
- **THEN** a API confirma a auditoria da leitura e então devolve o texto decifrado

#### Scenario: Perfil sem acesso à justificativa

- **WHEN** Caixa ou Manobrista solicita a justificativa do estorno
- **THEN** a API nega a leitura sem retornar o texto

#### Scenario: Falha de decifragem

- **WHEN** a chave ou o envelope da justificativa é inválido
- **THEN** a leitura falha fechado e não retorna texto vazio, puro ou conteúdo parcial

### Requirement: Atomicidade, concorrência e idempotência do caixa

O sistema SHALL confirmar pagamento ou estorno com revisão da razão, auditoria e resultado idempotente na mesma transação, SHALL impedir duas quitações ou dois estornos concorrentes e SHALL rejeitar com conflito operação baseada em revisão desatualizada.

#### Scenario: Dois pagamentos concorrentes

- **WHEN** dois caixas tentam quitar o mesmo saldo ao mesmo tempo
- **THEN** apenas um pagamento é confirmado, o outro recebe conflito e o saldo é recarregado

#### Scenario: Estornos concorrentes

- **WHEN** dois estornos disputam o mesmo pagamento
- **THEN** apenas um é confirmado e nunca existem dois estornos para o mesmo pagamento

#### Scenario: Pagamento com revisão desatualizada

- **WHEN** um pagamento parte de uma revisão anterior a desconto, estorno ou outro pagamento
- **THEN** a API recusa com conflito e não sobrescreve o saldo atual

#### Scenario: Falha no meio da operação

- **WHEN** a gravação de pagamento, estorno, revisão, auditoria ou resultado idempotente falha
- **THEN** toda a operação é revertida e o saldo anterior permanece

#### Scenario: Replay após resposta perdida

- **WHEN** o caixa reenvia a mesma chave e o mesmo conteúdo após perder a resposta
- **THEN** a API devolve o resultado confirmado original sem duplicar pagamento ou estorno

### Requirement: Tentativa incerta no caixa

A SPA do caixa SHALL manter pagamento e estorno somente em memória, bloquear navegação interna e logout enquanto o comando estiver pendente ou incerto, avisar antes de sair do navegador e repetir explicitamente apenas com a mesma chave e o mesmo conteúdo.

#### Scenario: Comando pendente bloqueia saída

- **WHEN** o pagamento ou estorno foi enviado e o resultado é desconhecido
- **THEN** a SPA impede navegação interna e logout e avisa antes de fechar ou recarregar o navegador

#### Scenario: Resultado incerto

- **WHEN** a resposta do caixa se perde por falha de rede
- **THEN** a tentativa permanece em memória e a repetição usa exatamente a mesma chave e o mesmo conteúdo

#### Scenario: Rejeição definitiva

- **WHEN** a API rejeita o comando por validação ou conflito de saldo
- **THEN** a tela permite corrigir o valor sem reenvio automático e a próxima submissão explícita usa nova chave

#### Scenario: Sucesso atualiza o saldo

- **WHEN** o pagamento ou estorno é confirmado
- **THEN** a tela recarrega o saldo e o histórico do servidor, não reapresenta o recibo antigo como estado atual e não mantém resumo de validação

### Requirement: Confirmação explícita da operação

A SPA do caixa SHALL apresentar, após a confirmação de pagamento ou estorno, um diálogo acessível com o resumo da operação e ação explícita de conclusão; o diálogo SHALL permanecer até ser dispensado e SHALL NOT substituir a recarga do saldo e do histórico do servidor.

#### Scenario: Confirmação persistente do pagamento

- **WHEN** o pagamento integral é confirmado
- **THEN** a SPA exibe o resumo do pagamento e o saldo recarregado em diálogo focado, que permanece até a ação de concluir

#### Scenario: Confirmação do estorno

- **WHEN** o estorno integral é confirmado
- **THEN** a SPA exibe o resumo do estorno e o saldo reaberto em diálogo focado, que permanece até a ação de concluir

#### Scenario: Falha não abre confirmação

- **WHEN** o comando é rejeitado ou o resultado permanece incerto
- **THEN** a SPA não apresenta o diálogo de sucesso, mantendo erro ou tentativa incerta
