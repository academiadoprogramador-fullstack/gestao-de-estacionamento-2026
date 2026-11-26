# Design — Fotos opcionais das vistorias

## Context

Ver [proposta](proposal.md) e [TDD §4.4](../../../docs/tdd.md) (F5-T01–07; DT-15–18/21–23). A Fase 4 encerrou o P0 textual com `AtendimentosController`, `RetiradasController`, `ServicoVistoriaSaida` e `ServicoEntrega`. A vistoria de entrada é gravada junto do check-in e não possui endpoint próprio de escrita; a de saída é imutável e vinculada ao pedido. O endpoint, a região, o bucket e os limites de `ArmazenamentoFotos` estão configurados em `appsettings.Development.json`; as credenciais S3 permanecem em User Secrets.

A SPA usa `comando` para tentativa incerta, `atendimentos` para detalhe/histórico e `delivery` para a operação de entrega. A API segue controllers finos, serviços em Infrastructure, portas em Application, opções validadas na inicialização e migrações versionadas após `20261123160433_Fase4EntregaLiberacao`.

## Goals / Non-Goals

**Goals:**

- Acrescentar fotos sem tocar nas transações de check-in, pedido ou entrega e sem tornar o R2 autoridade de estado.
- Manter o P0 textual disponível com R2, cota ou processamento de imagem indisponíveis.
- Provar o adapter S3/R2 e o SkiaSharp em Linux antes de construir o fluxo, com versões fixadas e licenças verificadas.
- Preservar histórico, auditoria e retenção mesmo após remoção física de objeto.

**Non-Goals:**

- Publicação, Azure, CI, CD, relatórios e qualquer mudança de cobrança ou entrega.
- Upload direto do navegador, URL pré-assinada, galeria pública ou exclusão manual.
- Recifragem de imagem na aplicação, preservação pericial do original ou reconhecimento de conteúdo.
- Editar a vistoria textual, criar nova vistoria ou anexar foto a atendimento anterior por retroatividade.

## Decisions

### D5-01 — Portas, camadas e composição

Criar em Application `IArmazenamentoFotos`, `IProcessadorImagens`, `IServicoFotosVistoria` e contratos de DTO; em Domain, `FotoVistoria`, estados e regras puras de janela/limite; em Infrastructure, `ArmazenamentoFotosR2`, `ProcessadorImagensSkia`, `ServicoFotosVistoria` e `ServicoManutencaoFotos`; em Api, `FotosController` ou ações no controller de atendimentos, mantendo a rota proposta. Registrar portas em `RegistroInfraestrutura` e opções em `ArmazenamentoFotosOptions` com `ValidateOnStart`; incluir a validação em `ValidadorConfiguracaoInicial`.

Alternativa descartada: controller próprio com lógica de domínio ou SDK S3 usado no serviço de atendimento. A porta isola o teste em memória e permite desabilitar o armazenamento por configuração sem alterar check-in/entrega.

### D5-02 — Modelo e concorrência no SQL

Persistir somente metadados, em duas tabelas aditivas além das existentes:

- `FotosVistoria`: `Id`, `AtendimentoId`, `Tipo` (`entrada`/`saida`), `VistoriaEntradaId` ou `VistoriaSaidaId` (exatamente um conforme o tipo, com check constraint), `ChaveObjeto` opaca única, `Estado` (`pendente`, `disponivel`, `limpeza_pendente`, `descartada`), `Mime`, `TamanhoBytes`, `Largura`, `Altura`, `HashSha256`, `AtorId`, `CriadoEmUtc`, `ConfirmadoEmUtc`, `ReservadoAteUtc`, `ResultadoIdempotenteId` e `RowVersion`.
- `ConsumoFotosMensal`: competência `AAAA-MM`, `Ambiente`, bytes armazenados/transferidos, operações Classes A/B, bytes reservados, estado de reconciliação e `RowVersion`; uma linha por competência/ambiente, atualizada em transação curta.
- `TarefasFotos`: tentativa/chave/objeto, tipo (`confirmacao`, `limpeza`, `retencao`), `LeaseAteUtc`, tentativas, último erro e estado, para retomada idempotente após reinício.

O limite de cinco conta `pendente + disponivel` da vistoria e é verificado sob bloqueio da vistoria/atendimento na mesma transação que reserva a tentativa. Unicidade de `ChaveObjeto` e de `ResultadoIdempotenteId` impede duplicação; índice por atendimento/tipo/estado acelera lista e retenção. Foto confirmada nunca sofre `UPDATE` de vínculo, hash ou autoria; apenas o estado de limpeza/descarte avança.

