import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessaoService } from '../../core/sessao/sessao.service';
import { Tarifas } from './tarifas';
import { aguardarAtualizacao } from '../../testing/aguardar';

interface CorrenteTeste {
  temTarifa: boolean;
  versao: number | null;
  valor: number | null;
  publicadoPorId: string | null;
  publicadoEmUtc: string | null;
  versaoBase: number | null;
}

const correnteSemTarifa: CorrenteTeste = {
  temTarifa: false,
  versao: null,
  valor: null,
  publicadoPorId: null,
  publicadoEmUtc: null,
  versaoBase: null,
};

const paginaVazia = { items: [], page: 1, pageSize: 20, total: 0 };

function configurar(administrador = true) {
  TestBed.configureTestingModule({
    imports: [Tarifas],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: SessaoService,
        useValue: {
          possuiPerfil: (perfis: readonly string[]) =>
            administrador && perfis.includes('Administrador'),
        },
      },
    ],
  });

  return TestBed.inject(HttpTestingController);
}

function responderCargaInicial(
  http: HttpTestingController,
  corrente: CorrenteTeste = correnteSemTarifa,
): void {
  http.expectOne((requisicao) => requisicao.url.endsWith('/daily-rates/current')).flush(corrente);
  http.expectOne((requisicao) => requisicao.url.includes('/daily-rates?page=')).flush(paginaVazia);
}

describe('Tarifas', () => {
  let http: HttpTestingController;

  afterEach(() => http.verify());

  it('exibe o estado sem tarifa publicada', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(Tarifas);
    fixture.detectChanges();
    responderCargaInicial(http);
    await aguardarAtualizacao(fixture);

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('Nenhuma tarifa foi publicada');
    expect(elemento.textContent).toContain('Nenhuma versão publicada');
  });

  it('não chama a API quando o valor é inválido', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(Tarifas);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    responderCargaInicial(http);
    await aguardarAtualizacao(fixture);

    componente.formulario.valor().value.set('10.001');
    await componente.publicar(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    http.expectNone((requisicao) => requisicao.method === 'POST');
    expect(componente.resumoVisivel()).toBe(true);
    expect(componente.erroValor()).not.toBeNull();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.querySelector('#valor-diaria')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('publica versão válida e atualiza a tarifa corrente', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(Tarifas);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    responderCargaInicial(http);
    await aguardarAtualizacao(fixture);

    componente.formulario.valor().value.set('25,50');
    const publicacao = componente.publicar(new Event('submit', { cancelable: true }));
    await Promise.resolve();

    const requisicao = http.expectOne((pedido) => pedido.method === 'POST');
    expect(requisicao.request.body).toEqual({ valor: 25.5, versaoBaseEsperada: null });
    requisicao.flush({
      versao: 1,
      valor: 25.5,
      publicadoPorId: '00000000-0000-0000-0000-000000000001',
      publicadoPorNome: 'Administrador',
      publicadoEmUtc: new Date().toISOString(),
    });

    const corrente: CorrenteTeste = {
      temTarifa: true,
      versao: 1,
      valor: 25.5,
      publicadoPorId: '00000000-0000-0000-0000-000000000001',
      publicadoEmUtc: new Date().toISOString(),
      versaoBase: 1,
    };
    await aguardarAtualizacao(fixture);
    responderCargaInicial(http, corrente);
    await publicacao;
    await aguardarAtualizacao(fixture);

    expect(componente.corrente()?.versao).toBe(1);
    expect(componente.sucesso()).toContain('Versão 1 publicada');
    expect(componente.resumoVisivel()).toBe(false);
    expect(componente.formulario.valor().touched()).toBe(false);
  });
});
