# Spec Delta — Idempotência com I/O externo

## ADDED Requirements

### Requirement: Reserva e confirmação de comando com efeito externo

Para o envio de foto, o sistema SHALL reservar a tentativa idempotente e a cota de anexos em transação SQL antes do I/O externo, SHALL confirmar o resultado mínimo e os metadados em nova transação após o armazenamento e SHALL NOT manter transação aberta durante o R2. A mesma chave e conteúdo SHALL devolver o resultado original; conteúdo divergente SHALL conflitar (TDD F5-T04).

#### Scenario: Reserva antes do armazenamento

- **WHEN** o ator autorizado envia uma foto com `Idempotency-Key` válida
- **THEN** a API grava a reserva com vínculo, hash e cota antes de chamar o R2 e só então inicia o envio do conteúdo tratado

#### Scenario: Replay depois da confirmação

- **WHEN** o mesmo ator repete a mesma chave e o mesmo conteúdo após a confirmação
- **THEN** recebe o resultado mínimo original com identificação e estado da foto, sem novo objeto, metadado ou consumo de cota

#### Scenario: Rejeição antes do I/O

- **WHEN** a reserva falha por limite, janela, versão, autorização ou conflito de chave
- **THEN** nenhum envio ao R2 é iniciado e a chave não fica associada a resultado inexistente

#### Scenario: Resultado incerto

- **WHEN** a resposta se perde ou a confirmação SQL falha após o envio ao R2
- **THEN** a tentativa permanece somente em memória no cliente e pode ser repetida com a mesma chave/conteúdo ou reconciliada pelo servidor, sem retry automático com chave nova
