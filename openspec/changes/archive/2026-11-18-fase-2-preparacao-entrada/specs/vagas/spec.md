# Spec Delta

## Purpose

Define o cadastro administrativo das vagas da unidade e a consulta de disponibilidade compartilhada pelos perfis, sem reserva pela tela e com atualização periódica previsível.

## ADDED Requirements

### Requirement: Cadastro de vaga com código único

O sistema SHALL permitir apenas ao Administrador cadastrar vaga com código de 1 a 32 caracteres após remover espaços nas extremidades e converter para maiúsculas, garantindo unicidade e versão de concorrência.

#### Scenario: Cadastro válido

- **WHEN** o Administrador informa um código inédito dentro dos limites
- **THEN** a vaga é criada livre, com o código normalizado e versão inicial, e a auditoria registra o ator e o horário do servidor

#### Scenario: Código duplicado ou inválido

- **WHEN** o código normalizado já existe, está vazio ou excede 32 caracteres
- **THEN** a API recusa o cadastro com erro de validação ou conflito e não cria vaga parcial

#### Scenario: Acesso de outro perfil

- **WHEN** Caixa ou Manobrista tenta cadastrar vaga
- **THEN** a API nega a operação independentemente da interface

### Requirement: Consulta paginada de vagas

O sistema SHALL permitir aos três perfis consultar vagas com filtro informativo por código ou situação livre/ocupada, paginação padrão de 20 e máxima de 100, retornando itens, página, tamanho e total em ordem estável.

#### Scenario: Consulta pelos três perfis

- **WHEN** Administrador, Caixa ou Manobrista consulta a lista de vagas com filtros válidos
- **THEN** a resposta mostra código, situação derivada e versão de cada vaga na página solicitada

#### Scenario: Situação derivada da ocupação

- **WHEN** uma vaga possui ocupação aberta vinculada a um atendimento ativo
- **THEN** a consulta a apresenta como ocupada, e como livre somente após o encerramento da ocupação

#### Scenario: Disponibilidade não reserva

- **WHEN** um funcionário visualiza ou seleciona uma vaga livre no seletor de check-in
- **THEN** nenhuma reserva é criada e a atribuição definitiva continua sendo decidida pela transação do servidor

### Requirement: Atualização periódica sem sobreposição

A consulta de vagas SHALL atualizar por polling com intervalo padrão de 30 segundos e configurável, pausar em aba oculta, atualizar ao recuperar foco e após comando, cancelar consultas ao sair e nunca manter requisições sobrepostas.

#### Scenario: Atualização ao recuperar foco

- **WHEN** a aba oculta volta a ficar visível
- **THEN** a tela consulta o estado atual antes de apresentar a disponibilidade

#### Scenario: Aba oculta pausa o polling

- **WHEN** a aba permanece oculta
- **THEN** nenhuma nova consulta periódica é disparada até o retorno do foco

#### Scenario: Falha de atualização preserva contexto

- **WHEN** uma atualização periódica falha
- **THEN** a tela informa a falha, mantém filtros e página atuais e tenta novamente no próximo ciclo sem empilhar requisições

#### Scenario: Paginação completa e recuperável

- **WHEN** existem mais vagas do que o tamanho de página e uma carga falha
- **THEN** é possível navegar por todas as páginas e recarregar a página corrente sem perder filtros ou seleção do formulário
