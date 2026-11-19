# Design

## Context

Ver `proposal.md` — Why. A Fase 2 deixou uma base reutilizável que esta fase deve estender sem reorganização:

- `Atendimento` já guarda snapshot de tarifa, entrada, operador, estado e `RowVersion`; `EstadoAtendimento` conhece apenas `ativo`.
- `ServicoAtendimentos` demonstra o protocolo transacional: reservar a chave idempotente, bloquear os agregados com SQL, gravar efeito + eventos + auditoria + resultado mínimo e tratar violação de unicidade lendo o resultado confirmado em contexto novo.
- `CanonicalizadorIdempotencia` versão 1 já serializa campos com comprimento explícito e SHA-256, sem corpo nem segredo; `ResultadoIdempotente` é único por ator/operação/chave.
- `ICriptografiaCampos`/`CriptografiaCamposAesGcm` usam AES-256-GCM, envelope versionado e AAD reconstruído no servidor, hoje vinculado ao atendimento.
- `ServicoConsultaAtendimentos` entrega DTOs distintos por perfil; o Manobrista não recebe tarifa nem valores. `AtendimentosController` já centraliza `/api/v1/attendances` e `Idempotency-Key`.
- No frontend, `CoordenadorComandoService` mantém tentativa apenas em memória, bloqueia rota/logout e repete a mesma chave; `PollingVagasService` é a referência de polling sem sobreposição; `mensagemDoErro` concentra códigos estáveis.
- A stack permanece .NET 10, EF Core/SQL Server, Angular 22 com Signals/Signal Forms, Tailwind CSS 4.3.5, MSTest/Vitest/Playwright, conforme TDD §2.

Não há entrega, vistoria de saída, consumo de autorização, liberação de vaga, relatórios, fechamento de caixa, CI ou Azure nesta mudança. Nenhuma migração pode reescrever dados das Fases 1–2.

## Goals / Non-Goals

**Goals:**

- Acrescentar pedido de retirada, fila, cobrança, caixa e autorizações com migração aditiva e uma única fronteira transacional.
- Fixar cálculo e valores no pedido, com razão financeira append-only e revisão que serializa pagamento, desconto e estorno.
- Reutilizar cifragem, idempotência, auditoria, sessão, projeções por perfil e coordenador de comando existentes.
- Manter a Fase 3 incapaz de produzir entrega, consumo, vistoria de saída ou liberação de vaga.
- Entregar projeções de fila/caixa que não vazem finanças, conferência ou justificativas ao Manobrista.

**Non-Goals:**

- Definir política comercial além de PRD §5.4; não há desconto percentual, estorno parcial, crédito, troco ou pagamento por provedor.
- Criar endpoint de entrega, vistoria de saída, consumo de autorização ou liberação de vaga, nem “preparar” a Fase 4 com efeitos antecipados.
- Implementar relatórios, fechamento de caixa, conciliação automática, cancelamento/reabertura de pedido ou exclusão de lançamento.
- Alterar autenticação, CORS, rate limit, bootstrap, retenção ou stack.
- Persistir tentativa de comando no navegador; recarga continua exigindo novo login e perde a tentativa.

## Decisions

### D1. Modelo de domínio e estados

