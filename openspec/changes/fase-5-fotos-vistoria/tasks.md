# Tasks — Fase 5: fotos das vistorias

Antes de editar código, registrar revisão e aceite dos artefatos SDD e iniciar o fluxo Apply explicitamente. Rastreabilidade: PRD §5.6 e RF-13; TDD §4.4 (F5-T01–07) e roadmap F5-01–07. Os itens abaixo ainda não foram executados. Protótipos de referência: [check-in-fotos-entrada.html](prototypes/check-in-fotos-entrada.html), [atendimento-fotos-entrada.html](prototypes/atendimento-fotos-entrada.html) e [entrega-fotos-saida.html](prototypes/entrega-fotos-saida.html).

## 1. Prova técnica, dependências e configuração — F5-01

- [x] 1.1 Provar `AWSSDK.S3` contra o bucket R2 isolado de teste e `SkiaSharp` em Linux: upload/leitura/exclusão privada, encoding WebP, orientação, limites de memória e binários nativos. Fixar versões e licenças em `Directory.Packages.props` e registrar comandos, resultados e limitações em `docs/fases/fase-5.md`; se a prova falhar, parar e revisar a biblioteca com o solicitante (DT-16/17; F5-T01).
- [x] 1.2 Implementar `ArmazenamentoFotosOptions`/validador com endpoint HTTPS, região `auto`, bucket, limites, orçamento e flag `Habilitado`; registrar em `RegistroInfraestrutura` e `ValidadorConfiguracaoInicial`. Verificar startup que falha sem credencial quando habilitado, sem imprimir segredo, e `503 photo_storage_disabled` quando desabilitado, mantendo o fluxo textual (DT-18/25; F5-T01).
- [x] 1.3 Documentar as novas chaves não sensíveis e os nomes de segredos em `README.md`, confirmando que `appsettings.Development.json` contém apenas endpoint/região/bucket/limites e que credenciais seguem em User Secrets (DT-25; PRD §10).

## 2. Persistência, janelas e limite de cinco — F5-02

- [x] 2.1 Modelar `FotosVistoria`, `ConsumoFotosMensal` e `TarefasFotos` com FKs restritas, check constraint de vínculo por tipo, unicidade de chave/resultado, índices e `RowVersion`; gerar migration aditiva após `20261123160433_Fase4EntregaLiberacao`. Verificar script em catálogo vazio e upgrade da Fase 4 com dados sintéticos, sem fotos retroativas, e ensaiar rollback em catálogo descartável (F5-T02).
- [x] 2.2 Implementar regras de janela (entrada até pedido; saída até entrega), perfis e limite `pendente + disponivel ≤ 5` na mesma transação da vistoria/atendimento. Cobrir em MSTest: zero fotos, janela aberta/encerrada, Manobrista sem envio de entrada, envio de saída pelos três, entrega e sexta tentativa concorrente (F5-T02; specs `fotos-vistoria`, `vistoria-saida`, `entrega-veiculo`).
- [x] 2.3 Implementar contabilidade mensal de bytes/Classes A/B por competência/ambiente e ajustes de reconciliação. Cobrir em unitários/integração aviso de 70%, bloqueio antes de 80%, soma de reservas/retries, estado de consumo desconhecido e ausência de exclusão para liberar cota (DT-18; F5-T06).
- [x] 2.4 Adicionar contratos de domínio/aplicação de foto e resultados mínimos por perfil, sem binário, chave ou nome original. Verificar com testes de unidade que o DTO de lista/detalhe não serializa campos internos de armazenamento (F5-T02/T05).

## 3. Validação e normalização de imagem — F5-03

- [x] 3.1 Implementar `IProcessadorImagens`/`ProcessadorImagensSkia` com magic bytes, JPEG/PNG/WebP estáticos, rejeição de SVG/animação/corrupção, limites de 5 MiB/20 MP/10.000 px antes da alocação, orientação, redução até 2.560 px e WebP qualidade 85 (DT-17/22; F5-T03).
- [x] 3.2 Cobrir em MSTest formatos aceitos/recusados, MIME divergente, imagem corrompida, EXIF/GPS removido, orientação, ausência de ampliação, dimensões e saída WebP; verificar que o original e o nome do arquivo não são persistidos (F5-T03).
- [x] 3.3 Aplicar `SemaphoreSlim(1,1)`, timeout inicial de 30 s e liberação determinística de recursos; testar processamento concorrente, recusa controlada sem fila ilimitada e ausência de vazamento de memória/stream em falhas (F5-T03; DT-22).

