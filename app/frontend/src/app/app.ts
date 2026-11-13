import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal, viewChild } from '@angular/core';
import { email, form, FormField, required, submit } from '@angular/forms/signals';
import { LucideShieldCheck } from '@lucide/angular';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { Router } from '@angular/router';

interface LoginModel {
  email: string;
  password: string;
}

@Component({
  imports: [FormField, LucideShieldCheck, RouterLink, RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly routedPage = viewChild(RouterOutlet);
  protected readonly session = this.auth.session;
  protected readonly accessDenied = this.auth.accessDenied;
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly loginModel = signal<LoginModel>({ email: '', password: '' });
  protected readonly loginForm = form(this.loginModel, (path) => {
    required(path.email, { message: 'Informe seu e-mail.' });
    email(path.email, { message: 'Informe um e-mail válido.' });
    required(path.password, { message: 'Informe sua senha.' });
  });

  ngOnInit(): void {
    void this.auth.ensureSession();
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.errorMessage.set('');
    this.busy.set(true);
    try {
      await submit(this.loginForm, async () => {
        await this.auth.login(this.loginModel());
        this.loginModel.update((value) => ({ ...value, password: '' }));
        const returnUrl = new URLSearchParams(globalThis.location.search).get('returnUrl');
        await this.router.navigateByUrl(returnUrl?.startsWith('/') ? returnUrl : '/dashboard');
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
    const page = this.routedPage()?.isActivated
      ? (this.routedPage()?.component as { confirmNavigationAway?: () => boolean })
      : null;
    if (page?.confirmNavigationAway && !page.confirmNavigationAway()) return;
    this.errorMessage.set('');
    this.busy.set(true);
    try {
      await this.auth.logout();
      await this.router.navigateByUrl('/login');
    } catch {
      this.errorMessage.set('Não foi possível encerrar a sessão. Tente novamente.');
    } finally {
      this.busy.set(false);
    }
  }
}
