import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { SessaoService } from './core/sessao/sessao.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('cria o shell da aplicação', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('exibe o título do produto e não exibe navegação autenticada sem sessão', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('Gestão de Estacionamento');
    expect(elemento.querySelector('nav[aria-label="Navegação principal"]')).toBeNull();
  });

  it('exibe o nome do funcionário após autenticação em memória', async () => {
    const sessao = TestBed.inject(SessaoService);
    // Simula estado derivado sem persistir tokens no navegador.
    (sessao as unknown as { estadoInterno: { set: (valor: unknown) => void } }).estadoInterno.set({
      funcionario: {
        id: '00000000-0000-0000-0000-000000000001',
        nome: 'Funcionário Sintético',
        email: 'funcionario@example.test',
        perfis: ['Caixa'],
      },
      expiraSessaoEmUtc: new Date(Date.now() + 3_600_000).toISOString(),
    });

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Funcionário Sintético');
  });
});
