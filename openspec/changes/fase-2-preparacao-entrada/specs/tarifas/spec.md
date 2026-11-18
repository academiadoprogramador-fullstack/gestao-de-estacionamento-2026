# Spec Delta

## Purpose

Define a publicação administrativa de versões imutáveis da diária, a configuração corrente sem valor comercial padrão e o histórico consultável que garante previsibilidade para o snapshot da entrada.

## ADDED Requirements

### Requirement: Publicação de versão imutável de tarifa

O sistema SHALL permitir apenas ao Administrador publicar nova versão de diária com valor positivo em `decimal(18,2)`, rejeitando precisão excedente, valor zero ou negativo; cada publicação SHALL registrar valor, autor e horário UTC e SHALL NOT alterar versões anteriores.

#### Scenario: Publicação válida

- **WHEN** o Administrador publica um valor positivo com no máximo duas casas decimais
- **THEN** a nova versão passa a ser a corrente e o histórico preserva integralmente as versões anteriores

#### Scenario: Valor inválido

- **WHEN** o valor é zero, negativo, possui mais de duas casas decimais ou excede o limite representável
- **THEN** a API recusa a publicação sem arredondar silenciosamente e sem criar versão

#### Scenario: Acesso de outro perfil

- **WHEN** Caixa ou Manobrista tenta publicar tarifa
- **THEN** a API nega a operação independentemente da interface

### Requirement: Configuração corrente com versão-base

A consulta da tarifa corrente SHALL devolver a versão publicada e sua versão-base de concorrência, SHALL admitir ausência de tarifa e SHALL recusar publicação cuja versão-base não corresponda à corrente.

#### Scenario: Unidade ainda sem tarifa

- **WHEN** nenhuma versão foi publicada
- **THEN** a consulta informa ausência de tarifa, sem inventar valor padrão ou comercial

#### Scenario: Concorrência de publicação

- **WHEN** duas publicações partem da mesma versão-base
- **THEN** apenas uma é confirmada e a outra recebe conflito com a configuração corrente recarregável

#### Scenario: Nova consulta após publicação

- **WHEN** a tarifa é publicada ou substituída
- **THEN** a próxima consulta devolve a versão corrente e o histórico correspondente

### Requirement: Histórico de tarifas paginado

O sistema SHALL permitir apenas ao Administrador consultar o histórico de versões com paginação padrão de 20 e máxima de 100, em ordem estável e sem edição, exclusão ou recifragem de versões passadas.

#### Scenario: Histórico consultável

- **WHEN** o Administrador consulta o histórico
- **THEN** a resposta lista versão, valor, autor e horário de publicação, permitindo navegar por todas as páginas

#### Scenario: Falha de carga recuperável

- **WHEN** uma página do histórico falha ao carregar
- **THEN** a interface informa a falha e permite nova tentativa sem perder filtros nem formulário de publicação

#### Scenario: Versões passadas preservadas

- **WHEN** uma publicação posterior ocorre
- **THEN** as versões anteriores permanecem legíveis e inalteradas e não alteram atendimentos que já capturaram snapshot
