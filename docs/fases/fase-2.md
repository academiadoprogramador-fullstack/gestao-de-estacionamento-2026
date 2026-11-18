# Fase 2 — Preparação e entrada

**Status:** implementação e verificações locais concluídas para o recorte da Fase 2.

**Data da execução:** 18/11/2026

**Branch:** `feat/fase-2-preparacao-entrada`

**Rastreabilidade:** PRD §5.1–5.3, RF-01–03, parte de RF-07/10/11 e RNF-01–05/07; TDD §4.1, §5.1–5.2, §6.2–6.4, §7.1 e F2-01–F2-06. Especificações em `openspec/changes/archive/2026-11-18-fase-2-preparacao-entrada/specs`; protótipos em `openspec/changes/archive/2026-11-18-fase-2-preparacao-entrada/prototypes`.

## 1. Escopo entregue

- Administração de funcionários sem autorregistro, com perfis fixos, token de concorrência próprio, proteção do último administrador ativo, incremento de versão de acesso e revogação de sessões/refreshes na mesma transação.
- Cadastro administrativo de vagas com código normalizado único e consulta paginada pelos três perfis, com situação derivada da ocupação aberta.
- Polling de vagas de 30 s configurável, pausa em aba oculta, atualização no foco e após comando, cancelamento e trava contra sobreposição.
- Tarifa versionada imutável em `decimal(18,2)`, configuração singleton sem valor padrão e publicação concorrente por versão-base.
- Check-in transacional com identificador público `AT-{anoUTC}-{sequência:D6}`, snapshot de tarifa, vistoria cifrada, ocupação exclusiva, eventos append-only, auditoria e resultado idempotente.
- Busca exata por placa ou identificador, detalhe e histórico projetados por perfil, com omissão de documento/vistoria das listagens e de finanças do DTO do Manobrista.
- Leitura explícita e auditada de documento (Caixa/Administrador) e vistoria (três perfis), com auditoria confirmada antes da decifragem.
- Cifragem AES-256-GCM com envelope versionado, AAD reconstruído no servidor, propósitos distintos, rotação sem recifragem e falha fechada.
- Idempotência por `Idempotency-Key` UUID, SHA-256 de conteúdo canônico versionado, replay autorizado e conflito por conteúdo divergente.
- Telas Angular de funcionários, vagas, tarifas, check-in e atendimentos com Signal Forms, resumo de erros focado, `aria-invalid`, foco em diálogo e descarte de respostas tardias.
- Coordenador de comando em memória com estados `ocioso`, `pendente`, `incerto`, `sucesso` e `rejeitado`, bloqueio de rota/logout e aviso `beforeunload`.

## 2. Contratos HTTP implementados

Prefixo `/api/v1`. Todas as respostas da API são `no-store`.

| Rota | Perfis | Resumo |
| --- | --- | --- |
| `GET /employees` | Administrador | Listagem paginada e filtro por status, sem credenciais. |
| `POST /employees` | Administrador | Criação com e-mail normalizado, senha inicial e perfis fixos. |
| `PUT /employees/{id}/access` | Administrador | Estado, perfis e token de concorrência; revoga sessões. |
| `GET /spaces` | Três perfis | Consulta paginada por código/situação, com situação derivada. |
| `POST /spaces` | Administrador | Cadastro de vaga com código normalizado único. |
| `GET /daily-rates` | Administrador | Histórico paginado de versões. |
| `GET /daily-rates/current` | Administrador | Versão corrente e versão-base, inclusive ausência de tarifa. |
| `POST /daily-rates` | Administrador | Publicação imutável com `expectedBaseVersion`. |
| `POST /attendances` | Caixa/Administrador | Check-in com `Idempotency-Key`; `201` com `Location` e resultado mínimo. |
| `GET /attendances?plate=&identifier=` | Três perfis | Exatamente um filtro, busca exata e paginada. |
| `GET /attendances/{id}` | Três perfis | Detalhe projetado; sem finanças no DTO do Manobrista. |
| `GET /attendances/{id}/history` | Três perfis | Histórico filtrado por allowlist de perfil. |
| `GET /attendances/{id}/document` | Caixa/Administrador | Leitura explícita e auditada. |
| `GET /attendances/{id}/entry-inspection` | Três perfis | Leitura explícita e auditada. |

Códigos estáveis de erro: `stale_access_version`, `last_active_administrator`, `space_code_conflict`, `space_unavailable`, `stale_rate_version`, `rate_not_published`, `idempotency_conflict`, `field_protection_failure`, além dos códigos de validação e sessão já existentes. As respostas seguem `application/problem+json` com `code` e `correlationId`.

## 3. Banco e segurança

- Migration aditiva `20261118174120_Fase2PreparacaoEntrada`: cria `Vagas`, `TarifasDiarias`, `ConfiguracaoTarifa`, `Atendimentos`, `VistoriasEntrada`, `Ocupacoes`, `EventosAtendimento`, `ResultadosIdempotentes`, `EventosAuditoriaNegocio`, a `SEQUENCE SequenciaIdentificadorAtendimento` e a coluna `RowVersion` em `AspNetUsers`.
- Verificação do script entre a migration inicial e a Fase 2: nenhum `DROP`/`ALTER` destrutivo; apenas `CREATE TABLE`, `CREATE INDEX`, `CREATE SEQUENCE`, `ADD [RowVersion]`, FKs e checks.
- FKs `NO ACTION`; índices únicos filtrados de ocupação aberta por vaga e por atendimento; unicidade de identificador público quando preenchido; `(AtorId, Operacao, Chave)` único em resultados idempotentes.
- Envelope de campo: `[versão:1][tamanhoKid:1][kid][nonce:12][tag:16][ciphertext]`; AAD `v1|kid:{kid}|ambiente:{ambiente}|app:Estacionamento|proposito:{propósito}|atendimento:{guid}`.
- Nenhum segredo novo versionado; as chaves AES da Fase 1 passam a cifrar conteúdo real. A versão ativa continua obrigatória na inicialização.

