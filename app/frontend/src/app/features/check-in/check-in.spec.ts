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
    expect(componente.coordenador.fase()).toBe('sucesso');
  });
});
