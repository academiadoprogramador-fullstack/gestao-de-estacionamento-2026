# Tasks

## 1. Fundação de dados, cálculo e protocolos

- [x] 1.1 Criar a migration aditiva `Fase3RetiradaCaixa` com `PedidosRetirada`, `Cobrancas`, `RazoesAtendimento`, `LancamentosFinanceiros`, `ConcessoesAutorizacao`, `RevogacoesAutorizacao` e as colunas `ResultadoJson`/`CodigoResposta` em `ResultadosIdempotentes`; verificar com `dotnet ef migrations script 20261118174120_Fase2PreparacaoEntrada` que o DDL é somente aditivo.
- [x] 1.2 Mapear as entidades EF com FKs `NO ACTION`, `decimal(18,2)`, checks, unicidade de pedido/cobrança/razão, índice único filtrado de estorno por pagamento e índices de fila; estender `ImpedirAlteracaoDeHistorico` a cobrança, lançamentos e autorizações; verificar com teste de integração sobre catálogo da Fase 2 que dados de identidade, atendimentos, vistorias e ocupações permanecem intactos e que `UPDATE`/`DELETE` nesses registros é recusado.
- [x] 1.3 Implementar `CalculadoraCobranca` em ticks UTC com mínimo de uma diária e multiplicação decimal verificada; verificar com testes de unidade em 0 h, 24 h exatas, 24 h + 1 tick, 48 h e acima de 48 h.
- [x] 1.4 Implementar a composição `cobrado`, `pagoLiquido` e `saldo` a partir de lançamentos, recusando saldo negativo e cobrança zerada com pagamento; verificar com testes de unidade de cada combinação e com a projeção de consulta.
- [x] 1.5 Estender `ICriptografiaCampos`/`CriptografiaCamposAesGcm` com vínculo ao registro e propósitos de justificativa de desconto, estorno e autorização; verificar com testes de ida/volta, troca entre registros, troca entre propósitos e leitura de envelopes da Fase 2.
- [x] 1.6 Ampliar `CanonicalizadorIdempotencia` e `ResultadoIdempotente` para as novas operações com resultado mínimo em JSON; verificar com testes de determinismo, replay pela versão persistida e ausência de corpo/documento/justificativa no resultado.

## 2. Pedido de retirada

- [x] 2.1 Implementar `RegistrarPedidoRetirada` com reautorização, presença obrigatória do titular, conferência aprovada/divergente, cálculo fixado e um único pedido por atendimento; verificar com testes de integração de pedido válido, atendimento inelegível, segundo pedido e recusa de terceiro.
- [x] 2.2 Confirmar pedido, cobrança, razão inicial, mudança para `aguardando`, eventos e auditoria na mesma transação; verificar com teste de integração que falha de auditoria ou cálculo não deixa registro parcial.
- [x] 2.3 Implementar replay idempotente e conflito por conteúdo/versão do pedido; verificar com testes de integração de replay idêntico, divergente, ator diferente e sessão revogada, sem pedido/cobrança duplicados.
- [x] 2.4 Criar `POST /api/v1/attendances/{id}/withdrawal` com `Idempotency-Key`, DTO sem foto/novo documento, `201` e resultado mínimo; verificar com testes de contrato de `400/403/409/422` e de propriedades inesperadas rejeitadas.
- [x] 2.5 Atualizar o OpenAPI com o pedido, exemplos e erros; verificar que o documento gerado corresponde às respostas HTTP testadas e não executa bootstrap.

## 3. Fila e início de busca

- [x] 3.1 Implementar `GET /api/v1/withdrawals` com paginação 1/20/100, filtro por estado, ordem estável e projeções por perfil; verificar com testes de integração de três perfis, paginação completa e ausência de finanças/documento/vistoria no DTO do Manobrista.
- [x] 3.2 Implementar `GET /api/v1/withdrawals/{id}` com projeção operacional para os três perfis e detalhe financeiro restrito a Caixa/Admin; verificar com testes de contrato de conteúdo por perfil e de pedido inexistente.
- [x] 3.3 Implementar `POST /api/v1/withdrawals/{id}/search` com versão esperada, lock de pedido/atendimento, transição `aguardando → em busca`, responsável e UTC do servidor; verificar com teste de integração concorrente que apenas uma transição é confirmada e a outra recebe `409`.
- [x] 3.4 Garantir que a fase não produza `entregue`, não libere vaga e não ofereça rota de entrega/consumo; verificar com teste de contrato que caminhos de entrega não existem ou não têm efeito e que a ocupação permanece aberta.
- [x] 3.5 Implementar a atualização periódica do cliente em serviço próprio, com 30 s configurável, pausa em aba oculta, refetch no foco/após comando, cancelamento e trava contra sobreposição; verificar com Vitest de polling e falha preservando filtros.
- [x] 3.6 Atualizar o OpenAPI da fila, do detalhe e da busca; verificar códigos, paginação e projeções por perfil contra o comportamento HTTP.

