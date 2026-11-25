# Fase 4 — entrega e liberação de vaga

**Status:** implementação e verificações locais concluídas; validação manual de teclado/foco/mensagens e validação operacional/comercial informadas como aprovadas pelo solicitante em 23/11/2026.

**Data da execução:** 23/11/2026

**Branch:** `feat/fase-4-entrega-liberacao`

**Rastreabilidade:** PRD §5.5, §9 e §10; RF-05/09/10/11 e RNF-01–04/07; TDD §4.3 (D4-T01–D4-T08), §5, §6 e §7.1; itens F4-01–F4-06 do roadmap. Especificações em `openspec/changes/fase-4-entrega-liberacao/specs`; protótipos em `openspec/changes/fase-4-entrega-liberacao/prototypes`.

## 1. Escopo entregue

- Vistoria textual de saída única por atendimento, registrada pelos três perfis com versão-base do atendimento e `Idempotency-Key`; texto de 1 a 4.000 caracteres após trim, cifrado com propósito próprio `exit-inspection` e vínculo ao atendimento e ao registro; imutável e recusada após entrega.
- Leitura explícita e auditada da vistoria de saída para os três perfis, com auditoria confirmada antes da decifragem, resposta `no-store` e falha fechada em envelope/chave inválidos.
- Prontidão informativa da entrega, sem reserva, restrita a Caixa/Administrador, com estado, saldo, conferência, presença da vistoria, ocupação própria, condições textuais, bloqueios e versões opacas.
- Confirmação transacional e explícita da entrega a partir de **aguardando** ou **em busca**, com bloqueios de vistoria, conferência aprovada e saldo zero; divergência e saldo positivo exigem autorizações vigentes independentes.
- Consumo único por entrega e tipo, referenciando a concessão corrente e a revisão utilizada, sem copiar justificativa.
- Gravação atômica, na mesma transação, de entrega, consumos, estados **entregue** no pedido e no atendimento, fim da ocupação no mesmo UTC da entrega, liberação da vaga, eventos, auditoria e recibo idempotente.
- Encerramento condicional da ocupação por ID da ocupação, atendimento, vaga e `FimUtc IS NULL`, exigindo uma linha afetada; vaga alheia ou ocupação ausente bloqueiam sem efeito parcial.
- Replay mínimo após commit, inclusive depois de nova entrada na mesma vaga; mesma chave com conteúdo/versões diferentes conflita; nova chave para pedido entregue é recusada.
- Fila e detalhe com estado **entregue**, identificador da entrega, responsável, horário, vaga histórica e liberação daquela visita, sem inferir disponibilidade corrente.
- Histórico com allowlist por perfil antes da paginação: os três perfis veem vistoria, entrega e liberação; somente Caixa/Administrador veem consumo de autorização.
- Caixa com consumo e estado histórico das autorizações, bloqueio de novas concessões/revogações após entrega e acerto integral posterior sem reabrir atendimento, pedido, ocupação ou vaga.
- SPA com rota `/delivery/:withdrawalId` para os três perfis, vistoria, leitura explícita, prontidão, bloqueios textuais, confirmação explícita, conflito recarregável, tentativa incerta apenas em memória e recarga do estado após sucesso.
- Diagnóstico estruturado da entrega com correlação, ação, resultado, conflito e latência, sem placa, nome, documento, texto, valores, justificativas ou tokens.
- Migração aditiva `20261123160433_Fase4EntregaLiberacao`, sem preencher vistorias, entregas, liberações ou consumos retroativamente.

**Fora do escopo mantido:** fotos, relatórios, fechamento de caixa, integração financeira, emissão fiscal, ticket, comunicação ao cliente, cancelamento/reabertura, nova vistoria após o registro, múltiplas unidades, CI, Azure, IaC e CD. A conclusão local do P0 não equivale a homologação ou publicação.

## 2. Contratos HTTP implementados

Prefixo `/api/v1`; respostas `no-store`; erros em Problem Details com `code` e `correlationId`; `Idempotency-Key` obrigatório nos comandos.

| Rota | Perfis | Resumo |
| --- | --- | --- |
| `POST /attendances/{id}/exit-inspection` | Três perfis | Vistoria de saída imutável; texto e versão-base; `201` com recibo mínimo sem texto. |
| `GET /attendances/{id}/exit-inspection` | Três perfis | Leitura explícita e auditada; `200` com texto, autor e UTC; falha fechada. |
| `GET /withdrawals/{id}/delivery-readiness` | Caixa/Administrador | Estado, saldo, conferência, presença de vistoria, ocupação, condições, bloqueios e versões; não reserva. |
| `POST /withdrawals/{id}/delivery` | Caixa/Administrador | Confirmação explícita com versões de atendimento, pedido, razão e controles; `201` com recibo mínimo e liberação. |

