# Fase 5 — Fotos opcionais das vistorias

## Why

As vistorias de entrada e saída são apenas textuais até a Fase 4. O PRD §5.6 e o TDD §4.4 acrescentam evidência visual opcional antes da publicação, sem tornar o armazenamento externo uma dependência do fluxo P0 e sem alterar as regras comerciais ou a entrega já concluída.

## What Changes

- Anexar de zero a cinco fotos JPEG, PNG ou WebP estáticos, de até 5 MiB cada, às vistorias textuais de entrada e saída já salvas; a ausência ou falha das fotos nunca bloqueia check-in ou entrega (RF-13).
- Permitir que o formulário de check-in selecione até cinco fotos antes do envio, mantendo os arquivos somente em memória e sem incluí-los no corpo, na chave ou na transação do check-in; após a confirmação, enviar cada foto individualmente com chave própria, sem desfazer a entrada se uma foto falhar.
- Validar o conteúdo real, limitar 20 MP e 10.000 px por lado na entrada e gravar WebP estático normalizado de até 2.560 px no lado maior, qualidade 85, sem EXIF/GPS ou outros metadados, sem persistir o original.
- Enviar fotos de entrada somente por Caixa/Administrador até o pedido de retirada e de saída pelos três perfis até a confirmação da entrega; consultar as duas vistorias pelos três perfis, com autorização no servidor e leitura explícita auditada.
- Armazenar binários em bucket Cloudflare R2 Standard privado, acessado apenas pela API; persistir no SQL somente metadados, vínculo, autor/UTC, hash e estado, sem chave, URL pública ou binário.
- Reservar a tentativa idempotente antes do I/O externo e confirmar os metadados em nova transação, com reconciliação durável e limpeza de órfãos após 24 h, sem manter transação SQL aberta durante o R2.
- Medir o orçamento mensal de bytes e operações Classes A/B de forma compartilhada entre ambientes, avisar aos 70% e recusar operações que ultrapassariam 80%, suspendendo fotos quando o consumo for desconhecido; o fluxo textual permanece disponível.
- Aplicar retenção de 90 dias às fotos após encerramento e quitação, com descarte retomável e auditado, sem apagar evidências antecipadamente para liberar cota; ampliar consulta/histórico com presença e quantidade de fotos, sem expor chaves ou conteúdo em listas.

Fora do escopo: publicação Azure, CI, relatórios, edição do texto da vistoria, exclusão/substituição manual de foto confirmada, upload direto do navegador ao R2, galeria pública, preservação pericial do original, dados reais, vídeo/animação e qualquer alteração das regras comerciais ou de entrega.

## Capabilities

### New Capabilities

- `fotos-vistoria`: anexos opcionais das duas vistorias, janelas e perfis, validação/normalização, armazenamento privado, idempotência/compensação, consulta auditada, orçamento de consumo e retenção.

### Modified Capabilities

- `atendimentos`: o check-in passa a permitir pré-selecionar fotos em memória e enviá-las após a confirmação, sem incluí-las no corpo, na chave ou na transação da entrada.
- `consulta-atendimentos`: detalhe e histórico passam a indicar presença/quantidade de fotos e a oferecer consulta explícita por vínculo, sem incluí-las em listas ou projeções comuns.
- `vistoria-saida`: a vistoria textual imutável passa a admitir fotos opcionais até a entrega, sem edição do texto ou nova vistoria.
- `entrega-veiculo`: a entrega fecha a janela de fotos de saída e não depende da presença, sucesso ou disponibilidade do armazenamento; uploads concorrentes não sobrevivem à confirmação.
- `idempotencia-comandos`: o envio de foto admite reserva confirmada antes do I/O externo e resultado mínimo confirmado depois, preservando replay pela mesma chave e reconciliação de tentativa incerta.

## Impact

- API `/api/v1/attendances/{id}/inspections/{tipo}/photos` e `/content`; controllers, serviços e DTOs de atendimento, vistoria e entrega; validação de configuração.
- Migração aditiva com metadados de foto, tentativas, orçamento de consumo e tarefas de reconciliação/descarte; vistorias existentes permanecem textuais.
- Infraestrutura: adaptador S3 compatível com R2 (`AWSSDK.S3`) e `SkiaSharp` com binários Linux, novos limites em `ArmazenamentoFotos`; credenciais somente em User Secrets/appsettings de ambiente.
- SPA: check-in passa a selecionar fotos em memória e a exibi-las/enviá-las após a confirmação; atendimentos e operação de entrega ganham consulta, envio, estados e contingência textual, mantendo tentativa incerta apenas em memória.
- Testes MSTest, SQL Server real, Vitest e Playwright, além de contrato real com bucket R2 isolado de teste; evidências em `docs/fases/fase-5.md`. Rastreabilidade: [PRD §5.6 e RF-13](../../../docs/prd.md), [TDD §4.4 e roadmap F5](../../../docs/tdd.md) (DT-15–18/21–23).