- `EstadoAtendimento` passa a declarar `Aguardando`, `EmBusca` e `Entregue`. A Fase 3 produz somente `aguardando` e `em busca`; `entregue` existe para o contrato futuro da Fase 4, mas nenhum caso de uso desta fase o atribui.
- `PedidoRetirada`: `Id`, `AtendimentoId` único, `Conferencia` (`aprovada`/`divergente`), `TitularPresente` obrigatório verdadeiro, `SolicitadoEmUtc`, `SolicitadoPorId`, `BuscaIniciadaEmUtc?`, `BuscaPorId?`, `RowVersion`. Um pedido por atendimento no ciclo do MVP; não há cancelamento/reabertura.
- `Cobranca`: `Id`, `PedidoId` único, `DuracaoTicks`, `Diarias`, `TarifaVersaoId`, `TarifaValorSnapshot`, `TotalOriginal` em `decimal(18,2)`, `CriadaEmUtc`. É imutável; descontos não a editam.
- `RazaoAtendimento`: cabeçalho financeiro único por atendimento, com `Revisao` inteira e `RowVersion`. Toda alteração financeira bloqueia esse registro, recalcula o saldo a partir dos lançamentos e incrementa a revisão na mesma transação.
- `LancamentoFinanceiro` append-only com discriminador `desconto`, `pagamento` ou `estorno`: `Id`, `AtendimentoId`, `Tipo`, `Valor decimal(18,2)`, `FormaPagamento?`, `PagamentoOriginalId?`, `JustificativaCifrada?`, `AtorId`, `OcorridoEmUtc`. Checks garantem valor positivo, forma apenas para pagamento, vínculo apenas para estorno e justificativa apenas em ajustes. Índice único filtrado em `PagamentoOriginalId WHERE Tipo = 'estorno'` garante um estorno por pagamento.
- `ConcessaoAutorizacao` e `RevogacaoAutorizacao` append-only: concessão com `PedidoId`, `Tipo` (`documental`/`financeira`), `RevisaoReferencia` (conferência do pedido para documental; revisão corrente da razão para financeira), justificativa cifrada, autor e UTC; revogação com `ConcessaoId` único, justificativa, autor e UTC.
- Vigência é **derivada**, nunca um campo mutável: a última concessão do tipo é vigente se não tiver revogação, não tiver sido ultrapassada por outra concessão do mesmo tipo e, no caso financeiro, se `RevisaoReferencia` ainda for a revisão corrente da razão. Estados exibidos: vigente, revogada, substituída, obsoleta ou não concedida.
- Novos tipos em `TiposEventoAtendimento` e `TiposAuditoriaNegocio` cobrem pedido, conferência, cálculo, busca, pagamento, desconto, estorno, concessão e revogação. A allowlist do Manobrista inclui apenas eventos operacionais.

**Alternativas:** uma tabela por tipo de lançamento exigiria somas e históricos paralelos; uma única tabela append-only com checks reduz divergência e preserva a ordem. Guardar a vigência como coluna mutável criaria uma segunda fonte de verdade em relação à revisão e à revogação.

### D2. Cálculo e composição do saldo

- `CalculadoraCobranca` é domínio puro: calcula a diferença em **ticks UTC** entre pedido e entrada, rejeita diferença negativa e conta `diarias = max(1, (ticks + TicksPerDay - 1) / TicksPerDay)`. Não usa `double`, arredondamento ou truncamento: exatamente 24 h resulta em 1; 24 h + 1 tick resulta em 2; 48 h resulta em 2.
- `TotalOriginal = diarias × TarifaValorSnapshot` em `decimal(18,2)`, com multiplicação verificada para rejeitar overflow. O snapshot e o total ficam na `Cobranca` e não são recalculados em consultas posteriores.
- Composição: `cobrado = TotalOriginal − Σ descontos`, `pagoLiquido = Σ pagamentos − Σ estornos`, `saldo = cobrado − pagoLiquido`. O saldo por atendimento nunca é negativo; um saldo negativo indicaria inconsistência e deve falhar, não ser exibido como crédito.
- A API devolve valores em BRL, `pt-BR` e `America/Sao_Paulo`; o servidor persiste UTC e decide o instante do pedido, nunca o cliente.

**Alternativas:** arredondar horas para cima em ponto flutuante produziria fronteiras instáveis em 24 h e 48 h; recalcular o total na leitura quebraria o snapshot.

### D3. Pedido de retirada

Fluxo de `RegistrarPedidoRetirada`, em uma transação `ReadCommitted` com locks explícitos:

1. Reautorizar Caixa/Administrador e validar `Conferencia`, `TitularPresente` e `Idempotency-Key` UUID. `TitularPresente = false` é recusado como `withdrawal_requires_holder`; não há campo para terceiro nem novo documento.
2. Reservar a chave idempotente usando canonicalização que inclui atendimento, conferência e declaração de presença.
3. Bloquear o `Atendimento` com `UPDLOCK, HOLDLOCK`, reler estado e verificar ausência de pedido. Estado diferente de `ativo` ou pedido existente resulta em `attendance_not_active`/`withdrawal_already_exists` (ou replay).
4. Calcular tempo/diárias/total com a tarifa snapshot e inserir `PedidoRetirada`, `Cobranca`, `RazaoAtendimento` (revisão inicial 1), eventos e auditoria; atualizar o atendimento para `aguardando` sob o mesmo lock, incrementando sua `RowVersion`.
5. Preencher o resultado mínimo idempotente (pedido, atendimento, estado, horário, diárias, total) e confirmar tudo no commit.

Um segundo pedido com chave nova colide no índice único ou na checagem de estado e devolve conflito sem alterar o original. O replay reautoriza, compara o hash da versão persistida e devolve o resultado mínimo. Divergência é apenas um resultado da conferência: não apaga pedido/cálculo e não bloqueia busca; bloqueia a entrega normal na Fase 4.

**Alternativas:** permitir mais de um pedido por atendimento exigiria regras de ciclo e cancelamento fora do MVP; criar o pedido sem cálculo permitiria estados parciais e inconsistência de saldo.

### D4. Fila e início de busca

- `GET /api/v1/withdrawals` consulta pedidos paginados (1/20/100) por estado (`aguardando`, `em busca`) em ordem de pedido e identificador. A consulta usa projeção explícita e índice em `(Estado, SolicitadoEmUtc, Id)`.
- `GET /api/v1/attendances?status=aberto` lista, com paginação e ordem estável de entrada, os atendimentos ainda não entregues (`ativo`, `aguardando`, `em busca`), com identificação, veículo, vaga, entrada e situação da retirada (pedido e estado). A projeção é a mesma para os três perfis, sem finanças, conferência, autorização, justificativa, documento ou vistoria. O filtro `status=aberto` é exclusivo e não se combina com placa/identificador; a busca exata permanece disponível.
- `POST /api/v1/withdrawals/{id}/search` aceita `expectedVersion` do pedido. Em transação, bloqueia pedido e atendimento, exige estado `aguardando`, grava `em busca`, responsável e UTC do servidor, atualiza o atendimento para `em busca`, registra evento/auditoria e devolve o estado atual. Não usa `Idempotency-Key`: a transição tem efeito único e concorrência é resolvida por versão; repetição recebe conflito com estado recarregável.
- O estado `entregue` não é produzido nem aceito como destino nesta fase. Não há rota de entrega, liberação ou consumo; uma tentativa de caminho inexistente resulta em `404`/método não permitido, sem efeitos.
- Projeções: Manobrista recebe identificação, veículo, vaga, estado, horários operacionais e responsável da busca; Caixa/Administrador recebem também total, pago líquido, saldo e situação das autorizações. Documento e vistoria continuam sob leitura explícita e auditada; o Manobrista não recebe justificativas.

**Alternativas:** usar `Idempotency-Key` para início de busca não elimina a necessidade de concorrência e adiciona retenção sem efeito duplicável; `SELECT` seguido de `UPDATE` sem lock permitiria duas transições.

### D5. Financeiro transacional e concorrência

- `RazaoAtendimento` é o ponto de serialização. Cada comando financeiro:
  1. Reautoriza e reserva a chave idempotente com o conteúdo canônico da operação, incluindo atendimento, valor, forma, pagamento original quando houver e `expectedLedgerRevision`.
  2. Abre transação, bloqueia a razão com `UPDLOCK, HOLDLOCK`, lê os lançamentos e verifica `Revisao` contra `expectedLedgerRevision`; divergência gera `ledger_revision_stale` com o estado atual.
  3. Valida a regra: pagamento exatamente igual ao saldo positivo; desconto positivo e soma ≤ total original, somente com `pagoLiquido = 0`; estorno integral de pagamento ainda não estornado.
  4. Insere o lançamento append-only, incrementa a revisão, grava eventos, auditoria e resultado idempotente; confirma tudo ou reverte.