Alterações em rotas existentes: `GET /withdrawals?state=entregue` passa a ser aceito; fila e detalhe incluem `vistoriaSaidaRegistrada` e `entrega` histórica; o caixa inclui `entrega` e `autorizacao*.vigencia = consumida`/`consumidaEmUtc`; o histórico usa allowlist de eventos por perfil.

Códigos estáveis acrescentados: `exit_inspection_invalid_text`, `exit_inspection_invalid_attendance`, `exit_inspection_requires_withdrawal`, `exit_inspection_already_exists`, `exit_inspection_not_found`, `stale_attendance_version`, `delivery_confirmation_required`, `delivery_invalid_versions`, `delivery_requires_exit_inspection`, `delivery_document_not_approved`, `delivery_balance_outstanding`, `delivery_authorization_stale`, `delivery_requires_open_occupancy`, `delivery_already_completed`, `delivery_state_invalid`. Conflitos de versão usam `concurrency_conflict`/`stale_*`; a chave reaproveitada com outro conteúdo usa `idempotency_conflict`.

## 3. Banco e segurança

- Migration aditiva cria `VistoriasSaida`, `EntregasVeiculo` e `ConsumosAutorizacao`.
- FKs restritas `NO ACTION`; unicidade de vistoria por atendimento e por pedido, de entrega por atendimento e por pedido, de consumo por entrega/tipo e por concessão.
- `VistoriaSaida`, `EntregaVeiculo` e `ConsumoAutorizacao` entram na proteção append-only do contexto, ao lado de auditoria, eventos, lançamentos, cobrança e autorizações.
- Fim da ocupação é atualizado por predicado condicional com ID da ocupação, atendimento, vaga e `FimUtc IS NULL`, exigindo exatamente uma linha.
- Ordem de bloqueio dos escritores: **atendimento → pedido → cabeçalho da razão → controles documental/financeiro**; início de busca, concessão e revogação foram harmonizados para a mesma ordem. Operações da razão podem começar na razão sem tomar bloqueios anteriores depois.
- Cifragem reutiliza AES-256-GCM versionado com propósito `exit-inspection` e AAD reconstruído com atendimento e ID do registro; chaves históricas continuam legíveis e não há nova chave.
- Idempotência usa SHA-256 canônico versionado por operação/ator/alvo/texto normalizado/versões; nenhum texto, justificativa ou corpo é persistido no resultado.

## 4. Verificações executadas

Ambiente: Linux x86-64, .NET SDK 10.0.114, Node 26.13.1, npm 12.2.2, SQL Server 2022 CU28 em container local, Chromium do Playwright 1.65.1. Catálogos de teste e E2E isolados e descartáveis.

| Verificação | Comando | Resultado |
| --- | --- | --- |
| Build da solução | `dotnet build app/backend/Estacionamento.slnx` | Sucesso, 0 erros e 0 avisos nesta execução. |
| Testes unitários MSTest | `dotnet test app/backend/tests/Estacionamento.UnitTests` | 72 aprovados (11 novos de prontidão/autorização da Fase 4). |
| Testes de integração MSTest com SQL Server real | `ESTACIONAMENTO_TESTE_CONEXAO=... dotnet test app/backend/Estacionamento.slnx` | 103 aprovados, incluindo os testes das Fases 1–4. |
| SPA (Vitest) | `npm test --prefix app/frontend -- --run` | 80 aprovados em 18 arquivos. |
| Build da SPA | `npm run build --prefix app/frontend` | Sucesso; chunk `delivery` gerado. |
| E2E Playwright em duas origens HTTPS | `npx playwright test` em `app/frontend` com `E2E_*` | 19 aprovados (5 Fase 2 + 4 Fase 3 + 2 Fase 4 + 8 jornadas de sessão). |
| Validação OpenSpec | `openspec validate --all --strict` | 16 itens aprovados, 0 falhas. |
| Migration aditiva/reversão | `PersistenciaFase4Testes` | Tabelas criadas em catálogo vazio, dados da Fase 3 preservados, nenhuma vistoria/entrega/consumo retroativo, reversão para a Fase 3 sem resíduo. |

