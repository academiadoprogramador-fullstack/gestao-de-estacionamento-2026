# billing/daily-rate Specification

## Purpose

Manter a tarifa diária configurada pela administração e preservar a condição comercial aplicável a cada entrada de veículo.

## Requirements

### Requirement: Configuração versionada da diária
O sistema SHALL permitir somente ao administrador consultar a configuração e publicar uma nova versão da tarifa diária com valor positivo, precisão explícita, horário e responsável. Não SHALL presumir uma tarifa inicial nem alterar versões anteriores.

#### Scenario: Primeira tarifa e revisão
- **WHEN** um administrador publica uma tarifa válida e depois outra de valor diferente
- **THEN** ambas permanecem preservadas e a última publicação válida é a vigente para novas entradas

#### Scenario: Valor inválido ou concorrência de publicação
- **WHEN** a publicação tem valor não positivo, precisão inválida ou versão-base obsoleta
- **THEN** a configuração não é alterada e o sistema informa validação ou conflito

### Requirement: Snapshot na entrada
O sistema SHALL capturar a versão e o valor da tarifa vigente na entrada, sem aceitar tarifa ou horário fornecidos pelo cliente como fonte de verdade. O snapshot SHALL permanecer inalterado após novas publicações de tarifa.

#### Scenario: Revisão posterior à entrada
- **WHEN** uma entrada foi registrada com tarifa A e a administração publica tarifa B
- **THEN** o atendimento preserva A e novas entradas usam B

#### Scenario: Entrada sem tarifa
- **WHEN** um caixa tenta registrar entrada antes da primeira tarifa configurada
- **THEN** a entrada é bloqueada com mensagem explicativa e não ocupa vaga

#### Scenario: Entrada simultânea à publicação
- **WHEN** um check-in e uma publicação de tarifa ocorrem simultaneamente
- **THEN** a entrada captura uma única versão vigente em uma ordem transacional consistente, sem combinar valor e versão de publicações diferentes
