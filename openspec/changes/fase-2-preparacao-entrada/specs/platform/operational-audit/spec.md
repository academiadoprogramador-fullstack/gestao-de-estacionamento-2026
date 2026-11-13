# Spec Delta

## Purpose

Preservar evidências das ações de preparação e entrada e proteger documentos e vistorias durante armazenamento e consulta operacional.

## ADDED Requirements

### Requirement: Histórico de entrada transacional e preservado
O sistema SHALL registrar entrada, atribuição de vaga e captura de tarifa com ator e horário na mesma transação do check-in. O histórico por atendimento SHALL ser consultável conforme perfil e não SHALL permitir edição ou exclusão por operações normais.

#### Scenario: Consulta do histórico
- **WHEN** um funcionário autorizado consulta o histórico de uma entrada confirmada
- **THEN** vê eventos com ator e horário, sem documento, vistoria em texto puro ou dados financeiros proibidos ao seu perfil

#### Scenario: Repetição ou tentativa de edição
- **WHEN** um check-in é repetido com a mesma chave ou um funcionário tenta editar um evento histórico
- **THEN** não há evento de entrada duplicado nem alteração do evento original

### Requirement: Auditoria da preparação administrativa
O sistema SHALL auditar criação/alteração de acessos, cadastro de vagas e publicação de tarifa com ator, horário, ação e referência do alvo, atomicamente com a alteração, sem registrar senhas ou dados pessoais desnecessários.

#### Scenario: Alteração administrativa confirmada
- **WHEN** um administrador confirma uma alteração de preparação
- **THEN** a alteração e seu evento são preservados juntos ou ambos são desfeitos em caso de falha

### Requirement: Proteção recuperável de campos sensíveis
O sistema SHALL armazenar número de documento e texto de vistoria criptografados, sem texto puro em índices, logs ou auditoria. Reinício e rotação de chaves SHALL preservar leitura autorizada de dados anteriores enquanto o material necessário estiver retido.

#### Scenario: Inspeção e reinício
- **WHEN** os registros persistidos são inspecionados e o serviço reinicia com suas chaves preservadas
- **THEN** documento e vistoria não constam em texto puro no armazenamento e continuam legíveis apenas por operações autorizadas

#### Scenario: Rotação e chave indisponível
- **WHEN** uma nova chave é ativada ou uma chave histórica necessária fica indisponível
- **THEN** registros antigos continuam legíveis com suas chaves retidas ou a leitura falha de modo controlado, sem substituir o conteúdo por dados vazios

### Requirement: Respostas e falhas sem vazamento
O sistema SHALL impedir cache das respostas privadas e omitir documento, vistoria, credenciais, cookies e corpo sensível dos logs/erros. Falhas inesperadas SHALL retornar mensagem genérica com identificador de correlação; negações de acesso SHALL ser investigáveis sem registrar o conteúdo protegido.

#### Scenario: Inspeção de sucesso e erro
- **WHEN** são executados check-in, leitura sensível, acesso indevido e falha inesperada
- **THEN** respostas privadas não são cacheáveis e logs/erros não revelam conteúdo sensível ou stack trace ao cliente

### Requirement: Contrato OpenAPI de erros operacionais
O documento OpenAPI gerado SHALL descrever respostas de erro operacionais fora dos endpoints de sessão com media type `application/problem+json` e schema Problem Details incluindo propriedades padrão e extensões opcionais `code`, `correlationId` e `errors`. O schema de `errors` SHALL representar um mapa de nomes de campo a arrays de strings. Os contratos de sessão SHALL permanecer inalterados.

#### Scenario: Consumidor lê resposta de erro documentada
- **WHEN** um consumidor inspeciona o OpenAPI gerado para uma operação operacional com resposta 4xx/5xx
- **THEN** encontra somente `application/problem+json` e schema com propriedades padrão e extensões opcionais compatíveis com o runtime

#### Scenario: Contrato de sessão
- **WHEN** um consumidor inspeciona as operações `/api/v1/session`
- **THEN** as respostas e media types de sessão permanecem conforme o contrato existente
