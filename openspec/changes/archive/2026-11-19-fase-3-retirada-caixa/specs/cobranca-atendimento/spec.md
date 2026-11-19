# Spec Delta

## Purpose

Define o cálculo fixado da permanência, o total pela tarifa da entrada, a composição auditável do saldo e o desconto administrativo, preservando a base original e impedindo ajustes sobre valores já pagos.

## ADDED Requirements

### Requirement: Cálculo por períodos iniciados de 24 horas

O sistema SHALL calcular a permanência pela diferença em UTC entre o pedido e a entrada, contando períodos iniciados de 24 horas com mínimo de uma diária: exatamente 24 horas SHALL resultar em uma diária, e qualquer fração após a primeira diária SHALL iniciar a seguinte, sem ponto flutuante ou truncamento.

#### Scenario: Imediatamente após a entrada

- **WHEN** o pedido ocorre no mesmo instante ou pouco depois da entrada
- **THEN** o cálculo fixa uma diária, por ser o mínimo previsto

#### Scenario: Exatamente 24 horas

- **WHEN** o pedido ocorre exatamente 24 horas após a entrada
- **THEN** o cálculo fixa exatamente uma diária

#### Scenario: Fração após 24 horas

- **WHEN** o pedido ocorre após 24 horas e antes ou em 48 horas
- **THEN** o cálculo fixa duas diárias, inclusive para uma fração persistida mínima após 24 horas

#### Scenario: Exatamente 48 horas

- **WHEN** o pedido ocorre exatamente 48 horas após a entrada
- **THEN** o cálculo fixa duas diárias

### Requirement: Total original pela tarifa da entrada

O sistema SHALL fixar o total original como a multiplicação das diárias pelo valor da tarifa capturada na entrada, em `decimal(18,2)`, e esse total SHALL permanecer imutável; publicação posterior de tarifa SHALL NOT alterar total, diárias ou snapshot do atendimento.

#### Scenario: Total calculado

- **WHEN** o pedido é confirmado com diárias e snapshot definidos
- **THEN** o total original passa a ser o produto exato entre eles, com duas casas decimais

#### Scenario: Tarifa alterada depois

- **WHEN** uma nova versão de tarifa é publicada após a entrada
- **THEN** o total do pedido continua baseado no valor capturado na entrada

#### Scenario: Reapresentação do pedido

- **WHEN** o detalhe financeiro é consultado novamente
- **THEN** diárias, valor aplicado e total original são os mesmos fixados no pedido, sem recálculo

### Requirement: Composição auditável do saldo

O sistema SHALL manter cobrado igual ao total original menos os descontos, pago líquido igual aos pagamentos menos os estornos e saldo igual ao cobrado menos o pago líquido, reconstruídos dos lançamentos, e o saldo SHALL NOT ser negativo por atendimento.

#### Scenario: Saldo inicial

- **WHEN** o pedido é confirmado sem descontos nem pagamentos
- **THEN** o saldo exibido é igual ao total original

#### Scenario: Saldo após pagamento

- **WHEN** um pagamento integral do saldo é confirmado
- **THEN** o pago líquido iguala o cobrado e o saldo passa a zero

#### Scenario: Saldo após desconto

- **WHEN** um desconto é concedido
- **THEN** o cobrado e o saldo diminuem pelo valor do desconto, sem alterar o total original

#### Scenario: Saldo reaberto por estorno

- **WHEN** um pagamento é integralmente estornado
- **THEN** o pago líquido diminui pelo estorno, o cobrado permanece e o saldo volta ao valor exigível

#### Scenario: Cobrança zerada

- **WHEN** os descontos somam o total original
- **THEN** o cobrado e o saldo são zero e nenhum pagamento fictício é exigido

### Requirement: Desconto administrativo limitado

O sistema SHALL permitir somente ao Administrador conceder desconto monetário positivo com no máximo duas casas decimais e justificativa, enquanto o pago líquido for zero; a soma dos descontos SHALL NOT exceder o total original e pode zerar o cobrado.

#### Scenario: Desconto válido

- **WHEN** o Administrador concede desconto positivo, com justificativa, antes de qualquer pagamento líquido
- **THEN** o desconto é registrado, reduz o cobrado e o novo saldo fica disponível

