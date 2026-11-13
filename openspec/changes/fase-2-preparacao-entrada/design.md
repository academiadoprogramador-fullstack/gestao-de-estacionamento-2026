# Design

## Context

Ver `proposal.md` para motivação e limites funcionais. Este design detalha o PRD aprovado e as decisões DT-01–12/14 de `docs/tdd.md`, não os substitui nem declara novas aprovações.

Estado observado: `SessionController` é o único controller; `IdentityDbContext` contém somente Identity; Domain/Application têm marcadores estruturais. O bootstrap em `Program.cs` migra esse contexto e provisiona três contas sintéticas só em `Development`. `AuthService` usa `/api/v1/session` e busca antiforgery por mutação. A UI raiz contém login/sessão, sem funcionalidades operacionais. Existem testes arquiteturais e de integração, SQL Server local e Playwright em `app/frontend/e2e`; não há workflow CI.

As três specs existentes de sessão, edge e ambiente são preservadas, com uma adição à sessão. O cenário de migração inicial sem tabelas operacionais descreve a migração histórica, não impede migrações incrementais. Nenhuma tabela financeira de cobrança/pagamento é necessária nesta fase.

## Goals / Non-Goals

**Goals:**
- Uma fronteira transacional relacional para entrada, disponibilidade, snapshot, auditoria e idempotência.
- Contratos e projeções explícitos, sem dependência de regras HTTP/EF no domínio.
- Verificação de concorrência, falhas e privacidade reproduzível no ambiente local existente.
- Preparar extensões das Fases 3–4 sem implementá-las antecipadamente.

**Non-Goals:**
- Não introduzir microsserviços, CQRS/framework de mediação, repositório genérico, broker, SignalR ou banco adicional.
- Não mudar mecanismo de autenticação, topologia ou versões-base sem necessidade comprovada.
- Não interpretar dados sintéticos como tarifa, moeda, fuso, mapa de vagas ou política de privacidade aprovados.

## Decisions

### D2-01 — Casos de uso e persistência única

Organizar módulos internos Identity, Vagas, Atendimentos, Tarifas e Auditoria nas camadas existentes. Application define casos de uso, DTOs de aplicação e portas específicas para persistência, criptografia, ator e relógio; Domain contém regras/valores sem EF ou Identity; Infrastructure implementa as portas; API converte HTTP e compõe dependências. Autorizar ações também na aplicação, não apenas em atributos/guards.

Evoluir o `IdentityDbContext` existente para incluir mapeamentos operacionais por arquivos separados, mantendo nome, conexão `Identity` e histórico de migrações nesta fase. É um nome técnico legado, não uma fronteira de domínio. `UserManager` e persistência operacional compartilham o contexto scoped e transações explícitas, permitindo que alterações administrativas e auditoria sejam atômicas. Não mover regras comerciais para esse contexto.

**Alternativas:** dois contextos/bancos exigiriam coordenação para auditoria de Identity; renomear agora acrescentaria migração de configuração sem valor funcional. Revisitar o nome em refatoração independente, se necessário.

### D2-02 — Modelo relacional e validação

| Registro | Campos/restrições relevantes |
| --- | --- |
| Vaga | GUID, código normalizado único, `rowversion`; situação derivada da ocupação aberta, sem booleano independente |
| Tarifa | GUID, valor `decimal(18,2)` positivo, publicação UTC, ator; versões imutáveis |
| ConfiguracaoTarifa | linha singleton sem valor inicial, referência opcional à versão vigente, `rowversion` para publicação concorrente |
| Atendimento | GUID interno, identificador público aleatório único, placa normalizada, modelo, nome do titular, documento cifrado, tarifa ID e valor snapshot, entrada UTC, operador, estado ativo, `rowversion` |
| Vistoria | GUID, atendimento, tipo entrada, texto cifrado, ator, UTC; única vistoria de entrada por atendimento |
| Ocupacao | GUID, vaga, atendimento, início UTC, fim anulável, ator; índice único filtrado por vaga com `Fim IS NULL`, e unicidade da ocupação por atendimento |
| EventoAuditoria | GUID, ator imutável, ação, alvo/atendimento opcional, UTC, metadados allowlisted; sem métodos de edição/exclusão |
| ResultadoIdempotente | ator + operação + chave únicos, HMAC do conteúdo canônico, versão do algoritmo, resultado mínimo e atendimento; sem payload sensível |

Chaves estrangeiras sem cascata que apague histórico. Índices adicionais: placa+entrada+ID e identificador público; ocupação por atendimento; auditoria por atendimento+UTC+ID. Ordenação usa ID como desempate para paginação determinística.

