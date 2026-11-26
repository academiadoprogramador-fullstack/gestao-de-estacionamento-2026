# Fase 5 — Fotos opcionais das vistorias

**Data da implementação:** 26/11/2026  
**Branch:** `feat/fase-5-fotos-vistoria`  
**Escopo:** RF-13, PRD §5.6, TDD §4.4 (F5-T01–07) e DT-15–18/21–23. Aplicação local; sem Azure, CI ou publicação.

## Entregue

- Metadados de foto em `FotosVistoria`, com vínculo exclusivo à vistoria de entrada ou saída, chave opaca, estado, MIME, dimensões, hashes, autoria, lease e versão-base. O binário nunca é persistido no SQL.
- Janelas de envio: entrada por Caixa/Administrador até o pedido de retirada; saída pelos três perfis até a entrega. Consulta pelos três perfis, sem URL pública ou chave do bucket.
- Validação e normalização com `SkiaSharp` 4.157.0: JPEG/PNG/WebP estáticos, rejeição de SVG/animação/corrupção, 5 MiB, 20 MP e 10.000 px por lado na entrada, orientação, redução proporcional até 2.560 px no lado maior e regravação em WebP qualidade 85 sem EXIF/GPS.
- Adaptador `AWSSDK.S3` 4.1.2.1 para R2 privado, com `DisablePayloadSigning` e `DisableDefaultChecksumValidation` exigidos pela compatibilidade Streaming SigV4 da R2.
- Reserva idempotente antes do I/O, confirmação em nova transação, replay pela mesma chave/conteúdo, limite de cinco sob concorrência, tarefas de limpeza e reconciliação de órfãos após 24 h.
- Contadores mensais de bytes e operações Classes A/B por ambiente, aviso em 70%, bloqueio em 80% e suspensão quando o consumo é desconhecido. Linhas de reconciliação permitem somar outros ambientes/cópias sem banco compartilhado.
- Retenção de 90 dias após encerramento e quitação, com negação de leitura antes da remoção física, descarte auditado e retomada em lote no início/reativação.
- Endpoints `POST/GET /api/v1/attendances/{id}/inspections/{entrada|saida}/photos` e `GET .../{fotoId}/content`, com Problem Details, auditoria antes do stream, `no-store`, `nosniff` e MIME do servidor.
- Detalhe, histórico e prontidão de entrega com quantidade/presença de fotos e estado do orçamento. Manobrista continua sem finanças, documento completo ou texto de vistoria.
- Componente Angular `app-fotos-vistoria`, integrado ao detalhe do atendimento e à operação de entrega, com consulta explícita, upload opcional, leitura em blob temporário, revogação de URL, erros focados e tentativa incerta em memória.
- Check-in com pré-seleção de até cinco fotos em memória; o formulário envia a entrada sem conteúdo de foto, consulta a versão do atendimento após o `201` e envia cada seleção individualmente, com chave própria. Falha de foto não desfaz a entrada, e seleções não enviadas são descartadas ao iniciar outro check-in, sair da rota/logout ou confirmar a saída do navegador.
- Migration aditiva `20261126151516_Fase5FotosVistoria` após `20261123160433_Fase4EntregaLiberacao`; vistorias existentes permanecem textuais.

## Configuração

Parâmetros não sensíveis em `appsettings.json` e `appsettings.Development.json`:

- `ArmazenamentoFotos:Habilitado`
- `ArmazenamentoFotos:R2:Endpoint`, `RegiaoAssinatura`, `Bucket`
- `ArmazenamentoFotos:Limites:*`
- `ArmazenamentoFotos:Orcamento:*`
- `ArmazenamentoFotos:RetencaoDias`, `DiasParaReconciliarOrfao`

Credenciais somente em User Secrets/appsettings de ambiente:

- `ArmazenamentoFotos:R2:AccessKeyId`
- `ArmazenamentoFotos:R2:SecretAccessKey`

Nenhum valor secreto é versionado, impresso em log ou enviado ao navegador. O bucket é privado e o navegador acessa o conteúdo apenas pela API bearer.

## Prova técnica e contrato R2

- `SkiaSharp` 4.157.0 foi executado em Linux x86-64, com encoding WebP e decodificação validados (imagem sintética de 320×200 produziu WebP válido).
- `AWSSDK.S3` 4.1.2.1 foi fixado. O contrato real foi executado com o bucket privado de testes **`gestao-de-estacionamento-testes`**, usando as credenciais `R2_TEST_*` e imagens sintéticas: `PutObject`, `GetObject` e `DeleteObject` passaram.
- O E2E Playwright anexou e leu uma foto de saída pela API/S3 de teste em `https://localhost:4300` e `https://localhost:4200`, sem expor chave do bucket ao navegador.
- O bucket de desenvolvimento é **`gestao-de-estacionamento-dev`**. O bucket de testes é **`gestao-de-estacionamento-testes`**. Os tokens correspondentes na Cloudflare são `gestao-de-estacionamento-dev-api` e `gestao-de-estacionamento-testes-api`; os valores de Access Key ID/Secret ficam em User Secrets/.env e não são versionados.
- Nenhum valor secreto foi impresso em logs, artefatos ou documentação.

