# vistoria-saida Specification

## Purpose

Preserva a evidência textual das condições do veículo na saída, vinculada à retirada e acessível somente em leitura explícita e auditada.

## Requirements

### Requirement: Registro único da vistoria de saída

O sistema SHALL permitir aos três perfis registrar uma única vistoria textual de saída por atendimento com pedido ainda não entregue. SHALL exigir texto de 1 a 4000 caracteres após trim e versão-base do atendimento. O registro SHALL ser imutável e vinculado ao pedido, com ator e UTC do servidor (F4-R01).

#### Scenario: Registro confirmado na tela de entrega

- **WHEN** Manobrista, Caixa ou Administrador salva observações válidas para pedido em aberto
- **THEN** a tela indica vistoria registrada e não oferece edição nem substituição; o histórico registra autoria e horário sem o texto

#### Scenario: Texto inválido ou propriedades inesperadas

- **WHEN** o texto fica vazio após trim, excede o limite ou o cliente envia foto, novos dados documentais, ator ou horário
- **THEN** a API recusa sem persistir; na tela, erro textual de texto inválido é associado ao campo com resumo focado

#### Scenario: Vistoria inelegível ou concorrente

- **WHEN** falta pedido, o atendimento foi entregue, já há vistoria ou a versão informada mudou
- **THEN** um novo registro é recusado sem substituir texto; em conflito a tela informa a rejeição e permite recarregar o estado

### Requirement: Proteção e leitura explícita da vistoria de saída

O sistema SHALL cifrar o texto com vínculo próprio ao atendimento e SHALL disponibilizá-lo aos três perfis apenas em leitura explícita auditada, sem cache, com ator, alvo e horário UTC. Listas, detalhe comum, recibo e log SHALL NOT incluir o texto (F4-R01/06).

#### Scenario: Consulta explícita ou por projeção

- **WHEN** um funcionário autenticado abre a operação de entrega com uma vistoria registrada
- **THEN** a tela mostra apenas a presença do registro até a ação de consultar; somente após auditoria confirmada apresenta o texto na leitura solicitada

#### Scenario: Falha fechada na leitura

- **WHEN** registro de auditoria ou decifragem falha
- **THEN** a API não devolve texto vazio, puro ou parcial e a tela informa falha sem expor conteúdo

#### Scenario: Registro revertido ou repetido

- **WHEN** a gravação da auditoria ou do resultado idempotente falha, ou o comando é reenviado após commit com a mesma chave e conteúdo
- **THEN** na falha nada é confirmado; no replay autorizado é devolvido somente o recibo original, sem texto ou novo registro, mesmo após entrega