## 4. R2, envio idempotente e reconciliação — F5-04

- [x] 4.1 Implementar o adaptador `ArmazenamentoFotosR2` e o dublê em memória, com chave opaca, `PutObject`/`GetObject`/`DeleteObject`, região `auto`, checksum/Streaming SigV4 conforme a versão fixada e erros classificados. Validar contrato real opt-in com o bucket isolado e imagens sintéticas, sem usar credenciais no navegador (DT-16; F5-T04).
- [x] 4.2 Implementar a reserva antes do I/O e a confirmação em nova transação, com replay por ator/operação/chave/conteúdo, limite e orçamento. Cobrir em integração SQL: resposta perdida, falha entre R2 e confirmação, chave divergente, retry sem duplicar objeto, janela fechada por pedido/entrega e concorrência de cinco anexos (F5-T04).
- [x] 4.3 Implementar `ServicoManutencaoFotos` em lotes retomáveis ao iniciar/reativar: confirmação pendente, órfão após 24 h com lease e ausência de upload ativo, limpeza idempotente e evento sem conteúdo. Testar reinício, lease vencido, falha de exclusão e nunca apagar foto disponível para liberar cota (F5-T04/T06).
- [x] 4.4 Implementar retenção de fotos em 90 dias a partir do marco mais recente de encerramento e quitação, ignorando atendimento ativo ou com saldo e negando leitura de item vencido mesmo antes da remoção física. Cobrir com fixtures SQL de atendimento ativo, com saldo, entregue e quitado, retomada e auditoria de descarte (DT-21; F5-T06).

## 5. Contratos, autorização e projeções — F5-05

- [x] 5.1 Implementar `POST/GET /api/v1/attendances/{id}/inspections/{tipo}/photos` e `GET .../photos/{fotoId}/content` com `tipo` `entrada`/`saida`, multipart de um arquivo, `Idempotency-Key`, `expectedAttendanceVersion`, autorização por perfil e Problem Details. Documentar no OpenAPI status/códigos e testar `400`, `409`, `413`, `415`, `422` e `503` (F5-T05).
- [x] 5.2 Implementar leitura explícita auditada com `no-store`, `nosniff`, MIME do servidor, stream e falha fechada; garantir que listas, detalhe comum e logs não exponham binário, chave ou URL e que a auditoria confirme antes da transmissão (RF-13; RNF-02/07).
- [x] 5.3 Estender detalhe, histórico e prontidão de entrega com quantidade/presença de fotos, janela e estado do orçamento, filtrando por perfil antes de paginar. Testar Manobrista sem finanças/documento, Caixa/Admin, foto vencida e ausência de chave S3 no JSON bruto (specs `consulta-atendimentos`, `entrega-veiculo`).
- [x] 5.4 Garantir que a transação de entrega feche a janela de fotos e que upload concorrente falhe ou vire órfão reconciliável, sem bloquear a entrega por ausência de foto. Cobrir em integração SQL a corrida entrega × confirmação de foto e o replay da entrega sem reabrir a janela (F5-T04/T05; spec `entrega-veiculo`).

## 6. Interface incremental e acessibilidade — F5-05

- [x] 6.1 Implementar no detalhe de atendimento a seção de fotos da vistoria de entrada conforme [atendimento-fotos-entrada.html](prototypes/atendimento-fotos-entrada.html): contagem, janela, envio só a Caixa/Admin, consulta pelos três, estados de limite/cota/armazenamento e leitura em blob temporário com revogação. Verificar com Vitest os perfis, erros focados, descarte de respostas tardias e nenhuma URL do R2 (spec `fotos-vistoria`).
- [x] 6.2 Implementar na operação de entrega a seção de fotos de saída e a prontidão com “fotos opcionais” conforme [entrega-fotos-saida.html](prototypes/entrega-fotos-saida.html), incluindo envio pelos três perfis até a entrega, fechamento da janela e contingência textual. Verificar com Vitest perfis, bloqueios, entrega sem fotos e janela encerrada (specs `vistoria-saida`, `entrega-veiculo`).
- [x] 6.3 Reutilizar o coordenador de comandos para tentativa incerta de upload: bloquear navegação/logout, avisar saída do browser, repetir explicitamente a mesma chave/conteúdo, não reenviar automaticamente e limpar ao trocar ator/rota/sessão. Cobrir com Vitest e verificar ausência de `localStorage`/`sessionStorage`/IndexedDB (RNF-03; F5-T05).
- [ ] 6.4 Revisar os protótipos e a SPA nos dois sentidos: todo cenário coberto aparece na interface e nenhum controle/estado novo carece de spec. Registrar a divergência intencional (Tailwind 4.3.5 nativo na SPA, controles de revisão apenas no protótipo) e fazer avaliação manual de teclado/foco/tecnologia assistiva com ambiente registrado, sem declarar WCAG por inferência (RNF-04; PRD §5.6).

