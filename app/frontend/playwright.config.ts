import { defineConfig, devices } from '@playwright/test';

const apiUrl = process.env['E2E_API_URL'] ?? 'https://localhost:4300';
const spaUrl = process.env['E2E_SPA_URL'] ?? 'https://localhost:4200';

const credenciaisDisponiveis =
  Boolean(process.env['E2E_CONEXAO']) &&
  Boolean(process.env['E2E_JWT_CHAVE']) &&
  Boolean(process.env['E2E_AES_CHAVE']) &&
  Boolean(process.env['E2E_ADMIN_EMAIL']) &&
  Boolean(process.env['E2E_ADMIN_SENHA']) &&
  Boolean(process.env['E2E_CAIXA_EMAIL']) &&
  Boolean(process.env['E2E_CAIXA_SENHA']) &&
  Boolean(process.env['E2E_MANOBRISTA_EMAIL']) &&
  Boolean(process.env['E2E_MANOBRISTA_SENHA']);

/**
 * E2E entre duas origens HTTPS locais distintas. A API roda em Development
 * contra catálogo isolado, com credenciais sintéticas explícitas por variável
 * de ambiente e sem User Secrets pessoais.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env['CI']),
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: spaUrl,
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    ...devices['Desktop Chrome'],
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: credenciaisDisponiveis
    ? [
        {
          name: 'api',
          command:
            'dotnet run --project ../backend/src/Estacionamento.Api --no-launch-profile',
          url: `${apiUrl}/health`,
          timeout: 180_000,
          reuseExistingServer: false,
          ignoreHTTPSErrors: true,
          env: {
            ASPNETCORE_ENVIRONMENT: 'Development',
            ASPNETCORE_URLS: apiUrl,
            ESTACIONAMENTO_DESABILITAR_USER_SECRETS: '1',
            ConnectionStrings__Estacionamento: process.env['E2E_CONEXAO']!,
            Autenticacao__Jwt__Emissor: 'gestao-estacionamento-e2e',
            Autenticacao__Jwt__Audiencia: 'gestao-estacionamento-api-e2e',
            Autenticacao__Jwt__VersaoAtiva: 'v1',
            Autenticacao__LimiteTentativasLoginPorMinuto: '100',
            Autenticacao__Jwt__Chaves__v1: process.env['E2E_JWT_CHAVE']!,
            ProtecaoDados__VersaoAtiva: 'v1',
            ProtecaoDados__Chaves__v1: process.env['E2E_AES_CHAVE']!,
            ContasSinteticas__Administrador__Email: process.env['E2E_ADMIN_EMAIL']!,
            ContasSinteticas__Administrador__Senha: process.env['E2E_ADMIN_SENHA']!,
            ContasSinteticas__Caixa__Email: process.env['E2E_CAIXA_EMAIL']!,
            ContasSinteticas__Caixa__Senha: process.env['E2E_CAIXA_SENHA']!,
            ContasSinteticas__Manobrista__Email: process.env['E2E_MANOBRISTA_EMAIL']!,
            ContasSinteticas__Manobrista__Senha: process.env['E2E_MANOBRISTA_SENHA']!,
            Cors__OrigensPermitidas__0: spaUrl,
            Cors__OrigensPermitidas__1: apiUrl,
          },
        },
        {
          name: 'spa',
          command: 'npm start -- --port 4200 --host localhost --ssl',
          url: spaUrl,
          timeout: 180_000,
          reuseExistingServer: false,
          ignoreHTTPSErrors: true,
        },
      ]
    : undefined,
});
