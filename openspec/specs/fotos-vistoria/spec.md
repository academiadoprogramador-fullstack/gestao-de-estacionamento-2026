# fotos-vistoria Specification

## Purpose

Preserva evidência visual opcional das vistorias de entrada e saída em armazenamento privado, com limites, janelas, autorização, idempotência, orçamento e retenção explícitos, sem tornar as fotos requisito do fluxo textual.

## Requirements

### Requirement: Anexos opcionais e janelas por vistoria

O sistema SHALL aceitar de zero a cinco fotos por vistoria textual já salva, SHALL permitir fotos de entrada somente a Caixa e Administrador até o pedido de retirada e de saída aos três perfis até a entrega, e SHALL NOT alterar o texto, substituir foto confirmada ou aceitar novo anexo após o fechamento da janela (RF-13; PRD §5.6).

#### Scenario: Vistoria sem fotos

- **WHEN** a vistoria é salva e o funcionário não anexa foto ou o armazenamento está indisponível
- **THEN** a entrada ou entrega permanece válida pelo texto e a tela informa que fotos são opcionais

#### Scenario: Anexo dentro da janela

- **WHEN** Caixa ou Administrador anexa foto à vistoria de entrada antes do pedido, ou qualquer perfil anexa foto à vistoria de saída antes da entrega
- **THEN** a foto é processada e vinculada à vistoria, mantendo o texto inalterado

#### Scenario: Janela encerrada

- **WHEN** o pedido de retirada já foi criado, a entrada já foi entregue ou outro upload concorrente fechou a janela
- **THEN** a API recusa o novo anexo sem remover ou substituir fotos confirmadas

#### Scenario: Perfil não autorizado

- **WHEN** um Manobrista tenta enviar foto da vistoria de entrada ou um perfil sem sessão válida tenta anexar ou consultar
- **THEN** a API nega a operação, sem gravar reserva, metadado ou objeto

### Requirement: Validação e normalização da imagem

O sistema SHALL aceitar um arquivo por requisição de até 5 MiB em JPEG, PNG ou WebP estáticos, validar o conteúdo real, recusar SVG, animação, corrupção, MIME incompatível e imagens acima de 20 MP ou 10.000 px por lado, e SHALL regravar WebP estático de até 2.560 px no lado maior, qualidade 85, sem EXIF/GPS ou metadados copiados (DT-17/22; TDD §4.4).

#### Scenario: Imagem válida normalizada

- **WHEN** o arquivo enviado é uma imagem estática aceita dentro dos limites
- **THEN** a API corrige a orientação, reduz proporcionalmente sem ampliar, grava WebP com os parâmetros definidos e persiste somente o resultado tratado

#### Scenario: Formato ou conteúdo inválido

- **WHEN** o arquivo é SVG, animação, imagem corrompida, tipo declarado incompatível ou conteúdo diferente de JPEG/PNG/WebP estático
- **THEN** a API recusa a foto sem armazenar objeto nem metadado confirmado e informa o motivo em texto

#### Scenario: Limites excedidos

- **WHEN** o arquivo ultrapassa 5 MiB, 20 MP ou 10.000 px por lado
- **THEN** a API recusa a foto e a tela permite continuar a vistoria sem ela

#### Scenario: Metadados removidos

- **WHEN** a imagem contém EXIF, GPS ou outros metadados
- **THEN** a versão armazenada não contém esses dados e não preserva o original enviado

### Requirement: Armazenamento privado e metadados mínimos

O sistema SHALL armazenar binários em bucket privado compatível com S3, acessível somente pelo servidor, e SHALL persistir no SQL apenas vínculo à vistoria, tipo, chave opaca, estado, MIME validado, tamanho, dimensões, hash SHA-256, ator, UTC e versão, sem binário, base64, nome original, credencial ou URL pública (DT-16; RNF-02).

#### Scenario: Metadados confirmados

- **WHEN** uma foto é confirmada
- **THEN** a resposta e a consulta mostram apenas identificação, estado e metadados permitidos, sem chave do objeto ou credencial do armazenamento

#### Scenario: Bucket privado

- **WHEN** alguém tenta acessar o objeto sem passar pela API autenticada
- **THEN** não existe domínio público nem URL assinada exposta ao navegador para a foto

