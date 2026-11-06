import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { email, form, FormField, required, submit } from '@angular/forms/signals';
import { LucideShieldCheck } from '@lucide/angular';
import { AuthService } from './core/auth/auth.service';

interface LoginModel {
  email: string;
  password: string;
}

@Component({
  imports: [FormField, LucideShieldCheck],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly auth = inject(AuthService);
  protected readonly session = this.auth.session;
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly loginModel = signal<LoginModel>({ email: '', password: '' });
  protected readonly loginForm = form(this.loginModel, (path) => {
    required(path.email, { message: 'Informe seu e-mail.' });
    email(path.email, { message: 'Informe um e-mail válido.' });
    required(path.password, { message: 'Informe sua senha.' });
  });

  ngOnInit(): void {
    void this.auth.refreshSession();
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.errorMessage.set('');
    this.busy.set(true);
    try {
      await submit(this.loginForm, async () => {
        await this.auth.login(this.loginModel());
        this.loginModel.update((value) => ({ ...value, password: '' }));
      });
    } catch (error) {
      this.errorMessage.set(
        error instanceof HttpErrorResponse && error.status === 0
          ? 'Não foi possível conectar ao serviço. Tente novamente.'
          : 'Não foi possível iniciar a sessão. Confira suas credenciais e tente novamente.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  async logout(): Promise<void> {
    this.errorMessage.set('');
    this.busy.set(true);
    try {
      await this.auth.logout();
    } catch {
      this.errorMessage.set('Não foi possível encerrar a sessão. Tente novamente.');
    } finally {
      this.busy.set(false);
    }
  }
}
