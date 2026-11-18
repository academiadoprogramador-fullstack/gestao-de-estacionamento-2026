# Spec Delta

## Purpose

Define o check-in completo do valet: entrada atômica com identificador público, snapshot da tarifa vigente, vistoria textual de entrada, ocupação exclusiva de vaga, campos do servidor e tratamento seguro de tentativa incerta no cliente.

## ADDED Requirements

### Requirement: Entrada completa com identificador público

O sistema SHALL registrar atendimento apenas com placa, modelo, nome e documento do titular, vistoria textual, vaga livre e identificador público único gerado pelo servidor; modelo, nome, documento e vistoria SHALL ser obrigatórios após remover espaços nas extremidades e respeitar 120, 200, 80 e 4000 caracteres.

#### Scenario: Entrada válida

- **WHEN** Caixa ou Administrador envia todos os campos obrigatórios com uma vaga livre e uma `Idempotency-Key`
- **THEN** a API cria o atendimento ativo, devolve `201` com `Location` e identificador público, e a vistoria é persistida cifrada

#### Scenario: Entrada incompleta

- **WHEN** falta placa, modelo, titular, documento, vistoria ou vaga
- **THEN** a API recusa a entrada com erro de validação e não cria atendimento, vistoria ou ocupação

#### Scenario: Placa normalizada sem unicidade

- **WHEN** a placa informada difere apenas por espaços, hífens ou caixa de uma entrada anterior
- **THEN** a API normaliza para 1 a 16 caracteres alfanuméricos e permite a nova visita sem exigir placa única

### Requirement: Snapshot da tarifa vigente na entrada

O sistema SHALL capturar na entrada a versão e o valor da tarifa corrente e SHALL bloquear a entrada quando não houver tarifa publicada; publicações posteriores SHALL NOT alterar o snapshot do atendimento.

#### Scenario: Snapshot capturado

- **WHEN** existe tarifa publicada e a entrada é confirmada
- **THEN** o atendimento guarda a versão e o valor vigentes, com o valor em duas casas decimais

#### Scenario: Entrada sem tarifa

- **WHEN** nenhuma tarifa foi publicada
- **THEN** a API recusa a entrada com regra não satisfeita e nenhum atendimento parcial é confirmado

#### Scenario: Publicação posterior não altera snapshot

- **WHEN** uma nova tarifa é publicada após a entrada
- **THEN** o atendimento mantém a versão e o valor capturados originalmente

### Requirement: Ocupação exclusiva de vaga

O sistema SHALL atribuir a vaga na mesma transação da entrada, SHALL impedir que duas entradas ativas ocupem a mesma vaga e SHALL rejeitar vaga inexistente ou já ocupada com conflito explícito.

#### Scenario: Vaga livre atribuída

- **WHEN** a vaga selecionada está livre no momento da confirmação
- **THEN** a ocupação é aberta vinculada ao atendimento e a vaga passa a ser apresentada como ocupada

#### Scenario: Disputa pela mesma vaga

- **WHEN** duas entradas concorrentes disputam a mesma vaga
- **THEN** apenas uma é confirmada, a outra recebe conflito, e nunca existem duas ocupações abertas para a mesma vaga

#### Scenario: Vaga inexistente

- **WHEN** a vaga informada não existe ou já possui ocupação aberta
- **THEN** a API recusa a entrada sem criar atendimento órfão e sem liberar ou transferir ocupação alheia

### Requirement: Atomicidade da entrada

O sistema SHALL confirmar atendimento, snapshot, vistoria, ocupação, eventos de auditoria e resultado idempotente na mesma transação, desfazendo integralmente a operação em qualquer falha.

#### Scenario: Confirmação conjunta

- **WHEN** a entrada é aceita
- **THEN** todos os registros da operação e a auditoria ficam visíveis juntos após o commit

#### Scenario: Falha no meio da operação

- **WHEN** qualquer etapa da entrada falha após gravação parcial
- **THEN** a transação é revertida e nenhum atendimento, vistoria, ocupação ou evento permanece confirmado

#### Scenario: Auditoria na mesma transação

- **WHEN** a gravação da auditoria da entrada falha
- **THEN** a entrada não é confirmada, sem registrar histórico sem o atendimento correspondente

### Requirement: Campos do servidor e rejeição de propriedades inesperadas

O sistema SHALL usar horário, operador, identificador e snapshot do servidor, SHALL permitir a entrada somente a Caixa e Administrador e SHALL rejeitar propriedades como tarifa, horário ou operador enviadas pelo cliente.

#### Scenario: Campos do cliente ignorados como inválidos

- **WHEN** o cliente envia tarifa, horário de entrada, operador ou identificador no corpo
- **THEN** a API rejeita a requisição em vez de aceitar valores fornecidos pelo cliente

#### Scenario: Perfil sem permissão

- **WHEN** um Manobrista tenta registrar entrada
- **THEN** a API nega a operação e não cria atendimento nem ocupação

#### Scenario: Horário e autoria do servidor

- **WHEN** a entrada é confirmada
- **THEN** o horário de entrada e o operador correspondem ao instante e à sessão validados no servidor

### Requirement: Tentativa incerta e replay explícito no cliente

A SPA SHALL manter a tentativa de check-in somente em memória, bloquear navegação interna e logout enquanto o comando estiver pendente ou incerto, avisar antes de sair do navegador e permitir repetição explícita apenas com a mesma chave e o mesmo conteúdo.

#### Scenario: Comando pendente bloqueia saída

- **WHEN** o check-in foi enviado e o resultado ainda é desconhecido
- **THEN** a SPA impede navegação interna e logout e mostra aviso antes de fechar ou recarregar o navegador

#### Scenario: Resultado incerto

- **WHEN** a resposta do check-in se perde por falha de rede
- **THEN** a tentativa permanece em memória e a repetição usa exatamente a mesma `Idempotency-Key` e o mesmo conteúdo, sem gerar nova chave

#### Scenario: Rejeição definitiva

- **WHEN** a API rejeita o check-in por validação ou regra de negócio
- **THEN** o formulário permite correção sem reenvio automático e a próxima submissão explícita usa uma nova chave

#### Scenario: Sucesso limpa dados sensíveis

- **WHEN** o check-in é confirmado
- **THEN** a SPA exibe o identificador com ação de copiar e limpa documento e vistoria do estado da tela

#### Scenario: Saída confirmada do navegador

- **WHEN** o usuário confirma a saída do navegador com uma tentativa apenas em memória
- **THEN** a tentativa é descartada e não é restaurada por storage, cookie ou aba
