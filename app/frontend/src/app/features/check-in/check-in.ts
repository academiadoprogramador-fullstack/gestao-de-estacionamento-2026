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

type StatusFotoCheckIn = 'selecionada' | 'enviando' | 'confirmada' | 'falhou' | 'incerta';

interface FotoCheckIn {
  readonly id: string;
  readonly arquivo: File;
  readonly nome: string;
  readonly status: StatusFotoCheckIn;
}

interface DetalheAtendimentoFotos {
  readonly versaoAtendimento?: string;
}

interface ConteudoFotoCheckIn {
  readonly foto: FotoCheckIn;
  readonly versaoAtendimento: string;
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

  readonly fotos = signal<readonly FotoCheckIn[]>([]);
  readonly erroFotos = signal<string | null>(null);
  readonly versaoAtendimento = signal<string | null>(null);
  readonly carregandoVersao = signal(false);
  readonly filaFotosAtiva = signal(false);

  readonly fotoEmFalha = computed(
    () => this.fotos().find((foto) => foto.status === 'falhou') ?? null,
  );

  readonly fotoIncerta = computed(
    () =>
      Boolean(this.resultado()) &&
      this.coordenador.fase() === 'incerto' &&
      this.fotos().some((foto) => foto.status === 'incerta'),
  );

  readonly checkInIncerto = computed(
    () => !this.resultado() && this.coordenador.fase() === 'incerto',
  );

  readonly fotosPendentes = computed(
    () => this.fotos().filter((foto) => foto.status === 'selecionada').length,
  );

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

  selecionarFotos(evento: Event): void {
    const entrada = evento.target as HTMLInputElement;
    const arquivos = Array.from(entrada.files ?? []);
    this.erroFotos.set(null);

    if (this.resultado() || this.coordenador.bloqueado()) {
      entrada.value = '';
      return;
    }

    const aceitos = ['image/jpeg', 'image/png', 'image/webp'];
    const limite = 5;
    const novas: FotoCheckIn[] = [];

    for (const arquivo of arquivos) {
      if (this.fotos().length + novas.length >= limite) {
        this.erroFotos.set('O limite de cinco fotos por vistoria foi atingido.');
        break;
      }

      if (!aceitos.includes(arquivo.type)) {
        this.erroFotos.set(
          'Formato não aceito. Envie JPEG, PNG ou WebP estático; SVG, animação e arquivo corrompido são recusados.',
        );
        continue;
      }

      if (arquivo.size > 5 * 1024 * 1024) {
        this.erroFotos.set('Arquivo acima de 5 MiB. O check-in continua possível sem esta foto.');
        continue;
      }

      novas.push({
        id: this.gerarIdFoto(),
        arquivo,
        nome: arquivo.name,
        status: 'selecionada',
      });
    }

    if (novas.length > 0) {
      this.fotos.update((atuais) => [...atuais, ...novas]);
    }

    entrada.value = '';
  }

  removerFoto(id: string): void {
    if (this.resultado() || this.coordenador.bloqueado()) {
      return;
    }

    this.fotos.update((fotos) => fotos.filter((foto) => foto.id !== id));
  }

  async repetirFoto(): Promise<void> {
    if (!this.fotoIncerta()) {
      return;
    }

    const foto = this.fotos().find((item) => item.status === 'incerta');
    if (!foto) {
      return;
    }

    this.erroFotos.set(null);
    this.atualizarStatusFoto(foto.id, 'enviando');

    try {
      await this.coordenador.repetir<ConteudoFotoCheckIn, unknown>((chave, conteudo) =>
        this.enviarFoto(chave, conteudo));
      this.atualizarStatusFoto(foto.id, 'confirmada');
      await this.continuarFila();
    } catch (erro) {
      this.atualizarStatusFoto(
        foto.id,
        this.coordenador.fase() === 'incerto' ? 'incerta' : 'falhou',
      );
      this.erroFotos.set(mensagemDoErro(erro, 'Não foi possível repetir a foto.'));
    }
  }

  async tentarFotoNovamente(): Promise<void> {
    const foto = this.fotoEmFalha();
    if (!foto || this.coordenador.bloqueado()) {
      return;
    }

    this.erroFotos.set(null);
    this.atualizarStatusFoto(foto.id, 'selecionada');
    await this.iniciarFilaFotos();
  }

  async continuarSemFoto(): Promise<void> {
    const foto = this.fotos().find(
      (item) => item.status === 'falhou' || item.status === 'incerta',
    );

    if (!foto) {
      return;
    }

    if (this.coordenador.fase() === 'incerto') {
      this.coordenador.descartar();
    }

    this.fotos.update((fotos) => fotos.filter((item) => item.id !== foto.id));
    this.erroFotos.set(null);
    await this.iniciarFilaFotos();
  }

