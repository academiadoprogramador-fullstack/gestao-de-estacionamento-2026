# Spec Delta

## Purpose

Permitir localizar veículos e distinguir seus atendimentos com informações operacionais adequadas ao perfil de cada funcionário.

## ADDED Requirements

### Requirement: Busca por placa ou identificador
O sistema SHALL permitir aos três perfis buscar atendimentos por placa normalizada ou identificador público, retornando lista paginada com identificador, placa, modelo, entrada, estado e vaga. Visitas com a mesma placa SHALL permanecer distintas, sem impor unicidade histórica da placa.

#### Scenario: Placa com mais de uma entrada
- **WHEN** existem atendimentos distintos com a mesma placa e a equipe pesquisa por ela
- **THEN** os resultados apresentam identificadores e horários próprios, sem fundir as visitas

#### Scenario: Normalização e ausência de resultado
- **WHEN** a equipe pesquisa a placa com diferenças de caixa/separadores ou um identificador inexistente
- **THEN** encontra a placa equivalente ou recebe resultado vazio, sem confundir busca com falha do serviço

### Requirement: Projeções mínimas por perfil
O sistema SHALL omitir documento completo e vistoria das listagens e SHALL negar ao manobrista qualquer campo financeiro, incluindo snapshot de tarifa. Caixa e administrador SHALL consultar apenas os campos adicionais necessários ao detalhe autorizado, sem receber entidades internas ou dados irrestritos.

#### Scenario: Manobrista consulta detalhe e resposta bruta
- **WHEN** um manobrista consulta diretamente a API de detalhe
- **THEN** recebe localização e informações operacionais permitidas, sem documento completo, tarifa ou outros dados financeiros

#### Scenario: Caixa consulta atendimento
- **WHEN** um caixa consulta o detalhe de um atendimento
- **THEN** recebe os dados operacionais e snapshot de tarifa permitidos, mas o documento completo depende de leitura sensível explícita e auditada

### Requirement: Leituras sensíveis explícitas
O sistema SHALL permitir leitura do documento completo somente a caixa/administrador e de vistoria aos perfis operacionais autorizados, por ação explícita com auditoria. Se a auditoria falhar, o conteúdo sensível SHALL NOT ser retornado.

#### Scenario: Documento necessário à operação
- **WHEN** caixa ou administrador solicita explicitamente o documento de um atendimento
- **THEN** o sistema registra ator, atendimento e horário da leitura e só então retorna o documento sem cache

#### Scenario: Leitura proibida ou auditoria indisponível
- **WHEN** um manobrista solicita documento completo ou uma leitura sensível não pode ser auditada
- **THEN** o conteúdo não é exposto
