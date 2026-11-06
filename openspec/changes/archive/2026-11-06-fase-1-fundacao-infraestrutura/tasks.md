# Tasks

## 1. Decisões e preparação da fundação

- [x] 1.1 Atualizar `docs/tdd.md` antes de implementar para definir o marco local da Fase 1 e adiar homologação Azure sem eliminar gates de produção; verificar coerência com `docs/prd.md`, proposta, specs e design.
- [x] 1.2 Registrar em `docs/` matriz de pendências com responsável/estado para fuso, moeda, volumes, domínios, produto/SKU/rede edge/API, privacidade, backup/chaves, RPO/RTO e contingência; verificar que homologação Azure permanece pendente e nenhum dado sintético figura como aprovação (PRD §12, TDD §8).
- [x] 1.3 Verificar compatibilidade de Angular 22/Signal Forms, .NET 10/EF Core SQL Server, Tailwind, Lucide, MSTest, Vitest e Playwright, fixar versões e lockfiles; verificar restore/install reprodutível, sem inferir versões do README (DT-01/02/03/05/10).

## 2. Solução e testes de base

- [x] 2.1 Criar `backend/Estacionamento.slnx` e camadas Api/Application/Domain/Infrastructure com dependências conforme TDD §3; verificar `dotnet build` e teste arquitetural MSTest que impede dependências invertidas.
- [x] 2.2 Criar `frontend/` Angular standalone/zoneless com estrutura core/features/shared, Tailwind, Lucide, configuração Vitest e controles acessíveis iniciais; verificar build e teste de componente executando sem ZoneJS.
- [x] 2.3 Criar harness `tests/e2e/` Playwright com setup de edge local equivalente; verificar execução de smoke de abertura da SPA e documentar comandos locais em `README.md`.

## 3. Persistência e proteção de configuração

- [x] 3.1 Configurar SQL Server real de desenvolvimento e integração local, Identity EF Core e migração inicial somente de contas/papéis técnicos; verificar migração e testes MSTest contra SQL Server, sem tabelas de vagas/atendimentos (environment-readiness).
- [x] 3.2 Configurar isolamento de desenvolvimento e testes locais, validação fail-fast de conexão/origem/chaves e configuração externa de segredos locais; verificar teste de startup com parâmetro ausente e documentação de variáveis sem valores secretos (environment-readiness).
- [x] 3.3 Persistir key ring Data Protection local de teste com proteção/permissões adequadas e provisionamento inicial idempotente de três papéis/contas sintéticas por canal seguro; verificar teste de integração com reinício e ausência de senha no git (employee-session, environment-readiness).

## 4. Sessão e autorização

- [x] 4.1 Implementar endpoints versionados de antiforgery, login, sessão e logout via Identity, cookie seguro host-only, expiração/bloqueio e rate limit; verificar testes HTTP de sucesso, senha inválida, inatividade, logout e expiração (employee-session).
- [x] 4.2 Aplicar política default-deny, papéis manobrista/caixa/admin e rota de verificação administrativa; verificar testes HTTP de 401/403 e acesso permitido somente ao admin, sem confiar em headers de identidade (employee-session).
- [x] 4.3 Exigir token antiforgery e origem esperada em toda mutação com cookie, incluindo login/logout; verificar testes HTTP positivos e negativos de CSRF, origem forjada e sessão preservada após rejeição (employee-session).
- [x] 4.4 Criar tela Angular de login/estado/logout com Signal Forms e comunicação por `/api/v1`, sem sessão em storage; verificar Vitest para erros, teclado e estados textuais e Playwright para fluxo local de login/logout (employee-session, RNF-04).
- [x] 4.5 Configurar respostas privadas não cacheáveis, logs mínimos e erros sem credenciais/cookies; verificar testes de cabeçalhos/respostas e inspeção automatizada de logs em casos de autenticação falha (employee-session, RNF-02/07).

## 5. Edge local e integração

- [x] 5.1 Implementar SPA/edge local com proxy allowlisted de `/api/*`, preservação de cookies/CSRF, origem HTTPS de teste e upstream fixo; verificar testes de proxy com host, caminho, `Set-Cookie`, headers e resposta privada sem cache (web-api-edge).
- [x] 5.2 Executar API como processo separado e comprovar que cabeçalhos de identidade forjados não conferem acesso; verificar testes HTTP negativos diretos e documentar que isolamento de ingress entre sites depende de futura homologação Azure (web-api-edge).
- [x] 5.3 Exercitar Playwright contra edge local em navegador real: bootstrap CSRF, login/logout, cookie host-only, expiração, autorização, origem segura e ausência de chamadas cross-site; verificar evidência reproduzível e documentar limites dos testes locais (employee-session, web-api-edge).

## 6. Evidências e gate de revisão local

- [x] 6.1 Documentar comando e revisão da migração inicial em banco local de teste e verificar reconstrução do esquema em SQL Server vazio pelo procedimento documentado (environment-readiness). Revisão posterior autoriza bootstrap automático somente em `Development`.
- [x] 6.2 Consolidar no registro da fase evidências de navegador local/key ring e pendências nomeadas de Azure, privacidade e operação; verificar que somente o marco local foi atingido, sem declarar homologação ou produção prontas (environment-readiness).

## 7. Ajuste aprovado: bootstrap local automático (PRD ajuste 06/11; TDD bootstrap local)

- [x] 7.1 Atualizar PRD, TDD, design e spec `environment-readiness` para delimitar startup automático só em `Development`, sem alterar gates de Azure.
- [x] 7.2 Migrar Identity e provisionar três contas sintéticas no `Program.cs` antes de servir em `Development`, falhando se faltar banco/credencial; preservar comando explícito e idempotência.
- [x] 7.3 Isolar testes de integração em `Testing`, configurar senhas externas no E2E e verificar primeiro startup, reinício e ausência de bootstrap fora de `Development`.
- [x] 7.4 Atualizar instruções de execução local e verificar build/testes aplicáveis, sem expor segredos.
