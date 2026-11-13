# parking/vehicle-check-in Specification

## Purpose

Registrar uma entrada completa de veículo, com identificação rastreável, vistoria textual e atribuição consistente de uma vaga livre.

## Requirements

### Requirement: Entrada completa por perfil autorizado
O sistema SHALL permitir check-in somente a caixa ou administrador, exigindo placa, modelo, nome e número do documento do titular, vistoria textual de entrada e vaga livre. O servidor SHALL atribuir identificador público único, estado ativo, horário UTC e operador autenticado.

#### Scenario: Entrada válida
- **WHEN** caixa ou administrador envia todos os dados válidos, com tarifa vigente e vaga livre
- **THEN** recebe identificador, horário, estado e vaga do atendimento criado, sem ecoar documento ou vistoria na resposta de criação

#### Scenario: Entrada incompleta ou sem permissão
- **WHEN** falta campo obrigatório, há somente espaços, um limite de tamanho é excedido ou um manobrista tenta criar entrada
- **THEN** a entrada é rejeitada sem ocupar vaga ou criar registros parciais

### Requirement: Atomicidade do check-in
O sistema SHALL confirmar atendimento, snapshot da tarifa, vistoria, ocupação, auditoria e resultado idempotente como uma única operação durável. Qualquer falha SHALL desfazer todos os efeitos da tentativa.

#### Scenario: Falha durante persistência
- **WHEN** ocorre falha ao salvar vistoria, ocupação, auditoria ou resultado idempotente
- **THEN** nenhum atendimento parcial fica consultável e a vaga continua livre

### Requirement: Repetição segura de entrada
O sistema SHALL exigir chave de idempotência por ação de check-in, vinculada ao ator, operação e conteúdo normalizado. Mesma chave e conteúdo SHALL devolver o resultado original; conteúdo diferente com a mesma chave SHALL gerar conflito. A autorização atual SHALL ser verificada também na repetição.

#### Scenario: Resposta perdida e repetição
- **WHEN** o commit ocorreu, a resposta se perdeu e o mesmo ator autorizado repete a mesma chave e conteúdo
- **THEN** recebe o mesmo identificador e resultado, sem nova ocupação, vistoria ou evento de entrada

#### Scenario: Mesma chave em chamadas simultâneas
- **WHEN** o mesmo ator envia simultaneamente duas chamadas com chave e conteúdo iguais
- **THEN** ambas convergem para o mesmo resultado confirmado, sem duplicar a entrada

#### Scenario: Chave reutilizada ou ausente
- **WHEN** o ator muda o conteúdo mantendo a chave ou envia check-in sem chave válida
- **THEN** recebe conflito ou erro de validação, respectivamente, sem novos efeitos

### Requirement: Feedback acessível e retomada segura
A interface SHALL apresentar erros textuais, rótulos e ações por teclado, impedir submissões sobrepostas e preservar a chave de uma tentativa com resultado desconhecido. Após sucesso SHALL exibir o identificador e atualizar vagas e busca, sem presumir emissão de ticket.

#### Scenario: Vaga ficou ocupada
- **WHEN** o servidor recusa o check-in porque outro atendimento ocupou a vaga selecionada
- **THEN** a interface explica o conflito, atualiza vagas e permite corrigir a seleção sem indicar sucesso

#### Scenario: Timeout após envio
- **WHEN** o navegador perde a resposta do check-in
- **THEN** a interface não declara falha definitiva nem sucesso e permite repetir o mesmo conteúdo com a mesma chave antes de iniciar nova ação
