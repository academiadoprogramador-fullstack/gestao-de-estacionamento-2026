import { expect, test, type Browser, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

const signIn = async (page: Page, email: string, password: string) => {
  await page.goto('/');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(password);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByText('Sessão autenticada por cookie seguro')).toBeVisible();
};

const watchSecurityBoundaries = (
  page: Page,
  crossOriginRequests: string[],
  missingCsrf: string[],
) => {
  page.on('request', (request) => {
    const requestUrl = new URL(request.url());
    if (requestUrl.origin !== 'https://localhost:4200') crossOriginRequests.push(request.url());
    if (
      requestUrl.pathname.startsWith('/api/') &&
      ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) &&
      !request.headers()['x-csrf-token']
    ) {
      missingCsrf.push(`${request.method()} ${requestUrl.pathname}`);
    }
  });
};

test('Fase 2: admin prepara a operação, cashier registra/consulta e valet vê somente sua projeção', async ({
  browser,
}) => {
  test.setTimeout(120_000);
  const adminPassword = process.env.TEST_ADMIN_PASSWORD;
  const cashierPassword = process.env.TEST_CASHIER_PASSWORD;
  const valetPassword = process.env.TEST_VALET_PASSWORD;
  expect(
    adminPassword,
    'Configure TEST_ADMIN_PASSWORD for the isolated E2E database.',
  ).toBeTruthy();
  expect(
    cashierPassword,
    'Configure TEST_CASHIER_PASSWORD for the isolated E2E database.',
  ).toBeTruthy();
  expect(
    valetPassword,
    'Configure TEST_VALET_PASSWORD for the isolated E2E database.',
  ).toBeTruthy();

  const adminEmail = process.env.TEST_ADMIN_EMAIL ?? 'admin@local.example.invalid';
  const cashierEmail = `phase2-${randomUUID()}@example.test`;
  const cashierInitialPassword = 'Cashier!Phase2-2026';
  const spaceCode = `E2E-${randomUUID().slice(0, 8).toUpperCase()}`;
  const vehiclePlate = `E2E${randomUUID().replaceAll('-', '').slice(0, 7).toUpperCase()}`;
  const vehicleDocument = `private-${randomUUID()}`;
  const vehicleInspection = `inspection-${randomUUID()}`;

  const adminContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const admin = await adminContext.newPage();
  const crossOriginRequests: string[] = [];
  const missingCsrf: string[] = [];
  watchSecurityBoundaries(admin, crossOriginRequests, missingCsrf);
  await signIn(admin, adminEmail, adminPassword!);
  const adminKeepAlive = await admin.evaluate(() =>
    window.setInterval(() => void fetch('/api/v1/session', { credentials: 'same-origin' }), 4_000),
  );

  await admin.goto('/employees');
  await admin.getByLabel('E-mail').fill(cashierEmail);
  await admin.getByLabel('Senha inicial').fill(cashierInitialPassword);
  await admin.getByRole('button', { name: 'Cadastrar funcionário' }).click();
  await expect(admin.getByRole('status')).toContainText('Funcionário cadastrado');
  await expect(admin.getByRole('heading', { name: cashierEmail })).toBeVisible();

  await admin.goto('/spaces');
  await admin.getByLabel('Código', { exact: true }).first().fill(spaceCode);
  await admin.getByRole('button', { name: 'Cadastrar vaga' }).click();
  await expect(admin.getByRole('status')).toContainText('Vaga cadastrada');
  await expect(admin.getByText(spaceCode, { exact: true })).toBeVisible();

  await admin.goto('/daily-rates');
  await admin.getByLabel('Novo valor diário').fill('1.01');
  await admin.getByRole('button', { name: 'Publicar tarifa' }).click();
  await expect(admin.getByRole('status')).toContainText('Nova versão publicada');

  const cashierContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const cashier = await cashierContext.newPage();
  watchSecurityBoundaries(cashier, crossOriginRequests, missingCsrf);
  await signIn(cashier, cashierEmail, cashierInitialPassword);
  const keepAlive = await cashier.evaluate(() =>
    window.setInterval(() => void fetch('/api/v1/session', { credentials: 'same-origin' }), 4_000),
  );
  let staleSpacePage: {
    items: Array<{ id: string; code: string; isOccupied: boolean; version: string }>;
    page: number;
    pageSize: number;
    total: number;
  } | null = null;
  cashier.on('response', async (response) => {
    if (
      response.url().includes('/api/v1/spaces?') &&
      response.url().includes('status=free') &&
      response.status() === 200
    ) {
      try {
        const body = await response.json();
        if (body.items?.some((space: { code: string }) => space.code === spaceCode)) {
          staleSpacePage = body;
        }
      } catch {
        // Ignore unrelated or aborted availability refreshes.
      }
    }
  });
  await cashier.goto('/check-in');
  await expect(cashier.getByLabel('Vaga livre')).toContainText(spaceCode);
  await cashier.getByLabel('Placa').fill(vehiclePlate);
  await cashier.getByLabel('Modelo').fill('E2E vehicle');
  await cashier.getByLabel('Nome do titular').fill('E2E owner');
  await cashier.getByLabel('Documento do titular').fill(vehicleDocument);
  await cashier.getByLabel('Vistoria de entrada').fill(vehicleInspection);
  await cashier.getByLabel('Vaga livre').selectOption({ label: spaceCode });

  let firstAttempt = true;
  let replayPending = true;
  let idempotencyHeader = '';
  await cashier.route('**/api/v1/attendances', async (route) => {
    if (route.request().method() !== 'POST') return route.continue();
    const key = route.request().headers()['idempotency-key'];
    if (firstAttempt) {
      firstAttempt = false;
      idempotencyHeader = key ?? '';
      await route.fetch();
      return route.abort('failed');
    }
    if (replayPending) {
      replayPending = false;
      expect(key).toBe(idempotencyHeader);
    }
    await route.continue();
  });
  await cashier.getByRole('button', { name: 'Confirmar entrada' }).click();
  await expect(
    cashier.getByRole('heading', { name: 'Resultado ainda não confirmado' }),
  ).toBeVisible();
  await cashier.getByRole('button', { name: 'Tentar novamente com a mesma solicitação' }).click();
  await expect(cashier.getByRole('heading', { name: 'Entrada confirmada' })).toBeVisible();
  await expect(cashier.getByText(vehicleDocument)).toHaveCount(0);
  await expect(cashier.getByText(vehicleInspection)).toHaveCount(0);
  const publicIdentifier = await cashier
    .locator('#check-in-result-title')
    .locator('..')
    .locator('dd')
    .first()
    .innerText();
  expect(publicIdentifier).toMatch(/^[0-9a-f]{32}$/i);

  await cashier.getByRole('link', { name: 'Consultar este atendimento' }).click();
  await expect(cashier.getByRole('heading', { name: 'Atendimentos' })).toBeVisible();
  await cashier.getByRole('button', { name: 'Abrir detalhe' }).click();
  await expect(
    cashier.getByRole('heading', { name: `Detalhe · ${publicIdentifier}` }),
  ).toBeVisible();
  await expect(cashier.getByText('Tarifa diária registrada')).toBeVisible();
  await cashier.getByRole('button', { name: 'Ler documento do titular' }).click();
  await expect(cashier.getByText(vehicleDocument, { exact: true })).toBeVisible();
  await cashier.getByRole('button', { name: 'Fechar detalhe' }).click();

  await expect.poll(() => staleSpacePage).not.toBeNull();
  let servedStaleAvailability = false;
  await cashier.route('**/api/v1/spaces?*', async (route) => {
    if (
      !servedStaleAvailability &&
      route.request().method() === 'GET' &&
      new URL(route.request().url()).searchParams.get('status') === 'free'
    ) {
      servedStaleAvailability = true;
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(staleSpacePage),
      });
    }
    return route.continue();
  });
  await cashier.goto('/check-in');
  await cashier.getByLabel('Placa').fill(`${vehiclePlate}X`);
  await cashier.getByLabel('Modelo').fill('Second E2E vehicle');
  await cashier.getByLabel('Nome do titular').fill('Second owner');
  await cashier.getByLabel('Documento do titular').fill(`second-${vehicleDocument}`);
  await cashier.getByLabel('Vistoria de entrada').fill('Second inspection');
  await cashier.getByLabel('Vaga livre').selectOption({ label: spaceCode });
  await cashier.getByRole('button', { name: 'Confirmar entrada' }).click();
  await expect(cashier.getByRole('alert')).toContainText('vaga acabou de ser ocupada');

  await admin.goto('/daily-rates');
  await admin.getByLabel('Novo valor diário').fill('2.02');
  await admin.getByRole('button', { name: 'Publicar tarifa' }).click();
  await expect(admin.getByRole('status')).toContainText('Nova versão publicada');
  await cashier.goto(`/attendances?identifier=${encodeURIComponent(publicIdentifier)}`);
  await cashier.getByRole('button', { name: 'Abrir detalhe' }).click();
  await expect(
    cashier.getByRole('heading', { name: `Detalhe · ${publicIdentifier}` }),
  ).toBeVisible();
  await expect(cashier.locator('dl')).toContainText('1.01');

  await admin.goto('/employees');
  const cashierArticle = admin.locator('article').filter({ hasText: cashierEmail });
  await cashierArticle.getByRole('button', { name: 'Desativar' }).click();
  await expect(admin.getByRole('status')).toContainText('Acesso atualizado');
  await cashier.goto('/spaces');
  await expect(cashier.getByRole('heading', { name: 'Acesso de funcionário' })).toBeVisible();
  await cashier.evaluate((timerId) => window.clearInterval(timerId), keepAlive);

  await cashierContext.close();

  const valetContext = await browser.newContext({ ignoreHTTPSErrors: true });
  const valet = await valetContext.newPage();
  watchSecurityBoundaries(valet, crossOriginRequests, missingCsrf);
  await signIn(
    valet,
    process.env.TEST_VALET_EMAIL ?? 'manobrista@local.example.invalid',
    valetPassword!,
  );
  await valet.goto(`/attendances?identifier=${encodeURIComponent(publicIdentifier!)}`);
  const openDetails = valet.getByRole('button', { name: 'Abrir detalhe' });
  await openDetails.focus();
  await valet.keyboard.press('Enter');
  await expect(valet.getByRole('heading', { name: `Detalhe · ${publicIdentifier}` })).toBeVisible();
  await expect(valet.getByText('Tarifa diária registrada')).toHaveCount(0);
  await expect(valet.getByRole('button', { name: 'Ler documento do titular' })).toHaveCount(0);
  await expect(valet.getByRole('button', { name: 'Ler vistoria de entrada' })).toBeVisible();
  await valet.keyboard.press('Tab');
  await expect(valet.locator(':focus')).toBeVisible();
  expect(crossOriginRequests).toEqual([]);
  expect(missingCsrf).toEqual([]);

  await valetContext.close();
  await admin.evaluate((timerId) => window.clearInterval(timerId), adminKeepAlive);
  await adminContext.close();
});
