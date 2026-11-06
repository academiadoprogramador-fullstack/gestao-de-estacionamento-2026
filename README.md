# Gestão de Estacionamento

[![.NET](https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![SQL Server](https://img.shields.io/badge/Microsoft_SQL_Server-CC2927?style=for-the-badge&logo=mssqlserver&logoColor=white)](https://learn.microsoft.com/sql/sql-server/tutorials-for-sql-server/)
[![OpenAPI / Swagger](https://img.shields.io/badge/OpenAPI%20%2F%20Swagger-85EA2D?style=for-the-badge&logo=swagger&logoColor=black)](https://swagger.io/)
[![Entity Framework Core](https://img.shields.io/badge/Entity%20Framework%20Core-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/ef/core/)
[![ASP.NET Core Identity](https://img.shields.io/badge/ASP.NET%20Core%20Identity-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/aspnet/core/security/authentication/identity)
[![Node.js](https://img.shields.io/badge/Node.js-339933?style=for-the-badge&logo=nodedotjs&logoColor=white)](https://nodejs.org/)
[![Angular](https://img.shields.io/badge/Angular-DD0031?style=for-the-badge&logo=angular&logoColor=white)](https://angular.dev/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind%20CSS-06B6D4?style=for-the-badge&logo=tailwindcss&logoColor=white)](https://tailwindcss.com/)
[![Lucide Icons](https://img.shields.io/badge/Lucide%20Icons-F56565?style=for-the-badge&logo=lucide&logoColor=white)](https://lucide.dev/)

## Introdução

O serviço de valet tradicional, embora conveniente para o cliente, é propenso a ineficiências: perda de tickets, dificuldade em localizar veículos, falta de transparência na cobrança, gestão manual de vagas e dificuldade em comprovar eventuais danos aos veículos. Isso pode levar à insatisfação do cliente, perda de receita e custos operacionais elevados.

## Verificações locais

```bash
# Frontend: instalar dependências travadas, executar testes e build
cd frontend
npm ci
npm test -- --watch=false
npm run build

# Smoke E2E local da SPA/API (requer SQL Server local e Chromium do Playwright)
cd ../tests/e2e
npm ci
npx playwright install chromium
# Configure TEST_SQLSERVER_CONNECTION, TEST_VALET_PASSWORD, TEST_CASHIER_PASSWORD e TEST_ADMIN_PASSWORD com valores locais não versionados.
npm test

# Backend
cd ../../backend
dotnet restore Estacionamento.slnx --locked-mode
dotnet build Estacionamento.slnx --no-restore
dotnet test Estacionamento.slnx --no-restore
```

O harness inicia a API como processo separado e serve a SPA por HTTPS local, encaminhando `/api/*` a um upstream fixo em loopback. O smoke verifica login/logout, cookie host-only `Secure`/`HttpOnly`, expiração, autorização e ausência de chamadas cross-site. Configure as variáveis conforme [`docs/local-development.md`](docs/local-development.md). Essa topologia de desenvolvimento não comprova isolamento de ingresso, TLS entre sites ou autenticação de rede na futura publicação Azure; esses gates seguem pendentes.