- O pagamento aceita saldo de atendimento entregue por fixture da Fase 4, desde que exista pedido/cobrança e saldo positivo; não reabre atendimento, pedido, ocupação ou vaga e não cria entrega. Cobrança zerada recusa pagamento.
- Desconto depois de pagamento líquido é recusado sob o mesmo bloqueio, mesmo que a leitura anterior tenha visto saldo livre. Dois descontos concorrentes não ultrapassam o total, pois o segundo relê os lançamentos após o lock.
- Estorno não reduz `TotalOriginal`, não apaga o pagamento e reabre o saldo; um novo pagamento integral do saldo atual é permitido. A unicidade filtrada de estorno por pagamento também protege contra corrida fora da aplicação.

**Alternativas:** concorrência otimista apenas por `rowversion` exigiria retry cego e poderia executar duas leituras do mesmo saldo; um saldo materializado sem lançamentos perderia a trilha append-only.

### D6. Autorizações de exceção

- `POST /api/v1/withdrawals/{id}/authorizations` (Administrador) recebe `tipo`, justificativa e `expectedVersion` do pedido; para o tipo financeiro, lê a revisão corrente da razão sob `HOLDLOCK` e grava essa revisão em `RevisaoReferencia`; para o documental, grava a revisão 1 da conferência, que é imutável. A concessão não altera conferência, saldo, pagamento ou estado.
- Outra concessão do mesmo tipo substitui a anterior por derivação (a anterior deixa de ser a última), sem atualizar seu registro. Tipos documental e financeiro são independentes.
- `POST /api/v1/withdrawals/{id}/authorizations/{authorizationId}/revoke` (Administrador) exige justificativa, grava `RevogacaoAutorizacao` única e impede nova revogação. A concessão e a revogação permanecem.
- A vigência documental é vinculada à conferência imutável do pedido (revisão 1); a financeira é vinculada à revisão da razão e fica obsoleta após qualquer pagamento, desconto ou estorno. Não há expiração por tempo. A derivação aplica a precedência `revogada` > `substituída` > `obsoleta` > `vigente`, para que uma concessão revogada nunca volte a parecer apenas substituída.
- Justificativas usam propósitos distintos por tipo de registro e são lidas somente por endpoint explícito, auditado antes da decifragem, restrito ao Administrador. Caixa vê tipo/vigência/efeito, nunca o texto; Manobrista não vê autorizações.
- Nenhum comando consome autorização nesta fase. A Fase 4 deverá revalidar e consumir atomicamente, sem reescrever a concessão.

**Alternativas:** expirar por tempo criaria uma regra comercial não decidida; guardar “status” mutável tornaria a substituição e a obsolescência sujeitas a atualizações perdidas.

### D7. Idempotência, resultados mínimos e retenção

- Ampliar `ResultadoIdempotente` com `ResultadoJson` anulável e `CodigoResposta` para as novas operações; as colunas específicas do check-in permanecem e continuam sendo usadas por ele. O JSON contém apenas IDs, estado, valores, revisão e `Location`, nunca documento, vistoria ou justificativa.
- Operações: `withdrawal.request`, `withdrawal.grant-authorization`, `withdrawal.revoke-authorization`, `cashier.payment`, `cashier.discount`, `cashier.refund`. `withdrawal.start-search` usa versão esperada, não chave idempotente.
- `CanonicalizadorIdempotencia` ganha métodos versionados por operação; todos usam a mesma serialização com comprimento explícito e nomes ordenados. O replay compara o hash pela versão persistida, reautoriza antes de devolver e distingue violação de unicidade de conflito de versão. A versão 1 do check-in não é alterada.
- Retenção acompanha o atendimento: não há TTL autônomo. O descarte de 365 dias após encerramento/quitação continua pertencendo às fases de retenção; a Fase 3 apenas nasce com os campos necessários.

