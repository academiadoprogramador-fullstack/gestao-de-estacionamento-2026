import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { signal } from '@angular/core';
import { guardaPerfil, guardaSessao } from './guards';
import { EstadoSessao, SessaoService } from '../sessao/sessao.service';

function configurar(estadoInicial: EstadoSessao | null) {
  const estado = signal<EstadoSessao | null>(estadoInicial);
  const sessaoStub = {
    autenticado: () => estado() !== null,
    possuiPerfil: (perfis: readonly string[]) =>
      perfis.some((perfil) => estado()?.funcionario.perfis.includes(perfil) ?? false),
  };

  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: SessaoService, useValue: sessaoStub },
    ],
  });

  return { estado, router: TestBed.inject(Router) };
}

describe('guards de sessão e perfil', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('encaminha ao login quando não há sessão em memória', () => {
    const { router } = configurar(null);

    const resultado = TestBed.runInInjectionContext(() => guardaSessao({} as never, {} as never));

    expect(resultado).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(resultado as UrlTree)).toBe('/login');
  });

  it('permite rota protegida com sessão autenticada', () => {
    const { estado } = configurar({
      funcionario: {
        id: '1',
        nome: 'Caixa',
        email: 'caixa@example.test',
        perfis: ['Caixa'],
      },
      expiraSessaoEmUtc: new Date().toISOString(),
    });

    expect(estado()).not.toBeNull();
    expect(TestBed.runInInjectionContext(() => guardaSessao({} as never, {} as never))).toBe(true);
  });

  it('encaminha à área de acesso negado quando o perfil é insuficiente', () => {
    const { router } = configurar({
      funcionario: {
        id: '1',
        nome: 'Caixa',
        email: 'caixa@example.test',
        perfis: ['Caixa'],
      },
      expiraSessaoEmUtc: new Date().toISOString(),
    });

    const guarda = guardaPerfil(['Administrador']);
    const resultado = TestBed.runInInjectionContext(() => guarda({} as never, {} as never));

    expect(resultado).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(resultado as UrlTree)).toBe('/forbidden');
  });

  it('permite a rota quando o perfil é suficiente', () => {
    configurar({
      funcionario: {
        id: '1',
        nome: 'Administrador',
        email: 'admin@example.test',
        perfis: ['Administrador'],
      },
      expiraSessaoEmUtc: new Date().toISOString(),
    });

    const guarda = guardaPerfil(['Administrador']);
    expect(TestBed.runInInjectionContext(() => guarda({} as never, {} as never))).toBe(true);
  });
});
