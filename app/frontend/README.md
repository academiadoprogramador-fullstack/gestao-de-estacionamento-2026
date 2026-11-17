# SPA — Gestão de Estacionamento

Aplicação Angular 22 do shell autenticado da Fase 1. A documentação de ambiente, User Secrets, API e testes está no [README principal](../../README.md).

## Comandos

```bash
npm ci
npm start -- --ssl      # https://localhost:4200
npm run build
npm test -- --run
npx playwright test
```

## Organização

- `src/app/core`: configuração pública, sessão em memória, interceptor, guards e erros.
- `src/app/features/auth`: login e acesso negado.
- `src/app/features/dashboard`: área de trabalho autenticada.
- `src/app/features/administracao`: verificação de perfil administrativo.
- `e2e`: jornadas Playwright entre duas origens HTTPS locais.

A URL da API vem de `src/environments/environment.ts` e é configuração pública. Nunca inclua tokens, chaves AES/JWT, credenciais SQL ou segredos nesta pasta.
