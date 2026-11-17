# Tasks

## 1. Fundação, versões e estrutura

- [x] 1.1 Verificar pré-requisitos locais (SDK .NET 10, Node/npm compatíveis com Angular 22, Docker Compose e host x86-64) e fixar versões exatas de SDK, Angular, EF Core/provider SQL Server, MSTest/Vitest/Playwright e imagem SQL em manifests/lockfiles; verificar com `dotnet --info`, `node --version`, `npm --version`, `docker compose version` e uma restauração inicial.
- [x] 1.2 Criar `app/backend/Estacionamento.slnx` com os projetos Api, Application, Domain, Infrastructure, UnitTests e IntegrationTests, respeitando as dependências de camadas; verificar com `dotnet build app/backend/Estacionamento.slnx` e inspeção das referências de projeto.
- [x] 1.3 Criar a SPA `app/frontend` com Angular 22 standalone/Zoneless, Signals, Signal Forms, Tailwind CSS e Lucide, sem Angular Material, e configurar Vitest e Playwright; verificar com `npm ci`, `npm run build` e `npm test -- --run`.
- [x] 1.4 Documentar em `README.md` os comandos de desenvolvimento, pré-requisitos, origens HTTPS locais e a proibição de segredos em arquivos versionados; verificar executando os comandos documentados em uma instalação limpa.

## 2. Compose de infraestrutura e preparação do SQL

- [x] 2.1 Criar `compose.yml` na raiz com SQL Server Developer em imagem fixada, healthcheck, volume nomeado, rede local e porta publicada somente em `127.0.0.1`; verificar com `docker compose config --quiet` e `docker compose up -d`, aguardando o healthcheck ficar saudável.
- [x] 2.2 Implementar preparação idempotente do login dedicado da API e das permissões necessárias para a migration criar o catálogo, recebendo `MSSQL_SA_PASSWORD` de fonte externa ao arquivo Compose; verificar catálogo ausente, início, reinício e conexão da API sem usar a conta `sa`, confirmando que nenhum esquema de domínio foi criado por SQL paralelo.
- [x] 2.3 Validar preservação e isolamento da infraestrutura: parar e reiniciar sem remover o volume, confirmar persistência, subir uma segunda instância de teste com portas/volumes próprios e verificar ausência de senha literal em YAML, `.env` versionado, argumentos e logs.
- [x] 2.4 Documentar os comandos de iniciar, consultar saúde e parar a infraestrutura preservando volumes; verificar seguindo a documentação e confirmando que a parada normal não remove dados.

## 3. Configuração tipada, segredos e mapas de chaves

- [x] 3.1 Criar opções tipadas para configuração regional, JWT, proteção de dados, CORS, operação e contas sintéticas, com validação no startup e mensagens sem valores sensíveis; verificar com testes unitários de configuração válida e inválida.
- [x] 3.2 Criar `appsettings.json` e `appsettings.Development.json` apenas com parâmetros não sensíveis, sem `ContasSinteticas`, connection string com senha, chaves JWT/AES ou credenciais; verificar com busca textual no repositório e testes que rejeitam configuração incompleta.
- [x] 3.3 Implementar validação dos mapas independentes `Autenticacao:Jwt:Chaves:<versao>` e `ProtecaoDados:Chaves:<versao>`, exigindo Base64 de exatamente 32 bytes, versão ativa existente e nenhuma reutilização entre finalidades; verificar com testes de sucesso, Base64 inválido, tamanho inválido, versão ausente e leitura de versão anterior.
- [x] 3.4 Configurar o carregamento de User Secrets somente em `Development` e a precedência de configuração por ambiente; verificar com testes de precedência, ausência de User Secrets em `Testing` e falha fechada quando um segredo obrigatório falta.
- [x] 3.5 Documentar a estrutura externa de User Secrets com placeholders, incluindo conexão, três contas e chaves JWT/AES, sem valores reutilizáveis; verificar que exemplos do README não contêm segredos e que a documentação corresponde aos nomes das opções.

## 4. Identidade, sessão e migration inicial

