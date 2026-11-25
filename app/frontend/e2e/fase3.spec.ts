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
  'Defina E2E_* (conexão, chaves e contas sintéticas) para executar as jornadas da Fase 3.',
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

async function concluirConfirmacao(pagina: Page): Promise<void> {
  const botao = pagina.getByRole('button', { name: 'Concluir', exact: true });
  await expect(botao).toBeVisible();
  await botao.click();
}

async function irParaCaixa(pagina: Page, atendimentoId: string): Promise<void> {
  await navegarSpa(pagina, '/withdrawals');
  await expect(pagina.getByRole('heading', { name: 'Fila de retiradas' })).toBeVisible();
  await navegarSpa(pagina, `/cashier/${atendimentoId}`);
  await expect(pagina.getByRole('heading', { name: 'Caixa do atendimento' })).toBeVisible();
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
  const codigo = `F3-${rotulo}-${sufixo}`.slice(0, 32);
  const vaga = await requisicao.post(`${api}/api/v1/spaces`, {
    headers: { Authorization: `Bearer ${tokenAdmin}` },
    ignoreHTTPSErrors: true,
    data: { codigo },
  });
  expect(vaga.status()).toBe(201);
  const vagaId = (await vaga.json()).id as string;

  const entrada = await requisicao.post(`${api}/api/v1/attendances`, {
    headers: {
      Authorization: `Bearer ${tokenCaixa}`,
      'Idempotency-Key': crypto.randomUUID(),
    },
    ignoreHTTPSErrors: true,
    data: {
      placa: `F3${rotulo}`.toUpperCase().slice(0, 7),
      modelo: 'Modelo E2E',
      titular: 'Titular E2E',
      documento: '000.000.000-00',
      vistoria: 'Vistoria sintética E2E.',
      vagaId,
    },
  });

  expect(entrada.status()).toBe(201);
  const dados = await entrada.json();
  return { atendimentoId: dados.atendimentoId, identificador: dados.identificador };
}

async function registrarPedidoApi(
  requisicao: APIRequestContext,
  token: string,
  atendimentoId: string,
  conferencia = 'aprovada',
): Promise<{ pedidoId: string }> {
  const resposta = await requisicao.post(
    `${api}/api/v1/attendances/${atendimentoId}/withdrawal`,
    {
      headers: {
        Authorization: `Bearer ${token}`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      ignoreHTTPSErrors: true,
      data: { titularPresente: true, conferencia },
    },
  );

  expect(resposta.status()).toBe(201);
  return resposta.json();
}

async function caixaDaApi(
  requisicao: APIRequestContext,
  token: string,
  pedidoId: string,
): Promise<Record<string, unknown>> {
  const resposta = await requisicao.get(`${api}/api/v1/withdrawals/${pedidoId}/cashier`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
  });
  expect(resposta.status()).toBe(200);
  return resposta.json();
}

async function pagarApi(
  requisicao: APIRequestContext,
  token: string,
  pedidoId: string,
  valor: number,
  revisao: number,
): Promise<void> {
  const resposta = await requisicao.post(`${api}/api/v1/withdrawals/${pedidoId}/payments`, {
    headers: {
      Authorization: `Bearer ${token}`,
      'Idempotency-Key': crypto.randomUUID(),
    },
    ignoreHTTPSErrors: true,
    data: { valor, forma: 'pix', revisaoEsperada: revisao },
  });
  expect(resposta.status()).toBe(201);
}

