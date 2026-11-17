import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { autenticacaoInterceptor } from './autenticacao.interceptor';
import { SessaoService } from '../sessao/sessao.service';
import { ambiente } from '../../../environments/environment';

const respostaLogin = {
  tokenAcesso: 'jwt-atual',
  refresh: 'refresh-atual',
  expiraAcessoEmUtc: new Date(Date.now() + 900_000).toISOString(),
  expiraSessaoEmUtc: new Date(Date.now() + 28_800_000).toISOString(),
  funcionario: {
    id: '00000000-0000-0000-0000-000000000001',
    nome: 'Caixa',
    email: 'caixa@example.test',
    perfis: ['Caixa'],
  },
};

describe('autenticacaoInterceptor', () => {
  let http: HttpClient;
  let controlador: HttpTestingController;
  let sessao: SessaoService;
  let router: Router;
  let navegacao: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([autenticacaoInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });

    http = TestBed.inject(HttpClient);
    controlador = TestBed.inject(HttpTestingController);
    sessao = TestBed.inject(SessaoService);
    router = TestBed.inject(Router);
    navegacao = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const login = sessao.login('caixa@example.test', 'SenhaTeste1');
    controlador.expectOne(`${ambiente.apiUrl}/api/v1/session/login`).flush(respostaLogin);
    await login;
  });

  afterEach(() => controlador.verify());

  it('anexa bearer somente à URL-base configurada da API', async () => {
    const daApi = firstValueFrom(http.get(`${ambiente.apiUrl}/api/v1/area-trabalho`));
    const requisicaoApi = controlador.expectOne(`${ambiente.apiUrl}/api/v1/area-trabalho`);
    expect(requisicaoApi.request.headers.get('Authorization')).toBe('Bearer jwt-atual');
    expect(requisicaoApi.request.withCredentials).toBe(false);
    requisicaoApi.flush({});

    const externa = firstValueFrom(http.get('https://externo.example.test/dados'));
    const requisicaoExterna = controlador.expectOne('https://externo.example.test/dados');
    expect(requisicaoExterna.request.headers.has('Authorization')).toBe(false);
    requisicaoExterna.flush({});

    await Promise.all([daApi, externa]);
  });

  it('em 401 renova uma vez e repete apenas requisição segura', async () => {
    const chamada = firstValueFrom(http.get(`${ambiente.apiUrl}/api/v1/area-trabalho`));

    const original = controlador.expectOne(`${ambiente.apiUrl}/api/v1/area-trabalho`);
    expect(original.request.headers.get('Authorization')).toBe('Bearer jwt-atual');
    original.flush({}, { status: 401, statusText: 'Unauthorized' });

    const renovacao = controlador.expectOne(`${ambiente.apiUrl}/api/v1/session/refresh`);
    expect(renovacao.request.body).toEqual({ refresh: 'refresh-atual' });
    renovacao.flush({ ...respostaLogin, tokenAcesso: 'jwt-renovado', refresh: 'refresh-2' });
    await new Promise<void>((resolver) => setTimeout(() => resolver(), 0));

    const repetida = controlador.expectOne(`${ambiente.apiUrl}/api/v1/area-trabalho`);
    expect(repetida.request.headers.get('Authorization')).toBe('Bearer jwt-renovado');
    repetida.flush({ ok: true });

    await expect(chamada).resolves.toEqual({ ok: true });
    expect(navegacao).not.toHaveBeenCalled();
  });

  it('não repete POST automaticamente após renovar token', async () => {
    const comando = firstValueFrom(
      http.post(`${ambiente.apiUrl}/api/v1/comando`, { valor: 1 }),
    ).catch((erro: unknown) => erro);

    controlador
      .expectOne(`${ambiente.apiUrl}/api/v1/comando`)
      .flush({}, { status: 401, statusText: 'Unauthorized' });

    controlador
      .expectOne(`${ambiente.apiUrl}/api/v1/session/refresh`)
      .flush({ ...respostaLogin, tokenAcesso: 'jwt-renovado' });

    await new Promise<void>((resolver) => setTimeout(() => resolver(), 0));
    await expect(comando).resolves.toMatchObject({ status: 401 });
    controlador.expectNone(`${ambiente.apiUrl}/api/v1/comando`);
  });

  it('em 403 mantém a sessão e não tenta renovar nem deslogar', async () => {
    const chamada = firstValueFrom(
      http.get(`${ambiente.apiUrl}/api/v1/administracao/verificacao`),
    ).catch((erro: unknown) => erro);

    controlador
      .expectOne(`${ambiente.apiUrl}/api/v1/administracao/verificacao`)
      .flush({ code: 'acesso_negado' }, { status: 403, statusText: 'Forbidden' });

    await expect(chamada).resolves.toMatchObject({ status: 403 });
    await new Promise<void>((resolver) => setTimeout(() => resolver(), 0));
    controlador.expectNone(`${ambiente.apiUrl}/api/v1/session/refresh`);
    expect(sessao.autenticado()).toBe(true);
    expect(navegacao).not.toHaveBeenCalled();
  });
});