**Cenários de integração cobertos:** vistoria válida/duplicada/replay/conteúdo divergente, texto vazio/excessivo/propriedade inesperada, ausência de pedido, entrega concluída, leitura pelos três perfis com auditoria, envelope corrompido, `no-store`, ausência de texto em projeções/recibo; entrega normal aguardando→entregue e em busca→entregue; saldo zero por desconto; exceções documental/financeira separadas e em conjunto; concessão revogada, substituída e obsoleta após desconto; consumo único e estado histórico; acerto posterior; liberação única e vaga reutilizada por novo check-in; ocupação ausente; duas entregas concorrentes com chaves distintas; mesma chave concorrente; replay após commit; nova chave após entrega; falhas forçadas na gravação de efeito, auditoria e recibo com rollback completo; disputas entrega × busca, entrega × revogação e entrega × pagamento com conexões independentes; JWT ausente/inválido, sessão revogada, `403`, CORS permitido/rejeitado, `409`/`422` sem PII; perfil do Manobrista sem finanças nem consumo.

**Cenários de UI/E2E cobertos:** Caixa registra vistoria, lê o texto por ação explícita, confirma entrega com versões do servidor e vê o recibo e a vaga histórica na fila; Manobrista registra vistoria, não vê prontidão de entrega nem confirmação e recebe `403` ao consultar a prontidão; tentativa incerta e replay com a mesma chave/conteúdo; conflito recarregável; erro de texto associado ao campo; sessão somente em memória. As jornadas das Fases 1–3 permaneceram verdes.

## 5. Acessibilidade e feedback de comandos

- Verificações automatizadas: `aria-invalid` e `aria-describedby` no texto da vistoria, resumo de erro com `tabindex="-1"` e foco, diálogo de confirmação com foco no botão, estados textuais de bloqueio/prontidão/entrega, rótulos e foco no recibo após sucesso.
- Validação manual de teclado/foco/mensagens e validação operacional/comercial informadas como aprovadas pelo solicitante em 23/11/2026; o ambiente específico não foi detalhado nesta resposta.
- Ajuste pós-validação: a sobreposição de comando pendente só aparece após 400 ms (ou imediatamente em resultado incerto), evitando o piscar de alertas em operações locais rápidas; após vistoria/pagamento a recarga é silenciosa e a mensagem de sucesso permanece legível.
- Nenhuma conformidade WCAG 2.2 AA é declarada; a decisão de publicação continua exigindo revisão específica.

## 6. Auditoria de PII e logs

- Respostas, projeções, recibos e logs não incluem placa, nome, documento, texto de vistoria, valores, justificativas ou tokens.
- Logs da entrega registram ação, resultado, conflito, correlação e latência; a falha de negócio é registrada pelo código estável.
- A vistoria só é devolvida por leitura explícita, após auditoria confirmada; o histórico guarda ator, alvo, tipo e UTC.
- A auditoria SQL é separada do log técnico; consumos e liberações só entram no histórico após commit.

## 7. Limitações e divergências intencionais dos protótipos

- A SPA usa Tailwind CSS 4.3.5 nativo e componentes Angular, enquanto os protótipos são HTML standalone com utilitários inline; os seletores de revisão, identidades, respostas de rede e fixtures existem apenas nos protótipos.
- A divergência do protótipo de entrega sobre “registrar / consultar” foi implementada como rota real `/delivery/:withdrawalId`, sem persistência entre páginas e sem armazenamento no navegador.
- O protótipo permite “Abrir operação de entrega” pela fila e pelo caixa; a implementação preserva os vínculos e mantém a confirmação restrita a Caixa/Administrador.
- A vaga liberada é exibida como vínculo histórico da visita; a disponibilidade corrente continua derivada da ocupação em aberto e não é inferida do recibo.
- **Operacional:** as suítes de integração não devem ser executadas simultaneamente contra o mesmo SQL Server; execuções sobrepostas provocaram deadlock em um teste pré-existente da Fase 1. A execução final limpa passou integralmente. O teste E2E de revogação da Fase 2 foi ajustado para aguardar a recusa da própria carga protegida, sem depender de um controle que pode ser desmontado pela navegação.
- O gate local não substitui homologação Azure, RF-12, publicação, revisão manual de acessibilidade ou validação operacional/comercial.

## 8. Migração e rollback

- A migração é aditiva após `20261119160017_Fase3RetiradaCaixa`; não altera colunas existentes.
- O ensaio de reversão foi feito em catálogo descartável, antes de existir entrega real; depois de gravar entregas de demonstração, não se deve aplicar `Down` que destrua histórico: suspender novos comandos e corrigir para frente ou restaurar cópia consistente apenas em exercício controlado.
- Não misturar versão anterior da aplicação com operação ativa de Fase 4.

## 9. Roadmap

Todos os itens F4-01–F4-06 foram marcados em `docs/tdd.md` com as verificações automatizadas e a validação manual/operacional registrada. A revisão do PR permanece como próximo passo.