- [x] 4.1 Implementar `UsuarioAplicacao` com estado ativo, versão de acesso e nome, configuração do Identity com senha mínima de 8, bloqueio de 15 minutos após 5 falhas e e-mail único; verificar com testes de opções e criação de usuário.
- [x] 4.2 Criar entidades e mapeamentos de sessão, refresh e auditoria de autenticação com FKs restritas, índices, unicidade do hash de refresh e `rowversion` onde houver mutação concorrente; verificar com testes de restrição e concorrência no SQL Server real.
- [x] 4.3 Gerar a migration inicial contendo apenas Identity, sessão, refresh e auditoria; verificar aplicação em catálogo vazio de teste e ausência de tabelas de vagas, atendimento, tarifa ou financeiro.
- [x] 4.4 Criar fixture de integração que valide o destino antes de aplicar migrations, use banco/catálogo isolado e não carregue User Secrets pessoais; verificar executando `dotnet test` em Testing e confirmando que o banco de desenvolvimento não é tocado.
- [x] 4.5 Implementar auditoria append-only de login, logout, bloqueio e revogação com ator opaco, UTC e correlação, sem senha, token ou PII; verificar com testes que leem os eventos e inspecionam ausência de campos sensíveis.

## 5. Bootstrap exclusivo de Development

- [x] 5.1 Validar previamente as três contas sintéticas (presença de e-mail e senha, formato aceito e unicidade após normalização), antes de qualquer acesso ao banco; verificar com testes de conta ausente, e-mail inválido, e-mail duplicado e configuração completa sem log de valores.
- [x] 5.2 Implementar provisionamento idempotente por e-mail normalizado que cria apenas contas ausentes, garante os perfis fixos e nunca altera e-mail, senha, perfis ou estado de conta existente; verificar com teste de primeira execução, reinício e conta previamente desabilitada.
- [x] 5.3 Orquestrar o startup de `Development` na ordem validação → conexão → `Database.MigrateAsync()` → provisionamento imediato → início do tráfego, sem etapa intermediária; verificar com testes de catálogo ausente criado pela migration, catálogo vazio, esquema atualizado, migration nova pendente e ordem registrada dos eventos.
- [x] 5.4 Garantir falha fechada: erro de configuração, conexão, migration ou provisionamento impede a API de aceitar requisições e não expõe segredos; verificar com testes de falha em cada etapa e ausência de tráfego HTTP.
- [x] 5.5 Proibir migrations e seed automáticos em `Testing` e `Production`, inclusive com flags de configuração; verificar iniciando a API nesses ambientes e comprovando que nenhuma tabela ou conta é criada pelo bootstrap.
- [x] 5.6 Documentar o fluxo de inicialização, a ordem obrigatória, os comandos de diagnóstico e a recuperação de falhas sem apagar volumes; verificar seguindo o procedimento em um catálogo vazio e em um reinício.

## 6. Autenticação, JWT e sessão revogável

- [x] 6.1 Implementar emissão e validação de JWT HS256 com chave de 32 bytes por versão, `kid`, emissor, audiência, 15 minutos, tolerância de 30 segundos e claims mínimos sem PII; verificar com testes de token válido, `none`, algoritmo alternativo, assinatura inválida, emissor/audiência incorretos, `kid` desconhecido e expiração.
- [x] 6.2 Criar sessão persistida com versão de acesso, expiração absoluta e revogação, verificada em cada requisição protegida; verificar com testes de logout, conta desativada, senha/perfis alterados, JWT anterior recusado e SQL indisponível falhando fechado.
- [x] 6.3 Implementar login com resposta genérica, `429` após 5 requisições por minuto por IP, bloqueio de 15 minutos após 5 falhas e `no-store`; verificar com testes de credenciais válidas/inválidas, conta inexistente versus bloqueada, excesso por IP e ausência de senha/token em logs.
- [x] 6.4 Implementar refresh opaco rotativo com hash no SQL, consumo atômico, família revogada em reutilização e limite absoluto de 8 horas sem extensão deslizante; verificar com testes de renovação, reuso, expiração absoluta, concorrência e refresh enviado somente no corpo.
- [x] 6.5 Implementar logout que revoga sessão e refreshes antes de responder, com auditoria; verificar com teste de revogação seguido de requisição recusada e evento de auditoria registrado.
- [x] 6.6 Documentar os contratos de sessão, os códigos de erro, as claims, a política de bloqueio e o procedimento de rotação de chave JWT; verificar que o README e o comportamento HTTP correspondem ao documentado.

