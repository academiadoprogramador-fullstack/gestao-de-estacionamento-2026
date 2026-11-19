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
  'Defina E2E_* (conexão, chaves e contas sintéticas) para executar as jornadas da Fase 2.',
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

async function criarVaga(
  requisicao: APIRequestContext,
  token: string,
  codigo: string,
): Promise<{ id: string; codigo: string }> {
  const resposta = await requisicao.post(`${api}/api/v1/spaces`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
    data: { codigo },
  });

  expect(resposta.status()).toBe(201);
  return resposta.json();
}

async function ajustarTarifa(
  requisicao: APIRequestContext,
  token: string,
  valor: number,
): Promise<void> {
  const corrente = await requisicao.get(`${api}/api/v1/daily-rates/current`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
  });
  expect(corrente.status()).toBe(200);
  const dados = await corrente.json();

  const publicacao = await requisicao.post(`${api}/api/v1/daily-rates`, {
    headers: { Authorization: `Bearer ${token}` },
    ignoreHTTPSErrors: true,
    data: { valor, versaoBaseEsperada: dados.versaoBase },
  });
  expect(publicacao.status()).toBe(201);
}

async function registrarEntrada(
  requisicao: APIRequestContext,
  token: string,
  vagaId: string,
  placa: string,
  documento: string,
  vistoria: string,
): Promise<Record<string, unknown>> {
  const resposta = await requisicao.post(`${api}/api/v1/attendances`, {
    headers: {
      Authorization: `Bearer ${token}`,
      'Idempotency-Key': crypto.randomUUID(),
    },
    ignoreHTTPSErrors: true,
    data: {
      placa,
      modelo: 'Modelo E2E',
      titular: 'Titular E2E',
      documento,
      vistoria,
      vagaId,
    },
  });

  expect(resposta.status()).toBe(201);
  return resposta.json();
}