Alternativa descartada: guardar binário no SQL ou usar o nome original como chave. Metadados mínimos evitam PII e permitem trocar o provedor sem migrar conteúdo.

### D5-03 — Envio em duas fases, sem transação distribuída

1. Autenticar, reautorizar o perfil e validar `tipo`, alvo e `expectedAttendanceVersion`.
2. Receber um único arquivo, validar limites de transporte e processar a imagem em memória; não abrir transação durante a decodificação.
3. Em transação curta, verificar replay, reservar a chave idempotente e a linha `pendente`, contar o limite e reservar bytes/operações no orçamento; confirmar a reserva antes do I/O externo.
4. Fazer `PutObject` no R2 com `ChaveObjeto` estável da tentativa e sem transação SQL aberta.
5. Em nova transação, reautorizar, revalidar janela/versão/limite, confirmar metadados `disponivel`, auditoria e resultado mínimo idempotente. Em falha, deixar `TarefasFotos` e a linha `pendente` para reconciliação.

Repetição com a mesma chave/conteúdo devolve o resultado original; conteúdo diferente conflita. O mesmo ator/alvo/tipo e a mesma chave são obrigatórios para retomar. Uma nova transação não pode depender de resposta perdida do R2; `404` isolado não prova que a escrita não ocorreu. A confirmação só anuncia foto disponível após o commit.

Alternativa descartada: `PutObject` dentro da transação SQL de check-in/entrega. O I/O externo alongaria locks, poderia falhar depois do commit e violaria a independência do P0 textual.

### D5-04 — Processamento e validação de imagem

`ProcessadorImagensSkia` deve, antes de alocar a imagem completa:

- reconhecer o formato por magic bytes e MIME permitido, recusando SVG, animação (`FrameCount > 1`), corrupção e conteúdo divergente do tipo declarado;
- ler dimensões/`20 MP` e `10.000 px` por lado; recusar acima disso;
- decodificar, aplicar orientação EXIF e redimensionar proporcionalmente até `2.560 px` no lado maior, sem ampliar;
- regravar em WebP estático qualidade 85 e descartar o original; não copiar EXIF/GPS nem nome/timestamp do arquivo.

O processamento usa `SemaphoreSlim(1,1)` por instância e timeout inicial de 30 s; concorrência excedente recebe `503 photo_processing_busy` sem fila ilimitada. O adaptador deve liberar `SKBitmap`/`SKData`/streams em `finally` e trabalhar com buffers limitados. A prova F5-01 verifica licença, binários Linux e encoding WebP antes de codificar o fluxo; se SkiaSharp não atender, a fase para e revê a biblioteca, sem trocar silenciosamente a política de metadados.

### D5-05 — R2 privado, chaves opacas e leitura via API

`ArmazenamentoFotosR2` usa `AWSSDK.S3` com endpoint `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`, região de assinatura `auto`, `ForcePathStyle` conforme prova, bucket privado e credenciais somente do servidor. A versão fixada deve seguir as orientações de checksum/Streaming SigV4 da R2; não presumir equivalência integral com AWS S3. Chave do objeto é opaca e imutável (`fotos/{atendimentoId}/{tipo}/{fotoId}`), sem nome original, PII ou URL pública.

Upload e download passam pela API bearer/CORS; o navegador nunca recebe credencial, chave de objeto ou URL do R2. O conteúdo é transmitido como stream com `Cache-Control: private, no-store`, `X-Content-Type-Options: nosniff`, `Content-Type` determinado pelo servidor e `Content-Disposition: inline` apenas com identificação opaca. A leitura é auditada antes de abrir o stream e falha fechada se a auditoria, o vínculo ou o objeto não forem confirmados.

Alternativa descartada: URL pré-assinada direta. Simplifica banda, mas expõe o bucket ao browser, dificulta auditoria e exigiria CORS no R2; o recorte escolhe a API como único caminho.

### D5-06 — Orçamento compartilhado e reconciliação

O orçamento usa `ConsumoFotosMensal` por competência e ambiente, com limites configuráveis (`FranquiaMensalBytes`, `FranquiaOperacoesClasseA`, `FranquiaOperacoesClasseB`, aviso 70%, bloqueio 80%). Bytes finais e operações são reservados antes do R2 e confirmados depois; leituras também debitam Classe B e bytes transferidos. O total exibido soma as linhas locais mais ajustes de reconciliação registrados manualmente para outros ambientes/cópias, já que não há banco compartilhado entre as fases.

Quando a conta não pode ser reconciliada, a linha entra em `consumo_desconhecido` e o serviço suspende novos anexos e leituras de foto; a vistoria textual continua. A interface mostra aviso aos 70% e mensagem específica ao bloquear em 80%, sem excluir objetos antigos para liberar cota. A contabilidade é um controle conservador da aplicação, não uma trava de cobrança da Cloudflare; a conta deve ser conferida manualmente.