## 7. CORS, erros e contratos HTTP

- [x] 7.1 Configurar política CORS com origens exatas, métodos e headers necessários, preflight `OPTIONS`, sem `AllowAnyOrigin` e sem `AllowCredentials`; verificar com testes de origem permitida, origem negada, preflight e chamada autenticada entre duas origens locais.
- [x] 7.2 Implementar Problem Details com `status`, `title`, `type`, `instance`, `code` e `correlationId`, sem stack trace ou PII, e `Cache-Control: no-store` em respostas, erros e negações; verificar com testes de contrato e inspeção de logs.
- [x] 7.3 Restringir OpenAPI, quando habilitado, a `Development` e a acesso administrativo, sem Swagger UI público; verificar acessando em `Development`, `Testing` e `Production` e conferindo a ausência de exposição pública.

## 8. Shell Angular, sessão em memória e acessibilidade

- [x] 8.1 Implementar o serviço de sessão com JWT, refresh e dados do funcionário apenas em memória, sem storage/cookie, e limpar tudo no logout; verificar com testes Vitest e inspeção do navegador após login, recarga e logout.
- [x] 8.2 Implementar interceptor que anexa bearer somente à URL-base configurada da API, sem `withCredentials`, e guards de sessão/perfil para `/login`, `/dashboard` e `/forbidden`; verificar com testes de escopo do interceptor, rota sem sessão e rota com perfil insuficiente.
- [x] 8.3 Implementar login com Signal Forms, labels associados, resumo focado, `aria-invalid`, mensagens textuais, uso por teclado e nenhuma chamada à API com dados inválidos; verificar com testes Vitest de foco/mensagens e revisão manual de teclado registrada.
- [x] 8.4 Implementar renovação single-flight no `401`, tratamento de `403` sem logout e logout que revoga no servidor antes de limpar a memória, informando falha de revogação remota; verificar com testes de expiração, revogação, concorrência de refresh, acesso negado e indisponibilidade no logout.
- [x] 8.5 Configurar Playwright com SPA e API em origens HTTPS distintas e credenciais sintéticas explícitas; verificar jornadas de login, acesso negado, logout, expiração/revogação e ausência de tokens em storage e cookie.

## 9. Verificação integrada e evidências da fase

- [x] 9.1 Executar a matriz integrada de bootstrap com catálogo ausente, catálogo vazio, esquema atualizado, migration nova, falha de migration, falha de seed e SQL indisponível; registrar comandos, resultados e limitações.
- [x] 9.2 Executar a matriz de segurança e acesso com JWT válido/inválido, revogação, refresh/reuso, bloqueio de login, rate limit, CORS entre origens e autorização por perfil; registrar resultados sem dados sensíveis.
- [x] 9.3 Executar `dotnet test`, `npm test -- --run` e `npx playwright test` completos e registrar versões, ambiente, duração e falhas conhecidas.
- [x] 9.4 Atualizar o checklist F1-01–F1-05 do TDD §8 somente para itens efetivamente concluídos, com evidências reais e limitações; verificar que nenhum item sem teste executado foi marcado.
- [x] 9.5 Revisar os artefatos OpenSpec contra a implementação realizada e atualizar specs/design/tasks se alguma decisão posterior alterar o comportamento planejado; verificar com `openspec validate "fase-1-fundacao-infraestrutura-local" --strict`.

## Workflow follow-up

- Abrir Pull Request da feature branch para `main` com o template de `.github/pull_request_template.md`, rastreabilidade ao PRD/TDD e evidências de validação.
- Arquivar a mudança OpenSpec somente após revisão, aprovação e merge do PR.