## Testes executados

```bash
dotnet build app/backend/Estacionamento.slnx
dotnet test app/backend/tests/Estacionamento.UnitTests
ESTACIONAMENTO_TESTE_CONEXAO='Server=127.0.0.1,14330;Database=master;User Id=sa;Password=<...>;Encrypt=True;TrustServerCertificate=True' \
ESTACIONAMENTO_TESTE_R2=1 ESTACIONAMENTO_R2_TESTE_BUCKET='gestao-de-estacionamento-testes' \
  dotnet test app/backend/tests/Estacionamento.IntegrationTests
cd app/frontend && npm test
# Playwright: E2E_* + E2E_R2_HABILITADO=true, E2E_FOTOS=1 e o bucket de teste
npx playwright test
```

- Unitários MSTest: **79 aprovados**, incluindo regras de janela/limite, validação de opções, formatos JPEG/PNG/WebP, redução sem ampliação, remoção de marcador EXIF/GPS, orientação, corrupção, dimensão excessiva e processamento concorrente limitado.
- Integração MSTest com SQL Server 2022 real: **111 aprovados**, incluindo o contrato real opt-in com o bucket R2 de testes. Cobrem entrada/saída com e sem fotos, perfis, limite de cinco sob concorrência, replay/conflito, falha de armazenamento, aviso/bloqueio/consumo desconhecido de orçamento, retenção, descarte, manutenção de órfãos, migration aditiva/rollback e Put/Get/Delete no R2 privado.
- Vitest: **86 aprovados**, incluindo seleção de fotos no check-in sem upload prévio, fila pós-confirmação, repetição da mesma foto, consulta explícita, leitura em blob com revogação, envio idempotente e validação local de formato/tamanho.
- Playwright: **23 jornadas aprovadas** entre duas origens HTTPS locais: 19 regressões das Fases 1–4 e 4 cenários da Fase 5 (foto de saída no R2, contingência de armazenamento, check-in com pré-seleção e falha de foto no check-in).
- `openspec validate --all --strict`: **18 itens aprovados, 0 falhas**.
- Os arquivos placeholder `Test1.cs` dos dois projetos de teste foram removidos.

## Limitações e pendências

- Configuração R2: bucket de desenvolvimento `gestao-de-estacionamento-dev` e bucket de testes `gestao-de-estacionamento-testes`; tokens Cloudflare `gestao-de-estacionamento-dev-api` e `gestao-de-estacionamento-testes-api`, com Access Key ID/Secret somente em User Secrets/.env.
- O contrato real e o E2E de upload foram validados com o bucket de testes `gestao-de-estacionamento-testes`.
- Validação manual de acessibilidade informada pelo solicitante como concluída; ambiente, dispositivo e tecnologia assistiva não foram detalhados nesta evidência. O alvo WCAG 2.2 AA não é declarado conformidade.
- O orçamento é um controle conservador local, não uma trava de cobrança da Cloudflare. A conta deve ser conferida manualmente.
- Sem CI, Azure, publicação, quotas reais de produção ou SLA.

## Runbook de reconciliação e cotas

1. Conferir na conta Cloudflare o consumo mensal de bytes, Classes A e B do bucket privado, incluindo objetos de teste, reservas e cópias em nuvem.
2. Registrar o consumo observado em `ConsumosFotosMensais` para a competência `AAAA-MM`, usando uma linha de reconciliação com `Ambiente` identificando o ambiente/cópia. Não copiar credenciais para SQL, script ou log.
3. Se houver divergência não reconciliável, marcar a linha como `EstadoReconciliacao = 'desconhecido'`. A API suspende novos uploads e leituras de foto e mantém o fluxo textual.
4. Reconciliar reservas órfãs com `TarefasFotos`; executar a manutenção em lote (reinício/reativação da API ou chamada controlada ao serviço) e confirmar que fotos `disponivel` não foram apagadas.
5. Manter o aviso visual em 70% e o bloqueio em 80%. Não apagar fotos confirmadas para liberar franquia; aplicar a retenção de 90 dias somente após encerramento e quitação.
6. Documentar data, responsável, competência, valores observados, ajustes e limitações. Nenhum passo deve imprimir `AccessKeyId`, `SecretAccessKey` ou chaves AES/JWT.