test.describe.serial('Fase 2 — preparação, entrada e consulta', () => {
  test('administrador cadastra vaga e tarifa; caixa consulta e é bloqueado na configuração', async ({
    page,
    request,
  }) => {
    await entrar(page, admin.email, admin.senha);

    const codigo = `E2E-${sufixo}`;
    await page.getByRole('link', { name: 'Vagas' }).click();
    await page.locator('#codigo-vaga').fill(codigo);
    await page.getByRole('button', { name: 'Cadastrar', exact: true }).click();
    await expect(page.getByText(`Vaga ${codigo} cadastrada.`)).toBeVisible();
    await page.locator('#filtro-codigo').fill(codigo);
    await page.getByRole('button', { name: 'Aplicar' }).click();
    await expect(page.getByRole('cell', { name: codigo })).toBeVisible();

    await page.getByRole('link', { name: 'Tarifas' }).click();
    await page.locator('#valor-diaria').fill('20,00');
    await page.getByRole('button', { name: 'Publicar', exact: true }).click();
    await expect(page.getByText(/Versão \d+ publicada/)).toBeVisible();

    await page.getByRole('banner').getByRole('button', { name: 'Sair' }).click();
    await expect(page).toHaveURL(/\/login$/);

    const tokenCaixa = await entrar(page, caixa.email, caixa.senha);
    await page.getByRole('link', { name: 'Vagas' }).click();
    await page.locator('#filtro-codigo').fill(codigo);
    await page.getByRole('button', { name: 'Aplicar' }).click();
    await expect(page.getByRole('cell', { name: codigo })).toBeVisible();
    await expect(page.getByText('O cadastro de vagas é exclusivo do Administrador.')).toBeVisible();

    // A navegação administrativa não é oferecida e a API nega o perfil.
    await expect(page.getByRole('link', { name: 'Tarifas' })).toHaveCount(0);
    const negado = await request.get(`${api}/api/v1/daily-rates/current`, {
      headers: { Authorization: `Bearer ${tokenCaixa}` },
      ignoreHTTPSErrors: true,
    });
    expect(negado.status()).toBe(403);
  });

  test('funcionário criado e alterado tem a sessão revogada na requisição seguinte', async ({
    page,
    browser,
  }) => {
    const emailFuncionario = `caixa.${sufixo.toLowerCase()}@example.test`;
    const senha = 'SenhaTeste1';

    await entrar(page, admin.email, admin.senha);
    await page.getByRole('link', { name: 'Funcionários' }).click();

    await page.getByLabel('Nome', { exact: true }).fill('Funcionário E2E');
    await page.getByLabel('E-mail', { exact: true }).fill(emailFuncionario);
    await page.getByLabel('Senha inicial').fill(senha);
    await page.getByLabel('Caixa', { exact: true }).check();
    await page.getByRole('button', { name: 'Criar funcionário' }).click();
    await expect(page.getByText(`Funcionário Funcionário E2E criado.`)).toBeVisible();

    const contextoFuncionario = await browser.newContext({ ignoreHTTPSErrors: true });
    const paginaFuncionario = await contextoFuncionario.newPage();
    await entrar(paginaFuncionario, emailFuncionario, senha);

    const linha = page.locator('tr', { hasText: emailFuncionario });
    await expect(linha).toBeVisible();
    await linha.getByRole('button', { name: 'Alterar acesso' }).click();
    const dialogo = page.getByRole('dialog');
    await dialogo.getByLabel('Caixa', { exact: true }).uncheck();
    await dialogo.getByLabel('Manobrista', { exact: true }).check();
    await dialogo.getByRole('button', { name: 'Salvar acesso' }).click();
    await expect(page.getByText(`Acesso de Funcionário E2E atualizado.`)).toBeVisible();

    // A sessão anterior é recusada na próxima requisição protegida, inclusive
    // com o refresh já revogado.
    await paginaFuncionario.getByRole('link', { name: 'Atendimentos' }).click();
    await paginaFuncionario.locator('#termo-busca').fill('REV1A23');
    await paginaFuncionario.getByRole('button', { name: 'Buscar', exact: true }).click();
    await expect(paginaFuncionario).toHaveURL(/\/login$/);

    await contextoFuncionario.close();
  });

  test('check-in com replay da mesma chave, conflito de vaga e limpeza de campos', async ({
    page,
    request,
  }) => {
    const tokenAdmin = await entrar(page, admin.email, admin.senha);
    const vaga = await criarVaga(request, tokenAdmin, `E2E-C-${sufixo}`);
    await ajustarTarifa(request, tokenAdmin, 20);

    await page.getByRole('banner').getByRole('button', { name: 'Sair' }).click();
    const tokenCaixa = await entrar(page, caixa.email, caixa.senha);
    await page.getByRole('link', { name: 'Check-in' }).click();

    const chaves: string[] = [];
    page.on('request', (requisicao) => {
      if (requisicao.method() === 'POST' && requisicao.url().endsWith('/api/v1/attendances')) {
        chaves.push(requisicao.headers()['idempotency-key'] ?? '');
      }
    });

    let primeiraTentativa = true;
    await page.route('**/api/v1/attendances', async (rota) => {
      if (rota.request().method() !== 'POST' || !primeiraTentativa) {
        await rota.continue();
        return;
      }

      primeiraTentativa = false;
      // Envia ao servidor e descarta a resposta do browser: o resultado fica
      // incerto e a repetição deve usar a mesma chave e conteúdo.
      await rota.fetch();
      await rota.abort('failed');
    });

    const placa = `E2E${sufixo.slice(-4)}`;
    await page.locator('#placa').fill(placa);
    await page.locator('#modelo-veiculo').fill('Modelo E2E');
    await page.locator('#titular').fill('Titular E2E');
    await page.locator('#documento').fill('111.222.333-44');
    await page.locator('#vistoria').fill('Vistoria E2E da entrada.');
    await page.locator('#vaga').selectOption(vaga.id);

    await page.getByRole('button', { name: 'Registrar entrada' }).click();
    await expect(page.getByRole('heading', { name: 'Resultado incerto' })).toBeVisible();

    await page.getByRole('button', { name: 'Repetir tentativa' }).click();
    await expect(page.getByRole('heading', { name: 'Entrada registrada' })).toBeVisible();
    await expect(page.getByText(/AT-\d{4}-\d{6}/)).toBeVisible();

    expect(chaves.length).toBe(2);
    expect(chaves[0]).toBe(chaves[1]);

    // Documento e vistoria são limpos do estado da tela após o sucesso.
    await page.getByRole('button', { name: 'Registrar outra entrada' }).click();
    await expect(page.locator('#documento')).toHaveValue('');
    await expect(page.locator('#vistoria')).toHaveValue('');

    // Conflito explícito ao tentar a mesma vaga com uma nova chave.
    const conflito = await request.post(`${api}/api/v1/attendances`, {
      headers: {
        Authorization: `Bearer ${tokenCaixa}`,
        'Idempotency-Key': crypto.randomUUID(),
      },
      ignoreHTTPSErrors: true,
      data: {
        placa: `${placa}X`,
        modelo: 'Modelo E2E',
        titular: 'Titular E2E',
        documento: '111.222.333-44',
        vistoria: 'Vistoria E2E da entrada.',
        vagaId: vaga.id,
      },
    });

    expect(conflito.status()).toBe(409);
    expect((await conflito.json()).code).toBe('space_unavailable');
  });

  test('busca, projeções por perfil, leituras auditadas e descarte de resposta tardia', async ({
    page,
    browser,
    request,
  }) => {
    const tokenAdmin = await entrar(page, admin.email, admin.senha);
    const vagaA = await criarVaga(request, tokenAdmin, `E2E-D1-${sufixo}`);
    const vagaB = await criarVaga(request, tokenAdmin, `E2E-D2-${sufixo}`);
    await ajustarTarifa(request, tokenAdmin, 20);

    const placa = `LATE${sufixo.slice(-4)}`;
    const primeiro = await registrarEntrada(
      request,
      tokenAdmin,
      vagaA.id,
      placa,
      '999.888.777-66',
      'Vistoria A da entrada.',
    );
    const segundo = await registrarEntrada(
      request,
      tokenAdmin,
      vagaB.id,
      placa,
      '999.888.777-66',
      'Vistoria B da entrada.',
    );

    const idPrimeiro = primeiro['atendimentoId'] as string;
    const idSegundo = segundo['atendimentoId'] as string;
    const identificadorPrimeiro = primeiro['identificador'] as string;

    await page.getByRole('banner').getByRole('button', { name: 'Sair' }).click();
    await entrar(page, caixa.email, caixa.senha);
    await page.getByRole('link', { name: 'Atendimentos' }).click();
    await expect(page.getByRole('heading', { name: 'Atendimentos em aberto' })).toBeVisible();
    await expect(
      page.getByRole('button', { name: new RegExp(identificadorPrimeiro) }),
    ).toBeVisible();

    await page.locator('#termo-busca').fill(placa);
    await page.getByRole('button', { name: 'Buscar', exact: true }).click();
    await expect(page.getByText('Resultados da busca')).toBeVisible();
    await expect(
      page.getByRole('button', { name: new RegExp(identificadorPrimeiro) }),
    ).toBeVisible();

    // A primeira seleção tem a resposta atrasada; a segunda deve prevalecer.
    await page.route(`**/api/v1/attendances/${idPrimeiro}`, async (rota) => {
      await new Promise((resolver) => setTimeout(resolver, 700));
      await rota.continue();
    });

    await page.getByRole('button', { name: new RegExp(identificadorPrimeiro) }).click();
    const identificadorSegundo = segundo['identificador'] as string;
    await page.getByRole('button', { name: new RegExp(identificadorSegundo) }).click();
    await expect(page.getByText(identificadorSegundo).last()).toBeVisible();
    await page.waitForTimeout(1200);
    await expect(page.getByText(identificadorSegundo).last()).toBeVisible();
    await expect(page.getByText(identificadorPrimeiro)).toHaveCount(1); // apenas na lista

    // Leituras explícitas e auditadas pelo Caixa.
    await page.getByRole('button', { name: 'Ler documento' }).click();
    await expect(page.getByText('999.888.777-66')).toBeVisible();
    await page.getByRole('button', { name: 'Ler vistoria de entrada' }).click();
    await expect(page.getByText(`Vistoria B da entrada.`)).toBeVisible();

    // Manobrista: sem documento nem bloco financeiro no contrato da tela.
    const contextoManobrista = await browser.newContext({ ignoreHTTPSErrors: true });
    const paginaManobrista = await contextoManobrista.newPage();
    await entrar(paginaManobrista, manobrista.email, manobrista.senha);
    await paginaManobrista.getByRole('link', { name: 'Atendimentos' }).click();
    await paginaManobrista.getByRole('radio', { name: 'Identificador' }).check();
    await paginaManobrista.locator('#termo-busca').fill(identificadorSegundo);
    await paginaManobrista.getByRole('button', { name: 'Buscar', exact: true }).click();
    await paginaManobrista.getByRole('button', { name: new RegExp(identificadorSegundo) }).click();
    await expect(
      paginaManobrista.getByRole('button', { name: 'Ler vistoria de entrada' }),
    ).toBeVisible();
    await expect(paginaManobrista.getByRole('button', { name: 'Ler documento' })).toHaveCount(0);
    await expect(paginaManobrista.getByText('Tarifa capturada na entrada')).toHaveCount(0);

    await contextoManobrista.close();
  });

  test('teclado, rótulos, aria-invalid e foco do diálogo nas novas telas', async ({ page }) => {
    await entrar(page, admin.email, admin.senha);

    await page.getByRole('link', { name: 'Vagas' }).click();
    const campoCodigo = page.locator('#codigo-vaga');
    await expect(page.getByLabel('Código', { exact: true }).last()).toBeVisible();
    await campoCodigo.focus();
    await page.keyboard.press('Enter');

    const resumo = page.getByRole('alert').filter({ hasText: 'Corrija os campos destacados' });
    await expect(resumo).toBeVisible();
    await expect(resumo).toBeFocused();
    await expect(campoCodigo).toHaveAttribute('aria-invalid', 'true');
    await expect(campoCodigo).toHaveAttribute('aria-describedby', 'erro-codigo-vaga');

    await page.getByRole('link', { name: 'Funcionários' }).click();
    const linha = page.locator('tr', { hasText: `caixa.${sufixo.toLowerCase()}@example.test` });
    const disparador = linha.getByRole('button', { name: 'Alterar acesso' });
    await disparador.focus();
    await page.keyboard.press('Enter');

    await expect(page.getByRole('heading', { name: /Alterar acesso de/ })).toBeFocused();

    await page.getByRole('button', { name: 'Cancelar' }).click();
    await expect(disparador).toBeFocused();
  });
});
