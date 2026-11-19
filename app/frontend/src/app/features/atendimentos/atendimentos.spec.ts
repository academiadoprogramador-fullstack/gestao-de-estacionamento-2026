import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessaoService } from '../../core/sessao/sessao.service';
import { Atendimentos } from './atendimentos';
import { aguardarAtualizacao } from '../../testing/aguardar';

function configurar() {
  TestBed.configureTestingModule({
    imports: [Atendimentos],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: SessaoService,
        useValue: {
          autenticado: () => true,
          possuiPerfil: (perfis: readonly string[]) => perfis.includes('Caixa'),
        },
      },
    ],
  });

  return TestBed.inject(HttpTestingController);
}

function detalhe(id: string, identificador: string) {
  return {
    id,
    identificador,
    placa: 'ABC1D23',
    modelo: 'Modelo',
    titular: 'Titular',
    estado: 'ativo',
    vagaCodigo: 'A-01',
    entradaUtc: new Date().toISOString(),
  };
}

const paginaHistorico = { items: [], page: 1, pageSize: 20, total: 0 };

function listaItem(id: string, identificador: string) {
  return {
    id,
    identificador,
    placa: 'ABC1D23',
    modelo: 'Modelo',
    titular: 'Titular',
    estado: 'ativo',
    vagaCodigo: 'A-01',
    entradaUtc: new Date().toISOString(),
    pedidoId: null,
    estadoRetirada: null,
  };
}

async function abrirTela(http: HttpTestingController, itens: unknown[] = []) {
  const fixture = TestBed.createComponent(Atendimentos);

  fixture.detectChanges();
  http
    .expectOne((requisicao) => requisicao.url.includes('/attendances?status=aberto'))
    .flush({ items: itens, page: 1, pageSize: 20, total: itens.length });
  await aguardarAtualizacao(fixture);

  return fixture;
}

describe('Atendimentos', () => {
  let http: HttpTestingController;

  afterEach(() => http.verify());

  it('lista atendimentos em aberto por omissão e mantém a busca exata como opcional', async () => {
    http = configurar();
    const fixture = await abrirTela(http, [listaItem('a', 'AT-2026-000001')]);
    const componente = fixture.componentInstance;
    const elemento = fixture.nativeElement as HTMLElement;

    expect(componente.abertos().length).toBe(1);
    expect(componente.buscou()).toBe(false);
    expect(elemento.textContent).toContain('Atendimentos em aberto');
    expect(elemento.textContent).toContain('AT-2026-000001');
    expect(elemento.textContent).toContain('Sem pedido');

    await componente.buscar();

    expect(componente.erroBusca()).toContain('Informe');
    http.expectNone((requisicao) => requisicao.method === 'GET');
  });

  it('filtra pela busca exata e retorna à lista de abertos ao limpar', async () => {
    http = configurar();
    const fixture = await abrirTela(http, [listaItem('a', 'AT-2026-000001')]);
    const componente = fixture.componentInstance;

    componente.termo.set('ABC1D23');
    const busca = componente.buscar(new Event('submit', { cancelable: true }));
    http
      .expectOne(
        (requisicao) =>
          requisicao.url.includes('/attendances?') &&
          requisicao.url.includes('plate=ABC1D23'),
      )
      .flush({ items: [listaItem('b', 'AT-2026-000002')], page: 1, pageSize: 20, total: 1 });
    await busca;
    await aguardarAtualizacao(fixture);

    expect(componente.buscou()).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Resultados da busca');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('AT-2026-000002');

    componente.limparBusca();
    await aguardarAtualizacao(fixture);

    expect(componente.buscou()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Atendimentos em aberto',
    );
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('AT-2026-000001');
  });

  it('descarta resposta de detalhe que chega após a troca de seleção', async () => {
    http = configurar();
    const fixture = await abrirTela(http);
    const componente = fixture.componentInstance;

    const selecaoA = componente.selecionar('a');
    const detalheA = http.expectOne((requisicao) => requisicao.url.endsWith('/attendances/a'));
    const historicoA = http.expectOne((requisicao) =>
      requisicao.url.includes('/attendances/a/history'),
    );

    const selecaoB = componente.selecionar('b');
    const detalheB = http.expectOne((requisicao) => requisicao.url.endsWith('/attendances/b'));
    const historicoB = http.expectOne((requisicao) =>
      requisicao.url.includes('/attendances/b/history'),
    );

    detalheB.flush(detalhe('b', 'AT-2026-000002'));
    historicoB.flush(paginaHistorico);
    await selecaoB;

    expect(componente.detalhe()?.id).toBe('b');

    // Respostas tardias de A são descartadas pelo contexto de seleção.
    if (!detalheA.cancelled) {
      detalheA.flush(detalhe('a', 'AT-2026-000001'));
    }
    if (!historicoA.cancelled) {
      historicoA.flush(paginaHistorico);
    }
    await selecaoA;
    await Promise.resolve();

    expect(componente.detalhe()?.id).toBe('b');
    expect(componente.selecionado()).toBe('b');
  });

  it('descarta leitura sensível quando o detalhe é fechado antes da resposta', async () => {
    http = configurar();
    const fixture = await abrirTela(http);
    const componente = fixture.componentInstance;

    const selecao = componente.selecionar('a');
    http
      .expectOne((requisicao) => requisicao.url.endsWith('/attendances/a'))
      .flush(detalhe('a', 'AT-2026-000001'));
    http
      .expectOne((requisicao) => requisicao.url.includes('/attendances/a/history'))
      .flush(paginaHistorico);
    await selecao;

    const leitura = componente.ler('vistoria');
    const requisicao = http.expectOne((pedido) =>
      pedido.url.endsWith('/attendances/a/entry-inspection'),
    );

    componente.fechar();

    expect(componente.selecionado()).toBeNull();
    expect(componente.detalhe()).toBeNull();

    if (!requisicao.cancelled) {
      requisicao.flush({ conteudo: 'conteúdo tardio que não deve aparecer' });
    }
    await leitura.catch(() => undefined);
    await Promise.resolve();

    expect(componente.leitura()).toBeNull();
  });
});
