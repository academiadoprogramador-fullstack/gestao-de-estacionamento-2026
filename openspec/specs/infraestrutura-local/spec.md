# infraestrutura-local Specification

## Purpose

Define como a infraestrutura local, a configuração por ambiente e o bootstrap de `Development` preparam o sistema para desenvolvimento e testes sem depender de serviços pagos, sem expor segredos e sem aplicar esquema ou contas automáticas fora de `Development`.

## Requirements

### Requirement: Infraestrutura local isolada

A infraestrutura local SHALL iniciar apenas o SQL Server Developer por `compose.yml`, com healthcheck, volume nomeado persistente e porta publicada exclusivamente em loopback; a API e a SPA SHALL executar no host, em origens HTTPS distintas.

#### Scenario: Início e parada preservam dados

- **WHEN** a infraestrutura é iniciada, populada e depois parada sem remoção explícita do volume
- **THEN** o SQL Server volta a responder com os dados preservados e o volume nomeado não é recriado

#### Scenario: Banco não acessível externamente

- **WHEN** um cliente fora do host tenta conectar à porta publicada do SQL Server
- **THEN** a conexão não é aceita porque a porta está vinculada somente a `127.0.0.1`

#### Scenario: SQL indisponível impede tráfego

- **WHEN** a API é iniciada em `Development` com o SQL Server indisponível
- **THEN** a inicialização falha antes de aceitar requisições HTTP e nenhuma migração ou provisionamento é considerado concluído

### Requirement: Configuração por ambiente sem segredos versionados

Parâmetros não sensíveis SHALL residir em arquivos de configuração por ambiente; conexão, senhas, chaves criptográficas e credenciais SHALL vir de User Secrets em `Development` ou de configuração externa do processo em outros ambientes, nunca de arquivos versionados, do frontend ou de logs.

#### Scenario: Segredo ausente interrompe a inicialização

- **WHEN** a API inicia sem uma chave, senha ou configuração obrigatória do ambiente
- **THEN** a inicialização é recusada com erro controlado que não imprime o valor ausente nem outros segredos

#### Scenario: Repositório e frontend sem segredos

- **WHEN** a configuração local é inspecionada em arquivos versionados, artefatos do frontend e logs
- **THEN** não há valores de chaves AES/JWT, senha de banco, senhas de contas ou demais credenciais

#### Scenario: Testes isolados da configuração pessoal

- **WHEN** a suíte de testes prepara o banco em `Testing`
- **THEN** ela usa destino e credenciais próprios, sem carregar User Secrets pessoais de `Development` e sem tocar no banco de desenvolvimento

### Requirement: Chaves versionadas validadas antes de servir

A API SHALL validar, antes de aceitar tráfego, mapas independentes de chaves JWT e AES, cada valor em Base64 decodificando exatamente 32 bytes, com uma versão ativa existente e versões anteriores disponíveis somente para leitura.

#### Scenario: Mapas válidos

- **WHEN** a configuração contém chaves JWT e AES válidas em versões distintas, com versão ativa existente
- **THEN** a API inicia e cada finalidade usa somente o próprio mapa, sem reutilizar material entre JWT, AES ou backups

#### Scenario: Chave inválida ou versão ativa ausente

- **WHEN** um valor de chave tem tamanho inválido, Base64 inválido ou a versão ativa não existe no mapa
- **THEN** a API não inicia e não substitui a chave por valor padrão, vazio, texto puro ou material de outra finalidade

#### Scenario: Versão anterior preservada

- **WHEN** uma nova versão de chave é ativada e a anterior permanece configurada
- **THEN** dados e tokens emitidos com a versão anterior continuam legíveis ou validáveis até o fim da dependência correspondente

### Requirement: Perfis fixos e contas sintéticas configuradas externamente

O sistema SHALL reconhecer exatamente os perfis Administrador, Caixa e Manobrista e SHALL exigir, em `Development`, e-mail e senha para cada uma das três contas sintéticas, validando presença, formato aceito e unicidade dos e-mails após normalização antes de tocar no banco.

#### Scenario: Configuração completa

- **WHEN** as três contas possuem e-mails válidos e distintos e senhas presentes em User Secrets
- **THEN** a validação prévia é concluída sem registrar e-mails ou senhas em log

#### Scenario: Conta ausente ou e-mail repetido

- **WHEN** falta o e-mail ou a senha de uma conta ou dois e-mails se normalizam para o mesmo valor
- **THEN** a inicialização é interrompida antes de qualquer migration ou provisionamento

