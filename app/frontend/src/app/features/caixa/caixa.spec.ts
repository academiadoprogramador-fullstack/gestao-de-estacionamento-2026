import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { SessaoService } from '../../core/sessao/sessao.service';
import { Caixa } from './caixa';
import { aguardarAtualizacao } from '../../testing/aguardar';

const atendimentoId = '22222222-2222-2222-2222-222222222222';
const pedidoId = '11111111-1111-1111-1111-111111111111';

const atendimento = {
  id: atendimentoId,
  identificador: 'AT-2026-000041',
  placa: 'BRA2E19',
  modelo: 'Hatch prata',
  titular: 'Ana Beatriz Ferreira',
  estado: 'aguardando',
  vagaCodigo: 'A-03',
  entradaUtc: new Date().toISOString(),
  tarifaVersao: 1,
  tarifaValorSnapshot: 20,
  retirada: {
    pedidoId,
    estado: 'aguardando',
    solicitadoEmUtc: new Date().toISOString(),
    buscaIniciadaEmUtc: null,
    buscaPor: null,
    diarias: 3,
    totalOriginal: 60,
  },
};

function financeiro(saldo = 60, pagoLiquido = 0) {
  return {
    id: pedidoId,
    atendimentoId,
    identificador: atendimento.identificador,
    placa: atendimento.placa,
    modelo: atendimento.modelo,
    vagaCodigo: atendimento.vagaCodigo,
    estado: 'aguardando',
    entradaUtc: atendimento.entradaUtc,
    solicitadoEmUtc: new Date().toISOString(),
    buscaIniciadaEmUtc: null,
    responsavelBusca: null,
    versaoPedido: 'AAAAAAA=',
    conferencia: 'aprovada',
    duracaoTicks: 2592000000000,
    diarias: 3,
    tarifaVersao: 1,
    tarifaValorSnapshot: 20,
    totalOriginal: 60,
    descontos: 0,
    pagamentos: pagoLiquido,
    estornos: 0,
    cobrado: 60,
    pagoLiquido,
    saldo,
    revisaoRazao: pagoLiquido > 0 ? 2 : 1,
    lancamentos: [],
    autorizacaoDocumental: {
      tipo: 'documental',
      id: null,
      vigencia: 'nao_concedida',
      revisaoReferencia: null,
      concedidaPor: null,
      concedidaEmUtc: null,
      temJustificativa: false,
    },
    autorizacaoFinanceira: {
      tipo: 'financeira',
      id: null,
      vigencia: 'nao_concedida',
      revisaoReferencia: null,
      concedidaPor: null,
      concedidaEmUtc: null,
      temJustificativa: false,
    },
  };
}

const paginaHistorico = { items: [], page: 1, pageSize: 50, total: 0 };

function configurar(perfil: 'Administrador' | 'Caixa' | 'Manobrista') {
  TestBed.configureTestingModule({
    imports: [Caixa],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: { paramMap: convertToParamMap({ attendanceId: atendimentoId }) },
        },
      },
      {
        provide: SessaoService,
        useValue: {
          autenticado: () => true,
          possuiPerfil: (perfis: readonly string[]) => perfis.includes(perfil),
        },
      },
    ],
  });

  return TestBed.inject(HttpTestingController);
}

