import { TestBed } from '@angular/core/testing';
import { PollingRetiradasService } from './polling-retiradas.service';

async function drenarPromessas(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
}

describe('PollingRetiradasService', () => {
  let servico: PollingRetiradasService;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({});
    servico = TestBed.inject(PollingRetiradasService);
  });

  afterEach(() => {
    servico.ngOnDestroy();
    vi.useRealTimers();
    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
  });

  it('consulta no intervalo configurado e não sobrepõe requisições', async () => {
    let liberar: (() => void) | null = null;
    const acao = vi.fn(
      () =>
        new Promise<void>((resolver) => {
          liberar = resolver;
        }),
    );

    servico.iniciar(acao, 30_000);

    await vi.advanceTimersByTimeAsync(30_000);
    expect(acao).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(90_000);
    expect(acao).toHaveBeenCalledTimes(1);

    liberar!();
    await drenarPromessas();
    await vi.advanceTimersByTimeAsync(30_000);
    expect(acao).toHaveBeenCalledTimes(2);
  });

  it('pausa em aba oculta e atualiza ao recuperar o foco', async () => {
    const acao = vi.fn().mockResolvedValue(undefined);
    servico.iniciar(acao, 30_000);

    Object.defineProperty(document, 'hidden', { value: true, configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));

    await vi.advanceTimersByTimeAsync(120_000);
    expect(acao).not.toHaveBeenCalled();

    Object.defineProperty(document, 'hidden', { value: false, configurable: true });
    document.dispatchEvent(new Event('visibilitychange'));
    await drenarPromessas();

    expect(acao).toHaveBeenCalledTimes(1);
  });

  it('atualiza após comando sem empilhar quando já existe consulta em andamento', async () => {
    let liberar: (() => void) | null = null;
    const acao = vi.fn(
      () =>
        new Promise<void>((resolver) => {
          liberar = resolver;
        }),
    );

    servico.iniciar(acao, 30_000);
    const comando = servico.notificarComando();
    const segundo = servico.atualizarAgora();

    await drenarPromessas();
    expect(acao).toHaveBeenCalledTimes(1);

    liberar!();
    await Promise.all([comando, segundo]);
    expect(acao).toHaveBeenCalledTimes(1);
  });

  it('para o polling e cancela o temporizador ao sair', async () => {
    const acao = vi.fn().mockResolvedValue(undefined);
    servico.iniciar(acao, 30_000);
    servico.parar();

    await vi.advanceTimersByTimeAsync(120_000);
    expect(acao).not.toHaveBeenCalled();
  });
});
