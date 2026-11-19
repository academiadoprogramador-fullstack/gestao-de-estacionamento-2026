# Fase 3 — retirada e caixa

**Status:** implementação e verificações locais concluídas para o recorte da Fase 3.

**Data da execução:** 19/11/2026

**Branch:** `feat/fase-3-retirada-caixa`

**Rastreabilidade:** PRD §5.4 e §10; RF-04–08/10/11 e RNF-01–05/07; TDD §4.2 (D3-T01–D3-T08), §5.3, §6.2–6.3 e §7.1; itens F3-01–F3-06 do roadmap. Especificações em `openspec/changes/fase-3-retirada-caixa/specs`; protótipos em `openspec/changes/fase-3-retirada-caixa/prototypes`.

## 1. Escopo entregue

- Lista paginada de atendimentos em aberto na tela de pedido e na tela de atendimentos, com identificação, veículo, vaga, entrada e situação da retirada, permitindo selecionar o veículo sem conhecer o identificador; a busca exata por placa/identificador permanece como filtro opcional, com “Limpar busca” para voltar à lista.
- Pedido de retirada único por atendimento ativo, com declaração obrigatória de presença do titular, conferência aprovada/divergente, recusa de terceiro e fixação imutável de duração, diárias, snapshot de tarifa e total.
- Fila paginada com estados **aguardando** e **em busca**, início de busca pelos três perfis, responsável e UTC do servidor, conflito por versão e polling de 30 s configurável.
- Razão financeira append-only com `rowversion` e revisão, composição de cobrado, pago líquido e saldo; acerto integral em dinheiro, cartão ou Pix.
- Desconto administrativo limitado ao total original, permitido somente sem pagamento líquido, com justificativa cifrada.
- Estorno integral e único por pagamento, vinculado ao lançamento original, com justificativa cifrada e novo acerto posterior.
- Autorizações documental e financeira independentes, com concessão, substituição, revogação e vigência derivada da conferência ou da revisão da razão; sem consumo nesta fase.
- Leitura explícita e auditada de justificativas, restrita ao Administrador e confirmada antes da decifragem.
- Extensão do detalhe e do histórico do atendimento, com projeções por perfil: o Manobrista não recebe finanças, conferência, autorizações ou justificativas.
- Telas `/withdrawals`, `/withdrawals/new` (com lista de atendimentos em aberto) e `/cashier/:attendanceId`, reutilizando sessão, guards, coordenador de comando, polling e componentes acessíveis.
- Diálogo de confirmação persistente após pagamento, desconto, estorno e autorizações, com resumo da operação, saldo/revisão quando aplicável e ação explícita de concluir.
- Validação de formulário limpa após sucesso em check-in, vagas, tarifas, funcionários, pedido de retirada e caixa; o resumo de erros e o estado “touched” não reaparecem enquanto a tela aguarda a próxima operação.
- Migração aditiva `20261119160017_Fase3RetiradaCaixa`, sem reescrever dados das Fases 1–2.

**Fora do escopo:** entrega, vistoria de saída, transição para **entregue**, liberação de vaga, consumo de autorização, relatórios, fechamento de caixa, integração financeira, fotos, ticket, CI, Azure, IaC e CD.

## 2. Contratos HTTP implementados

Prefixo `/api/v1`; respostas `no-store`; erros em Problem Details com `code` e `correlationId`.

| Rota | Perfis | Resumo |
| --- | --- | --- |
| `GET /attendances?status=aberto&page=&pageSize=` | Três perfis | Atendimentos ainda não entregues, com situação da retirada e projeção sem dados sensíveis. |
| `POST /attendances/{id}/withdrawal` | Caixa/Administrador | Pedido com presença e conferência; `Idempotency-Key`; `201` com resultado mínimo. |
| `GET /withdrawals?state=&page=&pageSize=` | Três perfis | Fila paginada, projeção sem finanças ao Manobrista. |
| `GET /withdrawals/{id}` | Três perfis | Detalhe operacional; finanças e conferência apenas a Caixa/Administrador. |
| `POST /withdrawals/{id}/search` | Três perfis | Início de busca com versão esperada; `200`/`409`. |
| `GET /withdrawals/{id}/cashier` | Caixa/Administrador | Cobrança, lançamentos, saldo, revisão e autorizações. |
| `POST /withdrawals/{id}/payments` | Caixa/Administrador | Acerto integral com revisão esperada e `Idempotency-Key`. |
| `POST /withdrawals/{id}/discounts` | Administrador | Desconto com justificativa, revisão esperada e `Idempotency-Key`. |
| `POST /withdrawals/{id}/refunds` | Administrador | Estorno integral vinculado ao pagamento, com justificativa. |
| `POST /withdrawals/{id}/authorizations` | Administrador | Concessão documental/financeira com versão esperada. |
| `POST /withdrawals/{id}/authorizations/{authId}/revoke` | Administrador | Revogação com justificativa e unicidade. |
| `GET /withdrawals/{id}/authorizations/{authId}/justification` | Administrador | Leitura auditada antes de decifrar. |
| `GET /withdrawals/{id}/launches/{launchId}/justification` | Administrador | Leitura auditada antes de decifrar. |

