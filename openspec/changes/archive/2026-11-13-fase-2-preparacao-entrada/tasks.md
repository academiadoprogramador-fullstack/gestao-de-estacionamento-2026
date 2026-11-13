# Tasks

Planejamento da Fase 2 em execução. Referências: `docs/prd.md`, `docs/tdd.md`, `design.md` (D2-01–09) e sete deltas em `specs/`. Cada tarefa só é marcada após implementação e verificação efetivas. Sem CI/Azure/CD nesta change.

## 1. Gate de revisão e estrutura de domínio

- [x] 1.1 Registrar a revisão e validação explícitas de proposta, specs, design e tarefas antes de editar código, confrontando RF-01–03/07/10/11 e limites das fases; verificar registro real de aprovação, sem tratar a geração dos artefatos como aprovação. Se houver alteração de requisito, validar PRD → TDD primeiro.
- [x] 1.2 Criar os módulos e valores de domínio para vagas, tarifa, atendimento, ocupação e vistoria, com validação/normalização de D2-02; criar `Estacionamento.UnitTests` na solução e verificar testes de obrigatoriedade, limites, precisão monetária e identificadores distintos.
- [x] 1.3 Definir portas específicas e casos de uso em Application para ator, relógio, persistência transacional, proteção e auditoria, com autorização por ação; verificar testes unitários de permissões e testes arquiteturais, atualizando referências a marcadores somente quando necessário.

## 2. Persistência e migrações aditivas

- [x] 2.1 Adicionar mapeamentos operacionais ao contexto scoped existente, FKs sem cascata destrutiva, precisão decimal, `rowversion`, índices de busca e índice único filtrado de ocupação; verificar modelo EF e testes de integração SQL Server que rejeitam duas ocupações abertas para a mesma vaga.
- [x] 2.2 Criar migrações aditivas para tabelas, unicidade de e-mail e linhas técnicas de configuração/controle, sem tarifa/vagas comerciais iniciais; verificar aplicação em banco vazio e upgrade de Identity com contas preservadas, incluindo diagnóstico seguro de duplicatas prévias.
- [x] 2.3 Adaptar preparação das fixtures e bootstrap Development ao esquema ampliado, mantendo ausência de migração/seed no startup Testing e outros ambientes; verificar regressões de primeiro startup, reinício, falha de configuração e testes de migração histórica versus última versão.
- [x] 2.4 Documentar aplicação local, histórico de migrações e recuperação não destrutiva em `docs/local-development.md`; verificar comandos contra banco sintético isolado e conferir que não há orientação de executar migrations Azure ou apagar registros confirmados.

## 3. Proteção de campos, auditoria e erros

- [x] 3.1 Implementar porta de criptografia com Data Protection local, propósitos versionados por campo/atendimento e envelope recuperável (D2-06); verificar testes de ciphertext sem texto puro, troca indevida entre campos, reinício, rotação e chave histórica ausente.
- [x] 3.2 Implementar persistência append-only de eventos e portas de leitura auditada, reutilizáveis pelos casos de uso; verificar integração que confirma evento/alteração juntos e desfaz a operação ao injetar falha de auditoria.
- [x] 3.3 Ampliar não-cache para respostas privadas e tratamento genérico de falhas com correlation ID, sem alterar contratos de sessão existentes; verificar cabeçalhos e inspeção automatizada de logs/respostas para documento, vistoria, senha e cookie sintéticos, incluindo falhas e negações.
- [x] 3.4 Documentar diretório/chaves locais, necessidade de preservar material histórico e limites de proteção em `docs/local-development.md`; verificar que exemplos não contêm segredos nem afirmam backup/retencão/produção aprovados.

## 4. Funcionários, perfis e revogação