  async recarregarVersaoEIniciarFotos(): Promise<void> {
    if (!this.resultado() || this.fotos().length === 0) {
      return;
    }

    await this.carregarVersaoEIniciarFotos();
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

    if (this.resultado() || this.coordenador.bloqueado()) {
      return;
    }

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

  rotuloStatusFoto(status: StatusFotoCheckIn): string {
    switch (status) {
      case 'selecionada':
        return 'Aguardando check-in';
      case 'enviando':
        return 'Enviando…';
      case 'confirmada':
        return 'Confirmada';
      case 'incerta':
        return 'Resultado incerto';
      default:
        return 'Falhou';
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
    if (this.coordenador.bloqueado() || this.filaFotosAtiva()) {
      return;
    }

    this.resultado.set(null);
    this.mensagemCopia.set(null);
    this.erro.set(null);
    this.fotos.set([]);
    this.erroFotos.set(null);
    this.versaoAtendimento.set(null);
    this.carregandoVersao.set(false);
    this.coordenador.descartar();
    void this.carregarVagas();
  }

  private async carregarVersaoEIniciarFotos(): Promise<void> {
    const entrada = this.resultado();
    if (!entrada || this.fotos().length === 0) {
      return;
    }

    this.carregandoVersao.set(true);
    this.erroFotos.set(null);

    try {
      const detalhe = await firstValueFrom(
        this.http.get<DetalheAtendimentoFotos>(
          urlApi(`/api/v1/attendances/${entrada.atendimentoId}`),
        ),
      );

      if (!detalhe.versaoAtendimento) {
        this.erroFotos.set(
          'A versão do atendimento não foi retornada. Nenhuma foto foi enviada; a entrada continua registrada.',
        );
        return;
      }

      this.versaoAtendimento.set(detalhe.versaoAtendimento);
      await this.iniciarFilaFotos();
    } catch (erro) {
      this.erroFotos.set(
        mensagemDoErro(
          erro,
          'Não foi possível carregar a versão do atendimento. Nenhuma foto foi enviada; a entrada continua registrada.',
        ),
      );
    } finally {
      this.carregandoVersao.set(false);
    }
  }

  private async iniciarFilaFotos(): Promise<void> {
    if (this.filaFotosAtiva() || this.coordenador.bloqueado()) {
      return;
    }

    const versao = this.versaoAtendimento();
    if (!versao) {
      this.erroFotos.set(
        'A versão do atendimento não está disponível. Recarregue o atendimento antes de enviar as fotos.',
      );
      return;
    }

    this.filaFotosAtiva.set(true);
    this.erroFotos.set(null);

    try {
      while (true) {
        const proxima = this.fotos().find((foto) => foto.status === 'selecionada');
        if (!proxima) {
          break;
        }

        this.atualizarStatusFoto(proxima.id, 'enviando');

        try {
          await this.coordenador.executar<ConteudoFotoCheckIn, unknown>(
            { foto: proxima, versaoAtendimento: versao },
            (chave, conteudo) => this.enviarFoto(chave, conteudo),
          );
          this.atualizarStatusFoto(proxima.id, 'confirmada');
        } catch (erro) {
          this.atualizarStatusFoto(
            proxima.id,
            this.coordenador.fase() === 'incerto' ? 'incerta' : 'falhou',
          );
          this.erroFotos.set(mensagemDoErro(erro, 'Não foi possível enviar a foto selecionada.'));
          break;
        }
      }
    } finally {
      this.filaFotosAtiva.set(false);
    }
  }

  private async continuarFila(): Promise<void> {
    if (this.coordenador.bloqueado()) {
      return;
    }

    await this.iniciarFilaFotos();
  }

  private enviarFoto(chave: string, conteudo: ConteudoFotoCheckIn): Promise<unknown> {
    const entrada = this.resultado();
    if (!entrada) {
      return Promise.reject(new Error('check_in_ausente'));
    }

    const formulario = new FormData();
    formulario.append('arquivo', conteudo.foto.arquivo, conteudo.foto.nome);
    formulario.append('versaoAtendimentoEsperada', conteudo.versaoAtendimento);

    return firstValueFrom(
      this.http.post(
        urlApi(`/api/v1/attendances/${entrada.atendimentoId}/inspections/entrada/photos`),
        formulario,
        { headers: { 'Idempotency-Key': chave } },
      ),
    );
  }

  private atualizarStatusFoto(id: string, status: StatusFotoCheckIn): void {
    this.fotos.update((fotos) =>
      fotos.map((foto) => (foto.id === id ? { ...foto, status } : foto)),
    );
  }

  private gerarIdFoto(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
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
    // O sucesso não é um erro: limpa o resumo e o estado “touched” para que a
    // validação não reapareça enquanto o formulário aguarda outra entrada.
    this.resumoVisivel.set(false);
    this.formulario.placa().reset();
    this.formulario.modelo().reset();
    this.formulario.titular().reset();
    this.formulario.documento().reset();
    this.formulario.vistoria().reset();
    this.formulario.vagaId().reset();
    await this.carregarVagas();
    await this.carregarVersaoEIniciarFotos();
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
