import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

const credenciaisDisponiveis =
  Boolean(process.env['E2E_ADMIN_EMAIL']) &&
  Boolean(process.env['E2E_ADMIN_SENHA']) &&
  Boolean(process.env['E2E_CAIXA_EMAIL']) &&
  Boolean(process.env['E2E_CAIXA_SENHA']) &&
  Boolean(process.env['E2E_MANOBRISTA_EMAIL']) &&
  Boolean(process.env['E2E_MANOBRISTA_SENHA']);

test.skip(
  !credenciaisDisponiveis,
  'Defina E2E_* (conexão, chaves e contas sintéticas) para executar as jornadas da Fase 4.',
);

const admin = {
  email: process.env['E2E_ADMIN_EMAIL'] ?? '',
  senha: process.env['E2E_ADMIN_SENHA'] ?? '',
};

const caixa = {
  email: process.env['E2E_CAIXA_EMAIL'] ?? '',
  senha: process.env['E2E_CAIXA_SENHA'] ?? '',
};

const manobrista = {
  email: process.env['E2E_MANOBRISTA_EMAIL'] ?? '',
  senha: process.env['E2E_MANOBRISTA_SENHA'] ?? '',
};

const api = process.env['E2E_API_URL'] ?? 'https://localhost:4300';
const sufixo = Date.now().toString(36).toUpperCase();

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

async function navegarSpa(pagina: Page, caminho: string): Promise<void> {
  await pagina.evaluate((destino) => {
    history.pushState({}, '', destino);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, caminho);
  await pagina.waitForURL((url) => `${url.pathname}${url.search}` === caminho);
}

async function loginApi(
  requisicao: APIRequestContext,
  email: string,
  senha: string,
): Promise<string> {
  const resposta = await requisicao.post(`${api}/api/v1/session/login`, {
    ignoreHTTPSErrors: true,
    data: { email, senha },
  });
  expect(resposta.status()).toBe(200);
  return (await resposta.json()).tokenAcesso as string;
}

async function garantirTarifa(requisicao: APIRequestContext, token: string): Promise<void> {
  const corrente = await requisicao.get(`${api}/api/v1/daily-rates/current`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
  });
  expect(corrente.status()).toBe(200);
  const dados = await corrente.json();

  if (dados.valor === 20) {
    return;
  }

  const publicacao = await requisicao.post(`${api}/api/v1/daily-rates`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
    data: { valor: 20, versaoBaseEsperada: dados.versaoBase },
  });
  expect(publicacao.status()).toBe(201);
}

async function criarAtendimento(
  requisicao: APIRequestContext,
  tokenAdmin: string,
  tokenCaixa: string,
  rotulo: string,
): Promise<{ atendimentoId: string; identificador: string }> {
  const codigo = `F4-${rotulo}-${sufixo}`.slice(0, 32);
  const vaga = await requisicao.post(`${api}/api/v1/spaces`, {
    headers: { Authorization: `Bearer ${tokenAdmin}` },
    ignoreHTTPSErrors: true,
    data: { codigo },
  });
  expect(vaga.status()).toBe(201);

  const entrada = await requisicao.post(`${api}/api/v1/attendances`, {
    headers: {
      Authorization: `Bearer ${tokenCaixa}`,
      'Idempotency-Key': crypto.randomUUID(),
    },
    ignoreHTTPSErrors: true,
    data: {
      placa: `F4${rotulo}`.toUpperCase().slice(0, 7),
      modelo: 'Modelo E2E',
      titular: 'Titular E2E',
      documento: '000.000.000-00',
      vistoria: 'Vistoria de entrada sintética E2E.',
      vagaId: (await vaga.json()).id,
    },
  });
  expect(entrada.status()).toBe(201);
  const dados = await entrada.json();
  return { atendimentoId: dados.atendimentoId, identificador: dados.identificador };
}

async function registrarPedido(
  requisicao: APIRequestContext,
  token: string,
  atendimentoId: string,
): Promise<{ pedidoId: string }> {
  const resposta = await requisicao.post(`${api}/api/v1/attendances/${atendimentoId}/withdrawal`, {
    headers: {
      Authorization: `Bearer ${token}`,
      'Idempotency-Key': crypto.randomUUID(),
    },
    ignoreHTTPSErrors: true,
    data: { titularPresente: true, conferencia: 'aprovada' },
  });
  expect(resposta.status()).toBe(201);
  return resposta.json();
}

async function pagarIntegral(
  requisicao: APIRequestContext,
  token: string,
  pedidoId: string,
): Promise<void> {
  const caixaResposta = await requisicao.get(`${api}/api/v1/withdrawals/${pedidoId}/cashier`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
  });
  expect(caixaResposta.status()).toBe(200);
  const dados = await caixaResposta.json();

  const pagamento = await requisicao.post(`${api}/api/v1/withdrawals/${pedidoId}/payments`, {
    headers: {
      Authorization: `Bearer ${token}`,
      'Idempotency-Key': crypto.randomUUID(),
    },
    ignoreHTTPSErrors: true,
    data: { valor: dados.saldo, forma: 'pix', revisaoEsperada: dados.revisaoRazao },
  });
  expect(pagamento.status()).toBe(201);
}

