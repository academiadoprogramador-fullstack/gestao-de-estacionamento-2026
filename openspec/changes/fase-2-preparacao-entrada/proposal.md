# Proposal

## Why

A fundação local já autentica funcionários, mas não permite preparar vagas e tarifa nem registrar a entrada de um veículo. A Fase 2 transforma essa base na primeira fatia operacional verificável do MVP, conforme `docs/prd.md` (RF-01–03, parte de RF-07/10/11 e RNF-01–05/07) e `docs/tdd.md` §8, sem antecipar publicação ou caixa.

## What Changes

- Disponibilizar administração de funcionários e atribuição dos três perfis fixos, sem autorregistro, preservando autoria após desativação e revogando permissões desatualizadas.
- Cadastrar vagas com código único e consultar disponibilidade; configurar tarifa diária por novas versões, sem valor comercial predefinido.
- Registrar check-in por caixa/administrador com placa, modelo, titular, documento, vistoria textual, vaga livre, identificador público único, horário/ator do servidor e snapshot da tarifa vigente.
- Garantir entrada, ocupação, vistoria, auditoria e resultado idempotente na mesma transação SQL Server; rejeitar concorrência e repetição incompatível sem resíduos.
- Consultar atendimentos por placa/identificador, distinguindo visitas e restringindo campos por perfil; proteger documento e vistoria por criptografia de campo e auditar leituras sensíveis.
- Criar interfaces acessíveis de preparação, entrada, vagas, busca e detalhe, usando sessão e proxy locais existentes; atualizar vagas por polling configurável.
- Entregar testes locais de unidade, integração SQL Server real, contrato, UI e E2E, com evidências do gate da Fase 2.
- **Fora do escopo:** retirada, conferência de saída, diárias calculadas, pagamentos, descontos/estornos, entrega/liberação, relatórios, fotos, ticket físico/canal de comunicação, CI (change própria), Azure, IaC e CD. Não criar atalhos para liberar vagas na demonstração.

## Capabilities

### New Capabilities

- `identity/employee-management`: provisionamento administrativo, perfis e desativação sem apagar autoria.
- `parking/space-availability`: cadastro, consulta e ocupação exclusiva de vagas da unidade.
- `billing/daily-rate`: tarifa diária versionada e preservação da versão/valor na entrada.
- `parking/vehicle-check-in`: entrada completa, atômica e idempotente com vistoria textual.
- `parking/attendance-query`: busca/detalhe com projeções por perfil e visitas distintas.
- `platform/operational-audit`: trilha transacional, proteção de campos e acesso sensível auditável para a fatia de entrada.

### Modified Capabilities

- `identity/employee-session`: validar conta ativa e permissões atuais também em sessões já emitidas após mudanças administrativas.

## Impact

- Backend em `app/backend`: ativar casos de uso e domínio hoje apenas estruturais; adicionar persistência operacional, migrações aditivas, contratos `/api/v1`, OpenAPI e testes, preservando a direção das dependências.
- Frontend em `app/frontend`: ampliar a tela de sessão para navegação por funcionalidades, serviços HTTP e formulários; estender o harness em `app/frontend/e2e`.
- Reutilizar SQL Server, EF Core, Identity, Data Protection, Angular e ferramentas de teste existentes; sem novo serviço externo. Detalhes técnicos e eventuais dependências ficam no design e nos lockfiles da implementação.
- A migração inicial Identity continua historicamente sem tabelas operacionais; novas migrações adicionam a Fase 2. Preservar bootstrap exclusivo de `Development`, isolamento de testes e restrições das specs de edge/ambiente.
- PRD e TDD aprovados são reutilizados, sem mudar regras comerciais; decisões detalhadas desta proposta aguardam revisão. Pendências de implantação e validação real continuam abertas. Nenhuma implementação, execução de migration ou aprovação de produção faz parte desta proposta.

## Review

- Em 13/11/2026, o solicitante revisou e aprovou explicitamente esta proposta, as sete specs delta, o design e as tarefas para implementação, sem alterações. A revisão confrontou RF-01–03/07/10/11 e confirmou os limites de fase descritos acima. Esta aprovação autoriza somente a implementação e verificação local planejadas; não aprova implantação, produção ou as pendências de negócio/privacidade/operação.
