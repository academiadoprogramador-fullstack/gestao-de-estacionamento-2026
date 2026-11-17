import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { Login } from './login';
import { SessaoService } from '../../../core/sessao/sessao.service';

describe('Login', () => {
  let loginMock: ReturnType<typeof vi.fn>;
  let navigateMock: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    loginMock = vi.fn();
    navigateMock = vi.fn().mockResolvedValue(true);

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        { provide: SessaoService, useValue: { login: loginMock } },
        { provide: Router, useValue: { navigateByUrl: navigateMock } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => null } } },
        },
      ],
    }).compileComponents();
  });

  it('não chama a API quando a validação local falha e foca o resumo', async () => {
    const fixture = TestBed.createComponent(Login);
    const componente = fixture.componentInstance;
    await fixture.whenStable();

    const evento = new Event('submit', { cancelable: true });
    await componente.entrar(evento);
    await fixture.whenStable();
    await new Promise<void>((resolver) => queueMicrotask(() => resolver()));

    expect(loginMock).not.toHaveBeenCalled();
    expect(componente.resumoVisivel()).toBe(true);
    expect(componente.formulario.email().touched()).toBe(true);
    expect(componente.formulario.senha().touched()).toBe(true);

    const resumo = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(resumo).not.toBeNull();
    expect(document.activeElement).toBe(resumo);
  });

  it('mantém o e-mail digitado e exibe mensagem genérica em credenciais inválidas', async () => {
    loginMock.mockResolvedValue({ sucesso: false, motivo: 'credenciais' });
    const fixture = TestBed.createComponent(Login);
    const componente = fixture.componentInstance;
    await fixture.whenStable();

    componente.formulario.email().value.set('caixa@example.test');
    componente.formulario.senha().value.set('SenhaErrada1');

    await componente.entrar(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    expect(loginMock).toHaveBeenCalledWith('caixa@example.test', 'SenhaErrada1');
    expect(componente.formulario.email().value()).toBe('caixa@example.test');
    expect(componente.erro()).toContain('E-mail ou senha inválidos');
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it('navega para a área de trabalho após login válido', async () => {
    loginMock.mockResolvedValue({ sucesso: true });
    const fixture = TestBed.createComponent(Login);
    const componente = fixture.componentInstance;
    await fixture.whenStable();

    componente.formulario.email().value.set('caixa@example.test');
    componente.formulario.senha().value.set('SenhaTeste1');

    await componente.entrar(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    expect(navigateMock).toHaveBeenCalledWith('/dashboard');
  });

  it('expõe controles com rótulos, aria-invalid e descrição de erro', async () => {
    const fixture = TestBed.createComponent(Login);
    const componente = fixture.componentInstance;
    await fixture.whenStable();

    componente.formulario.email().markAsTouched();
    componente.formulario.senha().markAsTouched();
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    const email = elemento.querySelector<HTMLInputElement>('#email')!;
    const senha = elemento.querySelector<HTMLInputElement>('#senha')!;
    const rotuloEmail = elemento.querySelector<HTMLLabelElement>('label[for="email"]')!;
    const rotuloSenha = elemento.querySelector<HTMLLabelElement>('label[for="senha"]')!;

    expect(rotuloEmail.textContent).toContain('E-mail');
    expect(rotuloSenha.textContent).toContain('Senha');
    expect(email.getAttribute('aria-invalid')).toBe('true');
    expect(senha.getAttribute('aria-invalid')).toBe('true');
    expect(email.getAttribute('aria-describedby')).toBe('erro-email');
    expect(senha.getAttribute('aria-describedby')).toBe('erro-senha');
  });
});
