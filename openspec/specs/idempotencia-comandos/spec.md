# idempotencia-comandos Specification

## Purpose

Define como comandos críticos são repetidos com segurança por chave idempotente e conteúdo canônico versionado, devolvendo o resultado original após reautorização e recusando conteúdo divergente sem duplicar efeitos.

## Requirements

### Requirement: Chave idempotente por ator e operação

O sistema SHALL exigir `Idempotency-Key` em formato UUID nos comandos críticos, com escopo único por ator, operação e chave, e SHALL recusar o comando sem chave antes de executar qualquer efeito.

#### Scenario: Chave válida

- **WHEN** Caixa envia um check-in com `Idempotency-Key` UUID
- **THEN** a chave é reservada na mesma transação do comando e vinculada ao ator, à operação e ao resultado

#### Scenario: Chave ausente ou inválida

- **WHEN** o comando chega sem chave ou com formato diferente de UUID
- **THEN** a API recusa a requisição com erro de validação e não executa o comando

#### Scenario: Mesma chave em ator diferente

- **WHEN** outro funcionário reutiliza a chave de um comando anterior
- **THEN** o escopo por ator impede que o resultado alheio seja devolvido ou reaproveitado

### Requirement: Conteúdo canônico versionado em SHA-256

O sistema SHALL comparar a repetição pelo SHA-256 de uma representação canônica determinística e não ambígua, incluindo operação, ator, alvo, valores normalizados e versões-base pertinentes, persistindo a versão do formato sem segredo HMAC e sem corpo da requisição.

#### Scenario: Canonicalização determinística

- **WHEN** o mesmo comando é serializado novamente a partir do mesmo conteúdo lógico
- **THEN** o hash resultante é idêntico, sem depender de ordem de propriedades, formatação ou espaço

#### Scenario: Versão histórica no replay

- **WHEN** um replay usa a chave de um comando gravado com versão anterior de canonicalização
- **THEN** a comparação usa a versão persistida no registro, não a versão corrente do código

#### Scenario: Sem corpo nem segredo persistido

- **WHEN** a chave idempotente é gravada
- **THEN** o armazenamento contém apenas chave, escopo, hash, versão e resultado mínimo, sem corpo, documento, vistoria ou segredo

### Requirement: Replay autorizado e conflito por conteúdo

O sistema SHALL reautorizar a sessão, o perfil e o alvo antes de devolver um replay e SHALL retornar o resultado mínimo original quando a chave e o conteúdo coincidem, recusando com conflito quando o conteúdo ou as versões-base diferem.

#### Scenario: Replay idêntico

- **WHEN** a mesma chave e o mesmo conteúdo são reenviados por sessão ainda válida e com o mesmo perfil
- **THEN** a API devolve o resultado mínimo original, sem criar novo atendimento, ocupação ou evento duplicado

#### Scenario: Conteúdo divergente

- **WHEN** a mesma chave é reenviada com conteúdo ou versões-base diferentes
- **THEN** a API responde com conflito e não altera o resultado original

#### Scenario: Sessão revogada no replay

- **WHEN** o acesso foi alterado e a sessão anterior tenta repetir a chave
- **THEN** a API recusa pela autorização antes de devolver o resultado existente

#### Scenario: Resultado mínimo

- **WHEN** um replay é devolvido
- **THEN** ele contém identificação, vaga, estado e horário permitidos, sem documento, vistoria ou conteúdo sensível

### Requirement: Retenção e concorrência do resultado

O sistema SHALL gravar chave e resultado na mesma transação do comando, SHALL resolver disputa da mesma chave relendo o resultado confirmado em contexto novo e SHALL preservar o registro por 365 dias após encerramento e quitação.

#### Scenario: Disputa da mesma chave

- **WHEN** duas requisições simultâneas usam a mesma chave e o mesmo conteúdo
- **THEN** apenas uma executa o comando e a outra recebe o resultado confirmado, sem retry cego de escrita

#### Scenario: Conflito conhecido

- **WHEN** a transação encontra conflito de versão, unicidade de vaga ou falha de validação
- **THEN** o efeito é revertido e a chave não fica associada a resultado inexistente

#### Scenario: Retenção acompanha o histórico

- **WHEN** o atendimento permanece ativo ou com saldo
- **THEN** o registro idempotente não é descartado nem perde a versão de canonicalização antes do prazo do histórico
