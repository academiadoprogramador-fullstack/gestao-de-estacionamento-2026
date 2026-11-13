# Spec Delta

## Purpose

Permitir ao administrador preparar os acessos da equipe com perfis fixos e preservar a identidade dos responsáveis pelas operações.

## ADDED Requirements

### Requirement: Provisionamento administrativo de funcionários
O sistema SHALL permitir somente ao administrador listar e criar funcionários com e-mail único, credencial inicial e um ou mais dos perfis fixos manobrista, caixa e administrador, sem autorregistro nem exposição de credenciais em respostas ou logs.

#### Scenario: Cadastro válido
- **WHEN** um administrador cadastra funcionário com e-mail ainda não utilizado, senha válida e perfis conhecidos
- **THEN** a conta é criada e pode autenticar com os perfis atribuídos, sem retornar a senha

#### Scenario: Cadastro inválido ou indevido
- **WHEN** o cadastro contém e-mail duplicado, senha inválida, perfil desconhecido ou é solicitado por caixa/manobrista
- **THEN** a operação é rejeitada sem criar conta parcial ou atribuições de perfil

### Requirement: Alteração de acesso sem perda de autoria
O sistema SHALL permitir somente ao administrador alterar os perfis e o estado ativo do funcionário, preservando registros históricos e auditando a mudança. O sistema SHALL impedir que uma alteração deixe a unidade sem administrador ativo.

#### Scenario: Desativação preserva histórico
- **WHEN** o administrador desativa um funcionário que registrou entradas
- **THEN** o funcionário perde acesso e seus identificadores continuam associados às entradas e eventos originais

#### Scenario: Remoção do último administrador
- **WHEN** uma desativação ou remoção de perfil eliminaria o último administrador ativo
- **THEN** a alteração é rejeitada, inclusive sob solicitações concorrentes

#### Scenario: Mudanças concorrentes no mesmo funcionário
- **WHEN** dois administradores alteram a mesma versão de uma conta
- **THEN** a alteração obsoleta recebe conflito e não sobrescreve silenciosamente a anterior
