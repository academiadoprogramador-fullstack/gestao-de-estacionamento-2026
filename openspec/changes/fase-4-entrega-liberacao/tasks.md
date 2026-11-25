# Tasks — Fase 4: entrega e liberação

Antes de editar código, registrar revisão e aceite dos artefatos SDD e iniciar o fluxo Apply explicitamente. Rastreabilidade: PRD §5.5 (F4-R01–07), TDD §4.3 (D4-T01–08) e roadmap (F4-01–06). Os itens abaixo ainda não foram executados.

## 1. Persistência aditiva e vistoria — F4-01 e F4-03

- [x] 1.1 Modelar vistoria de saída, entrega e consumo, com FKs restritas, unicidades e proteção append-only; gerar migration após Fase 3. Verificar script aditivo, catálogos vazio e atualizado com dados sintéticos, restrições/rollback em catálogo descartável e ausência de preenchimento retroativo (D4-T01).
- [x] 1.2 Implementar escrita imutável da vistoria pelos três perfis, com versão, vínculo ao pedido, ator/UTC, cifragem própria e auditoria/recibo atômicos. Cobrir em MSTest limites após trim, propriedades inesperadas, inexistência de pedido, entrega já concluída, concorrência, falha de auditoria e reaproveitamento de chave após commit (F4-R01; D4-T02/05).
- [x] 1.3 Implementar leitura explícita auditada da vistoria de saída sem cache, reaproveitando chaves AES históricas. Verificar em SQL/HTTP os três perfis, falha de auditoria/decifragem, troca entre campo/atendimento, chave inválida, ausência de texto em projeções/recibo/logs e refletir contrato em OpenAPI (F4-R01/06; D4-T02/06).

## 2. Elegibilidade, concorrência e entrega — F4-02 e F4-03

- [x] 2.1 Implementar regras de prontidão e autorização por tipo/revisão, sem reserva. Cobrir com unitários e contrato HTTP: ambos os estados de origem, ausência de pedido/vistoria/ocupação, saldo zero por desconto, exceções separadas e em conjunto, concessão revogada/substituída/obsoleta após pagamento, desconto ou estorno, e bloqueio de terceiro conforme regra existente (F4-R02/03; D4-T03/04/06).
- [x] 2.2 Harmonizar a ordem de bloqueios entre início de busca, entrega, concessão/revogação e operações de caixa. Testar com escritores SQL independentes nas disputas entrega × busca, entrega × revogação/concessão e entrega × pagamento/desconto/estorno, garantindo conflito ou serialização coerentes, sem consumo inválido nem deadlock por ordem invertida (D4-T03/04).
- [x] 2.3 Implementar confirmação com consumo, encerramento e liberação condicional da ocupação própria. Testar em SQL duas chaves concorrentes, ocupação ausente/alheia, único fim de ocupação no UTC da entrega, e falhas forçadas na gravação de efeitos/auditoria/recibo com rollback completo (F4-R03/04; D4-T03/04).
- [x] 2.4 Completar contrato/idempotência de entrega; validar UUID, confirmação/versões estritas, hash histórico, chave igual/divergente, releitura em contexto novo após rollback e replay mínimo após commit/reutilização da vaga; documentar DTOs, códigos e status em OpenAPI e testar autorização antes do replay (F4-R04/07; D4-T05/06).

## 3. Projeções, autorização e acerto posterior — F4-04 e F4-05

- [x] 3.1 Atualizar endpoints de fila e detalhe com entrega e liberação histórica, e histórico com allowlist antes da paginação. Verificar bruto por perfil, ordem/totais/paginação, lista padrão de abertos sem entregues, busca exata preservada e nenhuma inferência de disponibilidade atual a partir da visita anterior (F4-R06; D4-T06/08).
- [x] 3.2 Projetar consumo e estado histórico no caixa, bloquear novas concessões/revogações e manter justificativas admin sob leitura auditada. Validar acerto integral sobre entrega real já concluída, sem alterar consumos, lançamentos anteriores, entrega ou ocupação mesmo com vaga reutilizada (F4-R03/05/06; D4-T04/08).
- [x] 3.3 Adicionar diagnóstico estruturado e testes de contrato para as novas rotas e projeções: acessos direto/replay com JWT ausente/inválido, sessão revogada, `403` e CORS em origens permitidas/rejeitadas; inspecionar logs, auditoria e JSON para confirmar correlação e ausência de dados sensíveis (F4-R06/07; D4-T06/08).

