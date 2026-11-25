# Design — Entrega e liberação de vaga

## Context

Ver [proposta](proposal.md) para o escopo e [TDD §4.3](../../../docs/tdd.md) para D4-T01–08. A Fase 3 usa `AtendimentosController` e `RetiradasController`, `ServicoRetiradas`, `ServicoCaixa`, `ServicoAutorizacoes` e as consultas do `EstacionamentoDbContext`. `EstadoAtendimento` já reserva o valor `entregue`, mas a fila não o produz; o caixa o demonstra apenas por fixture. A vistoria existente é de entrada. Disponibilidade já deriva de ocupação com `FimUtc` nulo, protegida por índices filtrados por vaga e atendimento.

## Goals / Non-Goals

- **Objetivos:** estender o monólito pelas portas existentes, preservar a fronteira transacional SQL, manter os contratos anteriores estáveis e separar projeção, confirmação, recibo e trilha durável.
- **Não objetivos:** serviço ou armazenamento novos, mudança da razão financeira ou do cálculo, filas assíncronas, dados reais, alterações de política comercial e infraestrutura de publicação.

## Decisions

### D4-T01/02 — Registros imutáveis e portas específicas

Adicionar `VistoriaSaida`, `EntregaVeiculo` e `ConsumoAutorizacao` às camadas Domain/Application/Infrastructure, com contratos específicos e controllers existentes. Vistoria: unicidade por atendimento, vínculo ao pedido, envelope e autoria/UTC. Entrega: unicidade por atendimento e pedido, vínculo à vistoria, à ocupação encerrada e à vaga, autoria/UTC. Consumo: unicidade por entrega/tipo, referência à concessão e à revisão utilizada; nunca copiar a justificativa. Usar FKs sem cascata, restrições de chaves coerentes e índices de consulta, e estender a proteção append-only existente. Consumir não atualiza concessão/revogação. Os estados correntes permanecem mutáveis sob controle de concorrência. Isso preserva histórico sem misturar no registro da entrega o estado atual da vaga ou da razão.

Reutilizar AES-GCM versionado com propósito `exit-inspection` e AAD reconstruído com IDs imutáveis do atendimento e do registro; não reaproveitar propósito da entrada nem gerar chave nova. Normalizar o texto antes de validar/hash/cifrar. A gravação verifica a versão-base sob bloqueio, o pedido e a não entrega; auditoria e recibo são atômicos. Leitura explícita reconstrói o contexto, audita antes de devolver o texto, usa `Cache-Control: no-store` e falha fechada. Não persistir plaintext para pesquisa ou retorno genérico.

### D4-T03/04 — Ponto único de entrega e ordem de bloqueios

1. Validar sessão, perfil, alvo e estrutura; procurar replay autorizado, reservar chave no escopo da transação e só então validar o estado mutável sob bloqueios.
2. Adquirir bloqueios de escrita na ordem **atendimento → pedido → cabeçalho da razão → controle documental → controle financeiro**. Resolver vínculo inicial por leitura sem bloqueio de atualização e revalidá-lo após aquisição. Hoje `IniciarBuscaAsync` adquire pedido → atendimento; inverter essa ordem nesse fluxo para evitar ciclo. Concessão/revogação devem serializar no pedido e respeitar a mesma ordem ao alcançar razão/controles. Operações da razão podem começar na razão quando não tomam bloqueios anteriores depois; preservar a serialização com a entrega e testar com conexões independentes.
3. Revalidar versões e elegibilidade: pedido aguardando ou em busca vinculado a atendimento não encerrado elegível, vistoria existente e ocupação aberta do próprio atendimento. Usar a composição de saldo já existente. Conferência aprovada não exige exceção; divergência exige a concessão documental vigente (revisão `1`). Saldo zero não exige exceção; saldo positivo exige a concessão financeira vigente vinculada à `ledgerRevision`. Não reutilizar concessão anterior revogada, substituída ou obsoleta.
4. Derivar as versões opacas dos controles a partir dos registros correntes por tipo, incluindo concessão e revogação quando houver, com representação explícita para ausência; comparar sob o bloqueio do pedido e ler os tipos na ordem definida. Reusar as trilhas append-only atuais em vez de criar cópias da justificativa ou depender de validade calculada no cliente. A revisão financeira é validada separadamente. A projeção pós-entrega deriva `consumed` para a concessão usada, conserva revogada/substituída e não apresenta concessão não usada como vigente.
5. Na mesma transação, gravar entrega, consumos necessários, estados `entregue`, fim da ocupação no mesmo UTC da entrega, auditoria/eventos e recibo. Encerrar a ocupação por predicado composto por **ID da ocupação, atendimento, vaga e `FimUtc IS NULL`** e exigir uma linha afetada. Não atualizar disponibilidade separadamente ou liberar por código de vaga. Se nenhuma linha for afetada ou houver disputa, desfazer tudo. Após commit, uma nova entrada pode usar a vaga; entrega passada não pode fechá-la novamente.

