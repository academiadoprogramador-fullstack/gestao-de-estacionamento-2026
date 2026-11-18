# consulta-atendimentos Specification

## Purpose

Define como a equipe localiza atendimentos, recebe projeções adequadas ao perfil e obtém documento e vistoria somente por leitura explícita, auditada e imune a respostas tardias de outra seleção.

## Requirements

### Requirement: Busca exata por placa ou identificador

O sistema SHALL permitir aos três perfis buscar atendimentos exigindo exatamente um filtro, placa normalizada ou identificador, com paginação padrão de 20 e máxima de 100 e ordem estável por horário de entrada e identificador.

#### Scenario: Busca por placa

- **WHEN** um funcionário busca uma placa existente
- **THEN** a API devolve as visitas correspondentes normalizadas, sem confundir entradas distintas do mesmo veículo

#### Scenario: Busca por identificador

- **WHEN** um funcionário busca um identificador público existente
- **THEN** a API devolve o atendimento correspondente com estado e vaga permitidos ao perfil

#### Scenario: Filtros ausentes ou combinados

- **WHEN** a busca não informa filtro ou informa placa e identificador ao mesmo tempo
- **THEN** a API recusa a consulta em vez de listar todos os atendimentos por omissão

#### Scenario: Nenhum resultado

- **WHEN** a busca não encontra correspondência
- **THEN** a resposta é uma página vazia, sem revelar se o valor existe em outro contexto

### Requirement: Projeções por perfil no servidor

O sistema SHALL omitir documento completo e vistoria das listagens e do detalhe comum e SHALL omitir versão e valor de tarifa e demais dados financeiros do JSON entregue ao Manobrista, negando também no servidor o que o perfil não pode ver.

#### Scenario: Listagem sem conteúdo sensível

- **WHEN** qualquer perfil consulta a busca de atendimentos
- **THEN** os itens incluem identificação operacional e estado, sem documento completo ou texto de vistoria

#### Scenario: Dados financeiros fora do perfil

- **WHEN** um Manobrista consulta detalhe ou histórico
- **THEN** a resposta não contém snapshot de tarifa, valores ou eventos financeiros, e a omissão ocorre no DTO, não apenas na tela

#### Scenario: Acesso negado no servidor

- **WHEN** um perfil requisita diretamente um recurso que não lhe é permitido
- **THEN** a API responde com acesso negado sem depender do guard da SPA

### Requirement: Leitura explícita e auditada de conteúdo sensível

O sistema SHALL expor documento completo somente a Caixa e Administrador e vistoria de entrada aos três perfis, por ação explícita, registrando ator, alvo e horário UTC antes de retornar o conteúdo e impedindo a resposta se a auditoria ou a decifragem falhar.

#### Scenario: Leitura de documento autorizada

- **WHEN** Caixa ou Administrador solicita explicitamente o documento de um atendimento
- **THEN** a API confirma o evento de leitura e então devolve o documento decifrado, sem registrá-lo na auditoria

#### Scenario: Leitura de vistoria pelos três perfis

- **WHEN** qualquer perfil solicita explicitamente a vistoria de entrada
- **THEN** a API audita a leitura antes de devolver o texto decifrado

#### Scenario: Falha de auditoria ou decifragem

- **WHEN** o registro de auditoria ou a decifragem falha
- **THEN** o conteúdo não é retornado, nem vazio nem em texto puro, e o erro não expõe o valor

#### Scenario: Manobrista e documento

- **WHEN** um Manobrista solicita o documento completo
- **THEN** a API nega a leitura e não registra conteúdo sensível na negativa

### Requirement: Descarte de respostas tardias

A SPA SHALL associar cada busca, detalhe, histórico e leitura sensível à seleção e ao contexto que a originou e SHALL descartar respostas que cheguem após troca de seleção, fechamento do detalhe, logout ou destruição da tela.

#### Scenario: Troca rápida de seleção

- **WHEN** uma resposta de detalhe de um atendimento anterior chega após a seleção de outro atendimento
- **THEN** a resposta é descartada e não substitui o conteúdo atual

#### Scenario: Fechamento antes da resposta

- **WHEN** o detalhe é fechado antes de a leitura sensível responder
- **THEN** o conteúdo não é exibido nem reabre o painel encerrado

#### Scenario: Logout durante consulta

- **WHEN** o logout ocorre antes da resposta de busca ou leitura
- **THEN** a resposta é descartada e não restaura dados na tela nem na sessão

### Requirement: Identificador copiável e acessível

A confirmação do check-in e o detalhe SHALL exibir o identificador público em texto e oferecer ação de copiar pelo funcionário, sem exigir impressão ou envio digital e sem esconder o valor de tecnologias assistivas.

#### Scenario: Cópia do identificador

- **WHEN** o funcionário aciona a ação de copiar
- **THEN** o identificador é copiado e a interface confirma textualmente o resultado

#### Scenario: Falha ao copiar

- **WHEN** a área de transferência não está disponível
- **THEN** o identificador permanece selecionável em texto e a interface informa a limitação

#### Scenario: Leitura por tecnologia assistiva

- **WHEN** o identificador é apresentado na confirmação ou no detalhe
- **THEN** ele é lido como texto normal, sem depender de cor, ícone ou conteúdo apenas visual