Defaults técnicos **propostos para revisão**, não regras comerciais novas: código de vaga 1–32 caracteres (trim/maiúsculas), placa 1–16 caracteres alfanuméricos após remover espaços/hífens e converter para maiúsculas; não presumir CPF ou apenas placa brasileira. Modelo/nome até 120/200, documento até 80 e vistoria até 4000 caracteres após trim, todos não vazios. E-mail e senha seguem validação Identity; e-mail normalizado único é garantido no banco. Paginação `page >= 1`, `pageSize` padrão 20 e máximo 100. Rejeitar excesso de precisão monetária, sem arredondar silenciosamente. Fuso/moeda de apresentação vêm de configuração local explícita; exemplos sintéticos não são aprovados para implantação.

Não criar unicidade de placa ativa ou histórica: isso não foi exigido no PRD. Não implementar edição/exclusão de vaga, atendimento ou vistoria nesta fase. Cada check-in gera um identificador público novo; formato técnico não significa ticket físico ou canal de entrega ao cliente.

**Alternativas:** status de vaga gravado separadamente pode divergir; unicidade de placa criaria regra comercial não aprovada; dinheiro em ponto flutuante não preserva precisão.

### D2-03 — Transação do check-in, tarifa e concorrência

1. Validar sessão/ator atual, permissão, CSRF, chave e DTO; normalizar conteúdo e obter seu HMAC.
2. Em transação SQL Server, localizar resultado por ator/operação/chave antes de validar disponibilidade atual. Se já confirmado, comparar HMAC e retornar resultado original; não recalcular tarifa ou horário.
3. Reservar a chave pela constraint única na própria transação; não publicar reserva sem commit. Ler referência de tarifa vigente com bloqueio compartilhado mantido até commit (`HOLDLOCK`/isolamento equivalente) para ordenar check-in e publicação. Publicação atualiza a linha singleton com versão-base, guardando versão e valor de uma única tarifa.
4. Validar vaga e inserir atendimento, snapshot, vistoria cifrada e ocupação; usar `rowversion` na atualização da vaga para detectar estado obsoleto e índice filtrado como garantia final da exclusividade.
5. Registrar eventos de entrada, atribuição e captura de tarifa, e resultado mínimo idempotente; confirmar uma única transação.

Atualização de vaga incrementa seu token técnico, mas não grava status duplicado. Violações de unicidade/concurrency conhecidas viram `409`; rollback integral antes de responder. Falha de auditoria impede o commit. MARS fica desabilitado; não depender de savepoints para reparar estado parcial.

Mesma chave em chamadas simultâneas: o perdedor de unicidade desfaz sua transação/contexto, relê o resultado confirmado em novo contexto e devolve o mesmo resultado; HMAC diferente gera conflito. Se o vencedor reverteu, a tentativa pode executar normalmente. Timeout de transporte não é prova de rollback. Repetir sempre a mesma chave; se houver retry de falha transitória/deadlock, executar a unidade completa com contexto novo e resolução por chave, nunca somente `SaveChanges` ou nova chave. Não habilitar retry cego de escrita.

**Alternativas:** consultar vaga livre sem constraint permite corrida; bloquear todas as vagas globalmente limita concorrência; aplicar tarifa no navegador ou após entrada viola o snapshot do PRD.

### D2-04 — Idempotência sem novo depósito de dados pessoais

Cabeçalho `Idempotency-Key` aceita UUID por ação. Canonicalização versionada contém apenas os campos normalizados efetivos, com ordenação fixa; não inclui cookies, CSRF ou valores derivados pelo servidor. Não guardar JSON de entrada. Usar HMAC-SHA-256, em vez de hash simples, porque o documento tem baixa entropia. Segredo HMAC externo obrigatório e versionado; manter versões usadas enquanto seus resultados existirem, para comparar retries após rotação. Nunca utilizar senha de conta como chave.

Resultado persistido inclui somente identificador, ID técnico, vaga, estado e horário, com status HTTP original; não contém documento, vistoria ou tarifa. Replay devolve resultado original após reautorizar o ator. Outra pessoa não acessa o resultado de uma chave alheia: escopo inclui ator. Não implementar expiração/limpeza nesta fase; TTL e descarte continuam pendentes e nenhum prazo será arbitrado. UI mantém conteúdo e chave de resultado desconhecido apenas em memória, sem `localStorage`/`sessionStorage`; navegação/reload avisa sobre possível confirmação e orienta busca antes de nova entrada.