test.describe.serial('Fase 3 — retirada e caixa', () => {
  test('pedido aprovado, divergente, segundo pedido e titular ausente', async ({
    page,
    request,
  }) => {
    const tokenAdmin = await loginApi(request, admin.email, admin.senha);
    const tokenCaixa = await loginApi(request, caixa.email, caixa.senha);
    await garantirTarifa(request, tokenAdmin);

    const aprovado = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'APR');
    const divergente = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'DIV');
    const ausente = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'AUS');

    await entrar(page, caixa.email, caixa.senha);
    await navegarSpa(page, '/withdrawals/new');
    await expect(page.getByRole('heading', { name: 'Registrar pedido de retirada' })).toBeVisible();
    await expect(page.getByText(aprovado.identificador)).toBeVisible();

    await page
      .getByRole('listitem')
      .filter({ hasText: aprovado.identificador })
      .getByRole('button', { name: 'Selecionar' })
      .click();
    await expect(page.getByText(/Cobrança fixada no pedido/)).toBeVisible();
    await page.getByLabel(/Declaro que o titular está presente/).check();
    await page.getByLabel(/Aprovada — documento confere/).check();

    const respostaPedido = page.waitForResponse(
      (resposta) =>
        resposta.url().endsWith(`/attendances/${aprovado.atendimentoId}/withdrawal`) &&
        resposta.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Registrar pedido' }).click();
    expect((await respostaPedido).status()).toBe(201);

    await expect(page.getByRole('heading', { name: 'Pedido registrado' })).toBeVisible();
    await expect(page.getByText('Conferência aprovada')).toBeVisible();
    await expect(page.getByText('Aguardando busca')).toBeVisible();

    await navegarSpa(page, `/withdrawals/new?attendanceId=${aprovado.atendimentoId}`);
    await expect(page.getByText(/já possui pedido/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Registrar pedido' })).toHaveCount(0);

    await navegarSpa(page, '/withdrawals');
    await expect(page.getByRole('heading', { name: 'Fila de retiradas' })).toBeVisible();
    await navegarSpa(page, `/withdrawals/new?attendanceId=${divergente.atendimentoId}`);
    await expect(page.getByRole('heading', { name: 'Registrar pedido de retirada' })).toBeVisible();
    await page.getByLabel(/Declaro que o titular está presente/).check();
    await page.getByLabel(/Divergente — documento não confere/).check();
    await page.getByRole('button', { name: 'Registrar pedido' }).click();
    await expect(page.getByText(/Entrega normal bloqueada/)).toBeVisible();

    await navegarSpa(page, '/withdrawals');
    await expect(page.getByRole('heading', { name: 'Fila de retiradas' })).toBeVisible();
    await navegarSpa(page, `/withdrawals/new?attendanceId=${ausente.atendimentoId}`);
    await page.getByLabel(/Aprovada — documento confere/).check();
    await page.getByRole('button', { name: 'Registrar pedido' }).click();
    await expect(page.getByText(/Declare a presença do titular/)).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Pedido registrado' })).toHaveCount(0);

    await page.getByRole('banner').getByRole('button', { name: 'Sair' }).click();
    await expect(page).toHaveURL(/\/login$/);

    const tokenManobrista = await entrar(page, manobrista.email, manobrista.senha);
    await page.getByRole('link', { name: 'Fila de retiradas' }).click();
    await expect(page.getByRole('heading', { name: 'Fila de retiradas' })).toBeVisible();
    await expect(page.getByText(aprovado.identificador)).toBeVisible();
    await expect(page.getByRole('link', { name: 'Caixa' })).toHaveCount(0);

    await navegarSpa(page, `/withdrawals/new?attendanceId=${ausente.atendimentoId}`);
    await expect(page).toHaveURL(/\/forbidden$/);

    const pedidoNegado = await request.post(
      `${api}/api/v1/attendances/${ausente.atendimentoId}/withdrawal`,
      {
        headers: {
          Authorization: `Bearer ${tokenManobrista}`,
          'Idempotency-Key': crypto.randomUUID(),
        },
        ignoreHTTPSErrors: true,
        data: { titularPresente: true, conferencia: 'aprovada' },
      },
    );
    expect(pedidoNegado.status()).toBe(403);
  });

  test('replay do pedido após perda de resposta confirma um único registro', async ({
    page,
    request,
  }) => {
    const tokenAdmin = await loginApi(request, admin.email, admin.senha);
    const tokenCaixa = await loginApi(request, caixa.email, caixa.senha);
    await garantirTarifa(request, tokenAdmin);

    const atendimento = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'REP');
    await entrar(page, caixa.email, caixa.senha);

    let primeira = true;
    await page.route('**/api/v1/attendances/*/withdrawal', async (rota) => {
      if (primeira) {
        primeira = false;
        await rota.fetch();
        await rota.abort();
        return;
      }

      await rota.continue();
    });

    await navegarSpa(page, `/withdrawals/new?attendanceId=${atendimento.atendimentoId}`);
    await page.getByLabel(/Declaro que o titular está presente/).check();
    await page.getByLabel(/Aprovada — documento confere/).check();
    await page.getByRole('button', { name: 'Registrar pedido' }).click();

    await expect(page.getByRole('heading', { name: 'Resultado incerto' })).toBeVisible();
    await page.getByRole('button', { name: 'Repetir mesmo pedido' }).click();
    await expect(page.getByRole('heading', { name: 'Pedido registrado' })).toBeVisible();

    const fila = await request.get(
      `${api}/api/v1/withdrawals?state=aguardando&page=1&pageSize=100`,
      {
        headers: { Authorization: `Bearer ${tokenCaixa}` },
        ignoreHTTPSErrors: true,
      },
    );
    expect(fila.status()).toBe(200);
    const itens = (await fila.json()).items as { atendimentoId: string }[];
    expect(itens.filter((item) => item.atendimentoId === atendimento.atendimentoId)).toHaveLength(1);

    const armazenamento = await page.evaluate(() => ({
      local: localStorage.length,
      sessao: sessionStorage.length,
    }));
    expect(armazenamento.local).toBe(0);
    expect(armazenamento.sessao).toBe(0);
  });

  test('acerto integral e replay do pagamento sem entrega antecipada', async ({
    page,
    request,
  }) => {
    const tokenAdmin = await loginApi(request, admin.email, admin.senha);
    const tokenCaixa = await loginApi(request, caixa.email, caixa.senha);
    await garantirTarifa(request, tokenAdmin);

    const atendimento = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'PAG');
    const pedido = await registrarPedidoApi(request, tokenCaixa, atendimento.atendimentoId);

    await entrar(page, caixa.email, caixa.senha);
    await irParaCaixa(page, atendimento.atendimentoId);
    await expect(page.getByRole('heading', { name: 'Caixa do atendimento' })).toBeVisible();
    await expect(page.getByText('Conceder desconto')).toHaveCount(0);

    let primeira = true;
    await page.route('**/api/v1/withdrawals/*/payments', async (rota) => {
      if (primeira) {
        primeira = false;
        await rota.fetch();
        await rota.abort();
        return;
      }

      await rota.continue();
    });

    await page.locator('#valor-pagamento').fill('20,00');
    await page.locator('#forma-pagamento').selectOption('pix');
    await page.getByRole('button', { name: 'Registrar pagamento' }).click();

    await expect(page.getByRole('heading', { name: 'Resultado incerto' })).toBeVisible();
    await page.getByRole('button', { name: 'Repetir mesma operação' }).click();
    await expect(page.getByText('Sem saldo')).toBeVisible();
    await expect(page.getByRole('dialog').getByText('Operação confirmada após repetição')).toBeVisible();
    await concluirConfirmacao(page);
    await page.waitForTimeout(300);
    await expect(page.getByText('Pagamento não enviado')).toHaveCount(0);
    await expect(page.locator('#valor-pagamento')).toHaveAttribute('aria-invalid', 'false');

    const caixaFinal = await caixaDaApi(request, tokenCaixa, pedido.pedidoId);
    expect(caixaFinal.saldo).toBe(0);
    expect(caixaFinal.estado).toBe('aguardando');
    expect((caixaFinal.lancamentos as unknown[]).length).toBe(1);

    await expect(page.getByRole('link', { name: 'Abrir operação de entrega' })).toBeVisible();
    await expect(page.getByRole('button', { name: /Entregar|Liberar vaga/ })).toHaveCount(0);
  });

  test('desconto até zero, estorno com novo acerto e autorizações revogadas/obsoletas', async ({
    page,
    request,
  }) => {
    const tokenAdmin = await loginApi(request, admin.email, admin.senha);
    const tokenCaixa = await loginApi(request, caixa.email, caixa.senha);
    await garantirTarifa(request, tokenAdmin);

    const atendimentoDesconto = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'DES');
    const pedidoDesconto = await registrarPedidoApi(
      request,
      tokenCaixa,
      atendimentoDesconto.atendimentoId,
    );

    await entrar(page, admin.email, admin.senha);
    await irParaCaixa(page, atendimentoDesconto.atendimentoId);
    await page.locator('#valor-desconto').fill('10,00');
    await page.locator('#justificativa-desconto').fill('Cortesia administrativa E2E.');
    await page.getByRole('button', { name: 'Conceder desconto' }).click();
    await expect(page.getByRole('dialog').getByText('Desconto registrado')).toBeVisible();
    await concluirConfirmacao(page);
    await page.waitForTimeout(300);
    await expect(page.getByText('Desconto não enviado')).toHaveCount(0);

    await page.locator('#valor-desconto').fill('10,00');
    await page.locator('#justificativa-desconto').fill('Complemento da cortesia E2E.');
    await page.getByRole('button', { name: 'Conceder desconto' }).click();
    await expect(page.getByText('Cobrança zerada por desconto')).toBeVisible();
    await expect(page.getByText('Sem saldo')).toBeVisible();
    await concluirConfirmacao(page);

    const caixaDesconto = await caixaDaApi(request, tokenCaixa, pedidoDesconto.pedidoId);
    expect(caixaDesconto.cobrado).toBe(0);
    expect(caixaDesconto.saldo).toBe(0);

    const atendimentoEstorno = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'EST');
    const pedidoEstorno = await registrarPedidoApi(
      request,
      tokenCaixa,
      atendimentoEstorno.atendimentoId,
    );
    await pagarApi(request, tokenCaixa, pedidoEstorno.pedidoId, 20, 1);

    await irParaCaixa(page, atendimentoEstorno.atendimentoId);
    await page.locator('#pagamento-estorno').selectOption({ index: 0 });
    await page.locator('#justificativa-estorno').fill('Estorno integral E2E.');
    await page.getByRole('button', { name: 'Estornar integralmente' }).click();
    await expect(page.getByRole('dialog').getByText('Estorno integral confirmado')).toBeVisible();
    await expect(page.getByText('R$ 20,00', { exact: true }).first()).toBeVisible();
    await concluirConfirmacao(page);

    await page.locator('#valor-pagamento').fill('20,00');
    await page.locator('#forma-pagamento').selectOption('dinheiro');
    await page.getByRole('button', { name: 'Registrar pagamento' }).click();
    await expect(page.getByRole('dialog').getByText('Pagamento integral confirmado')).toBeVisible();
    await expect(page.getByText('Sem saldo')).toBeVisible();
    await concluirConfirmacao(page);
    await page.waitForTimeout(300);
    await expect(page.getByText('Pagamento não enviado')).toHaveCount(0);
    await expect(page.getByText('Estorno não enviado')).toHaveCount(0);

    const atendimentoAuth = await criarAtendimento(request, tokenAdmin, tokenCaixa, 'AUT');
    const pedidoAuth = await registrarPedidoApi(
      request,
      tokenCaixa,
      atendimentoAuth.atendimentoId,
      'divergente',
    );

    await irParaCaixa(page, atendimentoAuth.atendimentoId);
    await page.getByText('Conceder autorização documental').click();
    await page.locator('#justificativa-documental').fill('Justificativa documental E2E.');
    await page.getByRole('button', { name: 'Conceder', exact: true }).click();
    await expect(page.getByRole('dialog').getByText('Autorização registrada')).toBeVisible();
    await expect(page.getByText('Vigente', { exact: true }).first()).toBeVisible();
    await concluirConfirmacao(page);

    await page.getByText('Revogar autorização vigente').click();
    await page.locator('#revogacao-documental').fill('Revogação E2E.');
    await page.getByRole('button', { name: 'Revogar', exact: true }).click();
    await expect(page.getByRole('dialog').getByText('Autorização revogada')).toBeVisible();
    await expect(page.getByText('Revogada', { exact: true }).first()).toBeVisible();
    await concluirConfirmacao(page);

    await page.getByText('Conceder autorização financeira').click();
    await page.locator('#justificativa-financeira').fill('Justificativa financeira E2E.');
    await page.getByRole('button', { name: 'Conceder', exact: true }).click();
    await expect(page.getByRole('dialog').getByText('Autorização registrada')).toBeVisible();
    await concluirConfirmacao(page);

    await pagarApi(request, tokenCaixa, pedidoAuth.pedidoId, 20, 1);
    await irParaCaixa(page, atendimentoAuth.atendimentoId);
    await expect(page.getByText('Obsoleta', { exact: true }).first()).toBeVisible();

    await expect(page.getByRole('button', { name: /Entregar|Liberar vaga/ })).toHaveCount(0);
  });
});