## 4. Razão financeira e acerto integral

- [x] 4.1 Implementar o bloqueio de `RazaoAtendimento` com `UPDLOCK, HOLDLOCK`, revisão esperada e releitura dos lançamentos em toda operação financeira; verificar com testes de integração de revisão desatualizada e rollback.
- [x] 4.2 Implementar `POST /api/v1/withdrawals/{id}/payments` com valor exatamente igual ao saldo positivo, formas dinheiro/cartão/Pix, `Idempotency-Key` e resultado mínimo; verificar com testes de integração de acerto, parcial, excedente, cobrança zerada e perfil sem permissão.
- [x] 4.3 Permitir novo acerto integral após estorno ou atendimento entregue com saldo por fixture da Fase 4, sem reabrir atendimento/pedido/ocupação/vaga; verificar com teste de integração usando fixture SQL isolada e ausência de criação de entrega.
- [x] 4.4 Verificar concorrência de dois pagamentos e pagamento contra desconto/estorno com escritores independentes; confirmar um único lançamento, `409` no concorrente e saldo recarregável.
- [x] 4.5 Implementar a projeção `GET /withdrawals/{id}/cashier` com total, lançamentos, saldo, revisão e autorizações, restrita a Caixa/Admin; verificar com testes de contrato de conteúdo por perfil.
- [x] 4.6 Atualizar o OpenAPI do acerto e do caixa; verificar que valores, formas, revisão e erros documentados coincidem com os testes.

## 5. Desconto e estorno

- [x] 5.1 Implementar `POST /withdrawals/{id}/discounts` com valor positivo, duas casas, justificativa cifrada, limite ao total original e bloqueio com pago líquido maior que zero; verificar com testes de unidade/domínio e integração de desconto válido, até zero, limite excedido e saldo já pago.
- [x] 5.2 Implementar `POST /withdrawals/{id}/refunds` com estorno integral de pagamento ainda não estornado, justificativa, vínculo ao original e unicidade; verificar com testes de integração de estorno válido, parcial, segundo estorno e preservação do cobrado.
- [x] 5.3 Implementar leitura explícita e auditada das justificativas de desconto/estorno, restrita ao Administrador; verificar com testes de integração de auditoria antes da decifragem, falha fechada e negativa a Caixa/Manobrista.
- [x] 5.4 Confirmar desconto/estorno, revisão, auditoria e resultado idempotente na mesma transação; verificar com testes de rollback, replay e concorrência desconto × pagamento.
- [x] 5.5 Atualizar o OpenAPI de descontos, estornos e justificativas; verificar códigos e ausência de texto de justificativa em listagens/histórico.

## 6. Autorizações de exceção

- [x] 6.1 Implementar concessão documental/financeira com justificativa cifrada, vínculo ao pedido e à revisão relevante, sem alterar conferência/saldo/pagamento; verificar com testes de integração de concessão, justificativa ausente e perfil sem permissão.
- [x] 6.2 Implementar vigência derivada (vigente, revogada, substituída, obsoleta) e invalidação financeira após pagamento/desconto/estorno, sem expiração por tempo; verificar com testes de integração de cada estado e com a revisão da razão.
- [x] 6.3 Implementar revogação com justificativa e unicidade por concessão, além da substituição auditada do mesmo tipo; verificar com testes de revogação, segunda tentativa, substituição, tipos independentes e duas concessões concorrentes.
- [x] 6.4 Implementar leitura auditada da justificativa de autorização e omitir o texto de Caixa/Manobrista; verificar com testes de contrato de exposição por perfil e falha de decifragem.
- [x] 6.5 Atualizar o OpenAPI das autorizações; verificar que nenhum endpoint de consumo/entrega existe e que a concessão não modifica saldo ou vaga.

## 7. Detalhe, histórico e projeções da retirada