O pagamento posterior usa a mesma razão e conserva o histórico e os consumos; não altera a ocupação e não dispara entrega. Preferir uma transação SQL sobre compensação assíncrona, que deixaria pedido ou vaga temporariamente inconsistentes.

### D4-T05/06 — Contratos, replay e privacidade

| `/api/v1` | Perfis | Contrato proposto |
|---|---|---|
| `POST /attendances/{id}/exit-inspection` | Três | Texto e `expectedAttendanceVersion`; `Idempotency-Key`; `201` com recibo mínimo, sem o texto. |
| `GET /attendances/{id}/exit-inspection` | Três | Leitura explícita e auditada, `200` com texto/autor/UTC ou erro controlado; sem pré-carregamento. |
| `GET /withdrawals/{id}/delivery-readiness` | Caixa/Admin | `200` com estado, saldo, conferência, presença de vistoria, bloqueios e versões; sem texto, justificativa ou documento. Não reserva. |
| `POST /withdrawals/{id}/delivery` | Caixa/Admin | Confirmação `true`, versões-base de atendimento, pedido, cabeçalho financeiro e dos dois controles; `Idempotency-Key`; `201` com identificador, estado, hora, ator e liberação permitidos, sem texto nem dados financeiros. |

Usar DTOs estritos, versões de `rowversion` opacas, revisão da razão explícita, UTC do relógio e ator da sessão; rejeitar propriedades não previstas. Publicar no OpenAPI os códigos do D4-T06 e seu mapeamento para Problem Details: `400` validação; `401` sessão; `403` perfil; `404` recurso ausente; `409` concorrência/repetição incompatível/registro já confirmado; `422` condição de negócio não satisfeita. Versão-base antiga exige recarga, não retry cego; condição atual como `delivery_authorization_stale` pode ser devolvida como bloqueio de regra. Não aplicar um novo esquema de enum ao contrato corrente: os valores operacionais continuam `aguardando`, `em busca`, `entregue`.

Aplicar `attendance.exit-inspection` e `withdrawal.delivery` ao armazenamento existente de idempotência, por ator/operação/UUID. Hash SHA-256 canônico com versão, separação por operação, ator, alvo, texto normalizado quando aplicável, confirmação quando aplicável e todas as versões-base; comparar no formato histórico. Não persistir corpo ou segredo. Replay reautoriza e retorna status/recibo original antes das verificações de elegibilidade mutável, inclusive depois de entregue; chave diferente não contorna unicidade. Distinguir índice de chave de unicidades de negócio e `DbUpdateConcurrencyException`; na disputa da mesma chave, após rollback reler em **novo `DbContext`**, sem duplicar efeitos. Preservar retenção de §5/DT-21 do TDD.

### D4-T07/08 — Projeções, SPA e diagnóstico

