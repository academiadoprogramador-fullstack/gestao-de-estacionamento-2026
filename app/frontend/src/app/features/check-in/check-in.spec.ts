import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CheckIn } from './check-in';
import { aguardarAtualizacao } from '../../testing/aguardar';

const paginaVazia = { items: [], page: 1, pageSize: 20, total: 0 };

function configurar() {
  TestBed.configureTestingModule({
    imports: [CheckIn],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  return TestBed.inject(HttpTestingController);
}

function preencherEntrada(componente: CheckIn): void {
  componente.formulario.placa().value.set('ABC-1D23');
  componente.formulario.modelo().value.set('Modelo Sintético');
  componente.formulario.titular().value.set('Titular Sintético');
  componente.formulario.documento().value.set('000.000.000-00');
  componente.formulario.vistoria().value.set('Vistoria sintética de entrada.');
  componente.formulario.vagaId().value.set('11111111-1111-1111-1111-111111111111');
}

describe('CheckIn', () => {
  let http: HttpTestingController;

  afterEach(() => http.verify());

  it('não chama a API com dados inválidos e foca o resumo acessível', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(CheckIn);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    await componente.submeter(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    http.expectNone((requisicao) => requisicao.method === 'POST');
    expect(componente.resumoVisivel()).toBe(true);

    const elemento = fixture.nativeElement as HTMLElement;
    const placa = elemento.querySelector<HTMLInputElement>('#placa')!;
    expect(placa.getAttribute('aria-invalid')).toBe('true');
    expect(placa.getAttribute('aria-describedby')).toBe('erro-placa');
    expect(elemento.querySelector('#vistoria')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('registra a entrada e limpa documento e vistoria após sucesso', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(CheckIn);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    preencherEntrada(componente);

    const envio = componente.submeter(new Event('submit', { cancelable: true }));
    const requisicao = http.expectOne((pedido) => pedido.method === 'POST');
    const chave = requisicao.request.headers.get('Idempotency-Key');
    expect(chave).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);

    requisicao.flush({
      atendimentoId: '22222222-2222-2222-2222-222222222222',
      identificador: 'AT-2026-000001',
      vagaId: '11111111-1111-1111-1111-111111111111',
      vagaCodigo: 'A-01',
      estado: 'ativo',
      entradaUtc: new Date().toISOString(),
      location: '/api/v1/attendances/22222222-2222-2222-2222-222222222222',
    });

    await aguardarAtualizacao(fixture);
    http.expectOne((pedido) => pedido.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.resultado()?.identificador).toBe('AT-2026-000001');
    expect(componente.formulario.documento().value()).toBe('');
    expect(componente.formulario.vistoria().value()).toBe('');
    expect(componente.formulario.documento().touched()).toBe(false);
    expect(componente.formulario.vagaId().touched()).toBe(false);
    expect(componente.resumoVisivel()).toBe(false);
    expect(componente.coordenador.fase()).toBe('sucesso');
  });

  it('seleciona fotos antes do check-in e só as envia depois da confirmação', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(CheckIn);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    preencherEntrada(componente);
    componente.selecionarFotos({
      target: {
        files: [new File(['a'], 'frente.jpg', { type: 'image/jpeg' })],
        value: '',
      },
    } as unknown as Event);

    expect(componente.fotos().length).toBe(1);
    expect(componente.fotos().every((foto) => foto.status === 'selecionada')).toBe(true);
    http.expectNone((pedido) => pedido.method === 'POST');

    const envio = componente.submeter(new Event('submit', { cancelable: true }));
    const entrada = http.expectOne((pedido) => pedido.method === 'POST');
    expect(entrada.request.body.foto).toBeUndefined();
    entrada.flush({
      atendimentoId: '22222222-2222-2222-2222-222222222222',
      identificador: 'AT-2026-000001',
      vagaId: '11111111-1111-1111-1111-111111111111',
      vagaCodigo: 'A-01',
      estado: 'ativo',
      entradaUtc: new Date().toISOString(),
      location: '/api/v1/attendances/22222222-2222-2222-2222-222222222222',
    });

    await aguardarAtualizacao(fixture);
    http.expectOne((pedido) => pedido.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);
    http
      .expectOne(
        (pedido) =>
          pedido.method === 'GET' &&
          pedido.url.endsWith('/api/v1/attendances/22222222-2222-2222-2222-222222222222'),
      )
      .flush({ versaoAtendimento: 'BBBBBBB=' });

    await aguardarAtualizacao(fixture);
    const foto = http.expectOne(
      (pedido) =>
        pedido.method === 'POST' &&
        pedido.url.endsWith('/attendances/22222222-2222-2222-2222-222222222222/inspections/entrada/photos'),
    );
    expect(foto.request.headers.get('Idempotency-Key')).toBeTruthy();
    foto.flush({
      fotoId: '33333333-3333-3333-3333-333333333333',
      estado: 'disponivel',
    });

    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.resultado()?.identificador).toBe('AT-2026-000001');
    expect(componente.fotos()[0].status).toBe('confirmada');
    expect(componente.coordenador.fase()).toBe('sucesso');
  });

  it('mantém a entrada confirmada e permite repetir a mesma foto após falha incerta', async () => {
    http = configurar();
    const fixture = TestBed.createComponent(CheckIn);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    preencherEntrada(componente);
    componente.selecionarFotos({
      target: {
        files: [new File(['a'], 'frente.jpg', { type: 'image/jpeg' })],
        value: '',
      },
    } as unknown as Event);

    const envio = componente.submeter(new Event('submit', { cancelable: true }));
    http.expectOne((pedido) => pedido.method === 'POST').flush({
      atendimentoId: '22222222-2222-2222-2222-222222222222',
      identificador: 'AT-2026-000001',
      vagaId: '11111111-1111-1111-1111-111111111111',
      vagaCodigo: 'A-01',
      estado: 'ativo',
      entradaUtc: new Date().toISOString(),
      location: '/api/v1/attendances/22222222-2222-2222-2222-222222222222',
    });
    await aguardarAtualizacao(fixture);
    http.expectOne((pedido) => pedido.url.includes('/api/v1/spaces')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);
    http
      .expectOne((pedido) => pedido.method === 'GET' && pedido.url.endsWith('/attendances/22222222-2222-2222-2222-222222222222'))
      .flush({ versaoAtendimento: 'BBBBBBB=' });

    await aguardarAtualizacao(fixture);
    const primeiraFoto = http.expectOne(
      (pedido) =>
        pedido.method === 'POST' &&
        pedido.url.endsWith('/attendances/22222222-2222-2222-2222-222222222222/inspections/entrada/photos'),
    );
    const chave = primeiraFoto.request.headers.get('Idempotency-Key');
    primeiraFoto.flush(
      { status: 503, code: 'photo_storage_unavailable' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.resultado()?.identificador).toBe('AT-2026-000001');
    expect(componente.fotoIncerta()).toBe(true);
    expect(componente.fotos()[0].status).toBe('incerta');

    const repeticao = componente.repetirFoto();
    const segundaFoto = http.expectOne(
      (pedido) =>
        pedido.method === 'POST' &&
        pedido.url.endsWith('/attendances/22222222-2222-2222-2222-222222222222/inspections/entrada/photos'),
    );
    expect(segundaFoto.request.headers.get('Idempotency-Key')).toBe(chave);
    segundaFoto.flush({ fotoId: '33333333-3333-3333-3333-333333333333', estado: 'disponivel' });
    await repeticao;
    await aguardarAtualizacao(fixture);

    expect(componente.fotos()[0].status).toBe('confirmada');
    expect(componente.coordenador.fase()).toBe('sucesso');
  });
});
