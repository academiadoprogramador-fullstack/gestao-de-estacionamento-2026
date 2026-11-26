import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

const credenciaisDisponiveis =
  Boolean(process.env['E2E_ADMIN_EMAIL']) &&
  Boolean(process.env['E2E_ADMIN_SENHA']) &&
  Boolean(process.env['E2E_CAIXA_EMAIL']) &&
  Boolean(process.env['E2E_CAIXA_SENHA']) &&
  Boolean(process.env['E2E_MANOBRISTA_EMAIL']) &&
  Boolean(process.env['E2E_MANOBRISTA_SENHA']);

test.skip(
  !credenciaisDisponiveis || process.env['E2E_FOTOS'] !== '1',
  'Defina E2E_* e E2E_FOTOS=1 com um bucket R2 de teste para executar as fotos da Fase 5.',
);

const api = process.env['E2E_API_URL'] ?? 'https://localhost:4300';
const sufixo = Date.now().toString(36).toUpperCase();
const pngSintetico = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAACAAAAAYCAIAAAAUMWhjAAAAJElEQVR4nGOQM3KjKWIYtWDUglELRi0YtWDUglELRi0YGhYAACD6whAorfVmAAAAAElFTkSuQmCC',
  'base64',
);

async function entrar(pagina: Page, email: string, senha: string): Promise<void> {
  await pagina.goto('/login');
  await pagina.getByLabel('E-mail').fill(email);
  await pagina.getByLabel('Senha', { exact: true }).fill(senha);
  const login = pagina.waitForResponse(
    (resposta) =>
      resposta.url().endsWith('/api/v1/session/login') && resposta.request().method() === 'POST',
  );
  await pagina.getByRole('button', { name: 'Entrar' }).click();
  expect((await login).status()).toBe(200);
  await expect(pagina).toHaveURL(/\/dashboard$/);
}

async function token(
  requisicao: APIRequestContext,
  email: string,
  senha: string,
): Promise<string> {
  const resposta = await requisicao.post(`${api}/api/v1/session/login`, {
    data: { email, senha },
    ignoreHTTPSErrors: true,
  });
  expect(resposta.status()).toBe(200);
  return (await resposta.json()).tokenAcesso as string;
}

async function criarVaga(
  requisicao: APIRequestContext,
  bearer: string,
  codigo: string,
): Promise<string> {
  const resposta = await requisicao.post(`${api}/api/v1/spaces`, {
    headers: { Authorization: `Bearer ${bearer}` },
    data: { codigo },
    ignoreHTTPSErrors: true,
  });
  expect(resposta.status()).toBe(201);
  return (await resposta.json()).id as string;
}

async function publicarTarifa(requisicao: APIRequestContext, bearer: string): Promise<void> {
  const corrente = await requisicao.get(`${api}/api/v1/daily-rates/current`, {
    headers: { Authorization: `Bearer ${bearer}` },
    ignoreHTTPSErrors: true,
  });
  expect(corrente.status()).toBe(200);
  const dados = await corrente.json();
  const publicacao = await requisicao.post(`${api}/api/v1/daily-rates`, {
    headers: { Authorization: `Bearer ${bearer}` },
    data: { valor: 20, versaoBaseEsperada: dados.versaoBase },
    ignoreHTTPSErrors: true,
  });
  expect(publicacao.status()).toBe(201);
}

async function criarAtendimento(
  requisicao: APIRequestContext,
  bearer: string,
  vagaId: string,
): Promise<{ id: string; identificador: string }> {
  const resposta = await requisicao.post(`${api}/api/v1/attendances`, {
    headers: {
      Authorization: `Bearer ${bearer}`,
      'Idempotency-Key': crypto.randomUUID(),
    },
    data: {
      placa: `F5${sufixo.slice(-4)}`,
      modelo: 'Modelo F5',
      titular: 'Titular F5',
      documento: '111.111.111-11',
      vistoria: 'Vistoria textual sintética de entrada.',
      vagaId,
    },
    ignoreHTTPSErrors: true,
  });
  expect(resposta.status()).toBe(201);
  const dados = await resposta.json();
  return { id: dados.atendimentoId as string, identificador: dados.identificador as string };
}

