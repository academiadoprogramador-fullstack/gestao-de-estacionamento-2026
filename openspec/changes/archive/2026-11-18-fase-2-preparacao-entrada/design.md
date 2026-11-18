# Design

## Context

Ver `proposal.md` — Why. A Fase 1 já entregou solução em camadas, JWT/sessão revogável, configuração validada, chaves AES versionadas e bootstrap de `Development`; a Fase 2 acrescenta o primeiro domínio operacional sobre essa base. Restrições que moldam o desenho:

- Stack fixada no TDD §2: .NET 10, EF Core/SQL Server, Angular 22 com Signals/Signal Forms, MSTest/Vitest/Playwright; nenhuma dependência de runtime nova é necessária.
- Identity e operações compartilham o mesmo `DbContext` e a mesma fronteira transacional (TDD §3/§4.1), sem segundo banco ou repositório genérico.
- A API é a autoridade de perfil, horário, operador, vaga e tarifa; a SPA apenas complementa com guards e feedback.
- Os protótipos revisados (`prototypes/index.html` e as cinco telas) já fixaram layout, estados e interação; este documento decide a implementação por trás deles.
- Nada desta fase publica Azure/R2, executa CI/CD ou implementa retirada, cobrança, pagamento, entrega, fotos ou relatórios.

## Goals / Non-Goals

**Goals:**

- Migração aditiva que preserva integralmente os dados e o esquema da Fase 1.
- Check-in atômico com snapshot imutável, vistoria cifrada, ocupação exclusiva e replay idempotente.
- Administração de acessos concorrente e auditada, com proteção do último administrador ativo.
- Projeções por perfil calculadas no servidor, com leitura sensível explícita e auditada.
- Protocolo de cifragem e de idempotência reutilizável pelas Fases 3–4 sem retrabalho.
- Comportamento de UI verificável: polling sem sobreposição, descarte de respostas tardias e tentativa incerta somente em memória.

**Non-Goals:**

- Definir política comercial de cobrança, diárias ou descontos; a Fase 2 só captura o snapshot.
- Editar ou excluir vagas, atendimentos, vistorias ou versões de tarifa.
- Criar endpoints de retirada, caixa, entrega, relatórios ou fotos, ou liberar vaga manualmente.
- Implementar recuperação/reenvio de senha, convite, autorregistro ou perfis customizados.
- Alterar autenticação, CORS, rate limit ou bootstrap da Fase 1.

## Decisions

### D1. Extensão modular e fronteiras de camadas

Manter a direção de dependências da Fase 1 e distribuir a fase por funcionalidade:

- **Domain:** `Vaga` e `CodigoVaga`; `TarifaDiaria`/`ValorDiaria`; `Atendimento`, `VistoriaEntrada`, `Ocupacao`, `EventoAtendimento`; `IdentificadorAtendimento` e regras de normalização/validação; sem EF ou HTTP.
- **Application:** casos de uso (`CriarFuncionario`, `AlterarAcessoFuncionario`, `ListarFuncionarios`, `CadastrarVaga`, `ConsultarVagas`, `PublicarTarifa`, `ConsultarTarifaCorrente`, `ListarTarifas`, `RegistrarEntrada`, `BuscarAtendimentos`, `ObterAtendimento`, `ListarHistoricoAtendimento`, `LerDocumentoTitular`, `LerVistoriaEntrada`), portas específicas (`IVagas`, `ITarifas`, `IAtendimentos`, `IAcessosFuncionario`, `IAuditoriaNegocio`, `IIdempotencia`, `ICriptografiaCampos`, `IRelogio`), autorização por ação e projeções por perfil.
- **Infrastructure:** mapeamentos EF, consultas projetadas, implementação da administração de acessos sobre Identity, AES-GCM, canonicalização SHA-256 e repositórios específicos.
- **Api:** controllers finos em `/api/v1`, DTOs de requisição sem campos do servidor, Problem Details e OpenAPI.

Não criar agregado único para toda a operação nem repositório genérico. `Atendimento` é o agregado que protege snapshot, vistoria e ocupação na confirmação; `Vaga` e `TarifaDiaria` são agregados próprios.

**Alternativas:** um módulo único de “operação” concentraria regras heterogêneas e dificultaria as fases seguintes; microsserviços/segundo banco contrariam DT-04/DT-05 e a transação única exigida.

