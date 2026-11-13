# Evidências locais — preparação de entrada (Fase 2)

**Data:** 13/11/2026
**Branch:** `feat/fase-2-preparacao-entrada`
**Escopo da evidência:** implementação e verificação local da change OpenSpec `fase-2-preparacao-entrada`. Isto não é aceite operacional, homologação, autorização de produção nem evidência de implantação Azure.

**Aplicação das correções V2-01–07 (13/11/2026):** regressões e correções implementadas; suítes repetidas após os ajustes. A revisão final concluiu 10.5 e consolidou as mudanças em commits locais na feature branch. Não houve push/PR. Resultado não é aceite operacional, validação normativa de acessibilidade ou autorização de publicação. Detalhes/riscos residuais no [TDD §7.1](tdd.md#71-revisão-de-coerência-e-lacunas-da-implementação--13112026).

## Resultado do gate local

| Verificação | Comando/ambiente | Resultado observado |
| --- | --- | --- |
| Restore travado | `cd app/backend && dotnet restore Estacionamento.slnx --locked-mode` | Passou em 13/11/2026. |
| Build backend + OpenAPI | `cd app/backend && dotnet build Estacionamento.slnx --no-restore --tl:off` | Passou, 0 avisos e 0 erros; documento gerado em `src/Estacionamento.Api/obj/Estacionamento.Api.json`. |
| Contrato OpenAPI de erros | `dotnet test tests/Estacionamento.IntegrationTests/Estacionamento.IntegrationTests.csproj --no-build --filter FullyQualifiedName~OpenApiErrorContractTests --tl:off` após o build | Passou: verifica `application/problem+json`, schema/extensions opcionais e preservação de media types de sessão no JSON gerado. |
| Formatação backend | `cd app/backend && dotnet format whitespace Estacionamento.slnx --verify-no-changes --no-restore` | Passou, sem diferenças. |
| Testes backend | `dotnet test Estacionamento.slnx --no-build --tl:off` com SQL Server local e senhas sintéticas no ambiente | Execução completa após a correção OpenAPI: Unit 12/12, Integration 56/56, Architecture 4/4 (72/72), em bases de teste dedicadas. |
| Testes frontend | `cd app/frontend && npm test` | Passou após as correções: 44/44 em 6 arquivos Vitest, incluindo respostas assíncronas fora de ordem, check-in pendente/replay, resumo de validação e paginação. |
| Build frontend | `cd app/frontend && npm run build` | Passou em modo de produção. |
| Formatação frontend/E2E | `npx prettier --check` nos arquivos TypeScript/CSS alterados | Passou após formatação final. |
| Tipagem E2E | `cd app/frontend/e2e && npx tsc --ignoreConfig --noEmit --target ES2022 --module Node16 --moduleResolution Node16 --esModuleInterop specs/fase2-operational.spec.ts specs/foundation.spec.ts playwright.config.ts` | Passou. |
| Playwright local | `cd app/frontend/e2e && npm test`, `TEST_SQLSERVER_CONNECTION` em base nova `EstacionamentoE2E_review_*` e senhas sintéticas no ambiente | Passou: 4/4 após as correções. API iniciada pelo harness; não reutiliza API ativa. |
| Integridade do diff | `git diff --check` | Passou antes dos commits finais. |

Os segredos foram fornecidos somente por variáveis de ambiente/arquivo local ignorado; nenhum valor de credencial integra este registro. Os testes não usam SQLite/InMemory como substituto do SQL Server.

## Rastreabilidade RF → capability/spec → evidência

| Requisito/limite | Capability/spec delta | Código e evidência principal |
| --- | --- | --- |
| RF-01, RNF-03 — entrada completa, identificador, ator/horário de servidor, dados obrigatórios e sem duplicação | `parking/vehicle-check-in` | `VehicleCheckIn`, `AttendancesController`, `VehicleCheckInTransactionTests`, `AttendanceControllerTests`, `CheckInPage` e seus Vitest; Playwright exercita check-in, timeout com replay da mesma chave e ausência de PII no resultado. |
| RF-02, RNF-03 — cadastro/disponibilidade e ocupação única | `parking/space-availability` | `SpaceAvailability`, `SpacesController`, índice filtrado/`rowversion`, `SpaceControllerTests`; polling de UI coberto por timers, pausa em aba oculta, foco, não sobreposição e teardown. Playwright apresenta lista obsoleta deliberadamente e recebe o conflito real `space_occupied` do servidor. |
| RF-03, RF-10 — busca, visitas distintas e projeção por perfil | `parking/attendance-query` | `AttendanceQuery`, `AttendancesController`, `AttendanceControllerTests`; Vitest cobre busca vazia, paginação, histórico, leituras explícitas e omissão de tarifa/documento para manobrista; Playwright repete busca por identificador nos perfis caixa/manobrista. |
| RF-07 (somente tarifa vigente na entrada), sem regras de retirada/cobrança da Fase 3 | `billing/daily-rate` | `DailyRateAdministration`, `DailyRateControllerTests`; Playwright publica tarifa A, faz entrada, publica B e confere que o detalhe preserva A. Não há preço comercial inicial definido. |
| RF-10, RNF-01/04 — perfis fixos, revogação, CSRF e sessão | `identity/employee-management`, `identity/employee-session` | `EmployeeControllerTests`, `SessionEndpointTests`, guards/interceptor e Vitest; E2E cadastra funcionário, desativa durante sessão ativa e verifica redirecionamento após `401`. Teste E2E observa cookie HttpOnly/Secure, token CSRF em mutações e nenhuma chamada cross-site. |
| RF-11, RNF-01/02 — trilha, proteção de campos e leitura sensível auditada | `platform/operational-audit` | `AuditStoreTests`, `FieldProtectionTests`, `PrivateResponseMiddlewareTests`, `AttendanceControllerTests`; UI só requisita documento/vistoria por ação explícita e mascara leitura de documento para manobrista. |
| Limite de fase | Proposta e `design.md` D2-01–09 | Sem retirada, cálculo de diárias/total, pagamento, entrega/liberação, relatórios, CI, Azure, IaC ou CD. A disponibilidade visual não reserva vaga. |

## Bancos e segurança do teste

- `TestApplicationFactory`, `SyntheticAccountProvisioningTests` e `IdentityMigrationTests` agora validam sem conexão de rede que o catálogo é dedicado `Estacionamento*` e não aparenta ser `Development`, `Local`, `Production`, `Prod` ou `Dev`; casos system DB/sem catálogo são testados e recusados. Fixtures de integração executam nas bases dedicadas e migration histórica recebe catálogo próprio. Helpers que criam DBs aleatórios continuam substituindo catálogo por nome da fixture.
- Na execução final, integração/sessão usaram uma nova base `EstacionamentoReview_*`, migração histórica uma nova base `EstacionamentoReviewMigration_*` e Playwright uma nova base `EstacionamentoE2E_final_*`, todas locais e sintéticas. `master` só foi acessado pelos comandos administrativos das fixtures para criar/remover suas bases; não houve limpeza/alteração de seus resíduos nesta revisão.
- A execução E2E anteriormente registrada usou `EstacionamentoE2E_agent_20261113_1705`; a repetição da revisão usou outra base dedicada. O prefixo é validado antes do startup da API, mas essa verificação lexical não substitui segregação de credenciais/infraestrutura ou revisão da conexão. Não há endpoint de reset na aplicação.
- A barreira de nome de catálogo é apenas defesa contra erro de configuração acidental; não substitui credenciais isoladas, permissões mínimas ou segregação da instância. `master` foi usado somente como catálogo administrativo por helpers de criação/remoção das bases dedicadas; nesta rodada não houve limpeza nem alteração de seus dados-resíduo. Não inferir retrospectivamente segurança de catálogo de execuções antigas sem respectivos valores configurados.
- A execução local não valida retenção/descarte, backup/restauração de SQL e chaves Data Protection, isolamento de rede/site em Azure, desempenho sob carga nem operação real.

## Pendências e limites

- O solicitante confirmou que executou e aprovou o roteiro manual de acessibilidade descrito em [`local-development.md`](local-development.md#roteiro-local-de-acessibilidade-da-ui). Navegador, dispositivo, tecnologia assistiva e observações detalhadas não foram informados; portanto, não se declara conformidade normativa nem compatibilidade além da validação relatada.
- PR, revisão, aprovação e merge não foram realizados.
- Permanecem fora desta change aprovação de privacidade/retencão, operação, implantação e migrações Azure; conferir pendências e gates do PRD/TDD antes de qualquer publicação.

## Correções e evidências de regressão

- **V2-01:** respostas pendentes de detalhes/histórico e leituras de documento/vistoria são descartadas quando a seleção muda, a tela fecha ou é destruída; testes A/B controlam a ordem de resolução e confirmam que conteúdo de A não aparece em B. Busca também ignora respostas antigas.
- **V2-02/03:** guard/logout retornam bloqueio durante POST inicial; `beforeunload` cancela navegação padrão e deixa aviso para confirmação; teste cobre antes de registrar `unknownAttempt`. Callback após destruição não restaura PII. Rejeições de negócio definitivas após replay liberam edição e conservam solicitação/dados sem novo envio automático; 400 de antiforgery sem código permanece resultado conservadoramente incerto.
- **V2-04/05:** testes cobrem mensagens/resumo/foco nas três telas administrativas, impedimento de requisição inválida, navegação das páginas de vaga/tarifa e recuperação por recarga após falha inicial, sem apagar campos do check-in.
- **V2-06:** testes diretos do filtro garantem code/correlation, errors de campo e preservação do contrato de sessão; integração executa sexta tentativa e verifica 429, `application/problem+json`, correlation e ausência de senha.
- **V2-07:** testes sem conectar recusam catálogo system DB, ambiente e catálogo ausente; teste completo usou `EstacionamentoReview_*`, `EstacionamentoReviewMigration_*`, e E2E uma base dedicada nova.
- Build frontend, Vitest 44/44, build backend/OpenAPI, teste do contrato OpenAPI, format, testes backend 72/72 e tipagem/E2E Playwright 4/4 passaram; a correção OpenAPI passou build/format e suíte backend completa. Evidências e limites completos: PRD §5.2–5.3 e TDD §7.1; gates não substituem avaliação manual de acessibilidade ou homologação.