## 4. Verificações executadas

Ambiente: Linux x86-64, .NET SDK 10.0.114, Node 26.13.1, npm 12.2.2, SQL Server 2022 CU28 em container local, Chromium do Playwright 1.65.1. Catálogos de teste e E2E isolados; nenhum banco operacional foi usado.

| Verificação | Comando | Resultado |
| --- | --- | --- |
| Build da solução | `dotnet build app/backend/Estacionamento.slnx` | Sucesso, 0 erros e 0 avisos. |
| Testes unitários MSTest | `dotnet test app/backend/tests/Estacionamento.UnitTests` | 53 aprovados. |
| Testes de integração MSTest com SQL Server real | `ESTACIONAMENTO_TESTE_CONEXAO=... dotnet test app/backend/tests/Estacionamento.IntegrationTests` | 61 aprovados. |
| SPA (Vitest) | `npm test --prefix app/frontend -- --run` | 43 aprovados em 12 arquivos. |
| E2E Playwright em duas origens HTTPS | `npx playwright test` em `app/frontend` com `E2E_*` | 13 aprovados (8 da Fase 1 + 5 da Fase 2). |
| Validação OpenSpec | `openspec validate --all --strict` | 4 itens aprovados, 0 falhas. |
| Script de migration | `dotnet ef migrations script 20261117145708_MigracaoInicial 20261118174120_Fase2PreparacaoEntrada` | Somente DDL aditivo. |

Cenários de integração cobertos: criação/listagem/alteração de acesso, token antigo e concorrência, último administrador ativo, concorrência entre administradores, código de vaga duplicado, paginação inválida, situação derivada, publicação concorrente de tarifa, check-in completo, replay idêntico/divergente, ausência de tarifa, vaga inexistente/ocupada, disputa concorrente da mesma vaga, identificadores únicos, busca por placa normalizada, duas visitas do mesmo veículo, projeções por perfil, leitura auditada, falha de decifragem, rotação de chave AES, índices filtrados, migration aditiva sobre dados da Fase 1, Problem Details e OpenAPI.

Cenários de UI/E2E cobertos: cadastro administrativo de vaga e tarifa, consulta pelos três perfis, bloqueio administrativo, criação/alteração de funcionário com revogação na requisição seguinte, check-in com perda de resposta e replay da mesma chave, conflito de vaga, limpeza de documento/vistoria, busca por placa e identificador, projeções por perfil, leituras auditadas, descarte de resposta fora de ordem e fluxo por teclado com foco no diálogo.

## 5. Acessibilidade

- Verificações automatizadas: rótulos associados, `aria-invalid`, `aria-describedby`, resumo de erros com foco, bloqueio de chamada com dados inválidos, foco no título do diálogo de alteração de acesso e retorno ao disparador.
- Revisão manual registrada: Chromium do Playwright 1.65.1, desktop Linux x86-64, jornada por teclado nas telas de vagas, funcionários, check-in e atendimentos.
- **Limitação:** não houve revisão com leitor de tela ou outra tecnologia assistiva nesta execução. Nenhuma conformidade WCAG 2.2 AA é declarada; a avaliação manual com tecnologia assistiva permanece necessária antes de publicação.

## 6. Auditoria de PII e segredos

- `git grep`/`rg` no repositório e nos artefatos gerados não encontrou a senha do SQL local, chaves reais, tokens JWT, chaves privadas ou documentos de titulares.
- Connection strings com `Password=design`/`Password=Invalida1` existem somente em fábrica de design-time e testes, como placeholders sem acesso a banco real.
- Logs da aplicação registram apenas correlação, código de erro e tipo de falha; não incluem documento, vistoria, placa, titular, valores ou chaves.
- A auditoria de negócio guarda tipo, ator, alvo e UTC, nunca o conteúdo sensível. O teste de leitura auditada verifica que o documento não aparece no detalhe do evento.
- `test-results/`, `playwright-report/` e `.env` estão ignorados pelo Git.

## 7. Limitações e pendências

- Retenção/descarte de 365 dias para idempotência e 90 dias para conteúdo pessoal não foi implementada nesta fase; o registro idempotente já nasce com os campos que a fase futura usará.
- Não há endpoint nem tela de retirada, cobrança, pagamento, entrega, relatórios ou fotos; esses itens pertencem às Fases 3–6.
- O OpenAPI é exposto apenas em `Development` e exige Administrador; não há Swagger UI pública.
- A validação de acessibilidade com leitor de tela e a avaliação de carga p95 não foram executadas; o perfil acadêmico e as metas continuam como critérios a medir.
- O E2E usa certificados locais não confiáveis pelo navegador (`ignoreHTTPSErrors`) exclusivamente no ambiente de teste; a validação TLS entre SPA e API permanece ativa no produto.
- Uma execução intermediária da suíte de integração apresentou uma falha transitória não reproduzida nas duas execuções completas seguintes; não foi identificado defeito funcional. Se reaparecer, deve ser investigada antes da publicação.
