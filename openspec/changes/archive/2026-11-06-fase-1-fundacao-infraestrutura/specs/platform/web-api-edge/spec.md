# Spec Delta

## Purpose

Garantir que o navegador use somente a origem da aplicação web para acessar a API e preservar a sessão, com uma topologia local verificável que prepare o isolamento futuro do ingresso.

## ADDED Requirements

### Requirement: API pela origem da aplicação
O sistema SHALL encaminhar todas as chamadas de API do navegador pelo caminho `/api` da origem da SPA, incluindo emissão de token, login, consulta de sessão e logout, enquanto SPA/edge e API executam como processos separados no ambiente local.

#### Scenario: Jornada pela mesma origem
- **WHEN** um funcionário abre a SPA via edge local e inicia/encerra sessão
- **THEN** todas as requisições do navegador para a API usam o domínio da SPA e retornam respostas correspondentes

### Requirement: Preservação do contrato de sessão
O edge SHALL preservar o caminho `/api`, cookies de requisição, `Set-Cookie` e cabeçalhos de antiforgery na comunicação com a API, sem depender de cookies cross-site ou permitir cache de respostas privadas.

#### Scenario: Cookie permanece de primeira parte
- **WHEN** o funcionário autentica e em seguida consulta a sessão pelo edge
- **THEN** o navegador armazena cookie host-only da origem da SPA e a sessão é reconhecida pela API

#### Scenario: Resposta privada atravessa o edge
- **WHEN** uma resposta de sessão cruza o edge
- **THEN** seu conteúdo e seus cabeçalhos de proteção são preservados sem cache compartilhado

### Requirement: Sem confiança em identidade enviada pelo cliente
A API SHALL rejeitar identidade afirmada apenas por cabeçalhos fornecidos pelo cliente; a configuração local SHALL impedir que o navegador dependa de acesso direto à API para a jornada de sessão.

#### Scenario: Cabeçalho falsificado
- **WHEN** um cliente envia cabeçalhos de identidade falsificados diretamente à API local sem sessão válida
- **THEN** a operação protegida é negada, sem elevar os privilégios do cliente

### Requirement: Transporte confiável
O ambiente de teste local SHALL usar origem segura no navegador para testar cookies Secure e SHALL limitar o proxy ao destino da API configurado, sem redirecionar requisições autenticadas para destinos arbitrários. Proteção do transporte entre sites publicados permanece gate da implantação futura.

#### Scenario: Sessão local em origem segura
- **WHEN** um cliente utiliza a aplicação pelo edge local com origem segura
- **THEN** login e consulta de sessão usam `/api` na origem da SPA sem chamadas cross-site no navegador
