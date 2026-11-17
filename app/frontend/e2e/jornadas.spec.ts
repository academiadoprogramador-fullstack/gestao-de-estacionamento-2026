import { expect, test, type Page } from '@playwright/test';

const credenciaisDisponiveis =
  Boolean(process.env['E2E_ADMIN_EMAIL']) && Boolean(process.env['E2E_ADMIN_SENHA']);

test.skip(
  !credenciaisDisponiveis,
  'Defina E2E_* (conexão, chaves e contas sintéticas) para executar a jornada E2E.',
);

const admin = {
  email: process.env['E2E_ADMIN_EMAIL'] ?? '',
  senha: process.env['E2E_ADMIN_SENHA'] ?? '',
};

const caixa = {
  email: process.env['E2E_CAIXA_EMAIL'] ?? '',
  senha: process.env['E2E_CAIXA_SENHA'] ?? '',
};

async function entrar(pagina: Page, email: string, senha: string): Promise<string> {
  await pagina.goto('/login');
  await pagina.getByLabel('E-mail').fill(email);
  await pagina.getByLabel('Senha', { exact: true }).fill(senha);

  const respostaLogin = pagina.waitForResponse(
    (resposta) =>
      resposta.url().endsWith('/api/v1/session/login') && resposta.request().method() === 'POST',
  );

  await pagina.getByRole('button', { name: 'Entrar' }).click();
  const resposta = await respostaLogin;
  expect(resposta.status()).toBe(200);

  await expect(pagina).toHaveURL(/\/dashboard$/);
  return (await resposta.json()).tokenAcesso as string;
}

async function conferirAusenciaDePersistencia(pagina: Page): Promise<void> {
  const armazenamento = await pagina.evaluate(async () => ({
    localStorage: { ...localStorage },
    sessionStorage: { ...sessionStorage },
    indexedDb: (await indexedDB.databases()).map((banco) => banco.name),
  }));

  expect(armazenamento.localStorage).toEqual({});
  expect(armazenamento.sessionStorage).toEqual({});
  expect(armazenamento.indexedDb).toEqual([]);

  const cookies = await pagina.context().cookies();
  expect(cookies).toEqual([]);
}

test('login entre origens distintas mantém a sessão somente em memória', async ({ page }) => {
  const token = await entrar(page, admin.email, admin.senha);

  await expect(page.getByRole('heading', { level: 1 })).toContainText('Administrador');
  await expect(
    page.getByRole('list', { name: 'Perfis do funcionário' }).getByText('Administrador'),
  ).toBeVisible();
  expect(token.split('.')).toHaveLength(3);

  await conferirAusenciaDePersistencia(page);
});

test('perfil sem permissão recebe 403 da API e mantém a sessão', async ({ page, request }) => {
  const token = await entrar(page, caixa.email, caixa.senha);
  const api = process.env['E2E_API_URL'] ?? 'https://localhost:4300';

  const negado = await request.get(`${api}/api/v1/administracao/verificacao`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
  });
  expect(negado.status()).toBe(403);

  const sessao = await request.get(`${api}/api/v1/session`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
  });
  expect(sessao.status()).toBe(200);

  await page.getByRole('link', { name: 'Área de trabalho' }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('button', { name: 'Sair' })).toBeVisible();
});

test('administrador navega entre rotas autenticadas sem recarregar a página', async ({ page }) => {
  await entrar(page, admin.email, admin.senha);

  await page.getByRole('link', { name: 'Verificação administrativa' }).click();

  await expect(page).toHaveURL(/\/administracao$/);
  await expect(page.getByText('Acesso confirmado')).toBeVisible();
});

test('logout revoga a sessão no servidor e limpa a memória', async ({ page, request }) => {
  const token = await entrar(page, caixa.email, caixa.senha);

  await page.getByRole('button', { name: 'Sair' }).click();
  await expect(page).toHaveURL(/\/login$/);
  await conferirAusenciaDePersistencia(page);

  const resposta = await request.get(
    `${process.env['E2E_API_URL'] ?? 'https://localhost:4300'}/api/v1/session`,
    {
      headers: { Authorization: `Bearer ${token}` },
      ignoreHTTPSErrors: true,
    },
  );

  expect(resposta.status()).toBe(401);
});

test('revogação no servidor conduz à reautenticação ao usar a SPA', async ({ page, request }) => {
  const token = await entrar(page, admin.email, admin.senha);

  const revogacao = await request.post(
    `${process.env['E2E_API_URL'] ?? 'https://localhost:4300'}/api/v1/session/logout`,
    {
      headers: { Authorization: `Bearer ${token}` },
      ignoreHTTPSErrors: true,
    },
  );
  expect(revogacao.status()).toBe(204);

  await page.goto('/administracao');

  await expect(page).toHaveURL(/\/login$/);
  await conferirAusenciaDePersistencia(page);
});

test('recarregar a página exige novo login', async ({ page }) => {
  await entrar(page, caixa.email, caixa.senha);

  await page.reload();

  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible();
});

test('login inválido exibe resumo focado e não chama a API', async ({ page }) => {
  await page.goto('/login');

  let chamadas = 0;
  page.on('request', (requisicao) => {
    if (requisicao.url().includes('/api/v1/session/login')) {
      chamadas++;
    }
  });

  await page.getByRole('button', { name: 'Entrar' }).click();

  const resumo = page.getByRole('alert').filter({ hasText: 'Corrija os campos destacados' });
  await expect(resumo).toBeVisible();
  await expect(resumo).toBeFocused();
  await expect(page.locator('#email')).toHaveAttribute('aria-invalid', 'true');
  expect(chamadas).toBe(0);
});

test('login pode ser concluído somente por teclado', async ({ page }) => {
  await page.goto('/login');
  await page.locator('body').click({ position: { x: 1, y: 1 } });

  for (let tentativa = 0; tentativa < 12; tentativa++) {
    const focado = await page.evaluate(() => document.activeElement?.id ?? '');
    if (focado === 'email') {
      break;
    }
    await page.keyboard.press('Tab');
  }

  await expect(page.locator('#email')).toBeFocused();
  await page.keyboard.type(admin.email);

  await page.keyboard.press('Tab');
  await expect(page.locator('#senha')).toBeFocused();
  await page.keyboard.type(admin.senha);

  // Alterna a visibilidade da senha e alcança o botão de envio.
  await page.keyboard.press('Tab');
  await page.keyboard.press('Tab');
  await page.keyboard.press('Enter');

  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('button', { name: 'Sair' })).toBeVisible();
});