- [x] 4.1 Implementar listagem/criação administrativa com e-mail único, credencial inicial e conjunto dos perfis fixos, na mesma transação da auditoria; verificar SQL real para duplicidade concorrente, senha/perfil inválido, rollback e ausência de credencial em resposta/log.
- [x] 4.2 Implementar alteração de acesso com concorrência da conta e proteção transacional do último administrador ativo (D2-05); verificar desativação preservando autoria, conta obsoleta e duas remoções concorrentes sem deixar a unidade sem administrador.
- [x] 4.3 Revalidar conta/stamp no cookie a cada requisição e atualizar stamp em mudança de acesso; verificar cookie antigo em endpoints operacionais e replay de check-in, login novo com papéis atuais, inatividade e regressões de sessão/CSRF.
- [x] 4.4 Ajustar provisionador sintético para não restaurar perfis, senha ou estado de contas já administradas; verificar reinício depois de alteração de perfis/desativação e criação inicial idempotente das três contas.
- [x] 4.5 Expor contratos `/employees` e `/employees/{id}/access` com autorização admin e DTOs explícitos; verificar matriz HTTP visitante/manobrista/caixa/admin e documentar provisionamento inicial seguro e invalidação de sessão em `docs/local-development.md`.

## 5. Vagas e tarifa versionada

- [x] 5.1 Implementar cadastro de vaga e consulta paginada por código/situação derivada de ocupação; verificar testes SQL de código normalizado único, filtros, paginação e matriz HTTP que só permite cadastro por admin.
- [x] 5.2 Implementar publicação/histórico/configuração corrente da tarifa com versão-base e auditoria, sem editar versões; verificar valores inválidos, ausência inicial de tarifa e duas publicações concorrentes com conflito explícito.
- [x] 5.3 Implementar coordenação transacional entre leitura da tarifa e publicação (D2-03), além da captura imutável do snapshot; verificar teste SQL com escritores independentes para entrada/publicação e preservação de A após publicação de B.
- [x] 5.4 Documentar contratos de vagas/tarifa e parâmetros exclusivamente sintéticos em `docs/local-development.md`; verificar que não se presume valor inicial, moeda/fuso reais nem liberação manual de vaga.

## 6. Check-in atômico e idempotente

- [x] 6.1 Implementar armazenamento de chave por ator/operação, canonicalização versionada e HMAC com configuração externa versionada, sem payload pessoal persistido; verificar testes de equivalência normalizada, isolamento por ator, conteúdo diferente e comparação após rotação do segredo.
- [x] 6.2 Implementar caso de uso transacional de check-in com horário/ator do servidor, tarifa snapshot, vistoria cifrada, ocupação e eventos; verificar sucesso completo e rollback por falha injetada em cada etapa, inclusive auditoria/resultado idempotente.
- [x] 6.3 Implementar resolução de repetição antes de reavaliar vaga/tarifa e corrida da mesma chave com novo contexto após rollback; verificar resposta perdida após commit, replay retornando o mesmo resultado, chamadas simultâneas convergentes e chave incompatível sem novos registros.
- [x] 6.4 Garantir concorrência de ocupação via índice filtrado e atualização com `rowversion`, mapeando conflitos conhecidos sem retries cegos; verificar duas entradas com chaves distintas/mesma vaga por conexões independentes e ausência de atendimento/vistoria/evento órfão no perdedor.
- [x] 6.5 Expor `POST /attendances` com chave obrigatória, validação/CSRF e permissão caixa/admin; verificar erros `400/401/403/404/409/422`, resposta mínima `201`/`Location` e rejeição de tarifa/horário/operador injetados no DTO.
- [x] 6.6 Documentar protocolo de resultado desconhecido, repetição com mesma chave e ausência de TTL/limpeza em `docs/local-development.md`; verificar exemplos sem documento/credencial real e sem instrução para nova chave após timeout.

## 7. Busca, detalhe e histórico