#### Scenario: Seção de contas ausente dos arquivos versionados

- **WHEN** os arquivos `appsettings*.json` do repositório são inspecionados
- **THEN** não existe seção `ContasSinteticas` nem credenciais das contas sintéticas

### Requirement: Bootstrap de Development com migrations antes do provisionamento

Somente em `Development`, a API SHALL aplicar todas as migrations versionadas pendentes — criando o catálogo quando ele ainda não existir — e, imediatamente após o sucesso, provisionar as contas sintéticas ausentes, iniciando o atendimento HTTP apenas após o provisionamento bem-sucedido.

#### Scenario: Catálogo vazio recebe esquema e contas

- **WHEN** a API inicia em `Development` apontando para um catálogo vazio com configuração válida
- **THEN** as migrations são aplicadas, as três contas são criadas com os perfis fixos e a API só então aceita requisições

#### Scenario: Catálogo ausente é criado pela migration

- **WHEN** a API inicia em `Development` com o login local válido e o catálogo configurado ainda não existe
- **THEN** a `Database.MigrateAsync()` cria o catálogo, aplica o esquema, provisiona as contas e só então a API aceita requisições

#### Scenario: Esquema atualizado e reinício idempotente

- **WHEN** a API reinicia com o esquema já atualizado e contas existentes
- **THEN** nenhuma migration é recriada, nenhuma conta é duplicada e e-mail, senha, perfis ou estado das contas existentes não são alterados

#### Scenario: Migration nova aplicada antes do provisionamento

- **WHEN** uma migration pendente é acrescentada e a API reinicia
- **THEN** a migration é aplicada antes da verificação e do provisionamento de contas ausentes

#### Scenario: Falha de migration bloqueia provisionamento e tráfego

- **WHEN** uma migration falha
- **THEN** o provisionamento não é executado e a API não aceita requisições

#### Scenario: Falha de provisionamento bloqueia tráfego

- **WHEN** a criação de uma conta sintética falha após as migrations
- **THEN** a API não aceita requisições e registra a falha sem expor senhas ou e-mails completos

### Requirement: Catálogo local criado pela migration

Em `Development`, a conexão da API SHALL usar a conta administrativa local `sa` fornecida por User Secrets exclusivamente para que `Database.MigrateAsync()` crie o catálogo e aplique todo o esquema; não SHALL existir preparação manual de banco. `Testing` e `Production` SHALL NOT reutilizar essa credencial administrativa de Development.

#### Scenario: Volume novo sem catálogo

- **WHEN** o volume local é novo e a conexão administrativa de `Development` está configurada
- **THEN** a primeira inicialização cria o catálogo pela migration, aplica o esquema e provisiona as contas, sem preparação manual do banco

#### Scenario: Login sem permissão de criação

- **WHEN** a credencial administrativa de `Development` está ausente, incorreta ou sem permissão para criar o catálogo
- **THEN** a inicialização falha de forma controlada antes do tráfego e não cria esquema parcial por SQL paralelo

### Requirement: Sem migration ou seed automático fora de Development

A API SHALL NOT aplicar migrations nem provisionar contas sintéticas automaticamente em `Testing` ou `Production`; nesses ambientes a preparação do banco pertence às fixtures ou ao procedimento controlado de publicação.

#### Scenario: Inicialização em Testing

- **WHEN** a API inicia em `Testing`
- **THEN** nenhuma migration ou conta é criada pelo bootstrap e o banco depende da preparação explícita da suíte de testes

#### Scenario: Inicialização em Production

- **WHEN** a API inicia em `Production`
- **THEN** nenhuma migration ou seed é executado no startup, independentemente de flags de configuração

#### Scenario: Falha em Development não altera outros ambientes

- **WHEN** o bootstrap de `Development` falha ou é desabilitado
- **THEN** nenhum comportamento de migração ou provisionamento é herdado por `Testing` ou `Production`

### Requirement: Operação local documentada e verificável

O projeto SHALL documentar os comandos para iniciar, verificar a saúde e parar a infraestrutura preservando volumes, além de validar a configuração do Compose sem expor segredos.

#### Scenario: Validação do Compose sem segredo

- **WHEN** a configuração do Compose é validada com a senha fornecida externamente
- **THEN** a validação conclui sem gravar a senha no arquivo Compose, em `.env` versionado ou em saída de log

#### Scenario: Parada comum não remove dados

- **WHEN** a infraestrutura é parada pelo comando normal
- **THEN** os volumes e dados de desenvolvimento permanecem, e a remoção exige ação explícita
