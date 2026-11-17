# autenticacao-sessoes Specification

## Purpose

Define como funcionários se autenticam, como a sessão é emitida, renovada e revogada, como os perfis autorizam operações no servidor e como o acesso entre origens distintas é restrito, sem persistir tokens no navegador e sem confiar apenas na interface.

## Requirements

### Requirement: Autorização por perfil no servidor

O sistema SHALL aplicar exatamente os perfis Administrador, Caixa e Manobrista em cada operação protegida; o Manobrista SHALL NOT acessar configuração, relatórios financeiros ou dados financeiros, e a ausência de permissão SHALL ser negada pela API independentemente da interface.

#### Scenario: Operação permitida ao perfil

- **WHEN** um funcionário autenticado executa uma operação autorizada ao seu perfil
- **THEN** a API processa a operação e registra o ator e o horário do servidor

#### Scenario: Operação negada ao perfil

- **WHEN** um Manobrista requisita uma operação administrativa ou financeira diretamente à API
- **THEN** a API responde com acesso negado e não executa nem expõe dados da operação

#### Scenario: Interface não substitui autorização

- **WHEN** um cliente contorna a interface e chama um endpoint protegido sem o perfil exigido
- **THEN** a API nega a operação mesmo que a requisição pareça legítima na origem

### Requirement: Login de funcionário com senha mínima

O sistema SHALL autenticar funcionários provisionados por e-mail e senha de no mínimo 8 caracteres, emitindo JWT de acesso e refresh somente após credenciais válidas, sem revelar se a conta existe ou está bloqueada.

#### Scenario: Credenciais válidas

- **WHEN** um funcionário ativo informa e-mail e senha corretos
- **THEN** a resposta contém JWT, refresh e dados mínimos da sessão, sem senha, hash ou enumeração de contas

#### Scenario: Credenciais inválidas ou conta bloqueada

- **WHEN** a senha está incorreta ou a conta está bloqueada
- **THEN** a resposta é genérica, sem distinguir os casos, e nenhum token válido é emitido

#### Scenario: Resposta de sessão não cacheável

- **WHEN** login, refresh, consulta de sessão ou logout respondem
- **THEN** a resposta impede armazenamento em cache e não inclui token em log, URL ou corpo de erro

### Requirement: JWT HS256 com chaves versionadas

A API SHALL emitir e validar JWT assinado exclusivamente com HS256, usando chave aleatória de 32 bytes por versão, validade de 15 minutos, tolerância de 30 segundos, emissor e audiência fixados e claims mínimos sem dados pessoais ou financeiros.

#### Scenario: Token válido

- **WHEN** um JWT com assinatura, emissor, audiência, versão e validade corretos é apresentado
- **THEN** a requisição é autenticada e a sessão correspondente é verificada no servidor

#### Scenario: Algoritmo ou assinatura inválidos

- **WHEN** o token usa `none`, outro algoritmo, assinatura inválida, emissor/audiência incorretos ou `kid` ausente/desconhecido
- **THEN** a API rejeita o token sem consultar chave fornecida pelo cliente ou URL externa

#### Scenario: Token expirado

- **WHEN** o JWT excede a validade de 15 minutos acrescida da tolerância de 30 segundos
- **THEN** a API o rejeita e a SPA conduz à reautenticação

#### Scenario: Chave JWT não é exposta

- **WHEN** o token ou a configuração pública do frontend é inspecionada
- **THEN** a chave de assinatura não está presente, pois o material secreto permanece somente no servidor

### Requirement: Sessão revogável verificada em cada requisição

A API SHALL associar cada JWT a uma sessão persistida no SQL e SHALL verificar conta, sessão e versão de acesso em toda requisição protegida, recusando a próxima requisição após logout, desativação, troca de senha ou alteração de perfis.

#### Scenario: Logout revoga a sessão

- **WHEN** o funcionário efetua logout
- **THEN** a sessão é revogada no SQL e uma nova requisição com o JWT anterior é recusada

#### Scenario: Alteração de acesso invalida a sessão

