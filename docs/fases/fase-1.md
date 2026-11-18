# Fase 1 — Fundação e infraestrutura local

**Status:** implementação e verificações locais concluídas para o recorte da Fase 1.

**Data da execução:** 17/11/2026

**Branch original:** `feat/fase-1-fundacao-infraestrutura-local`

**Mudança OpenSpec arquivada:** `openspec/changes/archive/2026-11-17-fase-1-fundacao-infraestrutura-local/`

**Rastreabilidade:** PRD (RF-10; RNF-01/02) e TDD §2–§3, §6.2–6.6, §7.1 e F1-01–F1-05 do roadmap; decisões DT-01/05–07/09–11/20/24–27. Especificações em `openspec/specs/infraestrutura-local`, `openspec/specs/autenticacao-sessoes` e `openspec/specs/shell-acesso-frontend`.

## 1. Escopo entregue

- Solução `.slnx` em .NET 10 com camadas Api, Application, Domain e Infrastructure, além dos projetos de testes unitários e de integração.
- SPA Angular 22 standalone, Zoneless, com Signals, Signal Forms, Tailwind CSS e Lucide; Vitest e Playwright configurados.
- `compose.yml` na raiz exclusivamente para SQL Server Developer, com healthcheck, volume nomeado, rede local e porta publicada somente em loopback.
- Configuração tipada por ambiente, com validação no startup e mensagens sem valores sensíveis.
- User Secrets exclusivamente em `Development` para conexão, contas sintéticas e chaves JWT/AES versionadas; nenhum segredo em arquivo versionado.
- Mapas independentes de chaves JWT HS256 e AES-256, com Base64 de exatamente 32 bytes, versão ativa existente, versões anteriores para leitura e proibição de reutilização entre finalidades.
- ASP.NET Core Identity com os três perfis fixos (`Administrador`, `Caixa`, `Manobrista`), senha mínima de 8 caracteres, bloqueio de 15 minutos após 5 falhas e e-mail único.
- JWT HS256 de 15 minutos com `kid`, sessão absoluta de 8 horas e refresh opaco rotativo com hash no SQL.
- Revogação efetiva no servidor: logout, desativação, alteração de acesso e reuso de refresh invalidam a próxima requisição protegida.
- Login com resposta genérica, limite de 5 requisições por minuto por IP, `429` sem enumeração de conta e auditoria append-only sem PII.
- CORS restrito às origens exatas da SPA, Problem Details com `code`/`correlationId` e `Cache-Control: no-store`.
- Bootstrap automático somente em `Development`, na ordem validação → conexão → migrations → provisionamento → tráfego HTTP.
- Shell Angular com login, área de trabalho e acesso negado; guards de sessão/perfil, token e refresh somente em memória e interceptor restrito à URL da API.

## 2. Implementação

### 2.1 Backend em camadas

| Projeto | Responsabilidade na Fase 1 |
| --- | --- |
| `Estacionamento.Api` | Controllers finos, autenticação JWT, CORS, Problem Details, rate limit, bootstrap e composição. |
| `Estacionamento.Application` | Contratos de sessão, portas de autenticação, provisionamento, auditoria, relógio e opções tipadas. |
| `Estacionamento.Domain` | Perfis fixos, normalização de e-mail, política de sessão, causas de revogação e tipos de auditoria. |
| `Estacionamento.Infrastructure` | `DbContext` único de Identity/sessão/auditoria, geradores JWT/refresh, provisionamento, auditoria e validação de configuração. |
| `tests/Estacionamento.UnitTests` | Opções, precedência de configuração, políticas e regras puras. |
| `tests/Estacionamento.IntegrationTests` | SQL Server real, bootstrap, autenticação, revogação, restrições e CORS. |

Dependências seguem a direção Api → Application/Infrastructure, Infrastructure → Application/Domain e Application → Domain. Não há repositório genérico nem entidades de negócio antecipadas.

### 2.2 Configuração e segredos

- `appsettings.json` e `appsettings.Development.json` contêm apenas parâmetros não sensíveis: ambiente, URLs, emissor/audiência, versões ativas, CORS, operação e logging.
- User Secrets de `Development` guardam conexão administrativa local, e-mails/senhas das três contas sintéticas e as chaves `Autenticacao:Jwt:Chaves:<versao>` e `ProtecaoDados:Chaves:<versao>`.
- `ContasSinteticas` não aparece em `appsettings.Development.json`.
- `Testing` não carrega User Secrets pessoais: a fixture fornece credenciais e chaves explícitas e catálogos `EstacionamentoTeste_*`.
- A validação falha fechada para conexão ausente, conta incompleta, e-mail inválido/duplicado, chave Base64 inválida, tamanho diferente de 32 bytes, versão ativa inexistente ou material compartilhado entre JWT e AES.

### 2.3 Autenticação e sessão

- `UsuarioAplicacao` acrescenta `Nome`, `Ativo` e `VersaoAcesso` ao Identity.
- O JWT contém apenas `sub`, `sid`, `jti`, `iat`, `exp`, perfis e versão de acesso; não carrega PII ou finanças.
- A validação fixa HS256, emissor, audiência, expiração, tolerância de 30 segundos e `kid` resolvido somente no mapa local.
- Cada requisição protegida consulta conta ativa, sessão não revogada/expirada e versão de acesso no SQL; indisponibilidade do SQL falha fechado.
- Refresh é opaco, rotativo e armazenado apenas como hash SHA-256. O consumo é atômico e o reuso revoga a família.
- Logout revoga sessão e refreshes antes de responder; a auditoria registra ator opaco, alvo opaco, correlação e UTC.

### 2.4 Bootstrap de Development

