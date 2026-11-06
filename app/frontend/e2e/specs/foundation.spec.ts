import { expect, test } from '@playwright/test';

test('opens the local SPA through the configured edge entry point', async ({ page }) => {
  const crossOriginRequests: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).origin !== 'https://localhost:4200') {
      crossOriginRequests.push(request.url());
    }
  });

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Operação segura começa aqui.' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Pular para o conteúdo' })).toBeAttached();
  expect(crossOriginRequests).toEqual([]);
});

test('employee can log in and log out through the same-origin API proxy', async ({ page }) => {
  const email = process.env.TEST_VALET_EMAIL ?? 'manobrista@local.example.invalid';
  const password = process.env.TEST_VALET_PASSWORD;
  expect(password, 'Set TEST_VALET_PASSWORD to a provisioned local synthetic account password.').toBeTruthy();

  const crossOriginRequests: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).origin !== 'https://localhost:4200') {
      crossOriginRequests.push(request.url());
    }
  });

  await page.goto('/');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(password!);
  await page.getByRole('button', { name: 'Entrar' }).click();

  await expect(page.getByText('Sessão ativa')).toBeVisible();
  await expect(page.getByText(email)).toBeVisible();
  const sessionCookie = (await page.context().cookies('https://localhost/api'))
    .find((cookie) => cookie.name === 'Estacionamento.Session');
  expect(sessionCookie).toBeDefined();
  expect(sessionCookie?.httpOnly).toBe(true);
  expect(sessionCookie?.secure).toBe(true);
  expect(sessionCookie?.path).toBe('/api');
  expect(sessionCookie?.domain).toBe('localhost');
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);
  expect(await page.evaluate(async () =>
    (await fetch('/api/v1/session/admin/verify', { credentials: 'same-origin' })).status)).toBe(403);

  const logoutResponse = page.waitForResponse((response) =>
    response.url().endsWith('/api/v1/session/logout'));
  await page.getByRole('button', { name: 'Encerrar sessão' }).click();
  const logoutResult = await logoutResponse;
  expect(logoutResult.status(), await logoutResult.text()).toBe(204);
  await expect(page.getByRole('heading', { name: 'Acesso de funcionário' })).toBeVisible();
  expect(crossOriginRequests).toEqual([]);
});

test('browser session expires and protected API access is denied', async ({ page }) => {
  const password = process.env.TEST_VALET_PASSWORD;
  expect(password, 'Set TEST_VALET_PASSWORD to a provisioned local synthetic account password.').toBeTruthy();

  await page.goto('/');
  await page.getByLabel('E-mail').fill(process.env.TEST_VALET_EMAIL ?? 'manobrista@local.example.invalid');
  await page.getByLabel('Senha').fill(password!);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByText('Sessão ativa')).toBeVisible();

  await page.waitForTimeout(11_000);
  const sessionStatus = await page.evaluate(async () =>
    (await fetch('/api/v1/session', { credentials: 'same-origin' })).status);
  expect(sessionStatus).toBe(401);
});
