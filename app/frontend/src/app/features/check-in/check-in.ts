import { HttpClient } from '@angular/common/http';
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
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import {
  LucideCalendarClock,
  LucideCheck,
  LucideCircleAlert,
  LucideClipboard,
  LucideCopy,
  LucideLoaderCircle,
  LucideRefreshCw,
  LucideRotateCcw,
} from '@lucide/angular';
import { urlApi } from '../../../environments/environment';
import { mensagemDoErro, Pagina } from '../../core/http/erro-api';
import { formatarDataHora } from '../../shared/formato';
import { CoordenadorComandoService } from './coordenador-comando';

interface VagaOpcao {
  readonly id: string;
  readonly codigo: string;
  readonly situacao: string;
  readonly tokenConcorrencia: string;
}

interface ResultadoEntrada {
  readonly atendimentoId: string;
  readonly identificador: string;
  readonly vagaId: string;
  readonly vagaCodigo: string;
  readonly estado: string;
  readonly entradaUtc: string;
  readonly location: string;
}

interface ModeloEntrada {
  placa: string;
  modelo: string;
  titular: string;
  documento: string;
  vistoria: string;
  vagaId: string;
}

const MODELO_INICIAL: ModeloEntrada = {
  placa: '',
  modelo: '',
  titular: '',
  documento: '',
  vistoria: '',
  vagaId: '',
};