Não existem rotas de entrega, vistoria de saída, consumo de autorização ou liberação de vaga; a ausência é verificada por contrato.

Códigos estáveis acrescentados: `withdrawal_requires_holder`, `withdrawal_invalid_conference`, `withdrawal_invalid_attendance`, `attendance_not_active`, `withdrawal_already_exists`, `withdrawal_not_found`, `withdrawal_not_waiting`, `stale_withdrawal_version`, `ledger_revision_stale`, `payment_must_equal_balance`, `balance_already_settled`, `payment_invalid_method`, `discount_requires_no_payment`, `discount_exceeds_balance`, `discount_requires_justification`, `refund_must_be_full`, `payment_already_refunded`, `payment_not_found`, `authorization_requires_justification`, `authorization_invalid_type`, `authorization_already_revoked`, `authorization_justification_denied`, `revocation_requires_justification`, `launch_not_found`, `launch_without_justification` e `ledger_not_found`.

## 3. Banco e segurança

- Migration aditiva cria `PedidosRetirada`, `Cobrancas`, `RazoesAtendimento`, `LancamentosFinanceiros`, `ConcessoesAutorizacao`, `RevogacoesAutorizacao` e acrescenta `ResultadoJson`/`CodigoResposta` a `ResultadosIdempotentes`.
- Script entre `20261118174120_Fase2PreparacaoEntrada` e `20261119160017_Fase3RetiradaCaixa`: 6 tabelas, 14 índices e 2 colunas, sem `DROP`, `ALTER COLUMN` ou reescrita de dados.
- FKs `NO ACTION`; unicidade de pedido por atendimento, cobrança por pedido, razão por atendimento, revogação por concessão e estorno filtrado por pagamento (`WHERE Tipo = 'estorno'`).
- `PedidoRetirada` e `RazaoAtendimento` evoluem sob versão; cobrança, lançamentos, concessões e revogações são protegidos contra `UPDATE`/`DELETE` pela aplicação.
- AAD de novos campos inclui o identificador opaco do registro (`|registro:{guid}`), impedindo troca de justificativas entre registros do mesmo atendimento; envelopes da Fase 2 continuam legíveis.
- Nenhum segredo novo é necessário: as chaves AES existentes recebem os propósitos de justificativa e o SHA-256 versionado cobre as novas operações.

## 4. Verificações executadas

Ambiente: Linux x86-64, .NET SDK 10.0.114, Node 26.13.1, npm 12.2.2, SQL Server 2022 CU28 em container local, Chromium do Playwright 1.65.1. Catálogos de teste e E2E isolados.

| Verificação | Comando | Resultado |
| --- | --- | --- |
| Build da solução | `dotnet build app/backend/Estacionamento.slnx` | Sucesso, 0 erros; 1 aviso pré-existente MSTEST0044 em teste da Fase 2. |
| Testes unitários MSTest | `dotnet test app/backend/tests/Estacionamento.UnitTests` | 61 aprovados. |
| Testes de integração MSTest com SQL Server real | `ESTACIONAMENTO_TESTE_CONEXAO=... dotnet test app/backend/tests/Estacionamento.IntegrationTests` | 85 aprovados. |
| SPA (Vitest) | `npm test --prefix app/frontend -- --run` | 70 aprovados em 17 arquivos. |
| E2E Playwright em duas origens HTTPS | `npx playwright test` em `app/frontend` com `E2E_*` | 17 aprovados (8 da Fase 1 + 5 da Fase 2 + 4 da Fase 3). |
| Protótipos (Chromium local) | Abertura de `atendimentos.html`, `fila-retiradas.html`, `pedido-retirada.html` e `caixa.html` | Lista padrão, busca, seleção, paginação e confirmação persistentes sem erros de script. |
| Validação OpenSpec | `openspec validate --all --strict` | Aprovado. |
| Script de migration | `dotnet ef migrations script 20261118174120_Fase2PreparacaoEntrada 20261119160017_Fase3RetiradaCaixa` | Somente DDL aditivo. |