### D2. Administração de acessos

- `GET /api/v1/employees?page=&pageSize=&status=` — somente Administrador, paginação 1/20/100 e projeção sem credenciais.
- `POST /api/v1/employees` — nome, e-mail normalizado, senha inicial e ao menos um dos três perfis fixos; usa `UserManager.CreateAsync`, não retorna senha/hash e audita na mesma operação.
- `PUT /api/v1/employees/{id}/access` — estado ativo, conjunto de perfis e token de concorrência opaco da conta, distinto do security stamp do Identity (TDD §5.2). O token é a representação Base64 do `rowversion` de `UsuarioAplicacao`.
- A alteração atualiza `VersaoAcesso`, persiste perfis/estado, revoga `SessoesAutenticacao` e `RefreshTokens` e grava auditoria na mesma transação.
- A proteção do último administrador é serializada com transação `Serializable` e leitura bloqueante das contas administradoras ativas (`UPDLOCK, HOLDLOCK`). `rowversion` sozinho não expressa “pelo menos um”; um índice único filtrado não cobre cardinalidade. A alternativa de checar antes de atualizar sob `READ COMMITTED` permitiria duas remoções concorrentes.
- Nome é obrigatório porque a identidade da Fase 1 já expõe `Nome`; não há endpoint de troca de senha nesta fase.

**Concorrência:** conflito devolve `409` com código `stale_access_version` e o estado atual recarregável, sem sobrescrita silenciosa.

### D3. Vagas e disponibilidade

- `Vaga`: `Id` (uniqueidentifier), `Codigo` normalizado, `CodigoNormalizado` com índice único, `RowVersion`, auditoria de criação. Não há edição/exclusão nesta fase.
- `Ocupacao`: `Id`, `VagaId`, `AtendimentoId`, `InicioUtc`, `FimUtc` nulo enquanto aberta, `ResponsavelId`; índice único filtrado em `VagaId WHERE FimUtc IS NULL` e outro em `AtendimentoId WHERE FimUtc IS NULL`.
- A situação é derivada por `EXISTS` na consulta; não há coluna “livre” que possa divergir da ocupação.
- `GET /api/v1/spaces` aceita `code`, `status`, `page`, `pageSize`, ordena por código e devolve `{items,page,pageSize,total}`.
- `POST /api/v1/spaces` é exclusivo do Administrador, normaliza (trim + maiúsculas, 1–32), e devolve `409` em duplicidade.

**Alternativas:** uma coluna de situação simplificaria a leitura, mas criaria duas fontes de verdade e exigiria compensação em cada falha; reserva na consulta foi descartada pelo PRD §5.1.

### D4. Tarifa versionada e snapshot

- `TarifaDiaria`: `Id`, `Versao` inteira com índice único, `Valor decimal(18,2)` com `CHECK (Valor > 0)`, `PublicadoPorId`, `PublicadoEmUtc`. Versões são imutáveis e nunca recifradas.
- `ConfiguracaoTarifa`: registro singleton (`Id = 1`) com `VersaoCorrenteId` anulável e `RowVersion`; ausência de versão significa “sem tarifa publicada”.
- `POST /api/v1/daily-rates` exige `expectedBaseVersion` (versão corrente ou “nenhuma”), abre transação, bloqueia a configuração com `UPDLOCK`, insere a versão, atualiza o ponteiro e audita. Conflito de base devolve `409`.
- O atendimento grava `TarifaVersaoId` **e** `TarifaValorSnapshot`; a versão é imutável e o valor denormalizado garante o snapshot mesmo se a leitura da versão falhar no futuro.
- A API rejeita escala maior que duas casas antes de converter, sem arredondar.

**Alternativas:** ler `MAX(Versao)` para publicar criaria corrida entre duas publicações; guardar apenas a FK reduziria duplicação, mas não materializa o valor exigido por RF-07/DT-14.

### D5. Check-in transacional

Fluxo do caso de uso `RegistrarEntrada`, na ordem exigida pelo TDD §4.1:

1. Reautorizar ator e perfil (Caixa/Administrador) e validar o DTO (sem tarifa, horário, operador ou identificador do cliente).
2. Normalizar placa (remover espaços/hífens, maiúsculas, 1–16 alfanuméricos), textos e vaga; validar limites 120/200/80/4000.
3. Reservar a chave idempotente e consultar repetição antes de reavaliar tarifa/vaga (D7).
4. Ler a configuração de tarifa corrente em transação, mantendo bloqueio compartilhado (`HOLDLOCK`) até o commit para que a publicação não troque o ponteiro no meio da captura. Sem tarifa → `422 rate_not_published`.
5. Ler a vaga e sua ocupação aberta com `rowversion`; vaga inexistente/ocupada → `409 space_unavailable`.
6. Inserir `Atendimento`, `VistoriaEntrada` cifrada, `Ocupacao` aberta, eventos append-only e resultado idempotente; confirmar tudo no commit.
7. Devolver `201`, `Location` e resultado mínimo (identificador, vaga, estado, horário), sem documento, vistoria ou tarifa.

O identificador público é gerado pelo servidor a partir de uma `SEQUENCE` SQL e formatado `AT-{anoUTC}-{sequência:D6}` com índice único. Ele é legível/copiável, não sequencial por unidade e não é usado como chave primária.

Disputas: violação do índice filtrado de ocupação ou de versão resulta em `409` e rollback; violação da chave idempotente é tratada em contexto novo (D7). Não há retry cego de escrita nem liberação manual de vaga.

**Alternativas:** gerar identificador no cliente facilitaria a cópia, mas violaria “campos do servidor” e permitiria colisão dirigida; GUID público é menos legível para conferência presencial.

### D6. Contratos, projeções e leitura sensível

Rotas da fase (todas em `/api/v1`):

| Rota | Perfis | Contrato |
| --- | --- | --- |
| `GET/POST /employees` | Admin | Lista paginada e criação sem retorno de credencial |
| `PUT /employees/{id}/access` | Admin | Estado, perfis e token de concorrência |
| `GET /spaces`, `POST /spaces` | Três perfis / Admin | Consulta paginada e cadastro |
| `GET /daily-rates`, `GET /daily-rates/current`, `POST /daily-rates` | Admin | Histórico, corrente e publicação |
| `POST /attendances` | Caixa/Admin | Check-in com `Idempotency-Key` |
| `GET /attendances?plate=&identifier=&page=&pageSize=` | Três perfis | Exatamente um filtro, busca exata |
| `GET /attendances/{id}`, `GET /attendances/{id}/history` | Três perfis | Projeções por perfil |
| `GET /attendances/{id}/document` | Caixa/Admin | Leitura explícita e auditada |
| `GET /attendances/{id}/entry-inspection` | Três perfis | Leitura explícita e auditada |

- Listagens devolvem `{items,page,pageSize,total}`, ordenam por `EntradaUtc` e identificador e omitem documento e vistoria.
- `AtendimentoDetalhe` comum não inclui campos sensíveis; para Manobrista, o DTO não possui as propriedades de tarifa/valores (omissão de contrato, não apenas da tela). O histórico usa allowlist de eventos por perfil.
- Leitura sensível: autorizar → inserir evento de auditoria e **confirmar o commit** → decifrar → responder `no-store`. Falha de auditoria ou decifragem impede a resposta. A auditoria guarda ator, alvo, tipo e UTC, nunca o conteúdo.
- Erros seguem o Problem Details da Fase 1 (`status`, `title`, `type`, `instance`, `code`, `correlationId`, `errors`), com códigos estáveis: `stale_access_version`, `space_unavailable`, `stale_rate_version`, `rate_not_published`, `idempotency_conflict`, `field_protection_failure`.
- Requisições rejeitam propriedades desconhecidas (`UnmappedMemberHandling.Disallow` no System.Text.Json) para que tarifa/horário/operador enviados pelo cliente não sejam silenciosamente ignorados. A mudança é coberta por contrato e por testes de compatibilidade.
- A API mantém `Cache-Control: no-store` em respostas e erros, e o OpenAPI é atualizado com os novos endpoints e exemplos, sem executar bootstrap durante a geração.

**Alternativas:** um DTO único filtrado na tela exporia campos ao Manobrista no JSON; aceitar propriedades desconhecidas contrariaria o PRD §5.1.

### D7. Protocolo de idempotência