**Alternativas:** hash não autenticado facilita inferência de documento; guardar resposta ampla replica dados sensíveis; TTL arbitrário pode permitir nova entrada de um retry antigo.

### D2-05 — Administração e revogação de acesso

Novos casos de uso: listar/criar funcionários; substituir conjunto de perfis fixos; ativar/desativar. Aceitar conjunto não vazio dos perfis existentes, preservando o modelo atual de múltiplos papéis. Senha inicial é enviada pelo administrador via origem segura e processada por Identity, nunca retornada/logada. Recuperação de senha, convite/e-mail e permissões customizadas não entram nesta fase.

Transação scoped envolve Identity, atualização do security stamp e evento de auditoria. Usar token de concorrência da conta no contrato; serializar alterações administrativas que possam remover o último administrador usando bloqueio transacional comum em linha de controle de acessos, contando administradores ativos dentro do bloqueio. Isso impede duas remoções simultâneas que individualmente pareceriam seguras. Nenhuma exclusão física de funcionário.

Na autenticação do cookie, revalidar a cada requisição conta ativa e security stamp atual, rejeitando principal desatualizado. Manter o validador Identity e configurar intervalo zero ou composição equivalente comprovada por teste; mudança de perfis/estado atualiza stamp explicitamente na mesma transação. Falha de banco nega acesso de forma controlada. Não prometer cancelamento de requisição que já havia passado pela autenticação antes da alteração; o contrato é a próxima requisição após commit. `AuthService` limpa estado em `401` e exige login novamente.

O provisionador sintético atual pode reatribuir o perfil padrão no startup: preservar o bootstrap idempotente sem reativar, redefinir senha ou restaurar perfis de contas existentes modificadas pela administração. Criar/atribuir defaults somente para contas sintéticas novas, com teste de reinício.

**Alternativas:** aguardar o intervalo padrão do security stamp deixa privilégios antigos utilizáveis; checar `IsActive` somente em `GetSession` não protege novos endpoints. Custo de consulta por requisição é aceito nesta unidade e deverá ser medido no piloto.

### D2-06 — Criptografia, projeções e trilha

Criar porta de proteção de campo com implementação local Data Protection usando propósitos separados e versionados para documento e vistoria, vinculados ao atendimento/campo; isso impede trocar ciphertext entre campos/atendimentos. Reutilizar persistência local de key ring e restringir acesso ao diretório; não usar proteção temporal. Envelope contém versão de formato e payload protegido, que referencia a chave. Guardar material histórico para leitura após rotação; testar troca de chave, reinício e erro por chave ausente. Essa solução é local: gestão de chaves/backup e adequação a retenção prolongada exigem aprovação na Fase 5.

DTOs de listas omitem documento e vistoria inteiramente. Detalhe comum omite documento completo; snapshot financeiro é projetado apenas para caixa/admin. Documento e vistoria têm endpoints GET explícitos separados: documento só caixa/admin; vistoria para os três perfis conforme TDD. Auditar acesso sem texto sensível, confirmar auditoria antes de retornar conteúdo; falha ao auditar ou decifrar retorna erro controlado. Histórico operacional é projetado por perfil, não serializa metadados internos irrestritos.

Expandir política `private, no-store` hoje limitada a `/session` para todas as respostas privadas. Logs estruturados incluem correlation ID, ação, ator interno, alvo e resultado; não logar corpos, placa, nome, documento, vistoria, senha ou cookies. Auditoria é append-only na aplicação e não exposta a endpoints de escrita. A proteção contra alteração por DBA privilegiado e segregação real de contas são gates de produção, não garantias dos testes locais.

**Alternativas:** criptografia só do disco não protege colunas; decifrar listagens aumenta exposição; criar KMS/Azure agora viola faseamento; logs técnicos não substituem histórico transacional.

### D2-07 — Contratos HTTP

Todos sob `/api/v1`, no mesmo proxy `/api`; controllers finos, DTOs explícitos e OpenAPI gerada com pacote compatível com .NET 10, fixado centralmente/lockfile na implementação. Não adicionar Swagger UI público por padrão. Manter sessão existente.

