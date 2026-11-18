import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { SessaoService } from '../../core/sessao/sessao.service';
import { PollingVagasService } from './polling-vagas.service';
import { Vagas } from './vagas';
import { aguardarAtualizacao } from '../../testing/aguardar';

function configurar(administrador: boolean) {
  const polling = {
    iniciar: vi.fn(),
    parar: vi.fn(),
    atualizarAgora: vi.fn().mockResolvedValue(undefined),
    notificarComando: vi.fn().mockResolvedValue(undefined),
  };

  TestBed.configureTestingModule({
    imports: [Vagas],
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
      { provide: PollingVagasService, useValue: polling },
    ],
  });

  return { polling, http: TestBed.inject(HttpTestingController) };
}

const paginaVazia = { items: [], page: 1, pageSize: 20, total: 0 };

describe('Vagas', () => {
  let http: HttpTestingController;

  afterEach(() => http.verify());

  it('exibe estado vazio e inicia o polling configurado', async () => {
    const configuracao = configurar(true);
    http = configuracao.http;

    const fixture = TestBed.createComponent(Vagas);
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('Nenhuma vaga corresponde aos filtros');
    expect(configuracao.polling.iniciar).toHaveBeenCalledTimes(1);
    expect(configuracao.polling.iniciar.mock.calls[0][1]).toBe(30_000);
  });

  it('não chama a API quando o código é inválido e foca o resumo', async () => {
    const configuracao = configurar(true);
    http = configuracao.http;

    const fixture = TestBed.createComponent(Vagas);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    await componente.cadastrar(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    http.expectNone((requisicao) => requisicao.method === 'POST');
    expect(componente.resumoVisivel()).toBe(true);

    const elemento = fixture.nativeElement as HTMLElement;
    const campo = elemento.querySelector<HTMLInputElement>('#codigo-vaga')!;
    expect(campo.getAttribute('aria-invalid')).toBe('true');
    expect(campo.getAttribute('aria-describedby')).toBe('erro-codigo-vaga');
  });

  it('preserva filtros e página quando uma atualização falha', async () => {
    const configuracao = configurar(true);
    http = configuracao.http;

    const fixture = TestBed.createComponent(Vagas);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    componente.codigoFiltro.set('A');
    componente.situacaoFiltro.set('livre');
    componente.pagina.set(2);

    const atualizacao = componente.carregar();
    const requisicao = http.expectOne(
      (pedido) =>
        pedido.url.includes('code=A') &&
        pedido.url.includes('status=livre') &&
        pedido.url.includes('page=2'),
    );
    requisicao.flush({ code: 'erro' }, { status: 503, statusText: 'Indisponível' });
    await atualizacao;
    await aguardarAtualizacao(fixture);

    expect(componente.codigoFiltro()).toBe('A');
    expect(componente.situacaoFiltro()).toBe('livre');
    expect(componente.pagina()).toBe(2);
    expect(componente.erro()).not.toBeNull();
  });

  it('exibe estado negado para perfis não administrativos', async () => {
    const configuracao = configurar(false);
    http = configuracao.http;

    const fixture = TestBed.createComponent(Vagas);
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('exclusivo do Administrador');
    expect(elemento.querySelector('#codigo-vaga')).toBeNull();
    http.expectNone((requisicao) => requisicao.method === 'POST');
  });
});
