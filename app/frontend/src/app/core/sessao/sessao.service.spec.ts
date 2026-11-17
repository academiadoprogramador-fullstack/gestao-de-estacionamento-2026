import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { SessaoService } from './sessao.service';
import { ambiente } from '../../../environments/environment';

const funcionario = {
  id: '00000000-0000-0000-0000-000000000001',
  nome: 'Funcionário Sintético',
  email: 'funcionario@example.test',
  perfis: ['Caixa'],
};

const respostaLogin = {
  tokenAcesso: 'jwt-em-memoria',
  refresh: 'refresh-em-memoria',
  expiraAcessoEmUtc: new Date(Date.now() + 900_000).toISOString(),
  expiraSessaoEmUtc: new Date(Date.now() + 28_800_000).toISOString(),
  funcionario,
};

describe('SessaoService', () => {
  let servico: SessaoService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });

    servico = TestBed.inject(SessaoService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('mantém JWT, refresh e funcionário somente em memória após login', async () => {
    const setItemLocal = vi.spyOn(Storage.prototype, 'setItem');

    const login = servico.login('funcionario@example.test', 'SenhaTeste1');
    const requisicao = http.expectOne(`${ambiente.apiUrl}/api/v1/session/login`);
    expect(requisicao.request.body).toEqual({
      email: 'funcionario@example.test',
      senha: 'SenhaTeste1',
    });
    requisicao.flush(respostaLogin);

    await expect(login).resolves.toEqual({ sucesso: true });
    expect(servico.autenticado()).toBe(true);
    expect(servico.tokenAcesso).toBe('jwt-em-memoria');
    expect(servico.estado()?.funcionario.nome).toBe('Funcionário Sintético');
    expect(setItemLocal).not.toHaveBeenCalled();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('limpa a memória no logout mesmo quando a revogação remota falha', async () => {
    const login = servico.login('funcionario@example.test', 'SenhaTeste1');
    http.expectOne(`${ambiente.apiUrl}/api/v1/session/login`).flush(respostaLogin);
    await login;

    const logout = servico.logout();
    const requisicao = http.expectOne(`${ambiente.apiUrl}/api/v1/session/logout`);
    requisicao.flush(null, { status: 503, statusText: 'Indisponível' });

    await expect(logout).resolves.toBe(false);
    expect(servico.autenticado()).toBe(false);
    expect(servico.tokenAcesso).toBeNull();
    expect(servico.estado()).toBeNull();
  });

  it('renova com uma única requisição concorrente (single-flight)', async () => {
    const login = servico.login('funcionario@example.test', 'SenhaTeste1');
    http.expectOne(`${ambiente.apiUrl}/api/v1/session/login`).flush(respostaLogin);
    await login;

    const primeira = servico.renovar();
    const segunda = servico.renovar();

    const requisicoes = http.match(`${ambiente.apiUrl}/api/v1/session/refresh`);
    expect(requisicoes.length).toBe(1);
    requisicoes[0].flush({ ...respostaLogin, tokenAcesso: 'jwt-renovado', refresh: 'refresh-novo' });

    await expect(Promise.all([primeira, segunda])).resolves.toEqual([true, true]);
    expect(servico.tokenAcesso).toBe('jwt-renovado');
  });

  it('login inválido não cria sessão e preserva o estado limpo', async () => {
    const login = servico.login('funcionario@example.test', 'SenhaErrada1');
    http
      .expectOne(`${ambiente.apiUrl}/api/v1/session/login`)
      .flush({ code: 'credenciais_invalidas' }, { status: 401, statusText: 'Unauthorized' });

    await expect(login).resolves.toEqual({ sucesso: false, motivo: 'credenciais' });
    expect(servico.autenticado()).toBe(false);
    expect(servico.tokenAcesso).toBeNull();
  });

  it('não persiste token quando o navegador recarrega (novo serviço sem sessão)', async () => {
    const login = servico.login('funcionario@example.test', 'SenhaTeste1');
    http.expectOne(`${ambiente.apiUrl}/api/v1/session/login`).flush(respostaLogin);
    await login;

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    const outraInstancia = TestBed.inject(SessaoService);
    expect(outraInstancia.autenticado()).toBe(false);
    expect(outraInstancia.tokenAcesso).toBeNull();
  });
});