test.describe.serial('Fase 4 — vistoria de saída e entrega', () => {
  test('caixa registra vistoria, confirma entrega e vê a vaga histórica na fila', async ({
    page,
    request,
  }) => {
    const tokenAdmin = await loginApi(request, admin.email, admin.senha);
    const tokenCaixa = await loginApi(request, caixa.email, caixa.senha);
    await garantirTarifa(request, tokenAdmin);

    const atendimento = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'ENT');
    const pedido = await registrarPedido(request, tokenCaixa, atendimento.atendimentoId);
    await pagarIntegral(request, tokenCaixa, pedido.pedidoId);

    await entrar(page, caixa.email, caixa.senha);
    await navegarSpa(page, `/delivery/${pedido.pedidoId}`);
    await expect(page.getByRole('heading', { name: 'Entrega do veículo' })).toBeVisible();
    await expect(page.getByText('Vistoria ainda não registrada')).toBeVisible();
    await expect(page.getByText('Entrega bloqueada')).toBeVisible();

    await page.locator('#observacoes-saida').fill('Vistoria de saída sintética E2E.');
    const vistoria = page.waitForResponse(
      (resposta) =>
        resposta.url().endsWith(`/attendances/${atendimento.atendimentoId}/exit-inspection`) &&
        resposta.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Registrar vistoria' }).click();
    expect((await vistoria).status()).toBe(201);

    await expect(page.getByText('Vistoria registrada').first()).toBeVisible();
    await expect(page.getByText('Pronto para confirmação')).toBeVisible();
    await page.getByRole('button', { name: 'Consultar texto da vistoria' }).click();
    await expect(page.getByText('Vistoria de saída sintética E2E.')).toBeVisible();

    await page.getByRole('button', { name: 'Revisar e confirmar entrega' }).click();
    const dialog = page.getByRole('dialog', { name: 'Confirmar entrega' });
    await expect(dialog).toBeVisible();

    const entrega = page.waitForResponse(
      (resposta) =>
        resposta.url().endsWith(`/withdrawals/${pedido.pedidoId}/delivery`) &&
        resposta.request().method() === 'POST',
    );
    await dialog.getByRole('button', { name: 'Confirmar entrega' }).click();
    expect((await entrega).status()).toBe(201);

    await expect(page.getByRole('heading', { name: 'Entrega registrada' })).toBeVisible();
    await expect(page.getByText(/Vaga .* liberada em/)).toBeVisible();

    await navegarSpa(page, '/withdrawals');
    await page.locator('#filtro-estado').selectOption('entregue');
    await expect(page.getByRole('heading', { name: 'Fila de retiradas' })).toBeVisible();
    await expect(page.getByText(atendimento.identificador)).toBeVisible();
    await expect(page.getByRole('table').getByText('Entregue', { exact: true })).toBeVisible();
    await expect(page.getByText(/vaga .* liberada às/)).toBeVisible();

    const fila = await request.get(`${api}/api/v1/withdrawals?state=entregue&page=1&pageSize=100`, {
      headers: { Authorization: `Bearer ${tokenCaixa}` },
      ignoreHTTPSErrors: true,
    });
    expect(fila.status()).toBe(200);
    const itens = (await fila.json()).items as {
      atendimentoId: string;
      estado: string;
      saldo: number;
      entrega: { entregaId: string; vagaCodigo: string };
    }[];
    const entregue = itens.find((item) => item.atendimentoId === atendimento.atendimentoId);
    expect(entregue?.estado).toBe('entregue');
    expect(entregue?.saldo).toBe(0);
    expect(entregue?.entrega.vagaCodigo).toContain('F4-ENT');

    const armazenamento = await page.evaluate(() => ({
      local: localStorage.length,
      sessao: sessionStorage.length,
    }));
    expect(armazenamento.local).toBe(0);
    expect(armazenamento.sessao).toBe(0);
  });

  test('manobrista registra a vistoria sem prontidão financeira nem confirmação', async ({
    page,
    request,
  }) => {
    const tokenAdmin = await loginApi(request, admin.email, admin.senha);
    const tokenCaixa = await loginApi(request, caixa.email, caixa.senha);
    await garantirTarifa(request, tokenAdmin);

    const atendimento = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'MAN');
    const pedido = await registrarPedido(request, tokenCaixa, atendimento.atendimentoId);

    const tokenManobrista = await entrar(page, manobrista.email, manobrista.senha);
    await navegarSpa(page, `/delivery/${pedido.pedidoId}`);
    await expect(page.getByRole('heading', { name: 'Entrega do veículo' })).toBeVisible();
    await expect(page.getByText('Vistoria ainda não registrada')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Revisar e confirmar entrega' })).toHaveCount(0);
    await expect(page.getByText('Prontidão para entrega')).toHaveCount(0);

    await page.locator('#observacoes-saida').fill('Vistoria do manobrista E2E.');
    await page.getByRole('button', { name: 'Registrar vistoria' }).click();
    await expect(page.getByText('Vistoria registrada').first()).toBeVisible();

    const prontidaoNegada = await request.get(
      `${api}/api/v1/withdrawals/${pedido.pedidoId}/delivery-readiness`,
      {
        headers: { Authorization: `Bearer ${tokenManobrista}` },
        ignoreHTTPSErrors: true,
      },
    );
    expect(prontidaoNegada.status()).toBe(403);
  });
});
