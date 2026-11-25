import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { SessaoService } from '../../core/sessao/sessao.service';
import { aguardarAtualizacao } from '../../testing/aguardar';
import { Delivery } from './delivery';

const pedidoId = '11111111-1111-1111-1111-111111111111';
const atendimentoId = '22222222-2222-2222-2222-222222222222';

function pedido(sobrescritas: Record<string, unknown> = {}) {
  return {
    id: pedidoId,
    atendimentoId,
    identificador: 'AT-2026-000041',
    placa: 'BRA2E19',
    modelo: 'Hatch prata',
    vagaCodigo: 'A-03',
    estado: 'aguardando',
    entradaUtc: new Date().toISOString(),
    solicitadoEmUtc: new Date().toISOString(),
    buscaIniciadaEmUtc: null,
    responsavelBusca: null,
    versaoPedido: 'AAAAAAA=',
    vistoriaSaidaRegistrada: false,
    entrega: null,
    conferencia: 'aprovada',
    duracaoTicks: 1,
    diarias: 1,
    tarifaVersao: 1,
    tarifaValorSnapshot: 60,
    totalOriginal: 60,
    descontos: 0,
    pagamentos: 0,
    estornos: 0,
    cobrado: 60,
    pagoLiquido: 0,
    saldo: 60,
    revisaoRazao: 1,
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
    ...sobrescritas,
  };
}

function atendimento(sobrescritas: Record<string, unknown> = {}) {
  return {
    id: atendimentoId,
    identificador: 'AT-2026-000041',
    placa: 'BRA2E19',
    modelo: 'Hatch prata',
    titular: 'Titular Sintético',
    estado: 'aguardando',
    vagaCodigo: 'A-03',
    entradaUtc: new Date().toISOString(),
    versaoAtendimento: 'BBBBBBB=',
    vistoriaSaidaRegistrada: false,
    entrega: null,
    retirada: {
      pedidoId,
      estado: 'aguardando',
      solicitadoEmUtc: new Date().toISOString(),
      buscaIniciadaEmUtc: null,
      buscaPor: null,
    },
    ...sobrescritas,
  };
}

function prontidao(sobrescritas: Record<string, unknown> = {}) {
  return {
    pedidoId,
    atendimentoId,
    identificador: 'AT-2026-000041',
    estado: 'aguardando',
    vagaCodigo: 'A-03',
    vistoriaRegistrada: true,
    conferencia: 'aprovada',
    saldo: 0,
    condicoes: [
      {
        codigo: 'vistoria_saida',
        descricao: 'Vistoria de saída',
        situacao: 'Registrada',
        atendida: true,
        codigoBloqueio: null,
        concessaoId: null,
        revisaoReferencia: null,
      },
      {
        codigo: 'conferencia_documental',
        descricao: 'Conferência documental',
        situacao: 'Aprovada',
        atendida: true,
        codigoBloqueio: null,
        concessaoId: null,
        revisaoReferencia: null,
      },
      {
        codigo: 'saldo_exigivel',
        descricao: 'Saldo exigível',
        situacao: 'Saldo zerado',
        atendida: true,
        codigoBloqueio: null,
        concessaoId: null,
        revisaoReferencia: null,
      },
      {
        codigo: 'ocupacao_aberta',
        descricao: 'Ocupação da visita',
        situacao: 'Ocupação aberta do próprio atendimento',
        atendida: true,
        codigoBloqueio: null,
        concessaoId: null,
        revisaoReferencia: null,
      },
    ],
    bloqueios: [],
    pronta: true,
    entrega: null,
    versoes: {
      versaoAtendimento: 'BBBBBBB=',
      versaoPedido: 'AAAAAAA=',
      versaoRazao: 'CCCCCCC=',
      versaoControleDocumental: 'ausente',
      versaoControleFinanceira: 'ausente',
    },
    ...sobrescritas,
  };
}