Alternativa descartada: consultar a API de analytics da Cloudflare em cada requisição. Introduz dependência, latência e disponibilidade de rede no caminho crítico; a reconciliação manual continua sendo a fonte de verdade.

### D5-07 — Retenção, descarte e manutenção

`ServicoManutencaoFotos` é executado em lotes no início/reativação da aplicação e por gatilho após operações, sem worker contínuo. Ele:

- conclui confirmações pendentes e limpa órfãos somente após 24 h, lease vencido e ausência de upload ativo;
- calcula retenção de 90 dias pelo marco mais recente de entrega e quitação, ignorando atendimento ativo ou com saldo;
- marca `limpeza_pendente`, tenta excluir o objeto e grava evento/estado `descartada`, com retomada por `TarefasFotos`;
- nunca apaga foto `disponivel` apenas para aliviar franquia e nunca expõe item vencido, mesmo se a remoção física estiver adiada.

A leitura de foto vencida é negada pela consulta, antes do R2. O backup local passa a incluir objetos e chaves conforme DT-23; o SQL guarda apenas metadados.

### D5-08 — Contratos HTTP e projeções

| `/api/v1` | Perfis | Contrato |
|---|---|---|
| `POST /attendances/{id}/inspections/{tipo}/photos` | Entrada: Caixa/Admin; saída: três | `multipart/form-data`, um arquivo, `Idempotency-Key` e `expectedAttendanceVersion`. Exige vistoria e janela; `201` com ID, estado e metadados mínimos, sem chave/URL. |
| `GET /attendances/{id}/inspections/{tipo}/photos` | Três | Metadados de fotos disponíveis, contagem, janela e estado do orçamento; sem chave S3. |
| `GET /attendances/{id}/inspections/{tipo}/photos/{fotoId}/content` | Três | Leitura explícita auditada; stream privado, `no-store`, `nosniff`, MIME do servidor. |

`tipo` aceita `entrada`/`saida` no caminho, alinhado ao domínio em português; qualquer outro valor é validação. Códigos propostos: `photo_storage_disabled`, `photo_storage_unavailable`, `photo_processing_busy`, `photo_unsupported_format`, `photo_too_large`, `photo_dimensions_exceeded`, `photo_corrupt`, `photo_limit_reached`, `photo_window_closed`, `photo_quota_exceeded`, `photo_quota_unknown`, `photo_not_found`, `photo_expired`, `photo_idempotency_conflict`, `stale_attendance_version`. Mapear para `400`, `409`, `413`, `415`, `422` e `503` conforme o caso, em Problem Details sem conteúdo sensível.

O detalhe do atendimento e o histórico recebem `fotos: { entrada: { quantidade, janela }, saida: { quantidade, janela }, orcamento }`; listas mostram apenas indicador mínimo. A prontidão de entrega inclui `fotosSaida` e nunca bloqueia por ausência. A entrega grava o fechamento lógico da janela na mesma transação e faz a confirmação concorrente de foto falhar ou virar órfã reconciliável.

### D5-09 — SPA e tentativa incerta

Reutilizar os padrões de `core/comando` e os serviços de atendimento/delivery:

- seção de fotos no detalhe de atendimentos para a vistoria de entrada, com envio só a Caixa/Admin e leitura pelos três; seção equivalente na operação de entrega para a saída, com envio pelos três;
- seleção de arquivo com `accept` de JPEG/PNG/WebP, validação de tamanho/MIME apenas como feedback; o servidor revalida;
- lista com estado textual, contagem, janela, aviso de cota e ações “Ler foto”/“Enviar foto”, sem exclusão ou substituição;
- leitura por `Blob` temporário, `URL.createObjectURL`, revogação ao fechar o diálogo, trocar seleção, sair da rota ou logout; sem cache, IndexedDB ou URL do R2;
- tentativa de upload pendente/incerta apenas em memória, com bloqueio de navegação/logout, aviso de saída, repetição explícita pela mesma chave/conteúdo e nenhuma nova chave automática;
- mensagens de limite, janela, formato, armazenamento e cota com resumo focado e `aria-invalid`/associação ao campo quando aplicável.

Não criar galeria fora do atendimento nem nova rota de administração de fotos. O protótipo de referência é [atendimento-fotos-entrada.html](prototypes/atendimento-fotos-entrada.html) e [entrega-fotos-saida.html](prototypes/entrega-fotos-saida.html).

### D5-10 — Configuração, versões e falha fechada