**Alternativas:** adicionar colunas específicas para cada novo comando criaria uma tabela cada vez mais esparsa; persistir o corpo da requisição exporia PII e justificativas.

### D8. Cifragem das justificativas

- Estender `ICriptografiaCampos` com operações que recebem também o identificador opaco do registro (`CifrarComRegistro`/`DecifrarComRegistro`). O AAD passa a incluir `|registro:{guid}` além de versão, `kid`, ambiente, aplicação, propósito e atendimento; as operações antigas mantêm o AAD atual e continuam lendo envelopes da Fase 2.
- Propósitos novos: `justificativa-desconto`, `justificativa-estorno`, `justificativa-autorizacao-documental` e `justificativa-autorizacao-financeira`. O vínculo ao ID do lançamento/concessão impede troca entre registros do mesmo atendimento.
- Falha de chave, envelope, tag ou contexto segue o comportamento fechado existente: erro controlado, sem texto puro/vazio, sem tentar outra chave e sem conteúdo em log.
- A rotação continua sem recifragem automática; justificativas novas usam a versão ativa e as antigas permanecem legíveis enquanto a chave existir.

**Alternativas:** usar um único propósito `justificativa` permitiria mover um envelope entre desconto, estorno e autorização do mesmo atendimento sem falhar; recifrar tudo na rotação contrariaria DT-09.

### D9. Contratos HTTP

Prefixo `/api/v1`; respostas `no-store`; Problem Details com `code` e `correlationId`.

| Rota | Perfis | Contrato |
| --- | --- | --- |
| `POST /attendances/{id}/withdrawal` | Caixa/Admin | Pedido com conferência, presença e `Idempotency-Key`; `201` com resultado mínimo |
| `GET /attendances?status=aberto&page=&pageSize=` | Três perfis | Atendimentos em aberto, com situação da retirada e projeção sem dados sensíveis |
| `GET /withdrawals?state=&page=&pageSize=` | Três perfis | Fila paginada, projeção por perfil |
| `GET /withdrawals/{id}` | Três perfis | Detalhe operacional; finanças só para Caixa/Admin |
| `POST /withdrawals/{id}/search` | Três perfis | Início de busca com versão esperada; `200`/`409` |
| `GET /withdrawals/{id}/cashier` | Caixa/Admin | Cobrança, lançamentos, saldo, revisão e autorizações |
| `POST /withdrawals/{id}/payments` | Caixa/Admin | Acerto integral com `Idempotency-Key` e revisão esperada |
| `POST /withdrawals/{id}/discounts` | Admin | Desconto com justificativa, `Idempotency-Key` e revisão esperada |
| `POST /withdrawals/{id}/refunds` | Admin | Estorno integral vinculado ao pagamento |
| `POST /withdrawals/{id}/authorizations` | Admin | Concessão documental/financeira |
| `POST /withdrawals/{id}/authorizations/{authId}/revoke` | Admin | Revogação com justificativa |
| `GET /withdrawals/{id}/authorizations/{authId}/justification` | Admin | Leitura auditada antes de decifrar |
| `GET /withdrawals/{id}/launches/{launchId}/justification` | Admin | Leitura auditada antes de decifrar |

`GET /attendances/{id}` e `GET /attendances/{id}/history` são estendidos com o resumo da retirada e os novos eventos, mantendo os DTOs por perfil. Listagens continuam sem documento/vistoria. Códigos propostos: `withdrawal_requires_holder`, `third_party_not_allowed`, `attendance_not_active`, `withdrawal_already_exists`, `withdrawal_not_found`, `withdrawal_not_waiting`, `stale_withdrawal_version`, `ledger_revision_stale`, `payment_must_equal_balance`, `balance_already_settled`, `discount_requires_no_payment`, `discount_exceeds_balance`, `refund_must_be_full`, `payment_already_refunded`, `authorization_not_current`, `authorization_already_revoked`, `authorization_justification_denied`.