| Contrato proposto | Perfis / comportamento |
| --- | --- |
| `GET/POST /employees` | admin; lista paginada/criação com e-mail, senha inicial e conjunto de perfis |
| `PUT /employees/{id}/access` | admin; ativo, perfis e token de concorrência |
| `GET /spaces?code=&status=&page=&pageSize=` | três perfis; código, ID, livre/ocupada e versão |
| `POST /spaces` | admin; código |
| `GET /daily-rates` e `GET /daily-rates/current` | admin; histórico e configuração com versão-base, mesmo antes da primeira tarifa |
| `POST /daily-rates` | admin; valor e versão-base da configuração |
| `POST /attendances` | caixa/admin; campos de entrada + vaga, `Idempotency-Key`; `201` e `Location` |
| `GET /attendances?plate=&identifier=&page=&pageSize=` | três perfis; busca exata normalizada por um dos filtros, sem listar tudo por omissão |
| `GET /attendances/{id}` | três perfis; detalhe projetado, financeiro só caixa/admin |
| `GET /attendances/{id}/document` | caixa/admin; leitura explícita e auditada |
| `GET /attendances/{id}/entry-inspection` | três perfis; leitura explícita e auditada |
| `GET /attendances/{id}/history` | três perfis; eventos paginados e filtrados por perfil |

Horários ISO 8601 UTC; valores decimais e tokens de concorrência opacos/base64. Respostas paginadas `{items,page,pageSize,total}`. Proibir propriedades inesperadas no DTO de criação, especialmente tarifa, operador e horário, para não sugerir que foram aplicadas.

Padronizar erros de negócio em Problem Details com `code`, erros por campo e correlation ID: `400` formato/CSRF/chave ausente, `401` sessão inválida, `403` perfil/origem, `404` alvo inexistente, `409` duplicidade/concurrency/chave incompatível/vaga ocupada, `422` tarifa ausente ou outra pré-condição de negócio. `5xx` genérico sem stack trace. Endpoint atual de sessão preserva compatibilidade; a política de não-cache se amplia. Cancelamento/timeout não autoriza gerar chave nova automaticamente.

**Alternativas:** DTO EF exposto vaza campos; retornar todos os atributos e esconder na UI não implementa autorização; GET de configuração para manobrista viola restrição financeira.

### D2-08 — Interface e atualização

Adicionar shell autenticado com rotas por funcionalidade: `features/admin` (acessos, vagas e tarifas), `features/check-in`, `features/spaces` e `features/attendances`; componentes compartilhados para estados vazios/erros, paginação e confirmação. Reutilizar Signals/Signal Forms, Tailwind/Lucide e controles nativos. Guards ajudam navegação, nunca substituem API.

Centralizar mutações HTTP com token antiforgery novo obtido na identidade atual (inclusive após login); manter credenciais apenas em memória/cookie. Polling de vagas configurável, cancelável e sem sobreposição; pausar com aba oculta, refetch ao foco e após mutação, desfazer subscriptions ao sair. Disponibilidade exibida é informativa, servidor decide. Tela de entrada preserva valores na rejeição conhecida; após resultado desconhecido bloqueia edição da tentativa e oferece repetição com chave original. Após confirmação limpa documento/vistoria do formulário e mostra identificador sem emitir ticket.

Teclado completo, foco visível, rótulos associados, estados textuais/`aria-live`, erros por campo e foco no resumo; sem depender só de cor. Fuso/moeda de teste explícitos, sem assumir parâmetros reais. Não liberar vagas por botão de demonstração; cada E2E prepara banco sintético isolado pelo harness, sem endpoint público de reset.

**Alternativas:** timer independente da requisição sobrepõe chamadas; storage persistente mantém PII e tentações de replay; Material ou nova biblioteca de estado não são necessários.

### D2-09 — Verificação e rastreabilidade

| PRD / specs | Verificação mínima |
| --- | --- |
| RF-01/02, vehicle-check-in/space-availability | unidade para validações; SQL real para migração vazia/upgrade, índice filtrado, disputa com conexões independentes e rollback injetado |
| RF-07 (configuração/snapshot), daily-rate | publicação concorrente, ausência de tarifa, troca A/B preservando atendimento A e captura coerente durante corrida |
| RF-03/10, attendance-query/employee-session/employee-management | matriz HTTP de todos os endpoints, sessão anterior à revogação, cabeçalhos falsos, paginação, múltiplas visitas e acesso direto sem guard |
| RF-11, operational-audit | eventos/ator/UTC, ausência de duplicação em replay e impossibilidade de alteração pela API; falha de auditoria desfaz comandos e bloqueia leitura sensível |
| RNF-02/03/07 | inspeção de colunas/logs/respostas, rotação/reinício/chave ausente, HMAC sem corpo salvo, mesmo/diferente conteúdo e corrida da mesma chave; simular resposta perdida após commit |
| RNF-04/05, specs UI | Vitest para validação/feedback, expiração e polling; Playwright por edge HTTPS para admin preparar, caixa entrar/buscar e manobrista consultar sem finanças/documento, também por teclado |

