import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SessaoService } from '../../core/sessao/sessao.service';
import { Funcionarios } from './funcionarios';
import { aguardarAtualizacao } from '../../testing/aguardar';

const paginaVazia = { items: [], page: 1, pageSize: 20, total: 0 };

function configurar(administrador: boolean) {
  TestBed.configureTestingModule({
    imports: [Funcionarios],
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

describe('Funcionarios', () => {
  let http: HttpTestingController;

  afterEach(() => http.verify());

  it('mostra o estado negado para Caixa ou Manobrista sem chamar a API', async () => {
    http = configurar(false);
    const fixture = TestBed.createComponent(Funcionarios);
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.textContent).toContain('Acesso negado');
    expect(elemento.textContent).toContain('Somente o Administrador');
    expect(elemento.querySelector('#nome')).toBeNull();
    http.expectNone((requisicao) => requisicao.url.includes('/api/v1/employees'));
  });

  it('limpa a validação após criar um funcionário com sucesso', async () => {
    http = configurar(true);
    const fixture = TestBed.createComponent(Funcionarios);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/employees')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    componente.formulario.nome().value.set('Funcionário Válido');
    componente.formulario.email().value.set('funcionario@example.test');
    componente.formulario.senhaInicial().value.set('SenhaTeste1');
    componente.perfisSelecionados.set(['Caixa']);

    const criacao = componente.criar(new Event('submit', { cancelable: true }));
    const requisicao = http.expectOne((pedido) => pedido.method === 'POST');
    requisicao.flush({
      id: '11111111-1111-1111-1111-111111111111',
      nome: 'Funcionário Válido',
      email: 'funcionario@example.test',
      ativo: true,
      perfis: ['Caixa'],
      versaoAcesso: 1,
      tokenConcorrencia: 'AAAA',
    });
    await aguardarAtualizacao(fixture);
    http.expectOne((pedido) => pedido.url.includes('/api/v1/employees')).flush(paginaVazia);
    await criacao;
    await aguardarAtualizacao(fixture);

    expect(componente.sucesso()).toContain('Funcionário Funcionário Válido criado');
    expect(componente.resumoVisivel()).toBe(false);
    expect(componente.formulario.nome().touched()).toBe(false);
    expect(componente.formulario.nome().value()).toBe('');
  });

  it('não chama a API com dados inválidos ou sem perfis e foca o resumo', async () => {
    http = configurar(true);
    const fixture = TestBed.createComponent(Funcionarios);
    const componente = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne((requisicao) => requisicao.url.includes('/api/v1/employees')).flush(paginaVazia);
    await aguardarAtualizacao(fixture);

    await componente.criar(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    http.expectNone((requisicao) => requisicao.method === 'POST');
    expect(componente.resumoVisivel()).toBe(true);
    expect(componente.erroPerfis()).not.toBeNull();

    // Preenche os textos, mas mantém o conjunto de perfis vazio.
    componente.formulario.nome().value.set('Funcionário Válido');
    componente.formulario.email().value.set('funcionario@example.test');
    componente.formulario.senhaInicial().value.set('SenhaTeste1');

    await componente.criar(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    http.expectNone((requisicao) => requisicao.method === 'POST');
    expect(componente.erroPerfis()).toBe('Selecione ao menos um perfil fixo.');

    const elemento = fixture.nativeElement as HTMLElement;
    expect(elemento.querySelector('#nome')?.getAttribute('aria-invalid')).toBe('false');
  });
});
