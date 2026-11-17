# Design

## Context

Ver `proposal.md` para motivação. O repositório contém apenas PRD, TDD, README e a configuração do OpenSpec; não há solução, código, migrations ou testes. A Fase 1 é a primeira implementação e precisa materializar as decisões DT-01–07, DT-09–11, DT-20 e DT-24–27 do TDD sem antecipar o domínio de atendimento das fases seguintes.

Restrições que moldam o desenho:

- .NET 10, Angular 22, SQL Server com EF Core e Identity, conforme TDD §2; versões exatas ainda serão verificadas e fixadas.
- SPA e API executam no host, em origens HTTPS distintas; somente o SQL Server roda no Compose.
- Publicação gratuita é alvo da Fase 6; nada desta fase provisiona Azure, R2 ou CI.
- User Secrets não é cofre cifrado; nenhum segredo pode entrar em arquivo versionado, log, frontend ou argumento de linha de comando.
- O bootstrap automático de esquema e contas existe exclusivamente em `Development`.

## Goals / Non-Goals

**Goals:**

- Solução executável e testável com camadas explícitas, sem dependências de negócio antecipadas.
- SPA e API em origens HTTPS separadas, com CORS restrito e bearer em memória.
- Sessão revogável com JWT HS256, refresh rotativo e limite absoluto, verificada no servidor.
- Configuração tipada e validada, com mapas independentes de chaves JWT/AES e falha fechada.
- Bootstrap `Development` na ordem exata validação → migrations → provisionamento → HTTP.
- Testes automatizados que comprovem bootstrap, autenticação, revogação, CORS e ausência de persistência.

**Non-Goals:**

- Entidades e regras de atendimento, vagas, tarifas, retirada, cobrança, pagamento, entrega, fotos ou relatórios.
- Cifragem efetiva de campos de negócio; nesta fase apenas se estabelece e valida o mapa de chaves AES.
- Publicação Azure/R2, CI/CD, IaC, observabilidade externa paga e operação contínua.
- Administração de funcionários, vagas ou tarifas pela interface; pertence à Fase 2.

## Decisions

### D1. Estrutura da solução e fronteiras de camadas

Criar `app/backend/Estacionamento.slnx` com:

- `Estacionamento.Api`: composição, controllers, autenticação, CORS, Problem Details e bootstrap.
- `Estacionamento.Application`: portas e casos de uso; nesta fase, contratos de sessão e provisionamento.
- `Estacionamento.Domain`: tipos e regras puras compartilhadas; sem dependências de EF/HTTP.
- `Estacionamento.Infrastructure`: EF Core, Identity, persistência de sessão e validação criptográfica.
- `tests/Estacionamento.UnitTests` e `tests/Estacionamento.IntegrationTests`.

Dependências seguem TDD §3: Api compõe Application/Infrastructure; Infrastructure referencia Application/Domain; Application referencia Domain; Domain não referencia camadas externas. Não criar repositório genérico, agregados artificiais nem módulos de negócio vazios.

**Alternativas:** projeto único ou Minimal APIs reduziriam arquivos agora, mas concentrariam controllers, EF e regras no mesmo assembly e contrariariam as fases seguintes; DDD com agregados completos seria excesso para a fundação.

### D2. SPA Angular

Criar `app/frontend` com Angular 22 standalone, Zoneless, Signals, Signal Forms, Tailwind CSS e Lucide, sem Angular Material. Organização:

- `src/app/core`: configuração pública, sessão, transporte HTTP, interceptor, guards e erros.
- `src/app/features/auth`: login, logout e acesso negado.
- `src/app/features/dashboard`: área de trabalho autenticada, sem relatório financeiro.
- `src/app/shared`: componentes acessíveis e formatação comum.
- `e2e/`: Playwright.

A URL da API vem de configuração pública de ambiente, nunca de segredo. O estado da sessão é exposto por Signals somente leitura; tokens ficam em campos privados do serviço de sessão, sem storage.

**Alternativas:** Reactive Forms foi descartado por decisão DT-01; Angular Material foi descartado para manter controles nativos e dependências reduzidas.

### D3. Compose apenas de infraestrutura

`compose.yml` na raiz define um serviço SQL Server Developer com:

- Imagem oficial Linux com tag imutável ou digest verificado na primeira tarefa; não usar `latest` como dependência permanente.
- Healthcheck com `sqlcmd` executando `SELECT 1` e lendo a senha da variável de ambiente do container, nunca literal no YAML.
- Volume nomeado para `/var/opt/mssql` e porta `127.0.0.1:${SQL_PORT:-1433}:1433`.
- `MSSQL_SA_PASSWORD` fornecida ao processo Compose por variável de ambiente, sem `.env` versionado e sem reutilizá-la na conexão da API.
- Nenhum serviço de API, SPA, proxy, Redis, Key Vault ou emulador S3.

O script de preparação local cria catálogo e usuário dedicado de forma idempotente; o esquema de domínio continua pertencendo às migrations. A conta `sa` não é usada pela API.

**Alternativas:** Compose de toda a aplicação foi descartado por dificultar debug, hot reload, HTTPS e User Secrets no host.

### D4. Configuração tipada e validação de chaves

Usar o sistema padrão de configuração do .NET com opções tipadas e `IValidateOptions`/validação no startup:

- `appsettings.json`: parâmetros comuns e não sensíveis.
- `appsettings.Development.json`: apenas parâmetros não sensíveis, incluindo `Autenticacao:Jwt:VersaoAtiva`, `ProtecaoDados:VersaoAtiva`, emissor/audiência e origens CORS. Sem `ContasSinteticas`.
- User Secrets em `Development`: connection string, `ContasSinteticas`, `Autenticacao:Jwt:Chaves:<versao>` e `ProtecaoDados:Chaves:<versao>`.
- App Settings/variáveis de ambiente em publicação, sem implementar publicação nesta fase.

Cada chave deve decodificar Base64 para exatamente 32 bytes; a versão ativa deve existir e JWT/AES não compartilham material. A validação roda antes de migrations e tráfego. Erros não imprimem valores.

**Alternativas:** Key Vault e Azure App Configuration foram descartados por custo e decisão DT-25; geração efêmera de chaves foi descartada porque invalidaria dados e sessões a cada reinício.

### D5. Bootstrap de Development em etapa explícita antes do servidor HTTP

Em `Program.cs`, quando o ambiente for exatamente `Development`, executar antes de `RunAsync`:

1. Construir a aplicação e obter um escopo DI.
2. Validar opções, connection string, mapas de chaves e as três contas sintéticas (presença, formato de e-mail, senhas e unicidade após normalização), sem registrar valores.
3. Verificar conexão e destino do banco.
4. Executar `Database.MigrateAsync()` com o contexto EF.
5. Imediatamente após o sucesso, executar o provisionador idempotente de contas e perfis.
6. Somente então iniciar o pipeline HTTP.

Qualquer exceção encerra o processo com mensagem controlada. O provisionador consulta por e-mail normalizado e cria apenas contas ausentes; não redefine senha, e-mail, perfis ou estado e não reativa conta desabilitada. Perfis fixos são constantes de código; o provisionador garante sua existência sem alterar contas.

**Alternativas:** `dotnet ef database update` manual foi descartado pelo requisito de bootstrap; `EnsureCreated` e seed em migration foram descartados por não representarem o histórico versionado e por misturar dados de ambiente ao esquema.

### D6. Identidade, JWT e sessão revogável

- `UsuarioAplicacao : IdentityUser<Guid>` acrescenta `Ativo`, `VersaoAcesso` e `Nome`.
- Identity configura senha mínima de 8 caracteres, bloqueio de 15 minutos após 5 falhas e e-mail único.
- Perfis são roles fixas: `Administrador`, `Caixa`, `Manobrista`.
- JWT é emitido pela API após login, com claims `sub`, `sid`, `jti`, `iat`, `exp`, perfis e `VersaoAcesso`. Sem PII/finanças.
- Assinatura HS256 com `SymmetricSecurityKey` derivada dos 32 bytes decodificados; `kid` corresponde à versão; emissor, audiência, validade de 15 minutos e tolerância de 30 segundos são validados. `none`, algoritmos alternativos, `kid` desconhecido e chave ausente são rejeitados.
- `SessaoAutenticacao` registra ID, usuário, versão de acesso, criação, expiração absoluta, revogação e causa.
- `RefreshToken` registra hash SHA-256 do token opaco, sessão/família, validade, consumo e substituição; o valor puro existe apenas em memória do cliente.
- Um evento de validação consulta, em cada requisição protegida, conta ativa, sessão não revogada e igualdade de versão de acesso. SQL indisponível falha fechado.
- Logout revoga a sessão e os refreshes; alterações de acesso incrementam `VersaoAcesso` e revogam sessões na mesma transação.