Os controllers permanecem finos: validação de cabeçalho/parâmetros, chamada de caso de uso e mapeamento para Problem Details. Não há endpoint de entrega, vistoria de saída, consumo ou liberação de vaga; ausência de rota é intencional e coberta por contrato.

**Alternativas:** um `POST /withdrawals` com `attendanceId` no corpo permitiria divergência entre alvo da URL e corpo; manter o alvo no caminho simplifica autorização e canonicalização. Um DTO único “com tudo” vazaria finanças ao Manobrista.

### D10. Persistência, migração e índices

Migração aditiva única `Fase3RetiradaCaixa`, após `20261118174120_Fase2PreparacaoEntrada`:

- `PedidosRetirada` com FK restrita para `Atendimentos`, unicidade de atendimento, `RowVersion`, índice `(Estado, SolicitadoEmUtc, Id)`.
- `Cobrancas` com unicidade por pedido, checks de diárias ≥ 1 e total ≥ 0, snapshot `decimal(18,2)`.
- `RazoesAtendimento` com unicidade por atendimento, `Revisao` inteira e `RowVersion`.
- `LancamentosFinanceiros` append-only com checks e índice único filtrado de estorno por pagamento; índice `(AtendimentoId, OcorridoEmUtc)`.
- `ConcessoesAutorizacao` e `RevogacoesAutorizacao` com FKs restritas, unicidade de revogação por concessão e índices `(PedidoId, Tipo, ConcedidaEmUtc)`.
- `ResultadosIdempotentes` recebe `ResultadoJson nvarchar(max)` e `CodigoResposta int` anuláveis; nada é removido.
- A proteção `ImpedirAlteracaoDeHistorico` do `EstacionamentoDbContext` passa a cobrir `Cobranca`, `LancamentoFinanceiro`, `ConcessaoAutorizacao` e `RevogacaoAutorizacao`, impedindo `UPDATE`/`DELETE` fora de procedimento explícito de retenção; `RazaoAtendimento` e `PedidoRetirada` permanecem mutáveis sob versão para revisão e início de busca.
- Nenhuma coluna existente é reescrita; atendimentos da Fase 2 permanecem `ativo` e sem pedido. O `Down` remove somente as tabelas/colunas novas, sendo usado apenas em ambiente local isolado.

**Alternativas:** renomear/mover tabelas da Fase 2 ampliaria o risco sem benefício; criar um segundo `DbContext` quebraria a transação única com Identity/auditoria.

### D11. Frontend

- Novas features: `features/retiradas` (fila e pedido) e `features/caixa` (acerto, ajustes, autorizações e histórico), com serviços HTTP tipados por funcionalidade.
- Rotas: `/withdrawals` (três perfis), `/withdrawals/new?attendanceId=` (Caixa/Admin) e `/cashier/:attendanceId` (Caixa/Admin), protegidas por `guardaSessao`/`guardaPerfil` e `guardaComandoPendente` nas telas com mutação. A tela de pedido carrega por padrão a lista paginada de atendimentos em aberto e permite selecionar o veículo sem conhecer o identificador; a busca exata por placa/identificador permanece como filtro opcional. A tela de atendimentos também carrega a lista de abertos por omissão, com a busca exata como filtro opcional e “Limpar busca” para retornar à lista. O parâmetro `attendanceId` continua aceito para atalhos e deep links.
- `CoordenadorComandoService` é reutilizado para pedido, pagamento, desconto, estorno e autorizações. A tela mantém apenas uma tentativa por vez, mostra pendente/incerto, bloqueia rota/logout, arma `beforeunload` e repete a mesma chave/conteúdo; rejeição 4xx libera correção com nova chave.
- A fila reutiliza o padrão de polling de vagas em um serviço próprio: 30 s configurável, pausa em aba oculta, atualização no foco e após comando, `takeUntilDestroyed`, cancelamento e trava contra sobreposição. Filtros/página são preservados em falha.
- Projeções: a tela do Manobrista não renderiza colunas financeiras nem ações de caixa; a do Caixa não renderiza desconto/estorno/concessão/revogação/justificativa; a do Administrador renderiza. A ocultação é complementar à API, nunca substituta.
- Respostas tardias de fila/detalhe/caixa são descartadas por token de contexto, como na consulta de atendimentos. Justificativas são abertas por ação explícita e nunca cacheadas.
- Acessibilidade: labels, fieldsets/legends para conferência e forma, resumo de erros focado, `aria-invalid`, `aria-describedby`, foco visível, diálogos com foco contido, estados textuais e teclado.
- Caixa: campos e ações de acerto/estorno usam grade alinhada com rótulos e alturas consistentes; após pagamento, desconto, estorno ou autorização, um diálogo de confirmação persistente apresenta o resumo e o saldo/revisão, permanecendo até a ação explícita de concluir.