Cenários de integração cobertos: lista paginada de atendimentos em aberto com situação da retirada e filtro exclusivo; pedido válido, dois pedidos concorrentes, cálculo em 0/24 h/24 h + 1 tick/48 h/49 h, snapshot após nova tarifa, atendimento inelegível, segundo pedido, replay idêntico/divergente, recusa de terceiro e de titular ausente, propriedades inesperadas, `403` por perfil, conflito de busca concorrente, fila paginada e projeções por perfil, ausência de rota/efeito de entrega, acerto integral/parcial/excedente/zerado, desconto válido/até zero/limite/saldo pago, estorno integral/parcial/duplicado, novo acerto após estorno e após fixture de entrega, concorrência de pagamentos e desconto × pagamento, justificativa cifrada com vínculo ao registro, leitura auditada antes da decifragem, falha de decifragem, rollback por falha de auditoria, replay após sessão ausente, autorização vigente/revogada/substituída/obsoleta, concessão que não altera saldo/estado, migration aditiva e proteção append-only.

Cenários de UI/E2E cobertos: lista padrão de atendimentos em aberto na tela de atendimentos, busca opcional e “Limpar busca”; seleção pela lista de atendimentos em aberto, atendimento com pedido existente e busca opcional; confirmação persistente de pagamento e estorno; pedido aprovado e divergente, segundo pedido, titular ausente, acesso negado ao Manobrista, fila sem finanças para o Manobrista, replay do pedido e do pagamento após perda de resposta, acerto integral, desconto até zero, estorno com novo acerto, autorização revogada e obsoleta após pagamento, ausência de ação/rota de entrega e de liberação de vaga.

## 5. Acessibilidade

- Verificações automatizadas: rótulos associados, `fieldset`/`legend` para conferência e forma, `aria-invalid`, `aria-describedby`, resumo de erros com `tabindex="-1"` e foco, foco em diálogo de documento/justificativa/confirmação, estados textuais e navegação por teclado.
- Revisão manual registrada: Chromium do Playwright 1.65.1, desktop Linux x86-64, jornadas das novas telas por teclado.
- **Limitação:** não houve revisão com leitor de tela ou outra tecnologia assistiva nesta execução. Nenhuma conformidade WCAG 2.2 AA é declarada; a avaliação manual com tecnologia assistiva permanece necessária antes de publicação.

## 6. Auditoria de PII e segredos

- Respostas, logs e trilha não contêm documento, vistoria, justificativa, senha ou token. A auditoria guarda ator, alvo, tipo e UTC.
- Listagens, fila e detalhes comuns não incluem justificativas; o texto só é devolvido por endpoint explícito do Administrador, após auditoria.
- Documento e vistoria continuam exigindo leitura explícita e auditada; o pedido de retirada não aceita foto ou novo documento.
- O Manobrista não recebe saldo, valores, formas, conferência, autorizações ou justificativas, e não existe rota de entrega/consumo nesta fase.

## 7. Limitações e divergências intencionais dos protótipos

- O seletor de perfil, os seletores de cenário, a simulação de rede/aba oculta e a “próxima resposta” existem apenas nos protótipos; a implementação usa sessão real, HTTP e o coordenador de comando.
- O seletor “Atendimento localizado” do protótipo foi substituído por uma lista real e paginada de atendimentos em aberto; a busca exata por placa/identificador permanece como filtro opcional, reutilizando o endpoint de consulta.
- O protótipo `prototypes/atendimentos.html` representa a lista padrão da tela de atendimentos, com busca exata opcional, detalhe por perfil e leituras auditadas.
- A linha “Idempotency-Key” exibe que a chave é gerada em memória, sem revelar o UUID; o coordenador mantém a chave e o conteúdo apenas até sucesso/rejeição.
- O protótipo permite “Abrir caixa” sem atendimento; na implementação, o caixa é aberto pela fila ou pela consulta de atendimentos, pois a rota depende do atendimento.
- A fixture “entregue com saldo” do caixa é criada apenas por SQL de teste, sem rota de entrega.
- A conferência divergente bloqueia a entrega normal apenas como aviso nesta fase; a validação/consumo pertence à Fase 4.
- Não há revisão manual com tecnologia assistiva, publicação, CI, Azure ou operação bancária.

## 8. Roadmap

Itens F3-01–F3-06 foram marcados no roadmap de `docs/tdd.md` com as evidências desta execução.