- [x] 7.1 Estender `GET /attendances/{id}` com existência/estado da retirada, horários e, para Caixa/Admin, valores e saldo; verificar com testes de contrato por perfil e com o detalhe anterior preservado.
- [x] 7.2 Estender `GET /attendances/{id}/history` com pedido, conferência, cálculo, busca, pagamentos, descontos, estornos e autorizações, filtrando a allowlist antes da paginação; verificar com testes de integração de ordem, paginação e ausência de eventos proibidos ao Manobrista.
- [x] 7.3 Garantir que justificativas não entram no histórico nem em DTOs comuns e que a auditoria guarda apenas ator/alvo/tipo/UTC; verificar com testes de contrato e inspeção de logs.
- [x] 7.4 Verificar que a consulta detalhada não expõe finanças, conferência, autorizações ou justificativas ao Manobrista e que documento/vistoria continuam exigindo leitura explícita; verificar com testes de contrato e E2E de perfil.

## 8. Interface de fila e pedido de retirada

- [x] 8.1 Criar as rotas e features `/withdrawals` e `/withdrawals/new`, reutilizando guards, sessão, serviços de consulta e componentes acessíveis; verificar com Vitest que perfis proibidos não acessam o pedido e que a fila é liberada aos três.
- [x] 8.2 Implementar a fila conforme `prototypes/fila-retiradas.html`, com estados, paginação, ações de busca, polling e projeções por perfil; verificar com Vitest os estados vazio/carregando/erro/conflito e a preservação de filtros.
- [x] 8.3 Implementar o pedido conforme `prototypes/pedido-retirada.html`, com presença, conferência, cálculo, segundo pedido, recusa de terceiro, leitura de documento e resultado divergente; verificar com Vitest que dados inválidos não chamam a API e que o resumo recebe foco.
- [x] 8.4 Integrar `CoordenadorComandoService` ao pedido, com pendente/incerto, bloqueio de rota/logout, `beforeunload` e replay da mesma chave/conteúdo; verificar com Vitest os cinco estados e a ausência de storage.
- [x] 8.5 Cobrir em Playwright pedido aprovado/divergente, segundo pedido, recusa de titular ausente e replay após perda de resposta, em duas origens HTTPS; verificar paridade de estados, textos e ações com `pedido-retirada.html`.

## 9. Interface de caixa e autorizações

- [x] 9.1 Criar a rota `/cashier/:attendanceId` restrita a Caixa/Admin, com resumo, lançamentos, saldo e histórico; verificar com Vitest a negativa ao Manobrista e a projeção sem justificativas ao Caixa.
- [x] 9.2 Implementar o acerto integral conforme `prototypes/caixa.html`, com valor exato, formas, saldo zero, pós-estorno e fixture pós-entrega; verificar com Vitest que parcial/excedente não chamam a API e que o saldo é recarregado após sucesso.
- [x] 9.3 Implementar desconto e estorno administrativos com validação, justificativa, limites, estados bloqueados e replay da mesma chave; verificar com Vitest e testes de componente que Caixa não vê os formulários, que valores inválidos não são enviados e que a resposta incerta bloqueia a saída.
- [x] 9.4 Implementar as autorizações documental/financeira, com concessão, revogação, substituição, obsolescência, leitura auditada, tentativa incerta e projeções por perfil; verificar com Vitest os estados, o replay e a ausência de justificativa fora do Administrador.
- [x] 9.5 Integrar o coordenador de comando ao caixa e cobrir em Playwright pagamento, desconto até zero, estorno com novo acerto, autorização revogada/obsoleta, replay e ausência de entrega/liberação; verificar paridade com `caixa.html`.

## 10. Contrato, acessibilidade e documentação

- [x] 10.1 Padronizar os novos erros em Problem Details com `code`/`correlationId`, sem PII, e manter `no-store` em sucesso/erro; verificar com testes de contrato e inspeção de logs.
- [x] 10.2 Atualizar `mensagemDoErro` e os estados textuais da SPA para os novos códigos; verificar com Vitest as mensagens e o foco do resumo.
- [x] 10.3 Validar acessibilidade automatizada das novas telas (labels, fieldsets, `aria-invalid`, resumo focado, teclado, foco em diálogo) e registrar navegador/dispositivo/tecnologia assistiva da revisão manual; não declarar conformidade sem evidência.
- [x] 10.4 Atualizar `README.md`, `docs/fases/fase-3.md` e o OpenAPI com rotas, comandos, configuração e limites conhecidos; verificar que os comandos documentados executam como escritos.
- [x] 10.5 Revisar a coerência entre PRD §5.4, TDD §4.2, specs e decisões da implementação; corrigir divergências documentais antes de encerrar a fase.

