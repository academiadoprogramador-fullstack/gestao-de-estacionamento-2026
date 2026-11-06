# platform/environment-readiness Specification

## Purpose

Disponibilizar uma fundação reproduzível para verificar localmente a segurança da sessão antes de construir os fluxos operacionais e implantar em nuvem.

## Requirements

### Requirement: Configuração local explícita e sem segredos versionados
O sistema SHALL executar localmente com configurações externas por ambiente, sem segredos embutidos no repositório, e SHALL falhar explicitamente quando parâmetros indispensáveis à sessão estiverem ausentes.

#### Scenario: Execução isolada de desenvolvimento e testes
- **WHEN** a aplicação e os testes são executados localmente
- **THEN** usam banco e chaves de teste isolados, sem acessar recursos ou credenciais de produção

#### Scenario: Configuração ausente
- **WHEN** a aplicação é iniciada sem configuração obrigatória de conexão, origem confiável ou chaves
- **THEN** a aplicação falha de modo explícito antes de disponibilizar a sessão

### Requirement: Persistência mínima e recuperável da sessão
O sistema SHALL persistir contas e papéis de funcionários para a autenticação e SHALL manter materiais criptográficos necessários à leitura de sessões entre reinícios locais do mesmo ambiente de teste, sem compartilhar chaves entre ambientes.

#### Scenario: Reinício da API
- **WHEN** a API reinicia no mesmo ambiente local
- **THEN** uma sessão ainda válida continua verificável conforme a política de expiração

### Requirement: Migrações reproduzíveis
O projeto SHALL versionar mudanças de esquema e permitir aplicar e verificar a migração inicial em banco SQL Server de teste. Somente no ambiente `Development`, a API SHALL aplicar migrações e provisionar idempotentemente contas sintéticas antes de servir requisições. Em qualquer outro ambiente, a API SHALL NOT migrar ou provisionar no startup; migrações de publicação exigem aplicação controlada.

#### Scenario: Banco de teste vazio
- **WHEN** a migração inicial é executada em SQL Server de teste vazio
- **THEN** o esquema de identidade é criado e validado sem tabelas operacionais nem dados de produção

#### Scenario: Primeiro startup local e reinício
- **WHEN** a API inicia em `Development` com banco local vazio e três senhas sintéticas externas configuradas, e depois reinicia
- **THEN** migra o esquema e cria contas/papéis antes de disponibilizar endpoints, sem duplicá-los ou redefinir senhas no reinício

#### Scenario: Bootstrap local inválido
- **WHEN** o banco está indisponível ou falta alguma senha sintética em `Development`
- **THEN** o startup falha antes de disponibilizar endpoints

#### Scenario: Ambiente não Development
- **WHEN** a API inicia em `Testing`, homologação ou produção
- **THEN** o startup não executa migrações nem provisiona contas sintéticas

### Requirement: Pendências de implantação rastreadas
O projeto SHALL registrar responsáveis, estado e critérios de validação para fuso, moeda, volume, topologia Azure, política de acesso e retenção de dados; SHALL registrar a validação de homologação Azure como pendente e não declarar a aplicação pronta para produção.

#### Scenario: Pendência de privacidade
- **WHEN** os testes locais da fundação terminam sem política aprovada de acesso ou retenção e sem homologação Azure
- **THEN** as pendências permanecem identificadas e nenhuma publicação é autorizada por esta mudança
