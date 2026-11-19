import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import {
  LucideCircleAlert,
  LucideLoaderCircle,
  LucidePlus,
  LucideRefreshCw,
  LucideSquareParking,
} from '@lucide/angular';
import { urlApi, ambiente } from '../../../environments/environment';
import { mensagemDoErro, Pagina } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';
import { PollingVagasService } from './polling-vagas.service';

interface VagaItem {
  readonly id: string;
  readonly codigo: string;
  readonly situacao: 'livre' | 'ocupada';
  readonly tokenConcorrencia: string;
}

interface ModeloCadastro {
  codigo: string;
}

@Component({
  selector: 'app-vagas',
  imports: [
    FormField,
    LucideCircleAlert,
    LucideLoaderCircle,
    LucidePlus,
    LucideRefreshCw,
    LucideSquareParking,
  ],
  templateUrl: './vagas.html',
})
export class Vagas implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly sessao = inject(SessaoService);
  private readonly polling = inject(PollingVagasService);
  private readonly detector = inject(ChangeDetectorRef);

  readonly ehAdministrador = computed(() => this.sessao.possuiPerfil(['Administrador']));
  readonly intervaloSegundos = ambiente.intervaloPollingSegundos;
  readonly codigoFiltro = signal('');
  readonly situacaoFiltro = signal('');
  readonly pagina = signal(1);
  readonly tamanhoPagina = 20;

  readonly itens = signal<readonly VagaItem[]>([]);
  readonly total = signal(0);
  readonly carregandoInicial = signal(true);
  readonly atualizando = signal(false);
  readonly erro = signal<string | null>(null);
  readonly sucesso = signal<string | null>(null);

  readonly modelo = signal<ModeloCadastro>({ codigo: '' });
  readonly formulario = form(this.modelo, (campo) => {
    required(campo.codigo, { message: 'Informe o código da vaga.' });
    maxLength(campo.codigo, 32, { message: 'O código deve ter até 32 caracteres.' });
  });
  readonly resumoErro = viewChild<ElementRef<HTMLDivElement>>('resumoErro');
  readonly resumoVisivel = signal(false);
  readonly salvando = signal(false);

  private consultaEmAndamento = false;
  private recargaPendente = false;

  readonly totalPaginas = computed(() => Math.max(1, Math.ceil(this.total() / this.tamanhoPagina)));

  ngOnInit(): void {
    void this.carregar({ inicial: true });
    this.polling.iniciar(
      () => this.carregar({ silencioso: true }),
      ambiente.intervaloPollingSegundos * 1000,
    );
  }

  ngOnDestroy(): void {
    this.polling.parar();
  }

  async carregar(opcoes: { inicial?: boolean; silencioso?: boolean } = {}): Promise<void> {
    if (this.consultaEmAndamento) {
      // Mantém uma única recarga pendente, sem empilhar consultas. Isso cobre
      // o comando confirmado durante uma carga inicial ou um ciclo de polling.
      this.recargaPendente = true;
      return;
    }

    this.consultaEmAndamento = true;
    if (opcoes.inicial) {
      this.carregandoInicial.set(true);
    } else if (!opcoes.silencioso) {
      this.atualizando.set(true);
    }

    try {
      const parametros = new URLSearchParams({
        page: String(this.pagina()),
        pageSize: String(this.tamanhoPagina),
      });

      if (this.codigoFiltro().trim()) {
        parametros.set('code', this.codigoFiltro().trim());
      }

      if (this.situacaoFiltro()) {
        parametros.set('status', this.situacaoFiltro());
      }

      const resposta = await firstValueFrom(
        this.http.get<Pagina<VagaItem>>(urlApi(`/api/v1/spaces?${parametros.toString()}`)),
      );

      this.itens.set(resposta.items);
      this.total.set(resposta.total);
      this.erro.set(null);
    } catch (erro) {
      // A falha preserva filtros, página e itens já exibidos.
      this.erro.set(mensagemDoErro(erro, 'Não foi possível atualizar a lista de vagas.'));
    } finally {
      this.consultaEmAndamento = false;
      this.carregandoInicial.set(false);
      this.atualizando.set(false);

      if (this.recargaPendente) {
        this.recargaPendente = false;
        void this.carregar({ silencioso: true });
      }
    }
  }

  aplicarFiltros(evento: Event): void {
    evento.preventDefault();
    this.pagina.set(1);
    void this.carregar();
  }

  limparFiltros(): void {
    this.codigoFiltro.set('');
    this.situacaoFiltro.set('');
    this.pagina.set(1);
    void this.carregar();
  }

  atualizarAgora(): void {
    void this.polling.atualizarAgora();
  }

  irParaPagina(pagina: number): void {
    if (pagina < 1 || pagina > this.totalPaginas()) {
      return;
    }

    this.pagina.set(pagina);
    void this.carregar();
  }

  async cadastrar(evento: Event): Promise<void> {
    evento.preventDefault();
    this.formulario.codigo().markAsTouched();
    this.erro.set(null);
    this.sucesso.set(null);

    if (this.formulario().invalid()) {
      this.resumoVisivel.set(true);
      this.detector.detectChanges();
      queueMicrotask(() => this.resumoErro()?.nativeElement.focus());
      return;
    }

    if (!this.ehAdministrador()) {
      this.erro.set('Seu perfil não permite cadastrar vagas.');
      return;
    }

    this.salvando.set(true);

    try {
      const criada = await firstValueFrom(
        this.http.post<VagaItem>(urlApi('/api/v1/spaces'), {
          codigo: this.formulario.codigo().value(),
        }),
      );

      this.sucesso.set(`Vaga ${criada.codigo} cadastrada.`);
      this.modelo.set({ codigo: '' });
      this.resumoVisivel.set(false);
      this.formulario.codigo().reset();
      this.pagina.set(1);
      await this.polling.notificarComando();
    } catch (erro) {
      this.erro.set(mensagemDoErro(erro, 'Não foi possível cadastrar a vaga.'));
      if (erro instanceof HttpErrorResponse && erro.status === 409) {
        await this.carregar({ silencioso: true });
      }
    } finally {
      this.salvando.set(false);
    }
  }
}