- Cabeçalho `Idempotency-Key` UUID obrigatório no `POST /attendances`; tabela `ResultadosIdempotentes` com unicidade `(AtorId, Operacao, Chave)`.
- Conteúdo canônico versão 1: serialização determinística com propriedades em ordem fixa contendo operação, ator, alvo, valores normalizados e versões-base pertinentes (`expectedBaseVersion`, vaga, tarifa esperada quando aplicável). SHA-256 binário de 32 bytes, sem segredo HMAC e sem corpo persistido.
- A versão de canonicalização e o hash são gravados com o resultado mínimo (`Identificador`, `Vaga`, `Estado`, `EntradaUtc`, `Location`). O replay usa a versão persistida, reautoriza a sessão e o perfil, compara o hash e devolve `201` com o mesmo resultado, sem reexecutar efeitos.
- Conteúdo divergente → `409 idempotency_conflict`. Sessão revogada → `401` antes de qualquer replay. Ator diferente → escopo separado, nunca acessa o resultado alheio.
- Disputa da mesma chave: a segunda transação falha na unicidade; após rollback, o caso de uso relê o resultado confirmado em contexto novo e o devolve se o conteúdo coincidir.
- Retenção acompanha o atendimento: não há TTL autônomo; registros de atendimento ativo ou com saldo não são limpos, e o descarte segue a retenção de 365 dias após encerramento/quitação (fases futuras).

**Alternativas:** HMAC exigiria mais um segredo sem resolver anonimização; persistir o corpo aumentaria a exposição de PII; comparar apenas a chave devolveria resultado incorreto para conteúdo divergente.

### D8. Cifragem de campos

- Porta `ICriptografiaCampos` com operações por propósito (`documento`, `vistoria-entrada`; as fases seguintes acrescentam `exit-inspection` e justificativas).
- Envelope binário: `[versãoFormato:1][tamanhoKid:1][kid UTF-8][nonce:12][tag:16][ciphertext]`, persistido em `varbinary(max)`. AES-256-GCM fixo, tag de 16 bytes, nonce novo por cifragem.
- AAD determinístico reconstruído no servidor: `v1|ambiente:{Development|Testing|Production}|app:Estacionamento|proposito:{...}|atendimento:{guid}`. Não inclui estado/valor mutável e nunca vem do cliente.
- Chaves vêm do mapa AES já validado na Fase 1 (32 bytes por versão, ativa para escrita, anteriores para leitura), separadas de JWT e backup. Rotação não recifra dados.
- `kid` desconhecido, envelope inválido, tag adulterada, troca entre registro/campo/ambiente ou chave ausente resultam em erro controlado `field_protection_failure`, sem texto puro, vazio ou tentativa de outra chave.
- O documento e a vistoria nunca entram em logs, telemetria, auditoria ou respostas de lista.

**Alternativas:** JSON/Base64 simplificaria inspeção, mas aumenta o payload e o risco de o envelope ser tratado como texto; Data Protection/key ring foi descartado por DT-09.

### D9. SPA: funcionalidades, polling e comando incerto

- `features/funcionarios`, `features/vagas`, `features/tarifas`, `features/check-in`, `features/atendimentos`; `shared` recebe tabela paginada, resumo de erros, diálogo de confirmação/leitura, botão copiar e estados vazios/carregando/erro.
- Serviços HTTP por funcionalidade, com `HttpClient` tipado e cancelamento; sessão permanece apenas em memória (Fase 1).
- **Polling de vagas:** serviço com intervalo configurável (30 s), `visibilitychange` para pausar/retomar, atualização após comando e no foco, flag de requisição em andamento para impedir sobreposição e `takeUntilDestroyed`.
- **Check-in:** `CoordenadorComando` mantém `{chave, conteúdo, fase}` em memória. `pending`/`uncertain` bloqueiam `CanDeactivate`, logout e navegação, armam `beforeunload` e desabilitam o formulário. Replay reenvia exatamente a mesma chave e o mesmo conteúdo; rejeição definitiva gera nova chave somente na próxima submissão explícita; sucesso limpa documento, vistoria e chave da tela.
- **Respostas tardias:** cada busca/detalhe/histórico/leitura sensível recebe um token de contexto; troca de seleção, fechamento, logout ou destruição incrementam o token e descartam a resposta (além de `unsubscribe`/cancelamento HTTP). Leituras sensíveis nunca são cacheadas.
- **Identificador:** texto selecionável e botão “copiar” com fallback quando a Clipboard API falhar; confirmação textual acessível.
- **Acessibilidade:** Signal Forms com labels, `aria-invalid`, `aria-describedby`, resumo de erros focado, foco visível, diálogos com foco contido e retorno de foco, linhas de tabela operáveis por teclado e estados textuais.

