# Spec Delta

## Purpose

Define como o administrador cria e mantém os funcionários e seus perfis fixos, garantindo concorrência segura, proteção do último administrador ativo, revogação imediata de sessões e preservação da autoria sem autorregistro.

## ADDED Requirements

### Requirement: Criação de funcionário pelo administrador

O sistema SHALL permitir apenas ao Administrador criar funcionário com e-mail normalizado e único, senha inicial de no mínimo 8 caracteres e conjunto não vazio de perfis fixos; a senha inicial SHALL NOT ser retornada, e criação, ator e horário UTC SHALL ser auditados.

#### Scenario: Criação válida

- **WHEN** o Administrador informa e-mail inédito, senha inicial válida e ao menos um perfil fixo
- **THEN** a conta é criada ativa, a resposta contém apenas dados de identificação e perfis, e a auditoria registra o ator e o horário do servidor

#### Scenario: Criação inválida ou duplicada

- **WHEN** o pedido omite perfis, usa e-mail já existente após normalização ou senha com menos de 8 caracteres
- **THEN** a API recusa a criação sem criar conta parcial e sem revelar a senha em resposta, erro ou log

#### Scenario: Autorregistro inexistente

- **WHEN** um cliente sem perfil Administrador tenta criar funcionário ou alterar o próprio acesso
- **THEN** a API nega a operação independentemente da interface

### Requirement: Alteração de acesso concorrente e auditada

O sistema SHALL exigir token de concorrência da conta para alterar perfis ou estado ativo, SHALL aplicar a alteração completa ou nenhuma em caso de conflito e SHALL incrementar a versão de acesso e revogar sessões e refreshes na mesma transação.

#### Scenario: Alteração confirmada

- **WHEN** o Administrador altera perfis ou estado com o token de concorrência corrente
- **THEN** a nova configuração é persistida com auditoria e o funcionário precisa autenticar novamente antes da próxima requisição protegida

#### Scenario: Concorrência detectada

- **WHEN** duas alterações partem do mesmo token de concorrência
- **THEN** apenas uma é confirmada e a outra recebe conflito com o estado atual, sem sobrescrever acessos silenciosamente

#### Scenario: Revogação inclui repetição idempotente

- **WHEN** um funcionário com acesso alterado repete uma entrada anterior usando a mesma chave idempotente
- **THEN** a API recusa a requisição pela sessão revogada antes de devolver qualquer resultado de replay

### Requirement: Proteção do último administrador ativo

O sistema SHALL impedir remover o perfil Administrador ou desativar a última conta ativa que o possui, inclusive sob alterações concorrentes, e SHALL NOT apagar autoria histórica ao desativar um funcionário.

#### Scenario: Remoção do último administrador

- **WHEN** uma alteração deixaria a unidade sem nenhum Administrador ativo
- **THEN** a API recusa a operação e mantém o acesso administrativo existente

#### Scenario: Concorrência entre administradores

- **WHEN** dois administradores tentam remover o perfil um do outro simultaneamente
- **THEN** a serialização garante que ao menos um Administrador ativo permaneça e a operação conflitante é recusada

#### Scenario: Desativação preserva histórico

- **WHEN** um funcionário é desativado
- **THEN** seus registros de autoria permanecem legíveis por referência opaca e a conta não pode mais autenticar

### Requirement: Listagem de funcionários sem credenciais

O sistema SHALL permitir ao Administrador listar funcionários de forma paginada, com filtros informativos, sem retornar senha, hash, token ou material de autenticação.

#### Scenario: Listagem paginada

- **WHEN** o Administrador consulta a listagem com página e tamanho válidos
- **THEN** a resposta contém itens, página, tamanho e total, sem credenciais ou hashes

#### Scenario: Paginação inválida

- **WHEN** a página é menor que 1 ou o tamanho excede o máximo técnico
- **THEN** a API recusa o parâmetro com erro de validação em vez de ajustar silenciosamente

#### Scenario: Acesso de outro perfil

- **WHEN** Caixa ou Manobrista requisita a listagem de funcionários
- **THEN** a API responde com acesso negado e não retorna dados de contas
