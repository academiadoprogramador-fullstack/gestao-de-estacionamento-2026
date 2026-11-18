# Spec Delta

## Purpose

Define a proteção dos campos sensíveis do atendimento em repouso, com cifragem autenticada, chaves externas versionadas, vínculo por propósito e registro, rotação sem recifragem automática e falha fechada.

## ADDED Requirements

### Requirement: Cifragem autenticada com envelope versionado

O sistema SHALL persistir documento do titular, vistorias textuais e justificativas administrativas cifrados com AES-256-GCM em envelope contendo versão do formato, identificador de chave, nonce de 12 bytes, conteúdo cifrado e tag de 16 bytes, sem fallback em texto puro.

#### Scenario: Escrita cifrada

- **WHEN** um campo sensível é gravado
- **THEN** o banco recebe somente o envelope cifrado e um nonce novo gerado por fonte criptográfica para cada cifragem

#### Scenario: Leitura autorizada

- **WHEN** um caso de uso autorizado lê o campo sensível
- **THEN** o conteúdo é decifrado com a chave e o propósito corretos e devolvido apenas ao fluxo que o solicitou

#### Scenario: Algoritmo ou tag divergente

- **WHEN** o envelope declara algoritmo ou tamanho de tag diferente do fixado
- **THEN** a leitura e a escrita são recusadas com erro controlado, sem aceitar parâmetros arbitrários do envelope

### Requirement: Vínculo autenticado por propósito e registro

O sistema SHALL fornecer dados associados determinísticos incluindo versão do envelope, identificador de chave, ambiente, aplicação, propósito e identificadores imutáveis do registro, reconstruídos na leitura a partir do contexto autorizado.

#### Scenario: Troca entre campos ou registros

- **WHEN** um envelope válido é movido para outro campo, propósito ou atendimento
- **THEN** a verificação falha e o conteúdo não é retornado

#### Scenario: Adulteração de conteúdo

- **WHEN** nonce, conteúdo ou tag são alterados no banco
- **THEN** a tag não valida e a leitura é recusada sem tentar outra chave indiscriminadamente

#### Scenario: Contexto reconstruído no servidor

- **WHEN** a leitura é solicitada
- **THEN** o vínculo esperado é reconstruído dos identificadores autorizados no servidor, não de contexto fornecido pelo cliente

### Requirement: Chaves versionadas e rotação sem recifragem automática

O sistema SHALL usar chaves AES de 32 bytes por versão, uma ativa para escrita e anteriores somente para leitura, distintas de material JWT e de backup, preservadas enquanto houver dados ou cópias dependentes.

#### Scenario: Nova versão ativa

- **WHEN** uma nova versão de chave é ativada
- **THEN** novas escritas usam a versão ativa e os envelopes anteriores continuam legíveis pela versão registrada

#### Scenario: Rotação não altera histórico

- **WHEN** a rotação de chaves ocorre
- **THEN** nenhum texto imutável é recifrado ou reescrito automaticamente

#### Scenario: Versão anterior removida

- **WHEN** uma chave ainda referenciada por dados ou backups não está disponível
- **THEN** a leitura falha fechado e o sistema não gera chave substituta nem retorna conteúdo vazio ou em texto puro

### Requirement: Falha fechada e ausência de exposição

O sistema SHALL recusar leitura e escrita quando a chave, o envelope ou a tag forem inválidos, registrando erro sem conteúdo sensível, sem segredo e sem stack trace na resposta ou no log.

#### Scenario: Chave ausente

- **WHEN** a versão de chave exigida não existe na configuração
- **THEN** a operação é recusada com erro controlado e nenhum conteúdo sensível aparece em resposta, log ou telemetria

#### Scenario: Envelope desconhecido

- **WHEN** o envelope tem versão de formato desconhecida ou Base64 inválido
- **THEN** a leitura falha sem tentar interpretar o conteúdo como texto

#### Scenario: Logs sem material sensível

- **WHEN** ocorre falha criptográfica
- **THEN** o log registra correlação e identificadores opacos, sem documento, vistoria, justificativa, nonce, tag ou chave