**Alternativas:** uma única feature “retirada” misturaria fila e caixa com projeções diferentes; um serviço global de polling dificultaria o cancelamento por tela.

### D12. Verificação

- **MSTest unidade:** cálculo 0/24 h/24 h + 1 tick/48 h/>48 h, multiplicação decimal, composição do saldo, limites e bloqueios de desconto, estorno único, vigência de autorização, canonicalização por operação, allowlist de histórico e coordenador de comando.
- **MSTest integração (SQL Server real):** migração aditiva sobre catálogo da Fase 2; dois pedidos concorrentes; duas buscas concorrentes; dois pagamentos concorrentes; desconto concorrente com pagamento; dois estornos do mesmo pagamento; justificativa cifrada com vínculo ao registro; falha de auditoria com rollback; replay idêntico/divergente/sessão revogada; acerto pós-entrega por fixture; ausência de rota/efeito de entrega e vaga preservada.
- **Contrato/API:** OpenAPI versus respostas, `Idempotency-Key` obrigatório nas operações críticas, `201/200/400/403/404/409/422`, `no-store`, DTOs por perfil sem finanças ao Manobrista e sem justificativas fora do Administrador.
- **Vitest:** polling sem sobreposição/pausa, validação sem chamada inválida, resumo focado, tentativa incerta com mesma chave, recusa de terceiro, saldo/parcial/excedente, desconto bloqueado, estorno integral e descarte de respostas tardias.
- **Playwright (duas origens HTTPS):** pedido aprovado e divergente; segundo pedido; início de busca; pagamento; desconto até zero; estorno e novo acerto; autorizações concedidas/revogadas/obsoletas; replay após perda; perfis; navegação por teclado; ausência de entrega/liberação.
- Evidências, comandos, ambiente e limitações serão registrados somente após execução em `docs/fases/fase-3.md`; nenhum item F3 do roadmap será marcado por este documento.

**Alternativas:** testes apenas unitários não comprovariam locks/índices SQL; E2E sem duas origens não cobriria bearer/CORS.

## Prototype

Os protótipos revisados em `prototypes/` são a referência de interface:

- `prototypes/fila-retiradas.html` — fila paginada, estados aguardando/em busca, início de busca, conflito, polling e projeções por perfil (D4/D11).
- `prototypes/pedido-retirada.html` — lista paginada de atendimentos em aberto, busca exata opcional, presença do titular, conferência aprovada/divergente, cálculo 0/24 h/24 h + 1 s/48 h, recusa de terceiro, segundo pedido e replay incerto (D2/D3/D7).
- `prototypes/caixa.html` — saldo, acerto integral, desconto, estorno, autorizações, vigência por revisão, justificativa auditada, histórico e fixture pós-entrega (D2/D5/D6/D8).
- `prototypes/index.html` — visão de revisão dos fluxos e limites da fase.