async function carregar(
  perfil: 'Administrador' | 'Caixa' | 'Manobrista',
  dados: Record<string, unknown> = financeiro(),
) {
  const http = configurar(perfil);
  const fixture = TestBed.createComponent(Caixa);
  fixture.detectChanges();

  if (perfil === 'Manobrista') {
    await aguardarAtualizacao(fixture);
    return { http, fixture, componente: fixture.componentInstance };
  }

  http.expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}`)).flush(atendimento);
  await aguardarAtualizacao(fixture);
  http.expectOne((req) => req.url.endsWith(`/withdrawals/${pedidoId}/cashier`)).flush(dados);
  http
    .expectOne((req) => req.url.includes(`/attendances/${atendimentoId}/history`))
    .flush(paginaHistorico);
  await aguardarAtualizacao(fixture);

  return { http, fixture, componente: fixture.componentInstance };
}

describe('Caixa', () => {
  let http: HttpTestingController;

  beforeEach(() => TestBed.resetTestingModule());

  afterEach(() => http.verify());

  it('registra o acerto integral e recarrega o saldo do servidor', async () => {
    const carregado = await carregar('Caixa');
    http = carregado.http;
    const { fixture, componente } = carregado;

    expect(fixture.nativeElement.textContent).toContain('Acerto integral');
    expect(fixture.nativeElement.textContent).not.toContain('Conceder desconto');

    componente.valorPagamento.set('60,00');
    componente.formaPagamento.set('pix');

    const envio = componente.submeterPagamento(new Event('submit', { cancelable: true }));
    const requisicao = http.expectOne((req) => req.url.endsWith('/payments'));
    expect(requisicao.request.body).toEqual({
      valor: 60,
      forma: 'pix',
      revisaoEsperada: 1,
    });
    requisicao.flush({ saldo: 0, revisaoRazao: 2 });
    await new Promise((resolver) => setTimeout(resolver, 0));
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}`))
      .flush(atendimento);
    await new Promise((resolver) => setTimeout(resolver, 0));
    http
      .expectOne((req) => req.url.endsWith(`/withdrawals/${pedidoId}/cashier`))
      .flush(financeiro(0, 60));
    http
      .expectOne((req) => req.url.includes(`/attendances/${atendimentoId}/history`))
      .flush(paginaHistorico);
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.saldo()).toBe(0);
    expect(componente.aviso()).toContain('Pagamento integral confirmado');
    expect(fixture.nativeElement.textContent).toContain('Sem saldo');

    expect(componente.confirmacaoAberta()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Operação confirmada');
    expect(fixture.nativeElement.textContent).toContain('Pagamento de');
    expect(fixture.nativeElement.textContent).toContain('em Pix');
    componente.fecharConfirmacao();
    expect(componente.confirmacaoAberta()).toBe(false);
  });

  it('não envia pagamento parcial ou excedente e destaca o resumo', async () => {
    const carregado = await carregar('Caixa');
    http = carregado.http;
    const { fixture, componente } = carregado;

    componente.valorPagamento.set('59,00');
    componente.formaPagamento.set('pix');
    await componente.submeterPagamento(new Event('submit', { cancelable: true }));
    await aguardarAtualizacao(fixture);
    http.expectNone((req) => req.url.endsWith('/payments'));
    expect(componente.errosPagamento().length).toBe(1);
    expect(componente.confirmacaoAberta()).toBe(false);

    componente.valorPagamento.set('61,00');
    await componente.submeterPagamento(new Event('submit', { cancelable: true }));
    await aguardarAtualizacao(fixture);
    http.expectNone((req) => req.url.endsWith('/payments'));
    expect(componente.errosPagamento().join(' ')).toContain('integral');

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.querySelector('#valor-pagamento')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('expõe desconto, estorno e autorizações somente ao administrador', async () => {
    const carregado = await carregar('Administrador');
    http = carregado.http;
    const { fixture } = carregado;

    expect(fixture.nativeElement.textContent).toContain('Conceder desconto');
    expect(fixture.nativeElement.textContent).toContain('Estornar pagamento');
    expect(fixture.nativeElement.textContent).toContain('Autorizações de exceção');
    expect(fixture.nativeElement.textContent).toContain('Não concedida');

    TestBed.resetTestingModule();
    const caixa = await carregar('Caixa');
    http = caixa.http;
    expect(caixa.fixture.nativeElement.textContent).not.toContain('Conceder desconto');
    expect(caixa.fixture.nativeElement.textContent).not.toContain('Estornar pagamento');
    expect(caixa.fixture.nativeElement.textContent).toContain('exclusivas do Administrador');
  });

  it('não envia desconto inválido, acima do cobrado ou sem justificativa', async () => {
    const carregado = await carregar('Administrador');
    http = carregado.http;
    const { fixture, componente } = carregado;

    componente.valorDesconto.set('70,00');
    componente.justificativaDesconto.set('Justificativa sintética.');
    await componente.submeterDesconto(new Event('submit', { cancelable: true }));
    await aguardarAtualizacao(fixture);
    http.expectNone((req) => req.url.endsWith('/discounts'));
    expect(componente.errosDesconto().join(' ')).toContain('não pode exceder');

    componente.valorDesconto.set('10,00');
    componente.justificativaDesconto.set('   ');
    await componente.submeterDesconto(new Event('submit', { cancelable: true }));
    await aguardarAtualizacao(fixture);
    http.expectNone((req) => req.url.endsWith('/discounts'));
    expect(componente.errosDesconto().join(' ')).toContain('justificativa');
  });

  it('mantém o pagamento incerto em memória e repete com a mesma chave', async () => {
    const carregado = await carregar('Caixa');
    http = carregado.http;
    const { fixture, componente } = carregado;

    componente.valorPagamento.set('60,00');
    componente.formaPagamento.set('dinheiro');

    const envio = componente.submeterPagamento(new Event('submit', { cancelable: true }));
    const primeira = http.expectOne((req) => req.url.endsWith('/payments'));
    const chave = primeira.request.headers.get('Idempotency-Key');
    primeira.error(new ProgressEvent('error'));
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('incerto');
    expect(componente.coordenador.bloqueado()).toBe(true);

    const repeticao = componente.repetir();
    const segunda = http.expectOne((req) => req.url.endsWith('/payments'));
    expect(segunda.request.headers.get('Idempotency-Key')).toBe(chave);
    expect(segunda.request.body).toEqual({
      valor: 60,
      forma: 'dinheiro',
      revisaoEsperada: 1,
    });
    segunda.flush({ saldo: 0, revisaoRazao: 2 });
    await new Promise((resolver) => setTimeout(resolver, 0));
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}`))
      .flush(atendimento);
    await new Promise((resolver) => setTimeout(resolver, 0));
    http
      .expectOne((req) => req.url.endsWith(`/withdrawals/${pedidoId}/cashier`))
      .flush(financeiro(0, 60));
    http
      .expectOne((req) => req.url.includes(`/attendances/${atendimentoId}/history`))
      .flush(paginaHistorico);
    await repeticao;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('sucesso');
    expect(componente.confirmacaoAberta()).toBe(true);
    componente.fecharConfirmacao();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('nega o acesso ao manobrista sem chamar o caixa', async () => {
    const { http: controlador, fixture } = await carregar('Manobrista');
    http = controlador;

    expect(fixture.componentInstance.acessoNegado()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Acesso negado');
    http.expectNone((req) => req.url.includes('/cashier'));
  });
});

describe('Caixa — entrega', () => {
  let http: HttpTestingController;

  beforeEach(() => TestBed.resetTestingModule());
  afterEach(() => http.verify());

  it('mostra consumo, recibo e bloqueia novas concessões após a entrega', async () => {
    const dados = {
      ...financeiro(0, 60),
      estado: 'entregue',
      entrega: {
        entregaId: 'ENT-0001',
        responsavel: 'Caixa Sintético',
        entregueEmUtc: new Date().toISOString(),
        vagaId: '33333333-3333-3333-3333-333333333333',
        vagaCodigo: 'A-03',
        liberadaEmUtc: new Date().toISOString(),
      },
      autorizacaoFinanceira: {
        tipo: 'financeira',
        id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        vigencia: 'consumida',
        revisaoReferencia: 1,
        concedidaPor: 'Administrador Sintético',
        concedidaEmUtc: new Date().toISOString(),
        temJustificativa: true,
        consumidaEmUtc: new Date().toISOString(),
      },
    };

    const carregado = await carregar('Administrador', dados);
    http = carregado.http;
    const { fixture } = carregado;
    const texto = fixture.nativeElement.textContent;

    expect(texto).toContain('Consumida');
    expect(texto).toContain('Consumida na entrega em');
    expect(texto).toContain('Não é possível conceder ou revogar autorizações de um pedido entregue');
    expect(texto).toContain('Abrir operação de entrega');
    expect(texto).toContain('ENT-0001');
    expect(texto).not.toContain('Conceder autorização documental');
    expect(texto).not.toContain('Revogar autorização vigente');
  });
});
