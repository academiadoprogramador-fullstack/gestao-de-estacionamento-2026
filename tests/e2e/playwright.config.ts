import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: 'https://localhost:4200',
    trace: 'retain-on-failure',
    ignoreHTTPSErrors: true,
    ...devices['Desktop Chrome'],
  },
  webServer: [
    {
      command: 'dotnet run --project ../../backend/src/Estacionamento.Api --no-build --no-launch-profile --urls http://127.0.0.1:5188',
      url: 'http://127.0.0.1:5188/healthz',
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: 'http://127.0.0.1:5188',
        ConnectionStrings__Identity: process.env.TEST_SQLSERVER_CONNECTION ?? '',
        SyntheticAccounts__ValetPassword: process.env.TEST_VALET_PASSWORD ?? '',
        SyntheticAccounts__CashierPassword: process.env.TEST_CASHIER_PASSWORD ?? '',
        SyntheticAccounts__AdministratorPassword: process.env.TEST_ADMIN_PASSWORD ?? '',
        Security__SpaOrigin: 'https://localhost:4200',
        DataProtection__KeyRingPath: `${process.cwd()}/.local/key-ring`,
        Authentication__IdleTimeoutSeconds: '10',
      },
    },
    {
      command: 'npm --prefix ../../frontend start',
      url: 'https://localhost:4200',
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
  ],
});