**Alternativas:** `switchMap` sozinho cobre busca, mas não protege leituras disparadas por diálogo nem logout; persistir a tentativa em storage violaria DT-20/PRD §5.3.

### D10. Persistência, migração e índices

Migração aditiva única `Fase2PreparacaoEntrada`, sem alterar colunas existentes além de acrescentar `RowVersion`/token de concorrência à conta:

- `Vagas` (`Id`, `Codigo`, `CodigoNormalizado` único, `CriadoPor`, `CriadoEmUtc`, `RowVersion`).
- `TarifasDiarias` (`Id`, `Versao` único, `Valor decimal(18,2)` com check positivo, autor, UTC) e `ConfiguracaoTarifa` singleton com FK anulável e `RowVersion`.
- `Atendimentos` (`Id`, `IdentificadorPublico` único, `Placa` normalizada, `Modelo`, `Titular`, `DocumentoTitularCifrado`, `TarifaVersaoId`, `TarifaValorSnapshot`, `EntradaUtc`, `OperadorId`, `Estado`, `RowVersion`).
- `VistoriasEntrada` (`AtendimentoId` único, `TextoCifrado`, ator, UTC) e `Ocupacoes` (`VagaId`, `AtendimentoId` único aberto, início/fim, responsável) com os índices filtrados de D3.
- `EventosAtendimento` append-only (tipo, ator opaco, UTC, correlação, referências opacas, sem conteúdo sensível) e `ResultadosIdempotentes` (D7).
- FKs `NO ACTION`, sem cascata que apague histórico; `Atendimentos` referencia vaga/tarifa sem permitir exclusão; `rowversion` em `Atendimentos`, `Vagas`, `ConfiguracaoTarifa` e `UsuarioAplicacao`.
- `SEQUENCE SequenciaIdentificadorAtendimento` para o identificador público.

O `DbContext` continua único para Identity e operações. Consultas de lista não rastreiam entidades e usam projeções explícitas; consultas de comando usam rastreamento e transação explícita.

### D11. Verificação

- **MSTest unidade:** normalização de placa/código, limites, escala decimal, regra do último administrador, composição/leitura de envelope, AAD, canonicalização determinística e por versão, allowlist de histórico e coordenador de comando.
- **MSTest integração (SQL Server real):** migração aditiva sobre catálogo da Fase 1; unicidade filtrada; duas entradas concorrentes na mesma vaga (um `201`, um `409`); publicação concorrente de tarifa; alterações concorrentes de acesso; replay idêntico/divergente/sessão revogada; rollback de auditoria; leitura auditada antes do conteúdo; cifragem ida/volta, adulteração, troca de registro/campo/ambiente, chave ausente e rotação com reinício; ausência de PII em logs; paginação completa.
- **Contrato/API:** OpenAPI versus respostas reais, Problem Details, `400` para propriedades desconhecidas, `401/403` por perfil, `409` de concorrência/vaga/idempotência, `422` sem tarifa, `no-store`, CORS com `Idempotency-Key`.
- **Vitest:** polling sem sobreposição e pausa em aba oculta, resumo/foco/`aria-invalid` sem chamada inválida, descarte de respostas tardias, bloqueio de rota/logout com comando incerto, reuso da chave e limpeza de campos.
- **Playwright (duas origens HTTPS):** administrador cria funcionário, vaga e tarifa; caixa faz check-in; replay após perda de resposta; conflito de vaga; manobrista negado no check-in e sem documento/finanças; leituras auditadas; sessão revogada; respostas fora de ordem; navegação por teclado.

Resultados e limitações só serão registrados após execução, conforme AGENTS.md e TDD §7.1.

### D12. Operação e segurança