#### Scenario: Falha do armazenamento

- **WHEN** o envio ao armazenamento falha ou fica incerto
- **THEN** nenhuma foto disponível é anunciada e a operação textual segue sem depender do objeto

### Requirement: Consulta explícita, auditada e sem cache

O sistema SHALL permitir aos três perfis listar metadados e obter o conteúdo de fotos disponíveis da vistoria, SHALL registrar a leitura explícita com ator, alvo e UTC antes de transmitir e SHALL responder com `no-store`, `nosniff` e MIME determinado pelo servidor, negando conteúdo vencido ou de vínculo inválido (RF-13; RNF-02/07).

#### Scenario: Consulta de metadados

- **WHEN** um dos três perfis abre a vistoria e solicita a lista de fotos
- **THEN** recebe somente fotos disponíveis daquela vistoria e tipo, com identificação e metadados mínimos

#### Scenario: Leitura de conteúdo

- **WHEN** o funcionário aciona explicitamente uma foto listada
- **THEN** a API audita a leitura e transmite o binário temporário pela própria API, com cache proibido

#### Scenario: Falha de auditoria ou vínculo

- **WHEN** a auditoria falha, a foto pertence a outra vistoria ou o objeto não existe mais
- **THEN** a API não transmite conteúdo nem associa a foto ao atendimento consultado e informa a indisponibilidade

#### Scenario: Item vencido

- **WHEN** a foto já venceu pela retenção
- **THEN** a leitura é negada mesmo que a remoção física ainda esteja pendente, sem devolver objeto ou metadado pessoal

### Requirement: Limite de cinco sob concorrência

O sistema SHALL contar reservas pendentes e fotos disponíveis no limite de cinco por vistoria, SHALL reservar cada tentativa em transação com bloqueio da vistoria e SHALL NOT confirmar mais de cinco fotos nem duplicar anexo por repetição concorrente (TDD F5-T02).

#### Scenario: Quinto anexo aceito

- **WHEN** a vistoria possui quatro fotos confirmadas e chega um novo envio dentro da janela
- **THEN** a quinta tentativa pode ser confirmada e a sexta é recusada com regra de limite

#### Scenario: Sexto anexo concorrente

- **WHEN** dois ou mais envios disputam a última vaga ou ultrapassam o limite simultaneamente
- **THEN** no máximo cinco fotos ficam disponíveis e as demais tentativas falham sem objeto órfão confirmado

#### Scenario: Repetição da mesma foto

- **WHEN** o mesmo ator repete a mesma chave e o mesmo conteúdo enquanto a tentativa está pendente ou já confirmada
- **THEN** o limite é contado uma única vez e nenhuma foto adicional é criada

### Requirement: Envio idempotente e reconciliação

O sistema SHALL reservar a tentativa idempotente antes do I/O externo, confirmar os metadados em nova transação após o armazenamento e SHALL NOT manter transação SQL aberta durante o R2. Repetição com mesma chave e conteúdo SHALL devolver o resultado original; conteúdo divergente SHALL conflitar. Falha após escrita SHALL deixar reconciliação durável e órfãos SHALL ser limpos após 24 h sem upload ativo (TDD F5-T04).

#### Scenario: Resposta perdida após envio

- **WHEN** o navegador perde a resposta depois de a API gravar no armazenamento ou confirmar os metadados
- **THEN** a tela preserva a tentativa em memória e a repetição explícita com a mesma chave e conteúdo devolve a foto original, sem duplicar objeto

#### Scenario: Falha entre armazenamento e confirmação

- **WHEN** a gravação no armazenamento ocorre, mas a confirmação SQL falha
- **THEN** a tentativa permanece reconciliável e não é anunciada como foto disponível até a verificação

#### Scenario: Chave reutilizada com outro conteúdo

- **WHEN** a mesma chave é reenviada com arquivo, tipo de vistoria ou alvo diferente
- **THEN** a API responde conflito sem alterar a tentativa original

#### Scenario: Limpeza de órfão

- **WHEN** existe objeto sem confirmação após 24 h, sem lease e sem upload ativo
- **THEN** a rotina retomável remove o objeto e registra a limpeza, nunca apagando foto disponível para liberar cota

