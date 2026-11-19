import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { SessaoService } from '../../core/sessao/sessao.service';
import { PedidoRetirada } from './pedido-retirada';
import { aguardarAtualizacao } from '../../testing/aguardar';

const atendimento = {
  id: '22222222-2222-2222-2222-222222222222',
  identificador: 'AT-2026-000041',
  placa: 'BRA2E19',
  modelo: 'Hatch prata',
  titular: 'Ana Beatriz Ferreira',
  estado: 'ativo',
  vagaCodigo: 'A-03',
  entradaUtc: new Date().toISOString(),
  tarifaVersao: 6,
  tarifaValorSnapshot: 20,
  pedidoId: null,
  estadoRetirada: null,
};

const recibo = {
  pedidoId: '11111111-1111-1111-1111-111111111111',
  atendimentoId: atendimento.id,
  identificador: atendimento.identificador,
  estado: 'aguardando',
  solicitadoEmUtc: new Date().toISOString(),
  diarias: 1,
  totalOriginal: 20,
  location: '/api/v1/withdrawals/11111111-1111-1111-1111-111111111111',
};

const paginaAbertos = (items: unknown[] = [atendimento]) => ({
  items,
  page: 1,
  pageSize: 20,
  total: items.length,
});

function configurar(
  perfil: 'Administrador' | 'Caixa' | 'Manobrista' = 'Caixa',
  attendanceId: string | null = null,
) {
  TestBed.configureTestingModule({
    imports: [PedidoRetirada],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: {
          snapshot: {
            queryParamMap: convertToParamMap(attendanceId ? { attendanceId } : {}),
          },
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

async function abrirTela(
  http: HttpTestingController,
  abertos: unknown[] = [atendimento],
  detalhe: unknown | null = null,
) {
  const fixture = TestBed.createComponent(PedidoRetirada);
  fixture.detectChanges();

  http
    .expectOne((req) => req.url.includes('/attendances?status=aberto'))
    .flush(paginaAbertos(abertos));

  if (detalhe) {
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimento.id}`))
      .flush(detalhe);
  }

  await aguardarAtualizacao(fixture);
  return fixture;
}

describe('PedidoRetirada', () => {
  let http: HttpTestingController;

  beforeEach(() => TestBed.resetTestingModule());

  afterEach(() => http.verify());

  it('lista atendimentos em aberto sem exigir placa ou identificador', async () => {
    http = configurar();
    const comPedido = {
      ...atendimento,
      id: '33333333-3333-3333-3333-333333333333',
      identificador: 'AT-2026-000038',
      placa: 'QWE4R56',
      pedidoId: '11111111-1111-1111-1111-111111111111',
      estadoRetirada: 'aguardando',
    };

    const fixture = await abrirTela(http, [atendimento, comPedido]);
    const texto = fixture.nativeElement.textContent;

    expect(texto).toContain('Atendimentos em aberto na unidade');
    expect(texto).toContain('AT-2026-000041');
    expect(texto).toContain('AT-2026-000038');
    expect(texto).toContain('Sem pedido');
    expect(texto).toContain('Retirada: aguardando');
    expect(fixture.componentInstance.totalAbertos()).toBe(2);
    http.expectNone((req) => req.url.endsWith(`/attendances/${atendimento.id}`));
  });

  it('seleciona um atendimento da lista e carrega o contexto', async () => {
    http = configurar();
    const fixture = await abrirTela(http);

    const selecao = fixture.componentInstance.selecionarDaLista(atendimento);
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimento.id}`))
      .flush(atendimento);
    await selecao;
    await aguardarAtualizacao(fixture);

    expect(fixture.componentInstance.atendimento()?.identificador).toBe(atendimento.identificador);
    expect(fixture.nativeElement.textContent).toContain('Cobrança fixada no pedido');
  });

  it('impede novo pedido para atendimento que já possui retirada', async () => {
    http = configurar();
    const detalheComPedido = {
      ...atendimento,
      estado: 'aguardando',
      retirada: {
        pedidoId: '11111111-1111-1111-1111-111111111111',
        estado: 'aguardando',
        solicitadoEmUtc: new Date().toISOString(),
        buscaIniciadaEmUtc: null,
        buscaPor: null,
      },
    };
    const itemComPedido = {
      ...atendimento,
      pedidoId: detalheComPedido.retirada.pedidoId,
      estadoRetirada: 'aguardando',
    };
    const fixture = await abrirTela(http, [itemComPedido]);
    const selecao = fixture.componentInstance.selecionarDaLista(itemComPedido);
    http
      .expectOne((req) => req.url.endsWith(`/attendances/${atendimento.id}`))
      .flush(detalheComPedido);
    await selecao;
    await aguardarAtualizacao(fixture);

    expect(fixture.nativeElement.textContent).toContain('já possui pedido');
    const botoes = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ) as HTMLButtonElement[];
    expect(botoes.some((botao) => botao.textContent?.trim() === 'Registrar pedido')).toBe(false);
  });

  it('mantém a busca exata como filtro opcional e permite voltar à lista', async () => {
    http = configurar();
    const fixture = await abrirTela(http);
    const componente = fixture.componentInstance;

    componente.termo.set('BRA2E19');
    const busca = componente.buscar(new Event('submit', { cancelable: true }));
    http
      .expectOne((req) => req.url.includes('/attendances?') && req.url.includes('plate=BRA2E19'))
      .flush(paginaAbertos());
    await busca;
    await aguardarAtualizacao(fixture);

    expect(componente.buscou()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Resultados da busca');

    componente.limparFiltro();
    await aguardarAtualizacao(fixture);
    expect(componente.buscou()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Atendimentos em aberto na unidade');

    componente.totalAbertos.set(21);
    componente.alterarPaginaAbertos(2);
    http
      .expectOne((req) => req.url.includes('status=aberto') && req.url.includes('page=2'))
      .flush({ items: [], page: 2, pageSize: 20, total: 21 });
    await aguardarAtualizacao(fixture);
    expect(componente.paginaAbertos()).toBe(2);
  });

  it('recusa presença e conferência ausentes sem chamar a API e foca o resumo', async () => {
    http = configurar('Caixa', atendimento.id);
    const fixture = await abrirTela(http, [atendimento], atendimento);

    await fixture.componentInstance.submeter(new Event('submit', { cancelable: true }));
    await aguardarAtualizacao(fixture);

    http.expectNone((req) => req.method === 'POST');
    expect(fixture.componentInstance.erros().length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('Declare a presença do titular');
  });

  it('recusa terceiro ou titular ausente sem chamar a API', async () => {
    http = configurar('Caixa', atendimento.id);
    const fixture = await abrirTela(http, [atendimento], atendimento);

    const componente = fixture.componentInstance;
    componente.titularPresente.set(false);
    componente.selecionarConferencia('aprovada');
    await componente.submeter(new Event('submit', { cancelable: true }));
    await aguardarAtualizacao(fixture);

    http.expectNone((req) => req.method === 'POST');
    expect(componente.erros().join(' ')).toContain('Retirada por terceiro');
  });

  it('registra o pedido, usa a chave em memória e limpa a conferência', async () => {
    http = configurar('Caixa', atendimento.id);
    const fixture = await abrirTela(http, [atendimento], atendimento);

    const componente = fixture.componentInstance;
    componente.titularPresente.set(true);
    componente.selecionarConferencia('aprovada');

    const envio = componente.submeter(new Event('submit', { cancelable: true }));
    const requisicao = http.expectOne((req) => req.method === 'POST');
    const chave = requisicao.request.headers.get('Idempotency-Key');
    expect(chave).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    expect(requisicao.request.body).toEqual({ titularPresente: true, conferencia: 'aprovada' });
    requisicao.flush(recibo);

    await new Promise((resolver) => setTimeout(resolver, 0));
    http
      .expectOne((req) => req.url.includes('/attendances?status=aberto'))
      .flush(paginaAbertos([{ ...atendimento, estadoRetirada: 'aguardando' }]));
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.resultado()?.pedidoId).toBe(recibo.pedidoId);
    expect(componente.titularPresente()).toBe(false);
    expect(componente.conferencia()).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Pedido registrado');

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('mantém a tentativa incerta em memória e repete exatamente a mesma chave e conteúdo', async () => {
    http = configurar('Caixa', atendimento.id);
    const fixture = await abrirTela(http, [atendimento], atendimento);

    const componente = fixture.componentInstance;
    componente.titularPresente.set(true);
    componente.selecionarConferencia('divergente');

    const envio = componente.submeter(new Event('submit', { cancelable: true }));
    const primeira = http.expectOne((req) => req.method === 'POST');
    const chave = primeira.request.headers.get('Idempotency-Key');
    primeira.error(new ProgressEvent('error'));
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('incerto');
    expect(componente.coordenador.bloqueado()).toBe(true);

    const repeticao = componente.repetir();
    const segunda = http.expectOne((req) => req.method === 'POST');
    expect(segunda.request.headers.get('Idempotency-Key')).toBe(chave);
    expect(segunda.request.body).toEqual({ titularPresente: true, conferencia: 'divergente' });
    segunda.flush({ ...recibo, estado: 'aguardando' });
    await new Promise((resolver) => setTimeout(resolver, 0));
    http
      .expectOne((req) => req.url.includes('/attendances?status=aberto'))
      .flush(paginaAbertos([{ ...atendimento, estadoRetirada: 'aguardando' }]));
    await repeticao;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('sucesso');
    expect(componente.resultado()?.pedidoId).toBe(recibo.pedidoId);
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
