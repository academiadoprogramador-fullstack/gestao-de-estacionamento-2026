# identity/employee-session Specification

## Purpose

Permitir que funcionários autorizados iniciem e encerrem uma sessão segura, com proteção efetiva de acesso e de operações mutáveis desde a primeira fase.

## Requirements

### Requirement: Acesso somente para funcionários provisionados
O sistema SHALL autenticar apenas funcionários previamente provisionados e ativos, sem autorregistro público, e SHALL negar acesso sem credenciais válidas.

#### Scenario: Login válido
- **WHEN** um funcionário ativo provisionado apresenta credenciais válidas
- **THEN** o sistema estabelece sessão e retorna o estado autenticado sem expor segredos

#### Scenario: Credenciais inválidas ou funcionário inativo
- **WHEN** alguém informa credenciais inválidas ou de conta inativa
- **THEN** o sistema não estabelece sessão nem revela se a conta existe

### Requirement: Sessão de primeira parte
O sistema SHALL manter a sessão do funcionário em cookie seguro, HttpOnly, vinculado à origem da SPA, e SHALL permitir consultar e encerrar a sessão.

#### Scenario: Consulta autenticada
- **WHEN** o funcionário autenticado consulta o estado da sessão pela origem da SPA
- **THEN** o sistema informa sua identidade operacional e papéis sem expor cookie ou informações sensíveis

#### Scenario: Logout e expiração
- **WHEN** o funcionário encerra a sessão ou uma sessão expira
- **THEN** as operações protegidas subsequentes são negadas como não autenticadas

### Requirement: Autorização no serviço
O sistema SHALL negar por padrão operações protegidas a visitantes e SHALL aplicar no serviço papéis fixos manobrista, caixa e administrador, sem depender da interface.

#### Scenario: Perfil autorizado e perfil indevido
- **WHEN** um funcionário consulta uma operação de teste restrita ao administrador
- **THEN** o administrador recebe resposta autorizada e os demais papéis recebem negação de acesso

#### Scenario: Visitante sem sessão
- **WHEN** um visitante solicita uma operação protegida
- **THEN** o serviço responde sem revelar dados protegidos

### Requirement: Proteção contra requisições forjadas
O sistema SHALL emitir token antiforgery pela origem da SPA e SHALL exigir token válido e origem permitida em todas as operações mutáveis baseadas em cookie, inclusive login e logout.

#### Scenario: Mutação com token válido
- **WHEN** um cliente da origem permitida envia login com token antiforgery válido
- **THEN** o sistema processa a autenticação conforme as credenciais

#### Scenario: Login ou logout sem proteção
- **WHEN** um cliente envia login ou logout sem token válido ou com origem não permitida
- **THEN** o sistema rejeita a mutação sem alterar a sessão

### Requirement: Respostas privadas e dados mínimos
O sistema SHALL evitar cache compartilhado de respostas de sessão e SHALL não expor cookie, credenciais ou documento pessoal em resposta, log ou erro de autenticação.

#### Scenario: Inspeção de login e estado
- **WHEN** login e consulta de sessão são efetuados
- **THEN** respostas privadas não são cacheáveis por intermediários e os registros técnicos não contêm credenciais ou valores de cookie

### Requirement: Revalidação de acesso em sessões existentes
O sistema SHALL revalidar conta ativa e versão de acesso em toda requisição protegida. Após desativação ou mudança de perfis confirmada, uma sessão emitida anteriormente SHALL ser rejeitada na próxima requisição protegida e exigir nova autenticação, sem confiar nos papéis antigos do cookie.

#### Scenario: Funcionário desativado com cookie válido
- **WHEN** um funcionário desativado tenta consultar ou executar uma operação protegida com cookie ainda não expirado
- **THEN** a operação é negada como não autenticada, sem expor dados ou gravar alterações

#### Scenario: Perfis alterados durante a sessão
- **WHEN** o administrador altera os perfis e o funcionário tenta usar a sessão emitida antes da alteração
- **THEN** a sessão é rejeitada e somente um novo login emite permissões atuais

#### Scenario: Repetição de comando após perda de acesso
- **WHEN** um funcionário perde acesso e tenta repetir check-in anteriormente confirmado com a mesma chave
- **THEN** o sistema nega a requisição antes de devolver o resultado idempotente
