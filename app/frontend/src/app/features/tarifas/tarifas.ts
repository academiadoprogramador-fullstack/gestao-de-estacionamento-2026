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
import { form, FormField, required } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import {
  LucideBadgeDollarSign,
  LucideCircleAlert,
  LucideLoaderCircle,
  LucideReceipt,
} from '@lucide/angular';
import { urlApi } from '../../../environments/environment';
import { formatarValor } from '../../shared/formato';
import { mensagemDoErro, Pagina } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';

interface TarifaCorrente {
  readonly temTarifa: boolean;
  readonly versao: number | null;
  readonly valor: number | null;
  readonly publicadoPorId: string | null;
  readonly publicadoEmUtc: string | null;
  readonly versaoBase: number | null;
}

interface TarifaResumo {
  readonly versao: number;
  readonly valor: number;
  readonly publicadoPorId: string;
  readonly publicadoPorNome: string;
  readonly publicadoEmUtc: string;
}

@Component({
  selector: 'app-tarifas',
  imports: [FormField, LucideBadgeDollarSign, LucideCircleAlert, LucideLoaderCircle, LucideReceipt],
  templateUrl: './tarifas.html',
})
export class Tarifas implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly sessao = inject(SessaoService);
  private readonly detector = inject(ChangeDetectorRef);

  readonly ehAdministrador = computed(() => this.sessao.possuiPerfil(['Administrador']));
  readonly formatarValor = formatarValor;

  readonly corrente = signal<TarifaCorrente | null>(null);
  readonly historico = signal<readonly TarifaResumo[]>([]);
  readonly total = signal(0);
  readonly pagina = signal(1);
  readonly tamanhoPagina = 20;
  readonly carregando = signal(true);
  readonly erro = signal<string | null>(null);
  readonly sucesso = signal<string | null>(null);

  readonly modelo = signal({ valor: '' });
  readonly formulario = form(this.modelo, (campo) => {
    required(campo.valor, { message: 'Informe o valor da diária.' });
  });

  readonly resumoErro = viewChild<ElementRef<HTMLDivElement>>('resumoErro');
  readonly resumoVisivel = signal(false);
  readonly erroValor = signal<string | null>(null);
  readonly publicando = signal(false);

  readonly totalPaginas = computed(() => Math.max(1, Math.ceil(this.total() / this.tamanhoPagina)));

  ngOnInit(): void {
    void this.carregarTudo();
  }

  async carregarTudo(): Promise<void> {
    if (!this.ehAdministrador()) {
      this.carregando.set(false);
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);

    try {
      const [corrente, historico] = await Promise.all([
        firstValueFrom(this.http.get<TarifaCorrente>(urlApi('/api/v1/daily-rates/current'))),
        firstValueFrom(
          this.http.get<Pagina<TarifaResumo>>(
            urlApi(`/api/v1/daily-rates?page=${this.pagina()}&pageSize=${this.tamanhoPagina}`),
          ),
        ),
      ]);

      this.corrente.set(corrente);
      this.historico.set(historico.items);
      this.total.set(historico.total);
    } catch (erro) {
      this.erro.set(mensagemDoErro(erro, 'Não foi possível carregar as tarifas.'));
    } finally {
      this.carregando.set(false);
    }
  }

  async publicar(evento: Event): Promise<void> {
    evento.preventDefault();
    this.formulario.valor().markAsTouched();
    this.erro.set(null);
    this.sucesso.set(null);
    this.erroValor.set(null);

    const valorNormalizado = this.formulario.valor().value().trim().replace(',', '.');
    const valor = Number(valorNormalizado);
    const formatoValido =
      /^\d+(\.\d{1,2})?$/.test(valorNormalizado) && Number.isFinite(valor) && valor > 0;

    if (!formatoValido) {
      this.erroValor.set('Informe um valor positivo, com no máximo duas casas decimais.');
    }

    if (this.formulario().invalid() || !formatoValido) {
      this.resumoVisivel.set(true);
      this.detector.detectChanges();
      queueMicrotask(() => this.resumoErro()?.nativeElement.focus());
      return;
    }

    this.publicando.set(true);

    try {
      const publicada = await firstValueFrom(
        this.http.post<TarifaResumo>(urlApi('/api/v1/daily-rates'), {
          valor,
          versaoBaseEsperada: this.corrente()?.versaoBase ?? null,
        }),
      );

      this.sucesso.set(
        `Versão ${publicada.versao} publicada com ${formatarValor(publicada.valor)}.`,
      );
      this.modelo.set({ valor: '' });
      this.pagina.set(1);
      await this.carregarTudo();
    } catch (erro) {
      this.erro.set(mensagemDoErro(erro, 'Não foi possível publicar a tarifa.'));
      if (erro instanceof HttpErrorResponse && erro.status === 409) {
        // Conflito de versão-base: recarrega a corrente e o histórico sem
        // apagar o valor já digitado.
        await this.carregarTudo();
      }
    } finally {
      this.publicando.set(false);
    }
  }

  irParaPagina(novaPagina: number): void {
    if (novaPagina < 1 || novaPagina > this.totalPaginas()) {
      return;
    }

    this.pagina.set(novaPagina);
    void this.carregarTudo();
  }

  recarregar(): void {
    void this.carregarTudo();
  }
}