**Alternativas:** cookie de primeira parte foi substituído por JWT bearer em decisão DT-06; validação somente de assinatura foi descartada porque não permite revogação efetiva; cache permissivo de sessão foi descartado por contrariar a revogação na próxima requisição.

### D7. Login, limitação de tentativas e erros de sessão

- `POST /api/v1/session/login` recebe e-mail/senha, usa Identity e retorna JWT, refresh e dados mínimos, sem senha ou hash.
- `POST /api/v1/session/refresh` recebe refresh no corpo JSON, consome e rotaciona atomicamente; reutilização revoga a família.
- `GET /api/v1/session` e `POST /api/v1/session/logout` operam sobre o bearer.
- Rate limiter fixed window de 5 requisições por minuto por IP no login, respondendo `429`; a identidade do IP só usa `ForwardedHeaders` quando a cadeia de proxy é explicitamente confiável.
- Falhas de senha alimentam o bloqueio do Identity; respostas são genéricas e não enumeram contas.
- Todas as respostas de sessão usam `Cache-Control: no-store`; erros seguem Problem Details com `code` e `correlationId`, sem stack trace ou PII.

Renovação no cliente é single-flight: uma única chamada de refresh por vez, com repetição no máximo uma vez da requisição original após `401`. Comandos de negócio nunca são repetidos automaticamente.

**Alternativas:** fila de login e tentativa de contornar o bloqueio por IP foram descartadas; refresh em cookie foi descartado por DT-06/20.

### D8. CORS, correlação e proteção de resposta

- Política nomeada com origens exatas de configuração, métodos necessários, headers `Authorization`, `Content-Type` e `Idempotency-Key`, preflight `OPTIONS`, sem `AllowAnyOrigin` e sem `AllowCredentials`.
- Middleware de correlação gera/propaga um identificador opaco e o devolve no erro; não recebe identidade do cliente.
- Respostas da API são `no-store`; erros e negações também.
- OpenAPI, se habilitado, fica restrito a `Development` e não é público por padrão.

**Alternativas:** proxy da SPA para a API foi descartado porque esconderia a topologia real de CORS; CORS amplo foi descartado por não ser autenticação e expor superfície desnecessária.

### D9. Shell, sessão em memória e navegação

- Serviço de sessão com Signals privados para JWT, refresh e dados do funcionário; métodos públicos apenas para estado derivado e operações.
- Interceptor funcional anexa bearer somente quando a URL começa pela URL-base configurada da API; nunca usa `withCredentials`.
- Guards funcionais: sessão resolve o estado antes da navegação; perfil encaminha a `/forbidden`.
- Rotas iniciais: `/login`, `/dashboard`, `/forbidden`; raiz redireciona para dashboard ou login.
- Logout chama a API antes de limpar a memória; em falha, limpa a sessão local e informa que a revogação remota não foi confirmada.
- `401` dispara renovação single-flight; se falhar, limpa a sessão e volta ao login. `403` mostra acesso negado sem logout.
- Login usa Signal Forms com labels, resumo focado, `aria-invalid`, mensagens textuais e nenhuma chamada quando a validação local falha.

**Alternativas:** `sessionStorage` e restauração após recarga foram descartados por DT-20; guard baseado apenas em perfil foi descartado porque a autorização efetiva é da API.

### D10. Modelo inicial e migration

A migration inicial cria apenas o necessário para identidade, sessão e auditoria de autenticação:

- Tabelas do Identity (`AspNetUsers`, roles, claims, logins, tokens) com `UsuarioAplicacao`.
- `SessoesAutenticacao` com FK para usuário, versão de acesso, expiração absoluta, revogação e índices por usuário/estado.
- `RefreshTokens` com hash único, FK para sessão, consumo/substituição e índice por hash.
- `EventosAuditoria` append-only para login/logout/bloqueio/revogação, com ator opaco, tipo, UTC e correlação, sem senha/token/PII.

Usar `rowversion` onde houver mutação concorrente de sessão/refresh e transações para consumo de refresh, revogação e auditoria. FKs restritas, sem cascata que apague histórico. Nenhuma tabela de vagas, atendimentos, tarifas ou financeiro nesta fase.

**Alternativas:** usar apenas tabelas padrão do Identity não atenderia revogação por sessão/versão nem auditoria; guardar refresh em texto puro foi descartado.

### D11. Verificação automatizada e isolamento

