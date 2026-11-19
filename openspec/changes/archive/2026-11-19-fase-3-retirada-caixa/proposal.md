# Proposal

## Why

A Fase 2 permitiu preparar a unidade e registrar a entrada do veículo, mas o fluxo termina no atendimento ativo: ainda não há como o caixa registrar o pedido de retirada, fixar o valor devido, receber pagamento ou tratar divergências e exceções. A Fase 3 fecha esse intervalo definido no PRD §5.4 e no TDD §4.2, entregando retirada, fila, cobrança e caixa sobre o snapshot, a ocupação e o protocolo de idempotência já existentes, sem antecipar a entrega do veículo, a vistoria de saída ou a liberação da vaga, que pertencem à Fase 4.

## What Changes

- **Pedido de retirada:** caixa ou administrador registra o pedido somente para atendimento ativo e sem pedido, após declarar a presença do titular e conferir nome/número registrados contra documento com foto. O resultado da conferência é **aprovado** ou **divergente**, com horário e ator do servidor; divergência preserva o pedido e o cálculo, mas bloqueia a entrega normal futura. Um segundo pedido no mesmo ciclo é recusado, salvo replay da mesma ação.
- **Identidade limitada ao titular:** retirada por terceiro ou com titular ausente é recusada no MVP, inclusive para administrador e mediante exceção; nenhum documento de terceiro é coletado e nenhuma foto do documento é registrada.
- **Cálculo fixado no pedido:** tempo decorrido até o pedido, diárias iniciadas de 24 horas com mínimo de uma e total pela tarifa capturada na entrada. O resultado é imutável e não muda com publicação posterior de tarifa.
- **Fila de retiradas:** consulta paginada e com polling padrão de 30 s pelos três perfis, estados **aguardando** e **em busca**, início de busca registrado com ator e horário do servidor. A Fase 3 não produz o estado **entregue**, não confirma entrega, não coleta vistoria de saída, não libera vaga e não consome autorizações.
- **Composição do saldo:** cobrado, pago líquido, descontos e saldo reconstruídos a partir de lançamentos append-only, com demonstração em BRL, `pt-BR` e `America/Sao_Paulo`.
- **Desconto administrativo:** somente administrador, valor monetário positivo, justificativa obrigatória e permitido apenas enquanto o pago líquido for zero; a soma não ultrapassa o total original e pode zerar a cobrança. Cada concessão é um lançamento preservado, não edição da base.
- **Acerto integral:** caixa ou administrador registra exatamente o saldo exigível positivo, em dinheiro, cartão ou Pix, com ator e horário do servidor; parcial, excedente, crédito e troco são recusados, e cobrança zerada não exige pagamento fictício. Após estorno ou entrega excepcional com saldo, admite novo pagamento integral do saldo atual, sem reabrir atendimento, pedido ou vaga.
- **Estorno integral:** somente administrador, integral do pagamento ainda não estornado, com justificativa e vínculo ao lançamento original; reabre o saldo, não reduz o cobrado e não executa operação bancária. Sem estorno parcial nem exclusão do original.
- **Autorizações de exceção:** administrador concede, revoga e substitui, de forma auditada, autorizações separadas para divergência documental e saldo pendente, vinculadas ao pedido e à versão relevante da conferência/razão. A autorização não altera conferência, saldo ou pagamento, não transfere o direito de retirada e não é consumida nesta fase.
- **Atomicidade, concorrência e idempotência:** pedido, cálculo, pagamento, desconto, estorno e autorizações confirmam efeitos, auditoria e resultado idempotente na mesma transação; disputas não duplicam pedido, quitação ou estorno, nem concedem desconto sobre saldo já pago.
- **Interface operacional:** fila, pedido de retirada e caixa com dados fictícios, projeções por perfil, polling sem sobreposição, tentativa incerta somente em memória, mesma chave/conteúdo no replay, bloqueio de rota/logout e mensagens acessíveis.

**Fora do escopo desta fase:** vistoria de saída, entrega, transição para **entregue**, liberação de vaga, consumo de autorização, relatórios, fechamento de caixa, integração financeira, ticket, fotos, cancelamento ou reabertura de pedido, CI, Azure, IaC e CD. Nenhuma migração remove ou reescreve dados das Fases 1 e 2; o acerto após entrega excepcional é contrato financeiro verificado por fixture sintética, sem criar endpoint de entrega antecipado.

## Capabilities

### New Capabilities

- `solicitacao-retirada`: pedido único para atendimento ativo, conferência presencial do titular, bloqueio de terceiro, fixação da cobrança, atomicidade, auditoria e replay da tentativa incerta.
- `fila-retiradas`: consulta paginada dos pedidos, estados aguardando e em busca, início de busca pelos três perfis, polling sem sobreposição e projeções por perfil sem entrega.
- `cobranca-atendimento`: cálculo por períodos iniciados de 24 horas, total pela tarifa da entrada, composição do saldo, desconto administrativo e preservação da razão.
- `pagamentos-caixa`: acerto integral do saldo, estorno integral vinculado ao pagamento, novo acerto após reabertura ou entrega excepcional, concorrência e idempotência.
- `autorizacoes-retirada`: concessão, revogação e substituição de autorizações documental e financeira vinculadas à versão vigente, sem consumo e sem transferir o direito de retirada.

### Modified Capabilities

- `consulta-atendimentos`: o detalhe e o histórico passam a expor o pedido de retirada, seus estados e, para caixa/administrador, a cobrança, o saldo e os lançamentos financeiros, mantendo o manobrista sem finanças, conferência, autorizações, justificativas, documento completo ou vistoria.

## Impact

- **Backend:** novos módulos de retirada, cobrança, caixa e autorizações em `Estacionamento.Domain` (regras de cálculo, estados, lançamentos e vigência), `Estacionamento.Application` (casos de uso, portas, autorização por ação e projeções) e `Estacionamento.Infrastructure` (mapeamentos EF, consultas, razão financeira e auditoria); `Estacionamento.Api` recebe controllers finos em `/api/v1` para retiradas, fila, caixa e autorizações.
- **Banco:** migração aditiva criando pedidos de retirada, cobrança, pagamentos, descontos, estornos, autorizações e eventos correspondentes, com FKs restritas, unicidade de pedido por atendimento, estorno único por pagamento, índices de fila e `rowversion` no cabeçalho financeiro.
- **Frontend:** novas funcionalidades de fila de retiradas, pedido de retirada e caixa em `app/frontend/src/app/features`, reutilizando shell, guards, interceptor, sessão em memória, coordenador de comando e componentes acessíveis das fases anteriores.
- **Configuração:** nenhum segredo novo; as chaves AES existentes ganham o propósito de justificativas administrativas e o protocolo SHA-256 da Fase 2 passa a cobrir as novas operações.
- **Rastreabilidade:** RF-04–08, RF-10, RF-11 e RNF-01–05/07; PRD §5.4 e §10; TDD §4.2 (D3-T01–D3-T08), §5.3, §6.2–6.3 e §7.1; itens F3-01–F3-06 do roadmap. Nenhuma verificação é declarada executada por esta proposta.
