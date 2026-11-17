# Sessão, autenticação e CORS

Este documento descreve os contratos implementados na Fase 1. A API é a autoridade de autenticação e autorização; a SPA apenas complementa a experiência de navegação.

## Topologia e transporte

- SPA em `https://localhost:4200` e API em `https://localhost:4300` (origens distintas).
- Autenticação exclusivamente por `Authorization: Bearer <JWT>`.
- Sem cookie de autenticação, token em URL, `withCredentials` ou proxy de desenvolvimento.
- CORS aceita somente as origens exatas de `Cors:OrigensPermitidas`, com `OPTIONS`, métodos necessários e cabeçalhos `Authorization`, `Content-Type` e `Idempotency-Key`. Não há `AllowAnyOrigin` nem `AllowCredentials`.
- Respostas de `/api` e `/openapi` usam `Cache-Control: no-store`.

## Endpoints

Prefixo: `/api/v1/session`.

| Contrato | Acesso | Resultado |
| --- | --- | --- |
| `POST /login` | anônimo, limitado por IP | `200` com JWT, refresh, expirações e dados mínimos do funcionário; `401` genérico em credenciais inválidas/bloqueio; `429` ao exceder 5 requisições/min por IP |
| `POST /refresh` | anônimo, refresh somente no corpo JSON | `200` com novo JWT e refresh rotacionado; `401` para refresh inválido, consumido ou sessão encerrada |
| `GET /` | bearer | `200` com sessão atual; `401` quando a sessão foi revogada, expirou ou a versão/conta mudou |
| `POST /logout` | bearer | `204` após revogar sessão e refreshes; a próxima requisição com o JWT anterior recebe `401` |

Corpo de login: `{ "email": "...", "senha": "..." }`. Corpo de refresh: `{ "refresh": "..." }`.

Resposta de login/renovação:

```json
{
  "tokenAcesso": "<JWT>",
  "refresh": "<TOKEN_OPACO>",
  "expiraAcessoEmUtc": "2026-11-17T15:18:12Z",
  "expiraSessaoEmUtc": "2026-11-17T23:03:12Z",
  "funcionario": {
    "id": "<GUID>",
    "nome": "Administrador",
    "email": "<EMAIL_DO_FUNCIONARIO>",
    "perfis": ["Administrador"]
  }
}
```

## JWT

- Algoritmo fixo: **HS256**. `none`, algoritmos alternativos, assinatura inválida, emissor/audiência incorretos e `kid` ausente/desconhecido são rejeitados.
- Chave de **32 bytes aleatórios** por versão, em Base64 na configuração sensível. `kid` corresponde à versão.
- Claims mínimos: `sub` (usuário), `sid` (sessão), `jti`, `iat`, `exp`, perfis (`role`) e `versaoAcesso`. Sem PII, documento, finanças ou chave no token.
- Validade do acesso: **15 minutos**; tolerância de relógio: **30 segundos**.
- Emissor/audiência são fixados por ambiente. A chave nunca é entregue à SPA, publicada em JWKS ou resolvida por URL do cliente.

### Rotação de chave JWT

1. Gere uma nova chave de 32 bytes e acrescente como `Autenticacao:Jwt:Chaves:<nova-versao>`.
2. Preserve as versões anteriores no mapa; elas continuam validando tokens emitidos.
3. Atualize `Autenticacao:Jwt:VersaoAtiva` para a nova versão e reinicie a API.
4. Remova a versão antiga somente após expirar o último JWT correspondente mais a tolerância de 30 s.
5. Comprometimento exige retirada imediata da chave e revogação das sessões afetadas, sem esperar a janela de rotação.

A rotação JWT (90 dias por padrão) é independente das chaves AES (`ProtecaoDados`), que devem permanecer enquanto dados ou backups dependerem delas.

## Sessão e refresh

- Sessão absoluta de **8 horas** a partir do login, sem extensão deslizante. Cada JWT termina no menor prazo entre 15 minutos e o fim da sessão.
- Refresh opaco de alta entropia; o SQL guarda somente SHA-256. O valor puro existe apenas em memória do cliente.
- Renovação consome o refresh atomicamente e emite outro. Reutilização de refresh já consumido revoga a família e a próxima renovação falha.
- Logout revoga a sessão e todos os refreshes antes de responder.
- Toda requisição protegida consulta conta ativa, sessão não revogada e igualdade de `versaoAcesso`. SQL indisponível **falha fechado**, negando a requisição.
- Mudanças administrativas de estado, senha ou perfis devem incrementar `VersaoAcesso` e revogar as sessões na mesma transação (a administração de funcionários pertence à Fase 2).

## Senhas e bloqueio

- Senha mínima de **8 caracteres**, sem exigência de composição além da decidida.
- Bloqueio de **15 minutos** após **5 falhas** consecutivas, aplicado pelo Identity.
- Login limitado a **5 requisições por minuto por IP** (`429` com `Retry-After: 60`), sem fila.
- Respostas de falha são genéricas e não enumeram conta existente, inexistente ou bloqueada.
- O IP só é considerado de proxy confiável quando a cadeia é configurada explicitamente; cabeçalho arbitrário do cliente não é aceito como identidade.

## Erros

Formato `application/problem+json`:

```json
{
  "type": "https://gestao-estacionamento.local/problems/credenciais_invalidas",
  "title": "Credenciais inválidas.",
  "status": 401,
  "instance": "/api/v1/session/login",
  "code": "credenciais_invalidas",
  "correlationId": "8c47be81e4a2491d9626ef979a0cff56"
}
```

| `code` | Situação |
| --- | --- |
| `credenciais_invalidas` | senha incorreta, conta inexistente, bloqueada ou refresh inválido (sem distinção) |
| `nao_autenticado` | ausência, expiração ou revogação do JWT/sessão |
| `acesso_negado` | perfil sem a permissão exigida |
| `limite_login_excedido` | excedeu 5 tentativas por minuto por IP |
| `requisicao_invalida` | corpo/parâmetros inválidos |
| `erro_interno` | falha não tratada, sem stack trace ou dados sensíveis |

O `correlationId` é gerado pelo servidor, não aceito do cliente e devolvido no cabeçalho `X-Correlation-Id`. Logs não contêm senha, token, refresh, e-mail completo ou PII.

## Bootstrap

Somente em `Development`, antes de aceitar tráfego:

1. validar configuração, mapas de chaves e as três contas sintéticas (presença, formato, senha mínima e unicidade após normalização), sem tocar no banco;
2. verificar conexão e catálogo de aplicação;
3. executar `Database.MigrateAsync()` para todas as migrations pendentes;
4. imediatamente após o sucesso, provisionar apenas contas ausentes e garantir os perfis fixos;
5. iniciar o pipeline HTTP.

Contas existentes não têm e-mail, senha, perfis ou estado alterados — inclusive conta desabilitada permanece desabilitada. Falha em qualquer etapa impede a API de aceitar requisições. `Testing` prepara o banco por fixture; `Production` usa procedimento controlado, sem migration ou seed automáticos no startup.
