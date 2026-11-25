import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ComandoPendenteService } from '../../core/comando/comando-pendente.service';
import { CoordenadorComandoService } from './coordenador-comando';

describe('CoordenadorComandoService', () => {
  let coordenador: CoordenadorComandoService;
  let pendente: ComandoPendenteService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    coordenador = TestBed.inject(CoordenadorComandoService);
    pendente = TestBed.inject(ComandoPendenteService);
  });

  afterEach(() => {
    coordenador.descartar();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('não exibe a sobreposição para comandos rápidos', async () => {
    let resolver: ((valor: string) => void) | null = null;
    const envio = coordenador.executar(
      'conteudo',
      () =>
        new Promise<string>((resolverInterno) => {
          resolver = resolverInterno;
        }),
    );

    expect(coordenador.fase()).toBe('pendente');
    expect(coordenador.bloqueado()).toBe(true);
    expect(coordenador.exibirBloqueio()).toBe(false);

    resolver!('ok');
    await expect(envio).resolves.toBe('ok');
    expect(coordenador.exibirBloqueio()).toBe(false);
  });

  it('exibe a sobreposição somente após o atraso de um comando pendente', async () => {
    vi.useFakeTimers();
    let resolver: ((valor: string) => void) | null = null;
    const envio = coordenador.executar(
      'conteudo',
      () =>
        new Promise<string>((resolverInterno) => {
          resolver = resolverInterno;
        }),
    );

    expect(coordenador.exibirBloqueio()).toBe(false);
    vi.advanceTimersByTime(399);
    expect(coordenador.exibirBloqueio()).toBe(false);

    vi.advanceTimersByTime(1);
    expect(coordenador.exibirBloqueio()).toBe(true);

    resolver!('ok');
    await envio;
    expect(coordenador.exibirBloqueio()).toBe(false);
  });

  it('começa ocioso e percorre pendente → sucesso', async () => {
    expect(coordenador.fase()).toBe('ocioso');
    expect(pendente.bloqueado()).toBe(false);

    let resolver: ((valor: string) => void) | null = null;
    const envio = coordenador.executar(
      'conteudo',
      () =>
        new Promise<string>((resolverInterno) => {
          resolver = resolverInterno;
        }),
    );

    expect(coordenador.fase()).toBe('pendente');
    expect(pendente.bloqueado()).toBe(true);

    resolver!('ok');
    await expect(envio).resolves.toBe('ok');

    expect(coordenador.fase()).toBe('sucesso');
    expect(pendente.bloqueado()).toBe(false);
  });

  it('mantém a mesma chave e conteúdo para replay após resultado incerto', async () => {
    const chaves: string[] = [];
    const conteudos: unknown[] = [];

    const incerto = coordenador.executar({ placa: 'ABC1D23' }, (chave, conteudo) => {
      chaves.push(chave);
      conteudos.push(conteudo);
      return Promise.reject(new HttpErrorResponse({ status: 0 }));
    });

    await expect(incerto).rejects.toBeInstanceOf(HttpErrorResponse);
    expect(coordenador.fase()).toBe('incerto');
    expect(pendente.bloqueado()).toBe(true);
    expect(coordenador.exibirBloqueio()).toBe(true);

    const replay = coordenador.repetir((chave, conteudo) => {
      chaves.push(chave);
      conteudos.push(conteudo);
      return Promise.resolve('confirmado');
    });

    await expect(replay).resolves.toBe('confirmado');
    expect(chaves.length).toBe(2);
    expect(chaves[0]).toBe(chaves[1]);
    expect(conteudos[0]).toEqual(conteudos[1]);
    expect(coordenador.fase()).toBe('sucesso');
    expect(pendente.bloqueado()).toBe(false);
  });

  it('rejeição definitiva gera nova chave na próxima submissão explícita', async () => {
    const chaves: string[] = [];

    await expect(
      coordenador.executar('primeiro', (chave) => {
        chaves.push(chave);
        return Promise.reject(new HttpErrorResponse({ status: 409 }));
      }),
    ).rejects.toBeInstanceOf(HttpErrorResponse);

    expect(coordenador.fase()).toBe('rejeitado');
    expect(pendente.bloqueado()).toBe(false);

    await expect(
      coordenador.executar('segundo', (chave) => {
        chaves.push(chave);
        return Promise.resolve('ok');
      }),
    ).resolves.toBe('ok');

    expect(chaves.length).toBe(2);
    expect(chaves[0]).not.toBe(chaves[1]);
  });

  it('não inicia novo comando enquanto pending ou incerto', async () => {
    const envio = coordenador.executar('conteudo', () => new Promise<string>(() => undefined));
    void envio;

    await expect(coordenador.executar('outro', () => Promise.resolve('nao'))).rejects.toThrow(
      'comando_pendente',
    );
  });

  it('não persiste tentativa em storage', async () => {
    await expect(
      coordenador.executar('conteudo', () => Promise.reject(new HttpErrorResponse({ status: 0 }))),
    ).rejects.toBeInstanceOf(HttpErrorResponse);

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