- Nas consultas de fila e atendimento, obter ID/ator/UTC da entrega e vaga/liberação histórica da entrega/ocupação encerrada; não reinterpretar a ocupação mais recente da vaga como a dessa visita. Preservar lista de abertos, busca exata, paginação e polling existentes. Dar ao Manobrista presença/estado operacional da vistoria sem prontidão financeira, conferência ou exceções; usar DTOs distintos e allowlists SQL antes de paginar histórico. Caixa/Admin veem consumo e finanças conforme acesso; somente Admin abre justificativa por leitura explícita. Invalidar respostas de vistoria ao mudar pedido/seleção/sessão.
- Acrescentar `/delivery/:withdrawalId` ligada à fila e ao caixa, usando o retorno de prontidão apenas para feedback; não replicar a regra no cliente. Reutilizar coordenador de comandos e guards de navegação/saída com confirmação acessível, resumo de erro focado, estado textual e anúncio discreto. Amarrar tentativa a operação/alvo/ator, recuperar `401` conforme política de sessão sem replay automático, impedir que outro ator herde a chave e reconsultar estado atual após sucesso. Não usar armazenamento no navegador. Se o usuário confirmar fechamento/recarga, a tentativa local se perde; consultar o estado ao voltar.
- Emitir diagnóstico estruturado com correlação, ação, resultado, latência e conflito; evento de liberação confirmado somente após commit. Manter dados sensíveis fora do log técnico, inclusive payload, tokens, valores e chaves; separar auditoria SQL de diagnóstico. Testar contratos e privacidade com as políticas de autenticação revogável e de origens distintas já existentes, sem mudar CORS.

## Prototype

- [Operação de entrega](prototypes/entrega.html): registro/leitura de vistoria, condições normais e excepcionais, bloqueios, confirmação e tentativa incerta.
- [Fila](prototypes/fila-retiradas.html): filtro de entregue, metadados, paginação e acesso ao pedido por perfil.
- [Atendimentos](prototypes/atendimentos.html): busca exata, detalhe entregue e histórico por perfil.
- [Caixa](prototypes/caixa.html): autorização consumida, acerto posterior e vínculo à operação.

CSS inline com paleta escura e utilitários selecionados do **Tailwind CSS 4.3.5** (distribuição oficial, cabeçalho MIT); a SPA mantém Tailwind nativo e componentes Angular. Protótipos são HTML standalone: seletores de revisão, identidades, respostas de rede e fixtures são simulações, sem persistência entre páginas. Apenas os fluxos alterados são ampliados; a SPA mantém as ações anteriores que continuam nas specs principais. Sem lacunas de apresentação pendentes.

## Risks / Trade-offs

- **Ciclo de locks ou versão velha** → padronizar a ordem nos escritores concorrentes e testar busca, revogação, acerto e nova entrada contra entrega no SQL Server real.
- **Liberação histórica confundida com disponibilidade atual** → distinguir visita encerrada de ocupação corrente, independentemente de replay e de acerto.
- **Consumo reclassificado após pagamento ou vazamento via histórico** → derivar consumo pelo fato imutável, filtrar antes de paginar e verificar JSON/logs brutos.
- **Resposta perdida** → preservar tentativa autorizada apenas em memória; validar também a recarga após resultado de uma tentativa antiga, sem prometer persistência offline.

## Migration Plan

Aplicar migração aditiva após `20261119160017_Fase3RetiradaCaixa` em catálogo local isolado, verificando banco vazio e upgrade de Fase 3 com dados sintéticos. Não preencher entregas, liberar vagas nem criar vistorias retroativamente; fixtures históricas ficam nos testes, e as jornadas novas devem confirmar entrega real. Ensaiar reversão em catálogo descartável e guardar cópia antes da ativação local. Depois de gravar entregas reais de demonstração, não aplicar `Down` que destrua histórico: suspender novos comandos e corrigir para frente ou restaurar cópia consistente apenas em exercício controlado. Não misturar versão anterior da aplicação com operação ativa de Fase 4.