## 4. Interface incremental — F4-01, F4-04 e F4-05

- [x] 4.1 Implementar `/delivery/:withdrawalId`, o formulário e a leitura explícita conforme [entrega.html](prototypes/entrega.html). Cobrir com Vitest os perfis, resumo focado/erros associados, registro imutável, falha de leitura e descarte de respostas tardias; manter texto fora de projeções e limpar quando seleção/rota/sessão mudar (F4-R01/06; D4-T07).
- [x] 4.2 Ligar prontidão e confirmação explícita às versões do servidor, com bloqueios textuais, conflito recarregável e tratamento de `401`. Em Vitest, verificar bloqueio de navegação/logout e aviso de saída desde o POST, replay explícito por mesmo ator/alvo/chave/conteúdo, nenhuma persistência e nenhuma repetição automática, nova chave após rejeição definitiva e recarga após sucesso sem usar recibo como estado (F4-R02/03/07; D4-T07).
- [x] 4.3 Integrar filtro/estado entregue e acesso pela fila, metadados e histórico no detalhe, e estado/acerto após entrega no caixa conforme [fila-retiradas.html](prototypes/fila-retiradas.html), [atendimentos.html](prototypes/atendimentos.html) e [caixa.html](prototypes/caixa.html). Verificar com Vitest projeções, página/filtro, polling, ausência de ação indevida e retirada dos avisos/fixtures visíveis da Fase 3. Registrar a divergência intencional: Tailwind 4.3.5 nativo na SPA, controles de revisão somente nos protótipos (F4-R05/06; D4-T07/08).

## 5. Gate local e evidências — F4-06

- [x] 5.1 Rodar build, MSTest unitário e integração em SQL Server real isolado, Vitest e Playwright local em duas origens HTTPS distintas; verificar regressões das Fases 1–3, ajustando somente a expectativa transitória de ausência de entrega. Cobrir entrega aguardando→entregue e em busca→entregue, manobrista registra vistoria, exceções e revogação, resposta perdida/replay, vaga reutilizada por check-in, saldo posterior e perfis. Registrar comandos, cenários, resultados e limitações em `docs/fases/fase-4.md` (D4-T08, TDD §7.1).
- [x] 5.2 Revisar os quatro protótipos e a implementação nos dois sentidos: cenários cobertos aparecem na interface e nenhum controle/estado novo carece de spec. Fazer validação manual de teclado, foco, mensagens e tecnologia assistiva com ambiente registrado, além da validação operacional/comercial com dados sintéticos. Documentar divergências aprovadas e pendências explicitamente, sem declarar WCAG, homologação ou publicação por inferência (RNF-04; PRD §5.5).
- [x] 5.3 Consolidar evidências na documentação da fase e atualizar o roadmap do TDD somente para verificações efetivamente concluídas. Rodar `openspec validate --all --strict` e checar rastreabilidade F4-R01–07, D4-T01–08 e F4-01–06 antes da revisão do PR.

## Workflow follow-up

- Depois da implementação e das verificações aplicáveis, abrir PR da feature branch para `main` como **Ready for review**, conforme `AGENTS.md` e o template, sem integrar diretamente.
- No fluxo de sincronização/arquivamento, revisar os `Purpose` das specs principais `fila-retiradas` e `autorizacoes-retirada`, hoje escritos para a Fase 3, para que descrevam entrega e consumo após incorporar os deltas. Validar novamente as specs principais.