- **MSTest/unitários:** validação de opções e mapas de chaves, regras de expiração/limite absoluto, normalização de e-mail, decisão de provisionamento e tratamento de erros.
- **MSTest/integração:** SQL Server real em instância/projeto Compose de teste, banco por execução, migrations aplicadas por fixture, sem bootstrap automático e sem User Secrets pessoais. Cobrir catálogo vazio, reinício idempotente, migration nova, falha de migration/seed, revogação, refresh/reuso, CORS e autenticação.
- **Vitest:** sessão em memória, interceptor restrito, renovação single-flight, guards e validação acessível do login.
- **Playwright:** SPA e API em origens HTTPS distintas, com credenciais sintéticas explícitas; login/logout, expiração, revogação, acesso negado e ausência de tokens em storage.

Os testes de integração devem validar o destino antes de aplicar migrations e limpar apenas o banco de teste identificado. E2E que inicia API em `Development` usa infraestrutura e catálogo isolados, sem tocar no banco de desenvolvimento.

**Alternativas:** SQLite/InMemory foram descartados por não reproduzirem restrições e concorrência do SQL Server; um único teste E2E com proxy não validaria a topologia de origens distintas.

### D12. Implantação e rollback desta fase

Não há implantação em ambiente publicado. A entrega é local e versionada na feature branch. O rollback consiste em reverter a mudança e manter os volumes locais; a remoção de dados de desenvolvimento exige ação explícita. A migration inicial pode ser removida apenas enquanto não houver dados relevantes; após uso local, correções devem ocorrer por migration aditiva.

## Risks / Trade-offs

- **Versões .NET 10/Angular 22/imagem SQL ainda não verificadas** → primeira tarefa fixa versões, restaura, compila e executa; se indisponíveis, interromper e revisar antes de codificar o restante.
- **Host incompatível com SQL Server Linux x86-64** → verificar arquitetura e recursos no início; não substituir por SQLite nem emulação sem revisão.
- **Bootstrap como ponto único de falha local** → ordem explícita, logs controlados sem segredos, testes de catálogo vazio, migration pendente e falhas.
- **User Secrets não cifrado** → tratar como credencial de desenvolvimento, proteger o perfil do desenvolvedor e nunca copiar para build/deploy.
- **Revogação consultando SQL em cada requisição** → aceitar o custo no perfil acadêmico; sem cache permissivo e com falha fechada.
- **Rate limit por IP atrás de proxy** → confiar apenas em cadeia de proxy configurada; não aceitar cabeçalho de IP arbitrário do cliente.
- **Corrida de refresh em múltiplas requisições** → renovação single-flight no cliente, consumo atômico no servidor e reautenticação em caso de resposta perdida.
- **Bloqueio de conta usado para negação de serviço** → bloqueio temporário de 15 minutos e resposta genérica; sem fila; revisar limites se necessário.
- **Senha do Compose visível a administradores do Docker** → fornecida por variável externa, aceita como ambiente de desenvolvimento e não descrita como segredo cifrado.
- **Migrations automáticas em Development podem divergir de produção** → proibir o fluxo fora de `Development`, testar `Testing`/`Production` sem bootstrap e preparar o procedimento controlado na Fase 6.

## Migration Plan

1. Verificar e fixar versões; criar solução, SPA e projetos de teste.
2. Criar `compose.yml`, script de preparação local e configuração não sensível; validar SQL saudável e persistente.
3. Implementar opções, validação e mapas de chaves; comprovar falha fechada.
4. Implementar Identity, JWT, sessão, refresh, logout, rate limit, CORS e auditoria com migration inicial.
5. Implementar bootstrap `Development` na ordem exigida e provisionamento idempotente.
6. Implementar shell, guards, interceptor, sessão em memória e login acessível.
7. Executar testes unitários, integração, Vitest, Playwright e cenários de reinício/falha; registrar evidências e limitações.

Rollback local: parar os processos, reverter a mudança e preservar os volumes; recriar o banco apenas por ação explícita em ambiente isolado.

## Open Questions

- Quais versões exatas de SDK .NET 10, Angular 22, provider EF Core para SQL Server e tag/digest da imagem SQL serão adotadas? A resposta é verificada na primeira tarefa e registrada em manifests/lockfiles; não altera specs nem o desenho.
- Quais portas HTTPS locais serão usadas para SPA e API? A configuração inicial usa 4200 e 4300 como exemplo, ajustável na implementação desde que as origens permaneçam distintas e explícitas.
