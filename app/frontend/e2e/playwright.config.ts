import { defineConfig, devices } from '@playwright/test';
import dotenv from 'dotenv';
import path from 'node:path';
import { randomBytes } from 'node:crypto';

dotenv.config({ path: path.resolve(__dirname, '.env'), quiet: true });

const idempotencyHmacKey =
  process.env.TEST_IDEMPOTENCY_HMAC_KEY ?? randomBytes(32).toString('base64');
const sqlCatalog = process.env.TEST_SQLSERVER_CONNECTION?.match(
  /(?:Initial\s+Catalog|Database)\s*=\s*(?:\{([^}]+)\}|([^;]+))/i,
);
const e2eDatabaseName = (sqlCatalog?.[1] ?? sqlCatalog?.[2] ?? '').trim();
if (!/^EstacionamentoE2E(?:[A-Za-z0-9_-]*)$/i.test(e2eDatabaseName)) {
  throw new Error(
    'Playwright API requires TEST_SQLSERVER_CONNECTION to target a dedicated EstacionamentoE2E* database; reserved and development databases are not allowed.',
  );
}

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
      command:
        'dotnet run --project ../../backend/src/Estacionamento.Api --no-build --no-launch-profile --urls http://127.0.0.1:5188',
      url: 'http://127.0.0.1:5188/healthz',
      reuseExistingServer: false,
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
        Idempotency__Hmac__ActiveKeyVersion: '1',
        Idempotency__Hmac__Keys__1: idempotencyHmacKey,
        Authentication__IdleTimeoutSeconds: '10',
      },
    },
    {
      command: 'npm --prefix .. start',
      url: 'https://localhost:4200',
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
  ],
});