### Requirement: Orçamento de consumo e contingência

O sistema SHALL contabilizar bytes e operações Classes A/B de forma compartilhada entre ambientes, SHALL avisar aos 70% e recusar operações que ultrapassariam 80% da franquia correspondente, SHALL suspender fotos quando o consumo for desconhecido e SHALL NOT impedir check-in ou entrega textual por indisponibilidade do armazenamento (DT-18; RNF-06/07).

#### Scenario: Aviso de consumo

- **WHEN** o consumo atingir 70% de qualquer franquia controlada
- **THEN** a interface mostra aviso textual e as operações permitidas continuam dentro do limite

#### Scenario: Bloqueio antes de exceder

- **WHEN** um envio ou leitura faria o consumo ultrapassar 80% da franquia correspondente, somando reservas e retries
- **THEN** a API recusa somente a operação de foto com mensagem específica, sem excluir evidência existente

#### Scenario: Consumo desconhecido

- **WHEN** não é possível reconciliar o consumo com segurança
- **THEN** novos anexos e leituras de foto ficam suspensos até a reconciliação, enquanto o fluxo textual permanece

#### Scenario: R2 indisponível

- **WHEN** o bucket está fora do ar, recusa conexão ou excede limite
- **THEN** a tela informa indisponibilidade de fotos e permite concluir a vistoria e a entrega pelo texto, sem repetição automática com chave nova

### Requirement: Retenção e descarte de fotos

O sistema SHALL descartar fotos após 90 dias contados do marco mais recente de encerramento e quitação, SHALL NOT descartar atendimento ativo ou com saldo, SHALL NOT apagar evidências antecipadamente para liberar cota e SHALL executar o descarte de forma auditada e retomável ao iniciar ou reativar o serviço (DT-21; PRD §10).

#### Scenario: Atendimento vencido

- **WHEN** o atendimento encerrado e quitado completa o prazo de retenção
- **THEN** as fotos deixam de ser listadas e lidas, e a remoção física pode ocorrer em lote posterior

#### Scenario: Atendimento ativo ou com saldo

- **WHEN** o atendimento ainda está ativo ou possui saldo pendente
- **THEN** suas fotos não entram no descarte automático, mesmo que tenham sido enviadas há mais de 90 dias

#### Scenario: Retomada após indisponibilidade

- **WHEN** a aplicação reinicia ou é reativada com descarte pendente
- **THEN** a rotina retoma em lotes o que venceu, preservando evento de descarte sem conteúdo e sem reexpor itens vencidos

#### Scenario: Nenhuma exclusão manual

- **WHEN** o funcionário consulta uma foto confirmada
- **THEN** a interface não oferece exclusão, substituição ou novo upload fora da janela da vistoria

### Requirement: Envio de fotos pré-selecionadas no check-in

Após o check-in confirmado, o sistema SHALL aceitar o envio individual das fotos pré-selecionadas na entrada, uma por requisição, com chave idempotente própria, e SHALL revalidar vínculo, janela, versão e limite. Falha de uma foto SHALL NOT desfazer a entrada nem impedir a repetição das demais (RF-13).

#### Scenario: Fila de fotos após o check-in

- **WHEN** o check-in é confirmado e a SPA carrega a versão do atendimento
- **THEN** cada foto selecionada é enviada em uma requisição própria, com vínculo à vistoria de entrada e chave distinta

#### Scenario: Falha isolada e repetição

- **WHEN** uma das fotos falha enquanto outra é confirmada
- **THEN** a entrada e as fotos confirmadas permanecem válidas, a foto afetada pode ser repetida com a mesma chave/conteúdo e as demais não são reenviadas

#### Scenario: Janela encerrada durante a fila

- **WHEN** o pedido de retirada é criado antes de uma foto pré-selecionada ser confirmada
- **THEN** a API recusa apenas essa foto, a entrada continua registrada e a tela informa que o anexo de entrada foi encerrado

#### Scenario: Nenhum envio antes da confirmação

- **WHEN** o check-in ainda está pendente, incerto ou foi rejeitado
- **THEN** nenhuma reserva, objeto ou metadado de foto é criado, mesmo que arquivos tenham sido selecionados