**Estilo:** os protótipos usam CSS inline com os tokens visuais do app (paleta slate/sky, tema escuro, cantos e espaçamento equivalentes), sem biblioteca vendorizada, porque a Fase 2 estabeleceu o mesmo padrão e a implementação real já usa **Tailwind CSS 4.3.5** com Lucide. A divergência esperada é a forma de autoria: classes utilitárias no build real contra CSS inline no protótipo; layout, estados, textos e comportamento permanecem os mesmos.

**Divergências intencionais e resoluções:**

- Seletor de perfil, seletores de cenário, simulação de rede/aba oculta e “próxima resposta” existem apenas para revisão; a implementação usa sessão real, HTTP e o coordenador de comando.
- O seletor “Atendimento localizado” do protótipo passa a representar a lista real de atendimentos em aberto; a busca exata continua como filtro opcional e a implementação reutiliza o endpoint de consulta.
- A fixture “entregue com saldo” do caixa demonstra o contrato de acerto posterior à entrega excepcional da Fase 4; ela será criada apenas por SQL/fixture de teste, sem rota de entrega.
- O protótipo não contém `[unspecified: ...]`; não há questões abertas de apresentação.

## Risks / Trade-offs

- **Dois pagamentos concorrentes** → bloqueio `UPDLOCK, HOLDLOCK` da razão, revisão esperada e releitura dos lançamentos; teste com escritores independentes.
- **Desconto sobre saldo já pago** → a regra é reavaliada sob o mesmo lock do pagamento; o teste desconto × pagamento garante um único efeito.
- **Estorno duplicado** → checagem transacional + índice único filtrado por pagamento; violação vira `409`, não erro genérico.
- **Autorização financeira obsoleta** → vínculo à revisão da razão; qualquer movimento financeiro a invalida por derivação, sem `UPDATE` silencioso.
- **Resultado idempotente genérico em JSON** → contrato mínimo e validado; nunca inclui justificativa, documento, vistoria ou corpo. Revisão de schema e testes de contrato mitigam regressão.
- **Cifragem vinculada ao registro** → extensão de interface exige testes de troca entre lançamentos; envelopes antigos continuam com o AAD anterior.
- **Fixture de entrega confundida com funcionalidade** → nenhuma rota/ação de entrega; a fixture é explícita em teste e documentação.
- **Polling e fila com muitas linhas** → índice por estado/horário, paginação e sem `N+1`; o perfil acadêmico é pequeno, mas a consulta não deve crescer sem índice.
- **Justificativas em histórico** → o histórico mostra apenas que a justificativa existe; o texto exige leitura explícita e auditada.
- **Estado entregue declarado e não produzido** → constante reservada e teste de ausência de transição; a Fase 4 será a única produtora.
- **Migração aditiva sobre dados reais locais** → `Down` só em ambiente isolado; rollback de produção será forward-only e preservará lançamentos.

## Migration Plan

1. Criar a migration aditiva `Fase3RetiradaCaixa` e os mapeamentos EF; aplicar sobre um catálogo da Fase 2 e verificar que identidade, sessão, atendimentos, vistorias, ocupações e idempotência permanecem intactos.
2. Implementar cálculo, cifragem por registro, canonicalização e modelos de razão/lançamentos com testes isolados.
3. Implementar pedido, fila, caixa e autorizações com transações, locks e auditoria; cobrir concorrência e idempotência antes das telas.
4. Implementar as features Angular, polling, projeções e coordenador; cobrir com Vitest e Playwright em duas origens.
5. Registrar evidências em `docs/fases/fase-3.md`, atualizar README/OpenAPI e marcar F3-01–F3-06 somente após os testes reais.
6. Rollback local: reverter a mudança e aplicar o `Down` da migration em catálogo isolado sem dados relevantes; nunca remover volume de desenvolvimento como parte da parada normal. Em publicação futura, correções serão aditivas e forward-only.

## Open Questions

Nenhuma. As decisões de escopo comercial estão fechadas no PRD §5.4, e as lacunas de apresentação foram resolvidas nos protótipos.
