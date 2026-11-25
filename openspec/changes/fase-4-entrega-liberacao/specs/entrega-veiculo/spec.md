# Delta — Entrega do veículo

## Purpose

Confirma a devolução do veículo com as condições de saída verificadas, encerramento rastreável e liberação única da ocupação correspondente.

## ADDED Requirements

### Requirement: Prontidão e confirmação de entrega

O sistema SHALL permitir somente a Caixa e Administrador consultar prontidão e confirmar explicitamente a entrega com as versões-base do atendimento, pedido, razão e controles de exceção. A prontidão SHALL informar condições e bloqueios sem texto sensível e SHALL NOT reservar a entrega (F4-R02/03/07).

#### Scenario: Entrega normal a partir dos dois estados
- **WHEN** Caixa ou Administrador abre a operação para pedido aguardando ou em busca com vistoria, conferência aprovada, saldo zero e ocupação própria aberta
- **THEN** a tela apresenta prontidão e exige confirmação explícita; após o comando, mostra entregue e a vaga liberada, com ator e horário
- **AND** desconto que zera a cobrança não exige lançamento de pagamento; não há busca obrigatória

#### Scenario: Bloqueios antes da confirmação
- **WHEN** falta pedido ou vistoria, a conferência diverge sem autorização vigente, o saldo é positivo sem autorização vigente ou não há ocupação própria aberta
- **THEN** o servidor bloqueia a entrega e, quando há pedido consultável, a tela apresenta as condições bloqueantes em texto antes de confirmar
- **AND** chamadas diretas ou prontidão de versão antiga não contornam as validações

#### Scenario: Exceções independentes na confirmação
- **WHEN** há divergência documental, saldo positivo ou ambos, com autorização vigente para cada condição aplicável
- **THEN** a tela distingue as condições excepcionais e permite a confirmação explícita sem indicar quitação ou aprovação documental fictícia

#### Scenario: Manobrista e sessão inválida
- **WHEN** Manobrista abre a operação ou tenta consultar prontidão ou confirmar diretamente, ou uma chamada chega sem sessão válida
- **THEN** Manobrista vê apenas dados operacionais e vistoria, sem controle de confirmação; a API nega prontidão e entrega ao perfil, e nega acesso sem sessão válida

### Requirement: Encerramento e liberação atômicos

A confirmação SHALL gravar uma única entrega e os consumos aplicáveis, encerrar pedido e atendimento, fechar somente sua ocupação aberta e confirmar eventos e recibo idempotente conjuntamente. O UTC da entrega, registrado com o ator do servidor, SHALL ser o mesmo do fim da ocupação (F4-R04).

#### Scenario: Liberação reutilizável
- **WHEN** uma entrega é confirmada
- **THEN** a tela de entrega e a consulta de vagas mostram a liberação ao atualizar; o check-in pode atribuir a vaga a outro atendimento se ainda estiver livre

#### Scenario: Ocupação ausente ou alheia
- **WHEN** não existe ocupação aberta do atendimento ou a ocupação aberta pertence a outro
- **THEN** o comando falha sem entregar, consumir autorização, registrar liberação ou afetar a ocupação alheia

#### Scenario: Disputa ou rollback
- **WHEN** duas entregas com chaves distintas disputam o pedido, a versão muda ou falha a gravação de qualquer efeito
- **THEN** nunca há efeitos parciais ou duplicados; a tentativa concorrente perde com conflito recarregável na tela

#### Scenario: Replay não afeta nova ocupação
- **WHEN** o mesmo ator autorizado repete chave e conteúdo após commit, inclusive após nova entrada na vaga
- **THEN** recebe o recibo original e nenhuma entrega, liberação ou consumo adicional ocorre; nova ação para o pedido entregue é recusada
- **AND** a mesma chave com conteúdo ou versão-base diferente conflita

### Requirement: Recuperação de tentativa incerta e feedback acessível

A SPA SHALL manter tentativa de vistoria ou entrega somente em memória, preservar chave, conteúdo e ator, e aceitar replay explícito sem reenvio automático. SHALL anunciar estados em texto, associar erros ao controle e focar o resumo de erro, sem tirar foco em atualizações não solicitadas (F4-R07).

#### Scenario: POST pendente ou resposta perdida
- **WHEN** o POST está em andamento ou perde a resposta
- **THEN** a tela bloqueia navegação interna e logout, avisa antes de recarregar/fechar e apresenta a opção de repetir a mesma tentativa apenas quando incerta
- **AND** confirmar saída descarta a tentativa; trocar de ator não permite reaproveitá-la

#### Scenario: Rejeição definitiva e recarga
- **WHEN** a API rejeita definitivamente uma tentativa por validação, conflito ou regra de negócio
- **THEN** a tela informa o motivo e permite corrigir ou recarregar sem novo POST; a próxima confirmação explícita usa nova chave

#### Scenario: Estado corrente após sucesso
- **WHEN** uma gravação ou seu replay é confirmado
- **THEN** a tela limpa a tentativa, recarrega as projeções correntes e não trata recibo histórico como saldo ou ocupação atual

### Requirement: Diagnóstico sem dados sensíveis

O sistema SHALL emitir logs estruturados para confirmações, falhas, conflitos e liberações com correlação, ação, resultado e latência, sem placa, nome, documento, textos, valores, justificativas ou tokens (F4-R07).

#### Scenario: Correlação sem vazamento
- **WHEN** a entrega confirma, conflita ou falha
- **THEN** logs permitem correlacionar a operação sem payload sensível; o histórico durável permanece separado e registra somente eventos confirmados