#### Scenario: Desconto até zero

- **WHEN** a soma dos descontos alcança exatamente o total original
- **THEN** a cobrança fica zerada e o atendimento não exige pagamento

#### Scenario: Limite excedido

- **WHEN** o desconto ultrapassa o valor que ainda pode ser descontado
- **THEN** a API recusa a concessão sem criar lançamento nem alterar o saldo

#### Scenario: Saldo já pago

- **WHEN** existe pagamento líquido maior que zero e o Administrador tenta conceder desconto
- **THEN** a API recusa a concessão e preserva pagamentos, cobrado e saldo

#### Scenario: Perfil sem permissão

- **WHEN** Caixa ou Manobrista tenta conceder desconto
- **THEN** a API nega a operação independentemente da interface

### Requirement: Desconto preservado como lançamento

O sistema SHALL registrar cada desconto como lançamento append-only com valor, ator, horário UTC e justificativa cifrada, sem editar o total original nem descontos anteriores, e SHALL NOT aceitar desconto percentual.

#### Scenario: Lançamento preservado

- **WHEN** um desconto é concedido
- **THEN** o histórico mantém o lançamento original com autor e horário, mesmo após pagamentos ou estornos posteriores

#### Scenario: Tentativa de edição ou exclusão

- **WHEN** um cliente tenta alterar ou excluir um desconto já confirmado
- **THEN** a API nega a operação e mantém o lançamento intacto

#### Scenario: Desconto percentual recusado

- **WHEN** o pedido informa percentual ou valor sem duas casas decimais válidas
- **THEN** a API recusa com erro de validação sem converter ou arredondar silenciosamente

### Requirement: Concorrência e atomicidade do desconto

O sistema SHALL serializar alterações do saldo por atendimento, SHALL recusar com conflito operação baseada em revisão desatualizada e SHALL confirmar desconto, revisão da razão, auditoria e resultado idempotente na mesma transação, desfazendo tudo em falha.

#### Scenario: Dois descontos concorrentes

- **WHEN** dois descontos partem da mesma revisão e, juntos, ultrapassariam o total original
- **THEN** apenas um é confirmado e o outro recebe conflito com o saldo atual recarregável

#### Scenario: Desconto concorrente com pagamento

- **WHEN** um desconto e um pagamento disputam a mesma revisão da razão
- **THEN** apenas a operação compatível com o saldo é confirmada e a outra recebe conflito

#### Scenario: Falha de auditoria

- **WHEN** a gravação da auditoria do desconto falha
- **THEN** o desconto e a revisão da razão são revertidos e o saldo anterior permanece

#### Scenario: Replay do desconto

- **WHEN** o mesmo desconto é reenviado com a mesma chave e conteúdo
- **THEN** a API devolve o resultado original sem duplicar lançamento ou reduzir o saldo duas vezes

### Requirement: Tentativa incerta do desconto

A SPA SHALL manter a concessão de desconto somente em memória, bloquear navegação interna e logout enquanto o comando estiver pendente ou incerto, avisar antes de sair do navegador e repetir explicitamente apenas com a mesma chave e o mesmo conteúdo.

#### Scenario: Comando pendente bloqueia saída

- **WHEN** o desconto foi enviado e o resultado ainda é desconhecido
- **THEN** a SPA impede navegação interna e logout e mostra aviso antes de fechar ou recarregar o navegador

#### Scenario: Resposta perdida

- **WHEN** a resposta do desconto se perde por falha de rede
- **THEN** a tentativa permanece em memória e a repetição usa exatamente a mesma chave e o mesmo conteúdo, sem novo desconto automático

#### Scenario: Rejeição definitiva

- **WHEN** a API recusa o desconto por limite, saldo já pago ou validação
- **THEN** a tela permite corrigir o valor ou a justificativa sem reenvio automático e a próxima submissão explícita usa nova chave

#### Scenario: Sucesso recarrega o saldo

- **WHEN** o desconto é confirmado
- **THEN** a tela recarrega o saldo e os lançamentos do servidor, não reapresenta o valor antigo como atual, não mantém resumo de validação e exibe um diálogo de confirmação que permanece até ser dispensado
