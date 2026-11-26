# Spec Delta — Entrega com janela de fotos encerrada

## MODIFIED Requirements

### Requirement: Prontidão e confirmação de entrega

O sistema SHALL permitir somente a Caixa e Administrador consultar prontidão e confirmar explicitamente a entrega com as versões-base do atendimento, pedido, razão e controles de exceção. A prontidão SHALL informar condições, bloqueios e presença/quantidade de fotos de saída sem conteúdo sensível, SHALL NOT exigir fotos e SHALL NOT reservar a entrega (F4-R02/03/07; RF-13).

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
- **THEN** Manobrista vê apenas dados operacionais, vistoria e fotos sem controle de confirmação; a API nega prontidão e entrega ao perfil, e nega acesso sem sessão válida

#### Scenario: Fotos opcionais não bloqueiam

- **WHEN** a vistoria de saída não possui foto, o armazenamento está indisponível ou o orçamento de fotos está suspenso
- **THEN** a prontidão continua informando a ausência/indisponibilidade e a entrega textual pode ser confirmada se as demais condições estiverem satisfeitas

### Requirement: Encerramento e liberação atômicos

A confirmação SHALL gravar uma única entrega e os consumos aplicáveis, encerrar pedido e atendimento, fechar somente sua ocupação aberta e confirmar eventos, recibo idempotente e fechamento da janela de fotos conjuntamente. O UTC da entrega, registrado com o ator do servidor, SHALL ser o mesmo do fim da ocupação e SHALL impedir que upload concorrente confirme foto depois da entrega (F4-R04; RF-13).

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
- **THEN** recebe o recibo original e nenhuma entrega, liberação, consumo ou reabertura de fotos ocorre; nova ação para o pedido entregue é recusada
- **AND** a mesma chave com conteúdo ou versão-base diferente conflita

#### Scenario: Upload concorrente com a entrega

- **WHEN** um envio de foto de saída está em andamento quando a entrega é confirmada
- **THEN** a confirmação da foto revalida a janela e é recusada ou reconciliada como órfã, sem expor imagem após o encerramento
