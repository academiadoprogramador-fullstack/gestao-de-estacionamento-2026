# Proposal

## Why

O PRD e o TDD descrevem o MVP completo, mas o repositório ainda não contém uma solução executável. Sem a fundação local da Fase 1, as fases seguintes não têm onde implementar entrada, retirada, entrega e proteção de dados, e decisões estruturantes de autenticação, configuração e bootstrap correriam o risco de retrabalho.

A Fase 1 entrega a base técnica verificável para desenvolver as demais fases: solução em camadas, SPA e API no host, SQL Server local via Compose, sessão revogável com JWT, configuração por ambiente e inicialização que aplica migrations e provisiona as contas sintéticas apenas em `Development`.

## What Changes

- Criar a solução `.slnx` do backend em .NET 10 com camadas Api, Application, Domain e Infrastructure, além dos projetos de teste; criar a SPA Angular 22 com componentes standalone, Zoneless, Signals, Signal Forms, Tailwind CSS e Lucide, sem Angular Material. Versões exatas e compatibilidade serão verificadas e fixadas em manifests/lockfiles antes da implementação.
- Criar `compose.yml` na raiz somente para infraestrutura local, com SQL Server Developer, healthcheck, volume nomeado, porta publicada apenas em loopback e senha fornecida externamente ao processo Compose.
- Estruturar `appsettings*.json` para parâmetros não sensíveis e User Secrets exclusivamente em `Development` para conexão, e-mails/senhas das três contas sintéticas e chaves AES/JWT versionadas; validar opções tipadas e mapas de chaves com falha fechada, sem segredos no repositório ou no frontend.
- Implementar autenticação com ASP.NET Core Identity e os três perfis fixos, JWT HS256 de 15 minutos associado a sessão revogável no SQL, refresh opaco rotativo com sessão absoluta de 8 horas, logout com revogação, senha mínima de 8 caracteres, bloqueio de conta após 5 falhas por 15 minutos e limite de 5 requisições de login por minuto por IP.
- Configurar CORS para origens exatas da SPA e tratamento de erros em Problem Details sem cache, sem PII, tokens ou stack trace.
- Implementar o bootstrap local exclusivo de `Development`: validar configuração, conexão e contas sintéticas; aplicar todas as migrations pendentes com `Database.MigrateAsync()`; imediatamente após o sucesso, provisionar somente contas ausentes usando e-mails/senhas de User Secrets; iniciar o tráfego HTTP apenas após o provisionamento. Falha em qualquer etapa impede a inicialização, e nenhuma conta existente é alterada.
- Criar o shell autenticado Angular com rotas de login, área de trabalho e acesso negado, guards de sessão/perfil, token e refresh somente em memória, interceptor restrito à URL da API, tratamento de `401`/`403` e logout com revogação prévia. Sem `localStorage`, `sessionStorage` ou IndexedDB.
- Criar a verificação automatizada da fase: MSTest para unidade/integração com SQL Server real, Vitest para a SPA e Playwright entre duas origens HTTPS locais, cobrindo bootstrap, autenticação, revogação, CORS, expiração e ausência de persistência de sessão.
- Registrar comandos, versões, resultados e limitações apenas após a execução efetiva.

**Fora do escopo desta fase:** check-in, vagas, tarifas, retirada, cobrança, pagamento, entrega, fotos, relatórios, publicação Azure/R2, CI, IaC e CD. Nenhuma migração ou seed automático será executado fora de `Development`.

## Capabilities

### New Capabilities

- `infraestrutura-local`: infraestrutura SQL via Compose, configuração não sensível por ambiente, segredos externos, validação de chaves versionadas, bootstrap de migrations e provisionamento idempotente de contas sintéticas somente em `Development`, com isolamento de testes e falha fechada.
- `autenticacao-sessoes`: perfis fixos, login de funcionários, JWT HS256 revogável por sessão SQL, refresh rotativo com limite absoluto, logout, política de senha e limitação de tentativas, autorização no servidor e CORS restrito às origens da SPA.
- `shell-acesso-frontend`: shell Angular autenticado, rotas protegidas por sessão e perfil, sessão somente em memória, interceptor com escopo restrito, tratamento de expiração/revogação e feedback acessível de autenticação.

### Modified Capabilities

Nenhuma. O projeto ainda não possui especificações em `openspec/specs/`.

## Impact

- **Código novo:** `compose.yml`, `app/backend` (solução em camadas e testes) e `app/frontend` (SPA Angular e E2E).
- **Configuração:** `appsettings.json` e `appsettings.Development.json` apenas com parâmetros não sensíveis; User Secrets para conexão, chaves AES/JWT e contas sintéticas.
- **Banco:** migração inicial de identidade, sessão e auditoria; nenhum dado de negócio, vaga ou tarifa nesta fase.
- **Segurança:** introduz os mapas independentes de chaves JWT e AES, a sessão revogável e o isolamento de origens; não implementa ainda cifragem de campos de atendimento.
- **Dependências:** SDK .NET 10, Node/npm compatíveis com Angular 22, Docker Engine com Compose e host x86-64 para SQL Server Linux.
- **Impacto nas fases seguintes:** fornece a base de autenticação, configuração e bootstrap reutilizada pelas Fases 2–6. Publicação, cotas gratuitas, fotos, relatórios e uso com dados reais permanecem fora desta mudança.