- **WHEN** o administrador desativa a conta, troca a senha ou altera os perfis do funcionário
- **THEN** as sessões desse funcionário são invalidadas atomicamente e a próxima requisição protegida exige novo login

#### Scenario: SQL indisponível falha fechado

- **WHEN** a verificação da sessão não pode consultar o SQL
- **THEN** a API nega a requisição em vez de aceitar apenas a assinatura do JWT

### Requirement: Refresh rotativo com limite absoluto de oito horas

A sessão SHALL ter duração absoluta de 8 horas a partir do login, sem extensão deslizante; o refresh SHALL ser opaco, armazenado apenas como hash no SQL, rotacionado atomicamente e reutilizado somente para renovar a mesma família.

#### Scenario: Renovação antes do limite absoluto

- **WHEN** um refresh válido é apresentado dentro das 8 horas
- **THEN** a API consome o refresh, emite outro e devolve novo JWT, sem estender o fim absoluto da sessão

#### Scenario: Limite absoluto atingido

- **WHEN** a sessão completa 8 horas desde o login
- **THEN** novas renovações são recusadas e é exigido novo login

#### Scenario: Reutilização de refresh consumido

- **WHEN** um refresh já consumido é apresentado novamente
- **THEN** a família de sessão é revogada e as renovações seguintes são recusadas

#### Scenario: Refresh nunca vai em URL ou cookie

- **WHEN** a SPA solicita renovação
- **THEN** o refresh é enviado no corpo JSON sobre HTTPS, não em URL, cookie ou armazenamento persistente do navegador

### Requirement: Limitação de tentativas de login

O sistema SHALL limitar o login a 5 requisições por minuto por IP, responder `429` ao excedente e bloquear a conta por 15 minutos após 5 falhas, sem fila e sem revelar a existência da conta.

#### Scenario: Excesso por IP

- **WHEN** mais de 5 tentativas de login partem do mesmo IP em um minuto
- **THEN** as requisições excedentes recebem `429` com correlação, sem processar credenciais

#### Scenario: Falhas consecutivas na conta

- **WHEN** uma conta acumula 5 falhas de senha
- **THEN** ela fica bloqueada por 15 minutos e continua respondendo de forma genérica

#### Scenario: Login válido não apaga histórico indevido

- **WHEN** um login válido ocorre após tentativas falhas dentro da janela de bloqueio
- **THEN** o bloqueio vigente é respeitado e a resposta não diferencia conta inexistente de conta bloqueada

### Requirement: CORS restrito e autenticação sem cookies

A API SHALL aceitar CORS apenas das origens exatas configuradas para a SPA, com os métodos e cabeçalhos necessários, preflight `OPTIONS` e sem `AllowCredentials`; a autenticação SHALL usar somente `Authorization: Bearer`, nunca cookie, query string ou cabeçalho arbitrário de identidade.

#### Scenario: Origem autorizada

- **WHEN** a SPA configurada envia preflight e requisição autenticada de sua origem exata
- **THEN** o preflight é aceito e a requisição recebe os cabeçalhos de resposta previstos

#### Scenario: Origem não autorizada

- **WHEN** uma origem não configurada ou curinga de preview tenta chamar a API
- **THEN** o navegador não recebe autorização CORS e a API continua exigindo JWT válido

#### Scenario: Tentativa de autenticação por cookie ou URL

- **WHEN** uma requisição apresenta identidade em cookie, query string ou cabeçalho alternativo
- **THEN** a API não autentica a requisição como funcionário

### Requirement: Auditoria de autenticação e sessão

O sistema SHALL registrar eventos de login, logout, bloqueio e revogação de sessão com ator opaco e horário UTC do servidor, sem armazenar senha, token, refresh ou conteúdo sensível.

#### Scenario: Evento registrado

- **WHEN** ocorre login bem-sucedido, logout ou revogação por alteração de acesso
- **THEN** a auditoria registra o evento e o ator sem senha ou token

#### Scenario: Falha de autenticação registrada com segurança

- **WHEN** uma tentativa de login falha ou é limitada
- **THEN** o evento é registrado com correlação e sem indicar se o e-mail existe