Criar `Estacionamento.UnitTests` MSTest, preservando projetos de arquitetura/integração e testes de regressão. Usar SQL Server real em banco dedicado com conexões/contextos independentes nas corridas; não inferir concorrência de execuções sequenciais. A fixture atual manipula variáveis globais: evitar paralelizar factories que compartilham processo/ambiente; concorrência é dentro do cenário com clientes independentes. Casos de placas repetidas podem usar entradas em vagas distintas, sem criar entrega artificial. Continuar sem CI nesta change; execução automatizada local é suficiente para o gate local, não para produção.

## Risks / Trade-offs

- [Escopo administrativo introduz invalidação de sessão] → testes com cookie prévio e bootstrap reiniciado; manter papéis fixos e rejeitar acesso obsoleto antes do replay.
- [Bloqueios de tarifa/admin e conflitos de vaga] → locks curtos por alvo, constraints finais, mapear deadlocks sem efeitos parciais e medir carga depois; não prometer SLA ainda não definido.
- [Perda de chave torna vistoria/documento ilegíveis] → key ring persistente, teste de rotação/reinício, sem limpeza automática; backup e restore aprovados seguem pendentes de produção.
- [Crescimento de auditoria/idempotência] → índices e paginação; retenção/TTL continuam pendentes, sem job destrutivo inventado.
- [Preparação sintética confundida com operação real] → somente dados de teste, sem seed de vagas/tarifa reais, sem publicação; manter `docs/pendencias-implantacao.md` aberto.
- [Aprovação do planejamento confundida com gate concluído] → tarefas começam desmarcadas; evidências, revisão e aprovação só são registradas quando efetivamente realizadas.

## Migration Plan

1. Revisar proposta/specs/design/tarefas antes de aplicar. PRD/TDD existentes são pré-requisitos aprovados; não precisam ser recriados. Se a revisão mudar regras aprovadas, atualizar/validar PRD → TDD antes de implementar.
2. Na feature branch, criar migrações aditivas após as duas de Identity existentes: tabelas operacionais, constraints/índices, unicidade de e-mail e linhas técnicas de configuração/controle (sem tarifa comercial inicial). Conferir duplicatas antes de índice único em banco persistido; falhar com diagnóstico, nunca remover contas automaticamente.
3. Aplicar e verificar apenas em bancos locais isolados: tanto vazio até última migração quanto upgrade de banco Identity com contas existentes. `Development` mantém bootstrap antes de servir; `Testing` usa aplicação explícita de migrações na fixture, demais ambientes não migram/provisionam no startup. Validar novos segredos externos sem valores no git.
4. Executar todas as suítes e registrar comandos/resultados/limites em `docs/fase-2-preparacao-entrada-evidencias.md` durante implementação; atualizar instruções locais e exemplos de configuração sem dados reais.
5. Rollback local do aplicativo conserva o esquema aditivo e dados; a versão anterior não poderá ler os módulos novos. Não executar `Down` destrutivo como recuperação de release. Bancos descartáveis de teste podem ser recriados pelo harness isolado, nunca por endpoint de negócio.
6. Após implementação e verificações, preparar PR para `main` usando `.github/pull_request_template.md`, ligando RFs, decisões D2 e specs. Não abrir PR de implementação concluída agora, não fazer merge direto. Azure/migration publicada/CD ficam na Fase 5.

## Open Questions

Apenas pendências de implantação já existentes, sem alterar esta fatia local: tarifa inicial real, fuso/moeda, mapa de vagas, usuários reais, retenção/descarte, formato de entrega do identificador, metas e operação Azure. Ver PRD §12 e `docs/pendencias-implantacao.md`; dados sintéticos não resolvem essas pendências.

### Referências técnicas verificadas para o design

- [EF Core: transações](https://learn.microsoft.com/ef/core/saving/transactions), [concorrência](https://learn.microsoft.com/ef/core/saving/concurrency) e [índices](https://learn.microsoft.com/ef/core/modeling/indexes).
- [Identity: security stamp](https://learn.microsoft.com/aspnet/core/security/authentication/identity-configuration) e [validação do principal de cookie](https://learn.microsoft.com/aspnet/core/security/authentication/cookie).
- [Data Protection: configuração](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview) e [propósitos](https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/purpose-strings).
- [Angular: Signal Forms](https://angular.dev/guide/forms/signals/overview) e [cancelamento com ciclo de vida](https://angular.dev/ecosystem/rxjs-interop/take-until-destroyed). A consulta Context7 da branch Angular 22 confirmou cancelamento; a sintaxe específica de forms seguirá a base 22.2 já usada e testes locais.
