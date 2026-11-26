import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { aguardarAtualizacao } from '../../testing/aguardar';
import { FotosVistoria } from './fotos-vistoria';

const atendimentoId = '33333333-3333-3333-3333-333333333333';
const fotoId = '44444444-4444-4444-4444-444444444444';

const orcamento = {
  percentualBytes: 10,
  percentualOperacoesClasseA: 1,
  percentualOperacoesClasseB: 2,
  aviso: false,
  suspenso: false,
  motivo: null,
  limiteFotosPorVistoria: 5,
};

const lista = {
  itens: [
    {
      fotoId,
      tipo: 'entrada',
      mime: 'image/webp',
      tamanhoBytes: 2048,
      largura: 640,
      altura: 480,
      criadoEmUtc: new Date().toISOString(),
    },
  ],
  quantidade: 1,
  janela: 'aberta',
  vencido: false,
  orcamento,
};

function configurar(podeEnviar = true): {
  fixture: ComponentFixture<FotosVistoria>;
  http: HttpTestingController;
  componente: FotosVistoria;
} {
  TestBed.configureTestingModule({
    imports: [FotosVistoria],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });

  const fixture = TestBed.createComponent(FotosVistoria);
  fixture.componentRef.setInput('atendimentoId', atendimentoId);
  fixture.componentRef.setInput('tipo', 'entrada');
  fixture.componentRef.setInput('versaoAtendimento', 'AAAAAAA=');
  fixture.componentRef.setInput('podeEnviar', podeEnviar);
  fixture.componentRef.setInput('quantidadeInicial', 0);
  fixture.componentRef.setInput('janelaInicial', 'aberta');
  fixture.componentRef.setInput('orcamentoInicial', orcamento);
  fixture.detectChanges();

  return {
    fixture,
    http: TestBed.inject(HttpTestingController),
    componente: fixture.componentInstance,
  };
}

function arquivo(): File {
  return new File([new Uint8Array([1, 2, 3])], 'foto-sintetica.jpg', {
    type: 'image/jpeg',
  });
}

describe('FotosVistoria', () => {
  it('carrega a lista por ação explícita e lê o conteúdo como blob temporário', async () => {
    const { fixture, http, componente } = configurar();

    const consulta = componente.consultar();
    http
      .expectOne(
        (requisicao) =>
          requisicao.method === 'GET' &&
          requisicao.url.endsWith(`/attendances/${atendimentoId}/inspections/entrada/photos`),
      )
      .flush(lista);
    await consulta;
    await aguardarAtualizacao(fixture);

    expect(componente.quantidade()).toBe(1);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Foto 1');

    const criarUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:foto');
    const revogarUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);

    const leitura = componente.ler(lista.itens[0] as never);
    http
      .expectOne(
        (requisicao) =>
          requisicao.method === 'GET' &&
          requisicao.url.endsWith(
            `/attendances/${atendimentoId}/inspections/entrada/photos/${fotoId}/content`,
          ),
      )
      .flush(new Blob([new Uint8Array([1, 2, 3])], { type: 'image/webp' }));
    await leitura;
    await aguardarAtualizacao(fixture);

    expect(criarUrl).toHaveBeenCalled();
    expect(componente.urlFoto()).toBe('blob:foto');

    componente.fecharFoto();
    expect(revogarUrl).toHaveBeenCalledWith('blob:foto');
  });

  it('envia foto com chave idempotente e repete a mesma tentativa após resposta incerta', async () => {
    const { fixture, http, componente } = configurar();

    componente.selecionarArquivo({
      target: { files: [arquivo()], value: '' },
    } as unknown as Event);

    const envio = componente.enviar(new Event('submit', { cancelable: true }));
    const primeira = http.expectOne(
      (requisicao) =>
        requisicao.method === 'POST' &&
        requisicao.url.endsWith(`/attendances/${atendimentoId}/inspections/entrada/photos`),
    );
    const chave = primeira.request.headers.get('Idempotency-Key');
    expect(chave).toBeTruthy();
    primeira.flush(
      { status: 503, code: 'photo_storage_unavailable' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await envio;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('incerto');

    const repeticao = componente.repetir();
    const segunda = http.expectOne(
      (requisicao) =>
        requisicao.method === 'POST' &&
        requisicao.url.endsWith(`/attendances/${atendimentoId}/inspections/entrada/photos`),
    );
    expect(segunda.request.headers.get('Idempotency-Key')).toBe(chave);
    segunda.flush({
      fotoId,
      atendimentoId,
      tipo: 'entrada',
      estado: 'disponivel',
      mime: 'image/webp',
      tamanhoBytes: 2048,
      largura: 640,
      altura: 480,
      criadoEmUtc: new Date().toISOString(),
      location: `/api/v1/attendances/${atendimentoId}/inspections/entrada/photos/${fotoId}`,
    });
    await new Promise((resolver) => setTimeout(resolver, 0));
    http
      .expectOne(
        (requisicao) =>
          requisicao.method === 'GET' &&
          requisicao.url.endsWith(`/attendances/${atendimentoId}/inspections/entrada/photos`),
      )
      .flush(lista);
    await repeticao;
    await aguardarAtualizacao(fixture);

    expect(componente.coordenador.fase()).toBe('sucesso');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Foto 1');
  });

  it('oculta o envio para perfil consultivo e mantém a consulta disponível', async () => {
    const { fixture, componente } = configurar(false);
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain(
      'o envio de fotos de entrada é exclusivo de Caixa e Administrador',
    );
    expect(elemento.querySelector('input[type="file"]')).toBeNull();
    expect(componente.podeEnviarAgora()).toBe(false);
  });

  it('recusa formato e tamanho inválidos antes de chamar a API', () => {
    const { http, componente } = configurar();

    componente.selecionarArquivo({
      target: {
        files: [new File(['x'], 'vetor.svg', { type: 'image/svg+xml' })],
        value: '',
      },
    } as unknown as Event);
    expect(componente.erroEnvio()).toContain('Formato não aceito');

    componente.selecionarArquivo({
      target: {
        files: [new File([new Uint8Array(6 * 1024 * 1024)], 'grande.jpg', { type: 'image/jpeg' })],
        value: '',
      },
    } as unknown as Event);
    expect(componente.erroEnvio()).toContain('acima de 5 MiB');

    http.expectNone((requisicao) => requisicao.method === 'POST');
  });
});