Adicionar `ArmazenamentoFotosOptions` com seção `ArmazenamentoFotos`: `Habilitado`, `R2` (endpoint HTTPS, região, bucket), `Limites` (5 fotos, 5 MiB, máxima dimensão, saída WebP/qualidade, timeout) e `Orcamento` (franquias, aviso, bloqueio). Parâmetros não sensíveis ficam em `appsettings*.json`; `AccessKeyId`/`SecretAccessKey` somente em User Secrets ou App Settings. Credenciais ausentes com `Habilitado=true` interrompem a inicialização sem imprimir valores; `Habilitado=false` responde `503 photo_storage_disabled` e preserva o texto.

Fixar `AWSSDK.S3`, `SkiaSharp` e `SkiaSharp.NativeAssets.Linux` em `Directory.Packages.props` após a prova técnica F5-01 e registrar versões/licenças. O adaptador de teste em memória cobre unidade/integração; o contrato real usa bucket isolado e imagens sintéticas, opt-in por variável de ambiente, sem sujar o bucket de desenvolvimento.

## Prototype

- [atendimento-fotos-entrada.html](prototypes/atendimento-fotos-entrada.html): detalhe do atendimento, vistoria de entrada, galeria, janela, perfis, limite, orçamento, tentativa incerta e leitura auditada.
- [entrega-fotos-saida.html](prototypes/entrega-fotos-saida.html): vistoria de saída, galeria, prontidão, entrega, fechamento da janela e chegada concorrente.

Os protótipos usam CSS inline com utilitários de **Tailwind CSS 4.3.5** (distribuição oficial, cabeçalho MIT) e a paleta existente. A SPA mantém Tailwind nativo e componentes Angular; os controles de perfil/cenário, fixtures e respostas são simulações, sem persistência. Não há placeholders `[unspecified]` nem divergência de comportamento; a única diferença é a ausência dos controles de revisão na aplicação real.

## Risks / Trade-offs

- **I/O externo lento ou indisponível** → reserva SQL curta, timeout de processamento, `503` controlado e contingência textual; não abrir transação durante o R2.
- **Objeto órfão ou confirmação perdida** → tarefa durável, lease de 24 h, reconciliação por chave/hash e limpeza idempotente; não tratar `404` isolado como falha definitiva.
- **Cota compartilhada desconhecida** → contador conservador por competência/ambiente, ajustes de reconciliação e suspensão de fotos até conferir a conta; nunca apagar evidência para liberar franquia.
- **Imagem maliciosa ou metadados residuais** → magic bytes, limite antes da alocação, reencode sem cópia de metadados, teste com fixture EXIF/GPS e contrato real R2.
- **Limite de cinco sob concorrência** → bloqueio/versão na mesma fronteira da vistoria/pedido/entrega e índice/constraint; testar com escritores SQL independentes.
- **Retenção apaga cedo demais** → elegibilidade derivada de entrega e quitação, leitura nega vencido antes da remoção, lote retomável e auditoria sem conteúdo.
- **SkiaSharp/AWSSDK incompatíveis com Linux/R2** → prova F5-01 bloqueante antes do fluxo; se falhar, parar e rever a biblioteca, sem trocar a política de metadados.

## Migration Plan

Aplicar migração aditiva após `20261123160433_Fase4EntregaLiberacao` em catálogo local isolado, verificando catálogo vazio, upgrade da Fase 4 com dados sintéticos e ausência de fotos retroativas. Criar tabelas, FKs restritas, check constraints, índices e `RowVersion`; não preencher fotos nem alterar vistorias existentes.

Rollback: desabilitar `ArmazenamentoFotos:Habilitado` para interromper novos uploads, mantendo metadados e leitura; não executar `Down` destrutivo após evidências reais. Em catálogo descartável, ensaiar a reversão e guardar cópia consistente antes da ativação. O bucket de teste é isolado do de desenvolvimento; nenhum objeto é removido pelo rollback.

## Open Questions

- Versões exatas de `AWSSDK.S3`, `SkiaSharp` e natives Linux serão fixadas após a prova F5-01; a escolha não altera abordagem, contratos ou tarefas. Se a prova falhar, a fase para e a biblioteca é revista com o solicitante.
- A forma exata de reconciliação manual do consumo de outros ambientes/cópias será registrada no runbook local; o modelo já reserva a competência/ambiente para receber esses ajustes.
- O contrato real com o R2 foi validado em 26/11/2026 com os buckets `gestao-de-estacionamento-dev` e `gestao-de-estacionamento-testes` e tokens `gestao-de-estacionamento-dev-api`/`gestao-de-estacionamento-testes-api` (Put/Get/Delete e E2E). O bucket de testes não deve ser usado como produção.
