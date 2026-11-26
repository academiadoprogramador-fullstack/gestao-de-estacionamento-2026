# Spec Delta — Vistoria de saída com fotos opcionais

## MODIFIED Requirements

### Requirement: Registro único da vistoria de saída

O sistema SHALL permitir aos três perfis registrar uma única vistoria textual de saída por atendimento com pedido ainda não entregue. SHALL exigir texto de 1 a 4000 caracteres após trim e versão-base do atendimento. O registro SHALL ser imutável e vinculado ao pedido, com ator e UTC do servidor; fotos opcionais SHALL ser anexadas depois do texto, até a entrega, sem alterar o registro (F4-R01; RF-13).

#### Scenario: Registro confirmado na tela de entrega

- **WHEN** Manobrista, Caixa ou Administrador salva observações válidas para pedido em aberto
- **THEN** a tela indica vistoria registrada e não oferece edição nem substituição; o histórico registra autoria e horário sem o texto

#### Scenario: Texto inválido ou propriedades inesperadas

- **WHEN** o texto fica vazio após trim, excede o limite ou o cliente envia foto, novos dados documentais, ator ou horário no mesmo comando
- **THEN** a API recusa sem persistir; na tela, erro textual de texto inválido é associado ao campo com resumo focado

#### Scenario: Vistoria inelegível ou concorrente

- **WHEN** falta pedido, o atendimento foi entregue, já há vistoria ou a versão informada mudou
- **THEN** um novo registro é recusado sem substituir texto; em conflito a tela informa a rejeição e permite recarregar o estado

#### Scenario: Fotos após o texto

- **WHEN** a vistoria de saída já foi salva e ainda não houve entrega
- **THEN** os três perfis podem anexar fotos opcionais e consultá-las; o texto permanece imutável e a ausência de fotos não bloqueia a entrega

#### Scenario: Fotos após a entrega

- **WHEN** a entrega já foi confirmada
- **THEN** nenhum novo anexo é aceito, inclusive envio concorrente iniciado antes da entrega, e as fotos confirmadas permanecem consultáveis até a retenção