async function criarPedido(
  requisicao: APIRequestContext,
  bearer: string,
  atendimentoId: string,
): Promise<string> {
  const resposta = await requisicao.post(
    `${api}/api/v1/attendances/${atendimentoId}/withdrawal`,
    {
      headers: {
        Authorization: `Bearer ${bearer}`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      data: { titularPresente: true, conferencia: 'aprovada' },
      ignoreHTTPSErrors: true,
    },
  );
  expect(resposta.status()).toBe(201);
  return (await resposta.json()).pedidoId as string;
}

async function registrarVistoria(
  requisicao: APIRequestContext,
  bearer: string,
  atendimentoId: string,
): Promise<void> {
  const detalhe = await requisicao.get(`${api}/api/v1/attendances/${atendimentoId}`, {
    headers: { Authorization: `Bearer ${bearer}` },
    ignoreHTTPSErrors: true,
  });
  expect(detalhe.status()).toBe(200);
  const versao = (await detalhe.json()).versaoAtendimento as string;
  const resposta = await requisicao.post(
    `${api}/api/v1/attendances/${atendimentoId}/exit-inspection`,
    {
      headers: {
        Authorization: `Bearer ${bearer}`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      data: {
        texto: 'Condições sintéticas de saída para a foto.',
        versaoAtendimentoEsperada: versao,
      },
      ignoreHTTPSErrors: true,
    },
  );
  expect(resposta.status()).toBe(201);
}

test.describe.serial('Fase 5 — fotos das vistorias', () => {
  test('manobrista anexa e lê foto de saída sem receber chave do bucket', async ({
    page,
    request,
  }) => {
    const tokenAdministrador = await token(
      request,
      process.env['E2E_ADMIN_EMAIL']!,
      process.env['E2E_ADMIN_SENHA']!,
    );
    const tokenCaixa = await token(
      request,
      process.env['E2E_CAIXA_EMAIL']!,
      process.env['E2E_CAIXA_SENHA']!,
    );
    const tokenManobrista = await token(
      request,
      process.env['E2E_MANOBRISTA_EMAIL']!,
      process.env['E2E_MANOBRISTA_SENHA']!,
    );

    const vagaId = await criarVaga(request, tokenAdministrador, `F5-${sufixo}`);
    await publicarTarifa(request, tokenAdministrador);
    const atendimento = await criarAtendimento(request, tokenCaixa, vagaId);
    await criarPedido(request, tokenCaixa, atendimento.id);
    await registrarVistoria(request, tokenManobrista, atendimento.id);

    await entrar(
      page,
      process.env['E2E_MANOBRISTA_EMAIL']!,
      process.env['E2E_MANOBRISTA_SENHA']!,
    );
    await page.getByRole('link', { name: 'Fila de retiradas' }).click();
    await expect(page.getByText(atendimento.identificador)).toBeVisible();
    await page
      .getByRole('link', {
        name: `Abrir operação de entrega do atendimento ${atendimento.identificador}`,
      })
      .click();
    await expect(page.getByRole('heading', { name: 'Entrega do veículo' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Fotos da vistoria de saída' })).toBeVisible();

    const entrada = page.locator('#foto-saida');
    await expect(entrada).toBeVisible();
    await entrada.setInputFiles({
      name: 'foto-sintetica.png',
      mimeType: 'image/png',
      buffer: pngSintetico,
    });

    const upload = page.waitForResponse(
      (resposta) =>
        resposta.url().endsWith('/inspections/saida/photos') &&
        resposta.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Enviar foto' }).click();
    const respostaUpload = await upload;
    expect(respostaUpload.status()).toBe(201);

    await expect(page.getByText('1 de 5 fotos')).toBeVisible();
    await expect(page.getByText('Foto 1')).toBeVisible();

    const conteudo = page.waitForResponse(
      (resposta) =>
        resposta.url().endsWith('/content') && resposta.request().method() === 'GET',
    );
    await page.getByRole('button', { name: 'Ler foto' }).click();
    const respostaConteudo = await conteudo;
    expect(respostaConteudo.status()).toBe(200);
    expect(respostaConteudo.headers()['content-type']).toContain('image/webp');
    await expect(page.getByRole('dialog')).toBeVisible();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: 'Fechar' })
      .click();
  });

  test('falha do R2 mantém a vistoria textual disponível na entrega', async ({
    page,
    request,
  }) => {
    const tokenAdministrador = await token(
      request,
      process.env['E2E_ADMIN_EMAIL']!,
      process.env['E2E_ADMIN_SENHA']!,
    );
    const tokenCaixa = await token(
      request,
      process.env['E2E_CAIXA_EMAIL']!,
      process.env['E2E_CAIXA_SENHA']!,
    );
    const tokenManobrista = await token(
      request,
      process.env['E2E_MANOBRISTA_EMAIL']!,
      process.env['E2E_MANOBRISTA_SENHA']!,
    );

    const vagaId = await criarVaga(request, tokenAdministrador, `F5D-${sufixo}`);
    await publicarTarifa(request, tokenAdministrador);
    const atendimento = await criarAtendimento(request, tokenCaixa, vagaId);
    await criarPedido(request, tokenCaixa, atendimento.id);
    await registrarVistoria(request, tokenManobrista, atendimento.id);

    await entrar(
      page,
      process.env['E2E_MANOBRISTA_EMAIL']!,
      process.env['E2E_MANOBRISTA_SENHA']!,
    );
    await page.getByRole('link', { name: 'Fila de retiradas' }).click();
    await expect(page.getByText(atendimento.identificador)).toBeVisible();
    await page
      .getByRole('link', {
        name: `Abrir operação de entrega do atendimento ${atendimento.identificador}`,
      })
      .click();

    await page.route('**/inspections/saida/photos', async (rota) => {
      if (rota.request().method() === 'POST') {
        await rota.fulfill({
          status: 503,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            status: 503,
            title: 'Armazenamento indisponível',
            code: 'photo_storage_unavailable',
          }),
        });
        return;
      }

      await rota.continue();
    });

    const entrada = page.locator('#foto-saida');
    await expect(entrada).toBeVisible();
    await entrada.setInputFiles({
      name: 'foto-sintetica.png',
      mimeType: 'image/png',
      buffer: pngSintetico,
    });
    await page.getByRole('button', { name: 'Enviar foto' }).click();

    await expect(
      page.getByText(/armazenamento privado de fotos está indisponível/i),
    ).toBeVisible();
    await expect(page.getByText(/Vistoria registrada/i)).toBeVisible();
    await expect(page.getByText('1 de 5 fotos')).toHaveCount(0);
  });
});