- Nenhum segredo novo em arquivo versionado; as chaves AES já configuradas passam a cifrar conteúdo real e a ausência da versão ativa impede tráfego.
- Logs estruturados com correlação, ator opaco, ação, resultado e latência; sem placa, nome, documento, vistoria, valores ou justificativas.
- Auditoria de negócio append-only no SQL, separada dos logs técnicos; não há endpoint de escrita da trilha.
- O bootstrap de `Development` continua criando apenas as três contas sintéticas e não altera contas criadas pela administração.

## Prototype

Os protótipos revisados em `prototypes/` são a referência de interface:

- `prototypes/administracao-funcionarios.html` — criação, alteração, conflito e bloqueio do último administrador (D2).
- `prototypes/vagas.html` — cadastro, filtros, paginação, polling e pausa em aba oculta (D3/D9).
- `prototypes/tarifas.html` — ausência de tarifa, publicação, conflito de base e histórico (D4).
- `prototypes/check-in.html` — validação acessível, seletor de vaga, comando pendente/incerto, replay, rejeição e identificador copiável (D5/D7/D9).
- `prototypes/atendimentos.html` — busca, visitas distintas, projeções, leituras auditadas e descarte de resposta tardia (D6/D9).
- `prototypes/index.html` — visão de revisão dos fluxos.

Divergências intencionais entre protótipo e implementação: o seletor de perfil no cabeçalho e os controles “Cenários do protótipo” existem apenas para revisão; o seletor de vaga real consulta o endpoint paginado e não mantém uma lista fixa; o protótipo simula latência/rede com temporizadores, enquanto a implementação usa HTTP real, cancelamento e tokens de contexto; os dados são fictícios e não há armazenamento.

Os textos de interface estão em português brasileiro, com BRL, `pt-BR` e `America/Sao_Paulo`, alinhados à implementação da Fase 1. Essa é uma divergência consciente da recomendação genérica de protótipos em inglês do schema `prototype-first`, solicitada para a revisão desta fase.

## Risks / Trade-offs

- **Transação `Serializable` na alteração de acesso** → contenção breve e previsível, restrita a operações administrativas; alternativa sem garantia permitiria zero administradores.
- **Bloqueio compartilhado da tarifa durante o check-in** → uma publicação pode esperar o commit de entradas em andamento; volume acadêmico é baixo e a alternativa aceitaria leitura de ponteiro em transição.
- **Canonicalização própria** → exige disciplina de versão e testes; mitigada por versão persistida, vetores de teste e escopo explícito.
- **Rejeição global de propriedades desconhecidas** → clientes futuros precisam versionar contratos; coberta por OpenAPI e testes de contrato.
- **Cifragem sem recifragem automática** → chaves antigas precisam ser preservadas; perda de chave é irrecuperável por desenho, mitigada pelo procedimento de backup da Fase 6.
- **Filtro por perfil apenas no DTO** → qualquer novo endpoint deve reutilizar as projeções; testes de contrato por perfil reduzem o risco de regressão.
- **Tentativa incerta somente em memória** → recarregar/fechar o navegador perde a tentativa; é decisão explícita do PRD §5.3, não um defeito a corrigir com storage.
- **Sequência pública de identificador** → revela volume de entradas a usuários autorizados; aceitável no escopo sintético e sem impacto na autorização.
- **Índice filtrado e `rowversion`** → específicos do SQL Server; o alvo é SQL Server/Azure SQL, sem suporte a outro banco nesta fase.
- **Polling e respostas tardias** → exigem cancelamento e tokens em todas as telas; verificados por Vitest/Playwright e revisão de código.

## Migration Plan

1. Criar migration aditiva e mapeamentos; aplicar sobre um catálogo da Fase 1 e verificar que dados de identidade/sessão permanecem intactos.
2. Implementar criptografia de campos e o protocolo de idempotência com testes isolados antes dos casos de uso.
3. Implementar administração de acessos, vagas e tarifas com auditoria e concorrência.
4. Implementar check-in, projeções, busca e leituras sensíveis.
5. Implementar as telas, o polling, o coordenador de comando e os testes de UI/E2E.
6. Executar todas as verificações, registrar evidências/limitações e atualizar o roadmap apenas após os resultados reais.

Rollback local: reverter a mudança e aplicar o `Down` da migration apenas em ambiente isolado sem dados relevantes; volumes locais são preservados e não há exclusão automática de dados. Correções após uso local seguem migrations aditivas, não edição destrutiva.