- [x] 7.1 Implementar busca exata normalizada por placa/identificador com paginação e ordenação estável; verificar múltiplos atendimentos da mesma placa em vagas distintas, resultado vazio, filtros inválidos e ausência de unicidade histórica da placa.
- [x] 7.2 Implementar detalhe e histórico com projeção por perfil, sem documento completo nem vistoria em listagens e sem tarifa para manobrista; verificar a resposta HTTP bruta de todos os perfis e ausência de exposição via eventos/DTOs internos.
- [x] 7.3 Implementar endpoints explícitos de documento e vistoria com auditoria antes da resposta, projeções mínimas e não-cache; verificar negação do documento ao manobrista, acesso à vistoria pelos perfis permitidos e nenhuma exposição se auditoria/decifragem falhar.
- [x] 7.4 Gerar OpenAPI dos controllers com dependência .NET 10 fixada centralmente e atualizar lockfiles necessários; verificar restore travado, geração do documento e testes de contrato dos endpoints, campos condicionais e erros; documentar como obter o contrato sem expô-lo publicamente por padrão.

## 8. Navegação, preparação e disponibilidade na UI

- [x] 8.1 Criar shell autenticado, rotas por funcionalidade e guards por perfil, centralizando mutações/antiforgery e tratamento de `401/403`; verificar Vitest para token após login, sessão revogada, navegação e regressão de login/logout sem storage de sessão.
- [x] 8.2 Criar telas administrativas de funcionários/perfis/estado, vagas e tarifa com Signal Forms, feedback e conflitos de versão; verificar Vitest de validação, permissões visuais, formulário sem senha residual e atualização após comandos.
- [x] 8.3 Criar consulta de vagas com filtros/paginação, polling configurável, pausa em aba inativa, cancelamento e refetch ao foco/comando; verificar testes com timers controlados sem sobreposição e teardown ao sair da rota.
- [x] 8.4 Documentar navegação/perfis e intervalo local de polling em `docs/local-development.md`; verificar coerência com API e indicar que disponibilidade da tela não garante reserva.

## 9. Entrada e consulta na UI

- [x] 9.1 Criar formulário de check-in com dados obrigatórios, seleção de vaga e resultado com identificador, limpando PII após confirmação; verificar Vitest para validações, falta de tarifa/vagas, submissão única e atualização de vagas/busca após sucesso.
- [x] 9.2 Implementar estado de resultado desconhecido com conteúdo/chave preservados em memória e repetição explícita, sem edição enquanto não resolvido; verificar timeout simulado seguido de replay, aviso de navegação/reload e ausência de PII em storage.
- [x] 9.3 Criar telas de busca/detalhe/histórico e ações explícitas para leitura sensível com apresentação por perfil; verificar Vitest para visitas repetidas, vazios, paginação e ausência de campos proibidos no manobrista.
- [x] 9.4 Aplicar rótulos, foco visível, resumo de erros, estados textuais e operação por teclado nas novas telas; verificar testes de componente e documentar roteiro local de acessibilidade sem declarar validação normativa/operacional ainda pendente.

## 10. Integração ponta a ponta e gate local

- [x] 10.1 Ampliar Playwright e preparação de dados/banco isolado pelo harness, incluindo novos segredos externos sem endpoint de reset; verificar jornada admin cadastra equipe/vaga/tarifa → caixa entra e busca → manobrista consulta sem finanças/documento, via edge HTTPS existente.
- [x] 10.2 Executar cenários E2E transversais de tarifa A/B, vaga ocupada, tentativa repetida após resposta perdida, revogação e teclado; verificar cookies/CSRF e ausência de chamadas cross-site, sem simular retirada/liberação fora desta fase.
- [x] 10.3 Executar restore/build/test/format backend e testes/build frontend e E2E pelos comandos locais documentados, inclusive regressões da Fase 1; verificar todas as suítes aplicáveis e registrar falhas/bloqueios reais, nunca aprovação presumida.
- [x] 10.4 Consolidar `docs/fase-2-preparacao-entrada-evidencias.md` com comandos, resultados, matriz RF/spec/teste e limites, atualizando README/caminhos se necessário; verificar gate local de autorização, entrada atômica, snapshot, idempotência e impossibilidade de dupla ocupação, mantendo pendências Azure/privacidade/operação abertas.
- [x] 10.5 Validar coerência final de proposta/design/specs/documentação com decisões efetivas usando `openspec validate fase-2-preparacao-entrada --strict` e revisar o diff; verificar tarefas marcadas somente com evidência e mudanças agrupadas em commits pequenos, focados e verificáveis na feature branch. A limitação OpenAPI identificada na revisão foi corrigida em 12.1; conclusão do roteiro manual local de acessibilidade foi confirmada pelo solicitante, sem declaração de conformidade normativa.