## 11. Verificação integrada e encerramento

- [x] 11.1 Executar `dotnet build app/backend/Estacionamento.slnx`, as suítes MSTest com SQL Server real, `npm test --prefix app/frontend` e Playwright em duas origens; registrar comandos, ambiente, resultados e limitações em `docs/fases/fase-3.md`.
- [x] 11.2 Auditar respostas, logs, trilha e protótipos quanto a PII/segredos e confirmar que Manobrista não recebe finanças/conferência/autorização/justificativa e que documento/vistoria só aparecem por leitura auditada.
- [x] 11.3 Executar `openspec validate --all --strict` e atualizar somente os itens F3-01–F3-06 comprovados no roadmap do `docs/tdd.md`, com evidências correspondentes.
- [x] 11.4 Verificar em conjunto as regressões das Fases 1–2: login/revogação, check-in, busca, leituras sensíveis, tarifa e vagas, sem alteração de dados existentes pela migration.
- [x] 11.5 Revisar os protótipos contra a implementação nas duas direções: toda cena coberta existe e nenhum controle/efeito não especificado foi acrescentado; registrar divergências intencionais de estilo.

## Workflow follow-up

- Abrir Pull Request da branch `feat/fase-3-retirada-caixa` para `main` com o template de `.github/pull_request_template.md`, relacionando RF/RNF, decisões D3-T01–D3-T08, specs e evidências.
- Arquivar a mudança após revisão, aprovações e verificações exigidas, e validar o resultado arquivado.

## 12. Ajuste — listagem de atendimentos em aberto

- [x] 12.1 Implementar `GET /api/v1/attendances?status=aberto` com paginação, ordem estável, filtro exclusivo e situação da retirada, sem dados sensíveis; verificar com testes de integração dos três perfis, paginação, combinação inválida de filtros e atendimento com pedido.
- [x] 12.2 Listar os atendimentos em aberto na tela de pedido (`/withdrawals/new`), com seleção, paginação, busca opcional, estado do pedido existente e atualização após registrar; verificar com Vitest.
- [x] 12.3 Atualizar `prototypes/pedido-retirada.html` e `prototypes/index.html` para a lista de atendimentos em aberto, sem controles além dos especificados.
- [x] 12.4 Cobrir em Playwright a seleção pela lista de abertos, o atendimento com pedido existente e a busca opcional; atualizar README e `docs/fases/fase-3.md`.

## 13. Ajuste — caixa e confirmação

- [x] 13.1 Alinhar os campos e ações dos formulários de acerto e estorno do caixa, mantendo rótulos, dicas e botões na mesma linha quando houver espaço.
- [x] 13.2 Apresentar diálogo de confirmação persistente após pagamento, desconto, estorno e autorizações, com resumo da operação, foco e ação explícita de concluir.
- [x] 13.3 Atualizar Vitest e Playwright para o diálogo persistente e registrar o ajuste em `docs/fases/fase-3.md`.

## 14. Ajuste — validação após sucesso

- [x] 14.1 Limpar resumo de erros, estado `touched`/`dirty` e valores aplicáveis após sucesso em check-in, vagas, tarifas e funcionários.
- [x] 14.2 Cobrir os fluxos de sucesso com Vitest, garantindo que o resumo não reapareça após o submit.
- [x] 14.3 Registrar o ajuste em `docs/fases/fase-3.md`.

## 15. Alinhamento dos protótipos

- [x] 15.1 Alinhar `fila-retiradas.html` à paginação e às métricas da página, `pedido-retirada.html` à lista de abertos com busca exata opcional, ações e ocultação do formulário após pedido, e `caixa.html` aos formulários alinhados e à confirmação persistente.
- [x] 15.2 Abrir os protótipos no Chromium local e verificar renderização, busca, seleção, paginação, confirmação e ausência de erros de script.
- [x] 15.3 Atualizar `prototypes/index.html`, `design.md` e `docs/fases/fase-3.md` para refletir o alinhamento.

## 16. Ajuste — listagem na tela de atendimentos

- [x] 16.1 Carregar a lista de atendimentos em aberto por omissão na tela `/attendances`, com paginação, situação da retirada e busca exata opcional com “Limpar busca”.
- [x] 16.2 Atualizar Vitest e Playwright para a lista padrão, busca opcional e detalhe por perfil.
- [x] 16.3 Criar `prototypes/atendimentos.html`, atualizar o índice e as referências em `design.md`, README e `docs/fases/fase-3.md`.