function configurar(perfil: 'Administrador' | 'Caixa' | 'Manobrista') {
  TestBed.configureTestingModule({
    imports: [Delivery],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: { paramMap: convertToParamMap({ withdrawalId: pedidoId }) },
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
  http: HttpTestingController,
  fixture: ComponentFixture<Delivery>,
  dados: {
    pedido?: Record<string, unknown>;
    atendimento?: Record<string, unknown>;
    prontidao?: Record<string, unknown>;
    comProntidao: boolean;
  },
) {
  http
    .expectOne((req) => req.method === 'GET' && req.url.endsWith(`/withdrawals/${pedidoId}`))
    .flush(pedido(dados.pedido));
  await aguardarAtualizacao(fixture);
  http
    .expectOne((req) => req.method === 'GET' && req.url.endsWith(`/attendances/${atendimentoId}`))
    .flush(atendimento(dados.atendimento));
  await aguardarAtualizacao(fixture);

  if (dados.comProntidao) {
    http
      .expectOne((req) => req.method === 'GET' && req.url.endsWith('/delivery-readiness'))
      .flush(prontidao(dados.prontidao));
    await aguardarAtualizacao(fixture);
  }
}

describe('Delivery', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('permite ao manobrista registrar e ler a vistoria sem prontidão ou confirmação', async () => {
    const http = configurar('Manobrista');
    const fixture = TestBed.createComponent(Delivery);
    fixture.detectChanges();
    await carregar(http, fixture, { comProntidao: false });

    expect(fixture.nativeElement.textContent).toContain('Vistoria ainda não registrada');
    expect(fixture.nativeElement.textContent).not.toContain('Revisar e confirmar entrega');
    http.expectNone((req) => req.url.endsWith('/delivery-readiness'));

    const componente = fixture.componentInstance;
    componente.textoVistoria.set('Risco leve na porta.');
    const envio = componente.registrarVistoria(new Event('submit', { cancelable: true }));
    const post = http.expectOne((req) =>
      req.url.endsWith(`/attendances/${atendimentoId}/exit-inspection`),
    );
    expect(post.request.method).toBe('POST');
    expect(post.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(post.request.body).toEqual({
      texto: 'Risco leve na porta.',
      versaoAtendimentoEsperada: 'BBBBBBB=',
    });
    post.flush({ vistoriaId: '33333333-3333-3333-3333-333333333333' });
    await new Promise((resolver) => setTimeout(resolver, 0));

    http
      .expectOne((req) => req.url.endsWith(`/withdrawals/${pedidoId}`))
      .flush(pedido({ vistoriaSaidaRegistrada: true }));
    await aguardarAtualizacao(fixture);
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}`))
      .flush(atendimento({ vistoriaSaidaRegistrada: true }));
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.vistoriaRegistrada()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Vistoria registrada');

    const leitura = componente.lerVistoria();
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}/exit-inspection`))
      .flush({
        conteudo: 'Risco leve na porta.',
        ator: 'Manobrista Sintético',
        registradaEmUtc: new Date().toISOString(),
      });
    await leitura;
    await aguardarAtualizacao(fixture);
    expect(componente.leituraVistoria()?.conteudo).toBe('Risco leve na porta.');
  });

  it('confirma a entrega com as versões do servidor e recarrega o estado sem usar o recibo como estado', async () => {
    const http = configurar('Caixa');
    const fixture = TestBed.createComponent(Delivery);
    fixture.detectChanges();
    await carregar(http, fixture, { comProntidao: true });

    const componente = fixture.componentInstance;
    expect(fixture.nativeElement.textContent).toContain('Prontidão para entrega');
    expect(fixture.nativeElement.textContent).toContain('Ocupação aberta do próprio atendimento');

    componente.abrirConfirmacao();
    expect(componente.confirmacaoAberta()).toBe(true);

    const entrega = componente.confirmarEntrega();
    const post = http.expectOne((req) => req.url.endsWith('/delivery'));
    expect(post.request.method).toBe('POST');
    expect(post.request.headers.get('Idempotency-Key')).toBeTruthy();
    expect(post.request.body).toEqual({
      confirmacao: true,
      versaoAtendimento: 'BBBBBBB=',
      versaoPedido: 'AAAAAAA=',
      versaoRazao: 'CCCCCCC=',
      versaoControleDocumental: 'ausente',
      versaoControleFinanceira: 'ausente',
    });
    post.flush({ entregaId: '44444444-4444-4444-4444-444444444444' });
    await new Promise((resolver) => setTimeout(resolver, 0));

    const entregaResumo = {
      entregaId: '44444444-4444-4444-4444-444444444444',
      responsavel: 'Caixa Sintético',
      entregueEmUtc: new Date().toISOString(),
      vagaId: '55555555-5555-5555-5555-555555555555',
      vagaCodigo: 'A-03',
      liberadaEmUtc: new Date().toISOString(),
    };

    http
      .expectOne((req) => req.url.endsWith(`/withdrawals/${pedidoId}`))
      .flush(pedido({ estado: 'entregue', entrega: entregaResumo, vistoriaSaidaRegistrada: true }));
    await aguardarAtualizacao(fixture);
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}`))
      .flush(atendimento({ estado: 'entregue', entrega: entregaResumo }));
    await entrega;
    await aguardarAtualizacao(fixture);

    expect(componente.entregue()).toBe(true);
    expect(componente.confirmacaoAberta()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Entrega registrada');
    expect(fixture.nativeElement.textContent).toContain('A-03');
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('mantém a tentativa incerta somente em memória e repete a mesma chave e conteúdo', async () => {
    const http = configurar('Caixa');
    const fixture = TestBed.createComponent(Delivery);
    fixture.detectChanges();
    await carregar(http, fixture, { comProntidao: true });

    const componente = fixture.componentInstance;
    const entrega = componente.confirmarEntrega();
    const primeira = http.expectOne((req) => req.url.endsWith('/delivery'));
    const chave = primeira.request.headers.get('Idempotency-Key');
    const corpo = primeira.request.body;
    primeira.error(new ProgressEvent('error'));
    await entrega;

    expect(componente.coordenador.fase()).toBe('incerto');
    expect(componente.coordenador.bloqueado()).toBe(true);
    expect(componente.aviso()).toContain('incerto');

    const repeticao = componente.repetir();
    const segunda = http.expectOne((req) => req.url.endsWith('/delivery'));
    expect(segunda.request.headers.get('Idempotency-Key')).toBe(chave);
    expect(segunda.request.body).toEqual(corpo);
    segunda.flush({ entregaId: '44444444-4444-4444-4444-444444444444' });
    await new Promise((resolver) => setTimeout(resolver, 0));

    http
      .expectOne((req) => req.url.endsWith(`/withdrawals/${pedidoId}`))
      .flush(pedido({ estado: 'entregue' }));
    await aguardarAtualizacao(fixture);
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}`))
      .flush(atendimento({ estado: 'entregue' }));
    await repeticao;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('sucesso');
    expect(localStorage.length).toBe(0);
  });

  it('trata conflito como rejeição definitiva, recarrega e libera nova confirmação', async () => {
    const http = configurar('Caixa');
    const fixture = TestBed.createComponent(Delivery);
    fixture.detectChanges();
    await carregar(http, fixture, { comProntidao: true });

    const componente = fixture.componentInstance;
    const entrega = componente.confirmarEntrega();
    http
      .expectOne((req) => req.url.endsWith('/delivery'))
      .flush(
        { code: 'concurrency_conflict', detail: 'conflito' },
        { status: 409, statusText: 'Conflict' },
      );
    await new Promise((resolver) => setTimeout(resolver, 0));

    http.expectOne((req) => req.url.endsWith(`/withdrawals/${pedidoId}`)).flush(pedido());
    await aguardarAtualizacao(fixture);
    http.expectOne((req) => req.url.endsWith(`/attendances/${atendimentoId}`)).flush(atendimento());
    await aguardarAtualizacao(fixture);
    http.expectOne((req) => req.url.endsWith('/delivery-readiness')).flush(prontidao());
    await entrega;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('rejeitado');
    expect(componente.coordenador.bloqueado()).toBe(false);
    expect(componente.conflito()).toContain('Recarregue');
    expect(fixture.nativeElement.textContent).toContain('Recarregar estado');
  });

  it('associa erro de texto inválido ao campo sem enviar POST', async () => {
    const http = configurar('Manobrista');
    const fixture = TestBed.createComponent(Delivery);
    fixture.detectChanges();
    await carregar(http, fixture, { comProntidao: false });

    const componente = fixture.componentInstance;
    componente.textoVistoria.set('   ');
    await componente.registrarVistoria(new Event('submit', { cancelable: true }));
    await aguardarAtualizacao(fixture);

    http.expectNone((req) => req.url.endsWith('/exit-inspection'));
    expect(componente.errosVistoria().length).toBe(1);
    const campo = (fixture.nativeElement as HTMLElement).querySelector('#observacoes-saida');
    expect(campo?.getAttribute('aria-invalid')).toBe('true');
    const resumo = (fixture.nativeElement as HTMLElement).querySelector('#observacoes-saida + p');
    expect(resumo?.textContent).toContain('1 a 4.000');
  });
});
