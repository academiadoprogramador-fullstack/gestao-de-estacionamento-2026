# shell-acesso-frontend Specification

## Purpose

Define o comportamento do shell autenticado da SPA: navegação protegida por sessão e perfil, uso de tokens somente em memória, chamadas à API em origem distinta, tratamento de expiração e revogação e feedback acessível durante a autenticação.

## Requirements

### Requirement: Shell autenticado com rotas protegidas

A SPA SHALL oferecer rotas de login, área de trabalho autenticada e acesso negado; o guard de sessão SHALL resolver o estado antes da navegação e o guard de perfil SHALL encaminhar à área de acesso negado sem substituir a autorização da API.

#### Scenario: Acesso sem sessão

- **WHEN** um visitante tenta abrir uma rota protegida sem sessão válida
- **THEN** a SPA o encaminha ao login antes de exibir conteúdo protegido

#### Scenario: Acesso com perfil insuficiente

- **WHEN** um funcionário autenticado abre uma rota não permitida ao seu perfil
- **THEN** a SPA o encaminha à área de acesso negado e a API continua negando a operação

#### Scenario: Sessão resolvida antes da navegação

- **WHEN** a SPA recarrega uma rota protegida com sessão em memória ainda válida
- **THEN** o estado da sessão é resolvido antes da renderização do conteúdo protegido

### Requirement: Sessão somente em memória

A SPA SHALL manter JWT, refresh e dados de sessão exclusivamente em memória, sem `localStorage`, `sessionStorage`, IndexedDB, cookie ou compartilhamento entre abas; recarregar a página SHALL exigir novo login.

#### Scenario: Recarga exige login

- **WHEN** o navegador recarrega a aplicação
- **THEN** a sessão anterior não é restaurada e o funcionário precisa autenticar novamente

#### Scenario: Nenhuma persistência de token

- **WHEN** o armazenamento do navegador é inspecionado após login e uso da aplicação
- **THEN** não há JWT, refresh ou credencial persistidos em storage, cookie ou cache da aplicação

### Requirement: Interceptor restrito à API configurada

O interceptor SHALL anexar `Authorization: Bearer` somente às URLs da API explicitamente configuradas, sem `withCredentials` e sem enviar token a destinos arbitrários, e SHALL limpar a sessão da memória após logout.

#### Scenario: Chamada à API autorizada

- **WHEN** a SPA chama a URL da API configurada com sessão ativa
- **THEN** o cabeçalho bearer é anexado e nenhum cookie de credencial é enviado

#### Scenario: Destino fora da API

- **WHEN** a SPA requisita uma URL externa ou não configurada como API
- **THEN** o interceptor não anexa o token nem credenciais

#### Scenario: Logout limpa a memória

- **WHEN** o logout é concluído, com ou sem confirmação remota
- **THEN** a SPA remove JWT, refresh e dados de sessão mantidos em memória

### Requirement: Tratamento de expiração, revogação e acesso negado

A SPA SHALL tratar `401` conduzindo à reautenticação ou renovação serializada, sem repetição infinita nem envio automático de comandos de negócio, e SHALL tratar `403` como acesso negado sem implicar logout nem prova de rollback de comando anterior.

#### Scenario: JWT expirado com refresh válido

- **WHEN** uma requisição recebe `401` e existe refresh válido dentro da sessão absoluta
- **THEN** a SPA renova a sessão uma única vez e repete a requisição original com o novo JWT

#### Scenario: Sessão revogada

- **WHEN** a API recusa a sessão após logout, desativação ou alteração de acesso
- **THEN** a SPA limpa a sessão em memória e exige novo login

#### Scenario: Acesso negado não desloga

- **WHEN** uma requisição autenticada recebe `403`
- **THEN** a SPA informa acesso negado, mantém a sessão e não repete o comando automaticamente

### Requirement: Logout revoga a sessão no servidor

O logout SHALL solicitar a revogação no servidor antes de descartar a sessão local e SHALL informar quando a revogação remota não pôde ser confirmada.

#### Scenario: Logout confirmado

- **WHEN** o funcionário confirma logout com a API disponível
- **THEN** o servidor revoga a sessão e a SPA descarta os tokens da memória

#### Scenario: Falha de rede no logout

- **WHEN** a chamada de logout falha por indisponibilidade de rede ou servidor
- **THEN** a SPA informa que a revogação remota não foi confirmada e não afirma logout concluído no servidor

### Requirement: Feedback acessível de autenticação

A tela de login SHALL usar rótulos associados aos campos, foco visível, mensagens textuais de erro e resumo focado; dados inválidos SHALL NOT gerar chamada à API, e a operação SHALL ser utilizável por teclado.

#### Scenario: Campos inválidos

- **WHEN** o formulário é enviado com e-mail ou senha inválidos segundo a validação local
- **THEN** mensagens textuais identificam os campos, o resumo recebe foco e nenhuma requisição é enviada

#### Scenario: Erro de credenciais

- **WHEN** a API recusa as credenciais
- **THEN** a SPA apresenta mensagem genérica e acessível, sem revelar existência da conta nem apagar o e-mail digitado

#### Scenario: Uso por teclado

- **WHEN** o funcionário navega pelo login usando somente teclado
- **THEN** todos os controles são alcançáveis, o foco é visível e o envio pode ser concluído sem mouse

### Requirement: SPA e API em origens distintas com bearer

A SPA SHALL chamar a API diretamente por HTTPS na URL configurada, em origem distinta da aplicação, usando bearer em memória e sem proxy de desenvolvimento que simule a topologia de publicação.

#### Scenario: Chamada entre origens locais

- **WHEN** a SPA em uma origem HTTPS local chama a API em outra origem HTTPS local com sessão válida
- **THEN** o preflight e a requisição autenticada são concluídos conforme o CORS restrito, sem cookies

#### Scenario: API indisponível

- **WHEN** a SPA não consegue alcançar a API
- **THEN** ela informa indisponibilidade, não inventa sessão ou resposta local e preserva a necessidade de novo login quando aplicável
