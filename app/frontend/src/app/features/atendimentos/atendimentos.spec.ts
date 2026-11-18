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

describe('Atendimentos', () => {
  let http: HttpTestingController;

  afterEach(() => http.verify());

  it('exige um critério e não lista tudo por omissão', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(Atendimentos);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

    await componente.buscar();

    expect(componente.erroBusca()).toContain('Informe');
    http.expectNone((requisicao) => requisicao.method === 'GET');
  });

  it('descarta resposta de detalhe que chega após a troca de seleção', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(Atendimentos);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

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
    const fixture = TestBed.createComponent(Atendimentos);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

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
