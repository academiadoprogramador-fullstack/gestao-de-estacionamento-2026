# Spec Delta

## ADDED Requirements

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