@Component({
  selector: 'app-check-in',
  imports: [
    FormField,
    LucideCalendarClock,
    LucideCheck,
    LucideCircleAlert,
    LucideClipboard,
    LucideCopy,
    LucideLoaderCircle,
    LucideRefreshCw,
    LucideRotateCcw,
  ],
  templateUrl: './check-in.html',
})
export class CheckIn implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly detector = inject(ChangeDetectorRef);
  readonly coordenador = inject(CoordenadorComandoService);
  readonly formatarDataHora = formatarDataHora;

  readonly modelo = signal<ModeloEntrada>({ ...MODELO_INICIAL });
  readonly formulario = form(this.modelo, (campo) => {
    required(campo.placa, { message: 'Informe a placa.' });
    maxLength(campo.placa, 20, { message: 'A placa deve ter até 20 caracteres com separadores.' });
    required(campo.modelo, { message: 'Informe o modelo.' });
    maxLength(campo.modelo, 120, { message: 'O modelo deve ter até 120 caracteres.' });
    required(campo.titular, { message: 'Informe o nome do titular.' });
    maxLength(campo.titular, 200, { message: 'O titular deve ter até 200 caracteres.' });
    required(campo.documento, { message: 'Informe o documento do titular.' });
    maxLength(campo.documento, 80, { message: 'O documento deve ter até 80 caracteres.' });
    required(campo.vistoria, { message: 'Descreva a vistoria de entrada.' });
    maxLength(campo.vistoria, 4000, { message: 'A vistoria deve ter até 4000 caracteres.' });
    required(campo.vagaId, { message: 'Selecione uma vaga livre.' });
  });

  readonly resumoErro = viewChild<ElementRef<HTMLDivElement>>('resumoErro');
  readonly resumoVisivel = signal(false);

  /** Validação adicional da placa: 1 a 16 caracteres alfanuméricos. */
  readonly placaInvalida = computed(() => {
    const normalizada = this.formulario.placa().value().replace(/[\s-]/g, '');
    return normalizada.length > 0 && !/^[A-Za-z0-9]{1,16}$/.test(normalizada);
  });

  readonly vagas = signal<readonly VagaOpcao[]>([]);
  readonly paginaVagas = signal(1);
  readonly totalVagas = signal(0);
  readonly carregandoVagas = signal(true);
  readonly erroVagas = signal<string | null>(null);
  readonly tamanhoPaginaVagas = 20;

  readonly enviando = signal(false);
  readonly erro = signal<string | null>(null);
  readonly resultado = signal<ResultadoEntrada | null>(null);
  readonly mensagemCopia = signal<string | null>(null);

  readonly totalPaginasVagas = signal(1);

  ngOnInit(): void {
    void this.carregarVagas();
  }

  ngOnDestroy(): void {
    // A navegação só chega aqui após a confirmação do guard de comando
    // pendente; nesse caso a tentativa apenas em memória é descartada.
    if (this.coordenador.bloqueado()) {
      this.coordenador.descartar();
    }
  }

  async carregarVagas(): Promise<void> {
    this.carregandoVagas.set(true);
    this.erroVagas.set(null);

    try {
      const resposta = await firstValueFrom(
        this.http.get<Pagina<VagaOpcao>>(
          urlApi(
            `/api/v1/spaces?status=livre&page=${this.paginaVagas()}&pageSize=${this.tamanhoPaginaVagas}`,
          ),
        ),
      );

      this.vagas.set(resposta.items);
      this.totalVagas.set(resposta.total);
      this.totalPaginasVagas.set(Math.max(1, Math.ceil(resposta.total / this.tamanhoPaginaVagas)));
    } catch (erro) {
      this.erroVagas.set(mensagemDoErro(erro, 'Não foi possível carregar as vagas livres.'));
    } finally {
      this.carregandoVagas.set(false);
    }
  }

  irParaPaginaVagas(pagina: number): void {
    if (pagina < 1 || pagina > this.totalPaginasVagas()) {
      return;
    }

    this.paginaVagas.set(pagina);
    void this.carregarVagas();
  }

  async submeter(evento: Event): Promise<void> {
    evento.preventDefault();
    this.marcarTocados();
    this.erro.set(null);
    this.mensagemCopia.set(null);

    const placa = this.formulario.placa().value();
    const placaNormalizada = placa.replace(/[\s-]/g, '');
    const placaValida = /^[A-Za-z0-9]{1,16}$/.test(placaNormalizada);

    if (this.formulario().invalid() || !placaValida) {
      this.resumoVisivel.set(true);
      this.detector.detectChanges();
      queueMicrotask(() => this.resumoErro()?.nativeElement.focus());
      return;
    }

    if (this.coordenador.bloqueado()) {
      return;
    }

    this.enviando.set(true);
    const conteudo: ModeloEntrada = {
      placa,
      modelo: this.formulario.modelo().value(),
      titular: this.formulario.titular().value(),
      documento: this.formulario.documento().value(),
      vistoria: this.formulario.vistoria().value(),
      vagaId: this.formulario.vagaId().value(),
    };

    try {
      const resultado = await this.coordenador.executar(conteudo, (chave, dados) =>
        this.enviar(chave, dados),
      );
      await this.concluir(resultado);
    } catch (erro) {
      this.tratarFalha(erro);
    } finally {
      this.enviando.set(false);
    }
  }

  async repetir(): Promise<void> {
    if (this.coordenador.fase() !== 'incerto') {
      return;
    }

    this.enviando.set(true);
    this.erro.set(null);

    try {
      const resultado = await this.coordenador.repetir<ModeloEntrada, ResultadoEntrada>(
        (chave, dados) => this.enviar(chave, dados),
      );
      await this.concluir(resultado);
    } catch (erro) {
      this.tratarFalha(erro);
    } finally {
      this.enviando.set(false);
    }
  }

  async copiarIdentificador(): Promise<void> {
    const identificador = this.resultado()?.identificador;
    if (!identificador) {
      return;
    }

    try {
      await navigator.clipboard.writeText(identificador);
      this.mensagemCopia.set('Identificador copiado.');
    } catch {
      this.mensagemCopia.set(
        'Não foi possível copiar automaticamente. Selecione o identificador em texto e copie manualmente.',
      );
    }
  }

  novoCheckIn(): void {
    this.resultado.set(null);
    this.mensagemCopia.set(null);
    this.erro.set(null);
    this.coordenador.descartar();
    void this.carregarVagas();
  }

  private async enviar(chave: string, dados: ModeloEntrada): Promise<ResultadoEntrada> {
    return firstValueFrom(
      this.http.post<ResultadoEntrada>(
        urlApi('/api/v1/attendances'),
        {
          placa: dados.placa,
          modelo: dados.modelo,
          titular: dados.titular,
          documento: dados.documento,
          vistoria: dados.vistoria,
          vagaId: dados.vagaId,
        },
        { headers: { 'Idempotency-Key': chave } },
      ),
    );
  }

  private async concluir(resultado: ResultadoEntrada): Promise<void> {
    this.resultado.set(resultado);
    this.modelo.update((atual) => ({ ...atual, documento: '', vistoria: '' }));
    this.formulario.vagaId().value.set('');
    await this.carregarVagas();
  }

  private tratarFalha(erro: unknown): void {
    this.erro.set(mensagemDoErro(erro, 'Não foi possível registrar a entrada.'));
    if (this.coordenador.fase() === 'incerto') {
      this.erro.set(
        'O resultado do check-in é incerto. Repita a tentativa para reenviar exatamente a mesma chave e o mesmo conteúdo.',
      );
    }
  }

  private marcarTocados(): void {
    this.formulario.placa().markAsTouched();
    this.formulario.modelo().markAsTouched();
    this.formulario.titular().markAsTouched();
    this.formulario.documento().markAsTouched();
    this.formulario.vistoria().markAsTouched();
    this.formulario.vagaId().markAsTouched();
  }
}
