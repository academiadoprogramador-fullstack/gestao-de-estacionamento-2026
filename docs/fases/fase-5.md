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

## Prova técnica e bloqueio do R2

- `SkiaSharp` 4.157.0 foi executado em Linux x86-64, com encoding WebP e decodificação validados (imagem sintética de 320×200 produziu WebP válido).
- `AWSSDK.S3` 4.1.2.1 foi fixado. A tentativa de contrato real com o endpoint/bucket configurados e as credenciais de `User Secrets`/`.env` retornou `AccessDenied`/`Forbidden` em `PutObject`, `GetObject`, `DeleteObject` e `ListBuckets`. O mesmo ocorreu com as credenciais de teste e com variações de path-style. Nenhum valor secreto foi impresso.
- A suíte `FotosR2ContratoTestes` fica opt-in (`ESTACIONAMENTO_TESTE_R2=1`) e termina inconclusiva quando o bucket/credencial recusa acesso. **O contrato real com o R2 e a execução E2E de upload não estão comprovados nesta entrega.** É necessário confirmar o nome do bucket, a conta do endpoint e a permissão `Object Read & Write` do token.

## Testes executados

```bash
dotnet build app/backend/Estacionamento.slnx
dotnet test app/backend/tests/Estacionamento.UnitTests
ESTACIONAMENTO_TESTE_CONEXAO='Server=127.0.0.1,14330;Database=master;User Id=sa;Password=<...>;Encrypt=True;TrustServerCertificate=True' \
  dotnet test app/backend/tests/Estacionamento.IntegrationTests
cd app/frontend && npm test
```

- Unitários MSTest: **79 aprovados**, incluindo regras de janela/limite, validação de opções, formatos JPEG/PNG/WebP, redução sem ampliação, remoção de marcador EXIF/GPS, orientação, corrupção, dimensão excessiva e processamento concorrente limitado.
- Integração MSTest com SQL Server 2022 real: **110 aprovados e 1 ignorado** (contrato real R2 opt-in). Cobrem entrada/saída com e sem fotos, perfis, limite de cinco sob concorrência, replay/conflito, falha de armazenamento, aviso/bloqueio/consumo desconhecido de orçamento, retenção, descarte, manutenção de órfãos e migration aditiva/rollback.
- Vitest: **84 aprovados**, incluindo consulta explícita, leitura em blob com revogação, envio idempotente, repetição da mesma tentativa, ocultação para perfil consultivo e validação local de formato/tamanho.
- Playwright: **19 jornadas aprovadas** entre duas origens HTTPS locais (regressões das Fases 1–4). O cenário de upload/consulta de fotos com R2 real não foi executado porque o contrato R2 está bloqueado por `AccessDenied`.
- `openspec validate --all --strict`: **18 itens aprovados, 0 falhas**.
- Os arquivos placeholder `Test1.cs` dos dois projetos de teste foram removidos.

## Limitações e pendências

- R2 real não validado; a aplicação usa o adaptador e o dublê em memória nos testes. Não declarar integração R2 concluída antes de corrigir o acesso ao bucket.
- E2E Playwright com upload/consulta de fotos não executado nesta sessão; as jornadas existentes das Fases 1–4 são regressão obrigatória quando o R2 estiver disponível.
- Revisão manual de acessibilidade com tecnologia assistiva não executada; o alvo WCAG 2.2 AA não é declarado conformidade.
- O orçamento é um controle conservador local, não uma trava de cobrança da Cloudflare. A conta deve ser conferida manualmente.
- Sem CI, Azure, publicação, quotas reais de produção ou SLA.

## Runbook de reconciliação e cotas

1. Conferir na conta Cloudflare o consumo mensal de bytes, Classes A e B do bucket privado, incluindo objetos de teste, reservas e cópias em nuvem.
2. Registrar o consumo observado em `ConsumosFotosMensais` para a competência `AAAA-MM`, usando uma linha de reconciliação com `Ambiente` identificando o ambiente/cópia. Não copiar credenciais para SQL, script ou log.
3. Se houver divergência não reconciliável, marcar a linha como `EstadoReconciliacao = 'desconhecido'`. A API suspende novos uploads e leituras de foto e mantém o fluxo textual.
4. Reconciliar reservas órfãs com `TarefasFotos`; executar a manutenção em lote (reinício/reativação da API ou chamada controlada ao serviço) e confirmar que fotos `disponivel` não foram apagadas.
5. Manter o aviso visual em 70% e o bloqueio em 80%. Não apagar fotos confirmadas para liberar franquia; aplicar a retenção de 90 dias somente após encerramento e quitação.
6. Documentar data, responsável, competência, valores observados, ajustes e limitações. Nenhum passo deve imprimir `AccessKeyId`, `SecretAccessKey` ou chaves AES/JWT.