## 11. Correções da revisão (PRD §5.3 / TDD §7.1)

As tarefas abaixo corrigem lacunas dos critérios já aprovados em D2-07–09, sem ampliar regras comerciais. A solicitação de aplicação das correções confirma este trabalho; 10.5 foi concluída após revisão final e commits locais.

- [x] 11.1 V2-01: descartar respostas obsoletas de busca, detalhe, histórico e leitura sensível após troca/fechamento/destruição da tela; Vitest cobre respostas A/B fora de ordem para documento, vistoria, histórico, detalhe e busca, sem anexar documento de A ao detalhe de B.
- [x] 11.2 V2-02: preservar tentativa/chave em memória e proteger saída por rota/logout/reload desde o envio inicial pendente, sem tratar cancelamento como rollback; Vitest verifica POST pendente, guard, logout, beforeunload, destruição e callback tardio. Confirmação explícita do diálogo de browser ainda pode perder estado em memória, conforme decisão de não persistir no navegador.
- [x] 11.3 V2-03: resolver rejeições definitivas após replay sem bloqueio permanente, preservar dados para correção e não gerar nova chave automaticamente; Vitest cobre timeout seguido de 400/404/409/422 e mantém rejeição sem code como resultado incerto.
- [x] 11.4 V2-04: apresentar erros de validação administrativos por campo/resumo com foco e feedback textual; Vitest cobre funcionários, vaga e tarifa, foco do resumo e ausência de chamadas inválidas. Solicitante confirmou conclusão/aprovação do roteiro manual local; validação formal permanece pendente.
- [x] 11.5 V2-05: paginar seleção de vagas livres e histórico de tarifas, oferecer recarga sem perder formulário; Vitest cobre 101 vagas, 45 tarifas e recuperação de disponibilidade/tarifa após falha inicial.
- [x] 11.6 V2-06: uniformizar erros operacionais em Problem Details com code/correlation ID/errors de validação, preservando contratos de sessão; filtro/middleware têm testes diretos e integração valida 429 na sexta tentativa, headers, corpo e ausência de senha.
- [x] 11.7 V2-07: impedir factory/provisionador/migration de teste de usar banco reservado/sem catálogo; testes sem conexão verificam rejeição e as suítes passaram em SQL Server com catálogos dedicados. Atualização de PRD/TDD/evidências registrada.

## 12. Alinhamento do contrato OpenAPI

- [x] 12.1 Alinhar respostas de erro operacionais no OpenAPI gerado ao runtime (`application/problem+json`, schema base e extensões opcionais `code`, `correlationId` e `errors`), preservando contratos de sessão; validar media type e schema por testes automatizados do documento gerado. Build/format passaram sem avisos; teste do documento gerado passou e suíte backend completa passou 72/72 (12 unitários, 56 integração, 4 arquitetura).

## Workflow follow-up

- Após implementação e verificações, abrir PR de `feat/fase-2-preparacao-entrada` para `main` com `.github/pull_request_template.md`, relacionando PRD, TDD, decisões D2, specs, evidências e pendências. Revisões/aprovações exigidas não estão concluídas por este planejamento.
- Integrar somente por PR após revisão e checks exigidos, sem merge direto ou contorno de proteção.
- Arquivar/sincronizar a change pelo workflow próprio após os gates aplicáveis; a geração destes artefatos não autoriza iniciar implementação, publicação ou arquivamento.