1. Validar opções, conexão e as três contas sintéticas sem imprimir valores.
2. Verificar o servidor por `master` e recusar catálogos de sistema.
3. Executar `Database.MigrateAsync()`, permitindo que a migration crie o catálogo ausente.
4. Imediatamente após o sucesso, provisionar apenas as contas ausentes e garantir os perfis fixos.
5. Somente então aceitar tráfego HTTP.

Reinícios não duplicam contas nem alteram e-mail, senha, perfis ou estado existentes. `Testing` e `Production` não executam migration nem seed automáticos pelo startup.

### 2.5 SPA e acessibilidade

- Rotas `/login`, `/dashboard` e `/forbidden`, com guards de sessão e perfil.
- Login em Signal Forms com labels associados, resumo de erros focado, `aria-invalid`, mensagens textuais e nenhuma chamada à API com dados inválidos.
- Token e refresh em campos privados do serviço de sessão, sem `localStorage`, `sessionStorage`, IndexedDB ou cookie.
- Interceptor anexa o bearer somente à URL-base configurada da API, sem `withCredentials`.
- Renovação single-flight em `401`; `403` não implica logout; falha de logout remoto é informada ao usuário.

## 3. Verificações executadas

Ambiente registrado na execução original: Linux x86-64, .NET SDK 10.0.114, Node 26.13.1, npm 12.2.2, SQL Server 2022 CU28 em container, Angular 22.2.6 e Playwright 1.65.1. Os testes de integração usaram instância Compose isolada na porta 14330 e catálogos `EstacionamentoTeste_*`.

| Verificação | Comando | Resultado registrado |
| --- | --- | --- |
| Build da solução | `dotnet build app/backend/Estacionamento.slnx` | Sucesso. |
| Testes unitários MSTest | `dotnet test app/backend/tests/Estacionamento.UnitTests` | 40 aprovados. |
| Testes de integração MSTest | `ESTACIONAMENTO_TESTE_CONEXAO=... dotnet test app/backend/tests/Estacionamento.IntegrationTests` | 36 aprovados com SQL Server real. |
| SPA (Vitest) | `npm test --prefix app/frontend -- --run` | 20 aprovados. |
| E2E Playwright | `npx playwright test` em `app/frontend` | 8 jornadas aprovadas entre `https://localhost:4200` e `https://localhost:4300`. |
| Compose | `docker compose config --quiet` e `docker compose up -d` | Configuração válida, healthcheck saudável, porta somente em `127.0.0.1`, volume preservado após restart. |
| Isolamento de teste | Segunda instância Compose com porta/volume próprios | Testes não tocaram o catálogo de desenvolvimento. |
| Bootstrap | Catálogo ausente, vazio, atualizado, migration pendente, falha de migration, falha de seed e SQL indisponível | Ordem validação → conexão → migrations → provisionamento → HTTP comprovada; falhas bloquearam o tráfego. |
| Ambientes não Development | API iniciada em `Testing` e `Production` | Nenhuma tabela/conta criada pelo bootstrap; OpenAPI não exposto. |
| OpenSpec | `openspec validate --strict` | Aprovado na época; mudança arquivada em 17/11/2026. |

Cenários de integração cobertos: login válido/inválido, resposta genérica, `429`, bloqueio de conta, JWT inválido/expirado/`kid` desconhecido, logout e revogação, reuso de refresh, sessão expirada, SQL indisponível, CORS/preflight entre duas origens, Problem Details/no-store, restrições de FK e unicidade, bootstrap idempotente e preservação de contas desabilitadas.

## 4. Mapa de evidências do roadmap

| Item | Evidência |
| --- | --- |
| **F1-01** | Stack e camadas compilando; versões fixadas em `global.json`, `Directory.Packages.props`, `package.json` e `package-lock.json`; imagem SQL fixada em `compose.yml`. |
| **F1-02** | Compose validado com healthcheck/volume/porta loopback; instância de teste separada; appsettings sem segredos; mapas JWT/AES independentes validados. |
| **F1-03** | Identity com três perfis, senha 8/bloqueio/limite; JWT HS256 15 min; refresh rotativo; sessão absoluta de 8 h; logout/revogação. |
| **F1-04** | CORS por origem exata; interceptor restrito; sessão somente em memória; expiração/revogação/reuso e SQL indisponível testados. |
| **F1-05** | Contas sintéticas validadas em User Secrets; migrations e provisionamento na ordem exigida; cenários de catálogo/falha testados; `Testing`/`Production` sem bootstrap. |

## 5. Limitações e pendências registradas

- A revisão manual de acessibilidade com tecnologia assistiva e a instalação em máquina limpa não foram executadas na Fase 1.
- O certificado HTTPS local é autoassinado; o Playwright o ignora explicitamente (`ignoreHTTPSErrors`), sem alterar a validação TLS do produto.
- Publicação Azure/R2, CI, IaC e CD permaneceram fora da fase.
- Nenhuma cifragem de campo de negócio foi implementada nesta fase; apenas o mapa de chaves AES foi criado e validado.
- Os números de testes acima são as evidências históricas da Fase 1. Execuções posteriores incluem os testes das fases seguintes e, por isso, apresentam totais maiores.

## 6. Commits relevantes da fase

- `e4c6ca4 chore: fixar versões e ferramentas do ambiente`
- `bf0e695 feat(infra): isolar SQL Server local por Compose`
- `4136e2c feat(autenticacao): criar fundação .NET com sessão revogável`
- `e701c6a feat(frontend): criar shell Angular com sessão em memória`
- `b71c2e2 docs: registrar contratos e evidências da fase 1`
- `2195872`/`0f4e9f6` ajustes do catálogo local criado pela migration
- `69d6055 docs(openspec): sincronizar specs e arquivar a fase 1`