## 7. Gate local, evidências e documentação — F5-06/07

- [x] 7.1 Rodar build, MSTest unitário e integração em SQL Server real isolado, Vitest e Playwright local em duas origens HTTPS distintas; cobrir entrada/saída com e sem fotos, seis anexos concorrentes, replay, R2 indisponível, orçamento 70/80/desconhecido, leitura auditada por perfil, retenção e regressões das Fases 1–4. Registrar comandos, cenários, resultados e limitações em `docs/fases/fase-5.md` (F5-T07; TDD §7.1).
- [x] 7.2 Executar a suíte opt-in de contrato real com R2 privado e imagens sintéticas, incluindo queda após `PutObject`, reconciliação, limpeza de órfão e leitura de conteúdo; registrar bucket/ambiente, resultados e o que não foi provado (F5-T07).
- [x] 7.3 Documentar o runbook de reconciliação de consumo (franquias, ajustes de outros ambientes/cópias, aviso 70%/suspensão 80%, conferência manual da conta e contingência textual) e validar que nenhum passo imprime credenciais ou chaves (DT-18/23; F5-T06).
- [x] 7.4 Atualizar o roadmap do TDD somente para verificações efetivamente concluídas, manter PRD/TDD coerentes com decisões posteriores e rodar `openspec validate --all --strict`. Checar rastreabilidade RF-13, F5-T01–07, F5-01–07 e os protótipos antes da revisão do PR.

## 8. Check-in com pré-seleção de fotos — F5-05

- [x] 8.1 Permitir ao formulário de check-in selecionar 0–5 fotos JPEG/PNG/WebP de até 5 MiB, validar localmente e manter os arquivos somente em memória, sem enviar conteúdo de foto no corpo, na chave ou na transação do check-in. Cobrir com Vitest: seleção válida, sexta foto, formato/tamanho inválido, remoção antes do envio e ausência de upload antes do `201` (spec `atendimentos`; D5-11).
- [x] 8.2 Após o check-in confirmado, consultar a versão do atendimento e renderizar a fila de fotos da vistoria criada, enviando uma foto por requisição com chave própria e estados por foto. Verificar que a entrada permanece confirmada se a consulta de versão falhar e que nenhuma foto é enviada antes do sucesso (specs `atendimentos`, `fotos-vistoria`; D5-11).
- [x] 8.3 Tratar falha isolada de foto após o check-in: manter a entrada e as fotos confirmadas, permitir repetir somente a foto afetada com mesma chave/conteúdo, impedir reenvio automático com chave nova e descartar seleções não enviadas em novo check-in, logout, troca de rota ou saída confirmada do navegador. Cobrir com Vitest e Playwright (RNF-03; D5-11).
- [ ] 8.4 Atualizar o protótipo [check-in-fotos-entrada.html](prototypes/check-in-fotos-entrada.html) e revisar a SPA nos dois sentidos: seleção antes do check-in, confirmação, fila, falha/retry, check-in rejeitado/incerto e saída. Fazer avaliação manual de teclado/foco/tecnologia assistiva com ambiente registrado, sem declarar WCAG por inferência (RNF-04; PRD §5.6).
- [x] 8.5 Executar Vitest, Playwright e as regressões das Fases 1–4; atualizar `docs/fases/fase-5.md`, roadmap/TDD e evidências com os novos cenários de check-in, sem alterar a conclusão dos gates anteriores.

## Workflow follow-up

- Depois da implementação e das verificações aplicáveis, abrir PR da feature branch para `main` como **Ready for review**, conforme `AGENTS.md` e `.github/pull_request_template.md`, sem integrar diretamente.
- No fluxo de sincronização/arquivamento, revisar os `Purpose` das specs principais `consulta-atendimentos`, `vistoria-saida`, `entrega-veiculo` e `idempotencia-comandos` para refletir fotos e confirmação em duas fases; validar novamente as specs principais.
