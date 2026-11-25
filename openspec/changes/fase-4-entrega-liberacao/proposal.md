# Fase 4 — Entrega e liberação de vaga

## Why

A Fase 3 registra pedido, busca, caixa e autorizações, mas não encerra a retirada nem devolve a vaga à operação. A Fase 4 fecha a jornada P0 local e textual com evidência das condições de saída, entrega auditável e liberação única, sem mudar as regras comerciais.

## What Changes

- Registrar uma vistoria textual de saída por atendimento, vinculada ao pedido, imutável, cifrada e com leitura explícita auditada para os três perfis (F4-R01).
- Permitir a Caixa/Administrador confirmar a entrega a partir de **aguardando** ou **em busca**. Exigir vistoria, conferência aprovada e saldo zero, ou autorização vigente para cada condição não atendida; exceções não permitem retirada por terceiro (F4-R02/03).
- Confirmar consumo das exceções aplicáveis, entrega, encerramento do pedido/atendimento/ocupação, liberação de vaga, histórico e recibo idempotente na mesma transação (F4-R04).
- Manter saldo excepcional exigível e permitir acerto integral posterior sem reabrir atendimento, pedido ou ocupação (F4-R05).
- Exibir entrega/liberação na fila e no detalhe/histórico, com projeções por perfil; apresentar bloqueios, confirmação explícita e recuperação de tentativa incerta apenas em memória. Registrar diagnósticos sem dados pessoais, financeiros ou tokens (F4-R06/07).

Fora do escopo: fotos, relatórios, fechamento de caixa, integração financeira, emissão fiscal, ticket, comunicação com o cliente, cancelamento/reabertura, nova vistoria, múltiplas unidades, CI, Azure, IaC e CD. O gate local não equivale a homologação ou publicação.

## Capabilities

### New Capabilities

- `vistoria-saida`: registro textual único e leitura auditada das condições de saída.
- `entrega-veiculo`: prontidão, confirmação, encerramentos e liberação atômica; protocolo de tentativa incerta e diagnóstico.

### Modified Capabilities

- `fila-retiradas`: exibe e filtra entregue; liga à operação de entrega sem transformar consulta ou busca em liberação.
- `autorizacoes-retirada`: consumo vinculado à entrega e à revisão; consulta mostra estado derivado e veda mutações novas após entrega.
- `consulta-atendimentos`: detalhe e histórico com vistoria, entrega, liberação e consumo projetados por perfil.
- `pagamentos-caixa`: acerto posterior validado sobre entrega real; tela de caixa acessa a operação e mantém saldo reconciliável.

## Impact

- API `/api/v1`, contratos, persistência SQL Server e migração aditiva após Fase 3; SPA nas superfícies de fila, entrega, atendimento e caixa. Não há novo serviço, banco ou dependência externa.
- Reutiliza políticas de sessão, cifragem, ocupação exclusiva e idempotência já especificadas. Substitui as restrições transitórias da Fase 3, preservando busca, regras do caixa e consulta de vagas.
- Rastreabilidade: [PRD §5.5 e critérios de aceite](../../../docs/prd.md) (RF-05/09/10/11, RNF-01–04/07; preserva RF-02/04/06–08), [TDD §4.3, §7.1 e roadmap](../../../docs/tdd.md) (D4-T01–08, F4-01–06) e [evidências da Fase 3](../../../docs/fases/fase-3.md). Gates SQL/E2E e validações manual/operacional ainda não executados.
