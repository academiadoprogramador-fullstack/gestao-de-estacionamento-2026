# parking/space-availability Specification

## Purpose

Disponibilizar o cadastro de vagas de uma unidade e uma visão de disponibilidade que não permita dupla ocupação durante o atendimento.

## Requirements

### Requirement: Cadastro administrativo de vagas
O sistema SHALL permitir somente ao administrador cadastrar vagas com código obrigatório e único após normalização, rejeitando códigos duplicados sem criar registros parciais.

#### Scenario: Vaga cadastrada
- **WHEN** o administrador informa um código válido ainda não cadastrado
- **THEN** a vaga passa a constar como livre na consulta operacional

#### Scenario: Código equivalente ou perfil indevido
- **WHEN** o código difere de outro apenas por espaços externos/caixa ou o solicitante não é administrador
- **THEN** o cadastro é rejeitado sem duplicar a vaga

### Requirement: Disponibilidade consultável pela equipe
O sistema SHALL permitir aos três perfis consultar vagas por código e situação livre/ocupada com paginação, sem expor dados financeiros ou documentos de titulares. A interface SHALL atualizar disponibilidade por polling configurável, após comandos e ao recuperar foco.

#### Scenario: Atualização de ocupação
- **WHEN** uma entrada ocupa uma vaga e a equipe atualiza a consulta
- **THEN** a vaga aparece ocupada e não é apresentada como livre para nova entrada

#### Scenario: Aba inativa ou consulta em andamento
- **WHEN** a aba fica inativa ou uma consulta de polling ainda está em andamento
- **THEN** a interface pausa novas consultas periódicas e não sobrepõe requisições

### Requirement: Ocupação exclusiva vinculada à entrada
O sistema SHALL atribuir cada vaga a no máximo um atendimento com ocupação aberta, apenas por check-in válido. Vaga inexistente ou já ocupada SHALL impedir a entrada, mesmo quando uma consulta anterior a indicava livre.

#### Scenario: Disputa pela última vaga
- **WHEN** duas entradas com chaves distintas disputam a mesma vaga livre simultaneamente
- **THEN** somente uma conclui e a outra recebe conflito, sem atendimento, vistoria ou ocupação residual

#### Scenario: Vaga inexistente
- **WHEN** uma entrada referencia vaga que não existe
- **THEN** a operação é rejeitada sem persistir atendimento
