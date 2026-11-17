import { ChangeDetectorRef, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { email, form, FormField, minLength, required } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import {
  LucideCircleAlert,
  LucideEye,
  LucideEyeOff,
  LucideKeyRound,
  LucideLoaderCircle,
  LucideLogIn,
  LucideParkingSquare,
} from '@lucide/angular';
import { mensagemDeFalha } from '../../../core/http/erro-api';
import { SessaoService } from '../../../core/sessao/sessao.service';

interface Credenciais {
  email: string;
  senha: string;
}

@Component({
  selector: 'app-login',
  imports: [
    FormField,
    LucideCircleAlert,
    LucideEye,
    LucideEyeOff,
    LucideKeyRound,
    LucideLoaderCircle,
    LucideLogIn,
    LucideParkingSquare,
  ],
  templateUrl: './login.html',
})
export class Login {
  private readonly sessao = inject(SessaoService);
  private readonly router = inject(Router);
  private readonly rota = inject(ActivatedRoute);
  private readonly detector = inject(ChangeDetectorRef);

  readonly modelo = signal<Credenciais>({ email: '', senha: '' });

  readonly formulario = form(this.modelo, (campo) => {
    required(campo.email, { message: 'Informe o e-mail.' });
    email(campo.email, { message: 'Informe um e-mail com formato válido.' });
    required(campo.senha, { message: 'Informe a senha.' });
    minLength(campo.senha, 8, { message: 'A senha deve ter ao menos 8 caracteres.' });
  });

  readonly resumoErro = viewChild<ElementRef<HTMLDivElement>>('resumoErro');

  readonly mostrarSenha = signal(false);
  readonly enviando = signal(false);
  readonly erro = signal<string | null>(null);
  readonly resumoVisivel = signal(false);
  readonly avisoRevogacaoNaoConfirmada = signal(
    this.rota.snapshot.queryParamMap.get('logout') === 'sem-confirmacao',
  );

  alternarSenha(): void {
    this.mostrarSenha.update((visivel) => !visivel);
  }

  async entrar(evento: Event): Promise<void> {
    evento.preventDefault();

    this.formulario.email().markAsTouched();
    this.formulario.senha().markAsTouched();
    this.erro.set(null);

    if (this.formulario().invalid()) {
      this.resumoVisivel.set(true);
      this.detector.detectChanges();
      queueMicrotask(() => this.resumoErro()?.nativeElement.focus());
      return;
    }

    this.enviando.set(true);

    try {
      const resultado = await this.sessao.login(
        this.formulario.email().value(),
        this.formulario.senha().value(),
      );

      if (resultado.sucesso) {
        await this.router.navigateByUrl('/dashboard');
        return;
      }

      this.erro.set(
        resultado.motivo === 'credenciais'
          ? 'E-mail ou senha inválidos. A conta pode estar temporariamente bloqueada.'
          : mensagemDeFalha(null),
      );
      this.formulario.senha().value.set('');
    } finally {
      this.enviando.set(false);
    }
  }
}
