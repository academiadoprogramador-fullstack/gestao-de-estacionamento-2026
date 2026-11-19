import { HttpErrorResponse } from '@angular/common/http';
import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { email, form, FormField, maxLength, minLength, required } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import {
  LucideCircleAlert,
  LucideLoaderCircle,
  LucidePencil,
  LucideShieldCheck,
  LucideUserPlus,
  LucideUsers,
} from '@lucide/angular';
import { urlApi } from '../../../environments/environment';
import { mensagemDoErro, Pagina } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';

interface FuncionarioResumo {
  readonly id: string;
  readonly nome: string;
  readonly email: string;
  readonly ativo: boolean;
  readonly perfis: readonly string[];
  readonly tokenConcorrencia: string;
}

interface ModeloCriacao {
  nome: string;
  email: string;
  senhaInicial: string;
}

interface ModeloEdicao {
  funcionario: FuncionarioResumo;
  ativo: boolean;
  perfis: string[];
}

const PERFIS_FIXOS = ['Administrador', 'Caixa', 'Manobrista'] as const;

@Component({
  selector: 'app-funcionarios',
  imports: [
    FormField,
    LucideCircleAlert,
    LucideLoaderCircle,
    LucidePencil,
    LucideShieldCheck,
    LucideUserPlus,
    LucideUsers,
  ],
  templateUrl: './funcionarios.html',
})
export class Funcionarios implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly sessao = inject(SessaoService);
  private readonly detector = inject(ChangeDetectorRef);

  readonly perfisFixos = PERFIS_FIXOS;
  readonly ehAdministrador = computed(() => this.sessao.possuiPerfil(['Administrador']));
  readonly itens = signal<readonly FuncionarioResumo[]>([]);
  readonly total = signal(0);
  readonly pagina = signal(1);
  readonly tamanhoPagina = 20;
  readonly statusFiltro = signal('');
  readonly carregando = signal(true);
  readonly erro = signal<string | null>(null);
  readonly sucesso = signal<string | null>(null);

  readonly modelo = signal<ModeloCriacao>({ nome: '', email: '', senhaInicial: '' });
  readonly formulario = form(this.modelo, (campo) => {
    required(campo.nome, { message: 'Informe o nome.' });
    maxLength(campo.nome, 200, { message: 'O nome deve ter até 200 caracteres.' });
    required(campo.email, { message: 'Informe o e-mail.' });
    email(campo.email, { message: 'Informe um e-mail com formato válido.' });
    required(campo.senhaInicial, { message: 'Informe a senha inicial.' });
    minLength(campo.senhaInicial, 8, {
      message: 'A senha inicial deve ter ao menos 8 caracteres.',
    });
  });

  readonly resumoErro = viewChild<ElementRef<HTMLDivElement>>('resumoErro');
  readonly tituloEdicao = viewChild<ElementRef<HTMLHeadingElement>>('tituloEdicao');
  private disparadorEdicao: HTMLElement | null = null;
  readonly resumoVisivel = signal(false);
  readonly perfisSelecionados = signal<string[]>([]);
  readonly erroPerfis = signal<string | null>(null);
  readonly salvando = signal(false);

  readonly edicao = signal<ModeloEdicao | null>(null);
  readonly erroEdicao = signal<string | null>(null);
  readonly salvandoEdicao = signal(false);

  readonly totalPaginas = computed(() => Math.max(1, Math.ceil(this.total() / this.tamanhoPagina)));

  ngOnInit(): void {
    void this.carregar();
  }

  async carregar(): Promise<void> {
    if (!this.ehAdministrador()) {
      this.carregando.set(false);
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);

    try {
      const parametros = new URLSearchParams({
        page: String(this.pagina()),
        pageSize: String(this.tamanhoPagina),
      });

      if (this.statusFiltro()) {
        parametros.set('status', this.statusFiltro());
      }

      const resposta = await firstValueFrom(
        this.http.get<Pagina<FuncionarioResumo>>(urlApi(`/api/v1/employees?${parametros}`)),
      );

      this.itens.set(resposta.items);
      this.total.set(resposta.total);
    } catch (erro) {
      this.erro.set(mensagemDoErro(erro, 'Não foi possível carregar os funcionários.'));
    } finally {
      this.carregando.set(false);
    }
  }

  alternarPerfil(perfil: string): void {
    this.perfisSelecionados.update((atuais) =>
      atuais.includes(perfil) ? atuais.filter((item) => item !== perfil) : [...atuais, perfil],
    );
    if (this.perfisSelecionados().length > 0) {
      this.erroPerfis.set(null);
    }
  }

  alternarPerfilEdicao(perfil: string): void {
    this.edicao.update((atual) => {
      if (!atual) {
        return atual;
      }

      const perfis = atual.perfis.includes(perfil)
        ? atual.perfis.filter((item) => item !== perfil)
        : [...atual.perfis, perfil];

      return { ...atual, perfis };
    });
  }

  async criar(evento: Event): Promise<void> {
    evento.preventDefault();
    this.formulario.nome().markAsTouched();
    this.formulario.email().markAsTouched();
    this.formulario.senhaInicial().markAsTouched();
    this.erro.set(null);
    this.sucesso.set(null);
    this.erroPerfis.set(null);

    const perfis = this.perfisSelecionados();
    const invalido = this.formulario().invalid() || perfis.length === 0;

    if (perfis.length === 0) {
      this.erroPerfis.set('Selecione ao menos um perfil fixo.');
    }

    if (invalido) {
      this.resumoVisivel.set(true);
      this.detector.detectChanges();
      queueMicrotask(() => this.resumoErro()?.nativeElement.focus());
      return;
    }

    if (!this.ehAdministrador()) {
      this.erro.set('Seu perfil não permite criar funcionários.');
      return;
    }

    this.salvando.set(true);

    try {
      const criado = await firstValueFrom(
        this.http.post<FuncionarioResumo>(urlApi('/api/v1/employees'), {
          nome: this.formulario.nome().value(),
          email: this.formulario.email().value(),
          senhaInicial: this.formulario.senhaInicial().value(),
          perfis,
        }),
      );

      this.sucesso.set(`Funcionário ${criado.nome} criado.`);
      this.modelo.set({ nome: '', email: '', senhaInicial: '' });
      this.perfisSelecionados.set([]);
      this.resumoVisivel.set(false);
      this.erroPerfis.set(null);
      this.formulario.nome().reset();
      this.formulario.email().reset();
      this.formulario.senhaInicial().reset();
      this.pagina.set(1);
      await this.carregar();
    } catch (erro) {
      this.erro.set(mensagemDoErro(erro, 'Não foi possível criar o funcionário.'));
      if (erro instanceof HttpErrorResponse && erro.status === 409) {
        await this.carregar();
      }
    } finally {
      this.salvando.set(false);
    }
  }

  abrirEdicao(funcionario: FuncionarioResumo, evento?: Event): void {
    this.erroEdicao.set(null);
    this.disparadorEdicao = (evento?.currentTarget as HTMLElement | null) ?? null;
    this.edicao.set({
      funcionario,
      ativo: funcionario.ativo,
      perfis: [...funcionario.perfis],
    });

    // Move o foco para o título do diálogo, que é o ponto de entrada do
    // painel, e devolve o foco ao disparador ao fechar.
    this.detector.detectChanges();
    queueMicrotask(() => this.tituloEdicao()?.nativeElement.focus());
  }

  fecharEdicao(): void {
    if (this.salvandoEdicao()) {
      return;
    }

    this.edicao.set(null);
    this.erroEdicao.set(null);
    const disparador = this.disparadorEdicao;
    this.disparadorEdicao = null;
    queueMicrotask(() => {
      if (disparador?.isConnected) {
        disparador.focus();
      }
    });
  }

  aplicarStatus(status: string): void {
    this.statusFiltro.set(status);
    this.pagina.set(1);
    void this.carregar();
  }

  definirAtivoEdicao(evento: Event): void {
    const ativo = (evento.target as HTMLInputElement).checked;
    this.edicao.update((atual) => (atual ? { ...atual, ativo } : atual));
  }

  async salvarEdicao(evento: Event): Promise<void> {
    evento.preventDefault();
    const atual = this.edicao();

    if (!atual) {
      return;
    }

    this.erroEdicao.set(null);

    if (atual.perfis.length === 0) {
      this.erroEdicao.set('Selecione ao menos um perfil fixo.');
      return;
    }

    this.salvandoEdicao.set(true);

    try {
      await firstValueFrom(
        this.http.put<FuncionarioResumo>(
          urlApi(`/api/v1/employees/${atual.funcionario.id}/access`),
          {
            ativo: atual.ativo,
            perfis: atual.perfis,
            tokenConcorrencia: atual.funcionario.tokenConcorrencia,
          },
        ),
      );

      this.sucesso.set(`Acesso de ${atual.funcionario.nome} atualizado.`);
      this.edicao.set(null);
      this.disparadorEdicao = null;
      await this.carregar();
    } catch (erro) {
      this.erroEdicao.set(mensagemDoErro(erro, 'Não foi possível alterar o acesso.'));
      if (erro instanceof HttpErrorResponse && erro.status === 409) {
        await this.carregar();
      }
    } finally {
      this.salvandoEdicao.set(false);
    }
  }

  irParaPagina(novaPagina: number): void {
    if (novaPagina < 1 || novaPagina > this.totalPaginas()) {
      return;
    }

    this.pagina.set(novaPagina);
    void this.carregar();
  }
}
