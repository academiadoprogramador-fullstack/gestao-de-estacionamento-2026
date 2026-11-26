import { HttpClient } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  LucideCamera,
  LucideEye,
  LucideImage,
  LucideRefreshCw,
  LucideUpload,
  LucideX,
} from '@lucide/angular';
import { urlApi } from '../../../environments/environment';
import { mensagemDoErro } from '../../core/http/erro-api';
import { CoordenadorComandoService } from '../../features/check-in/coordenador-comando';
import { formatarDataHora } from '../formato';
import { FotoVistoriaResumo, ListaFotosVistoria, OrcamentoFotos } from './fotos-vistoria.tipos';

type TipoFoto = 'entrada' | 'saida';

type ConteudoUpload = {
  readonly arquivo: File;
  readonly versaoAtendimento: string;
};

/**
 * Seção de fotos opcionais de uma vistoria. A lista é carregada por ação
 * explícita; metadados e contagem vêm do detalhe. O conteúdo é lido pela API
 * em blob temporário, sem chave ou URL do armazenamento.
 */
@Component({
  selector: 'app-fotos-vistoria',
  imports: [
    LucideCamera,
    LucideEye,
    LucideImage,
    LucideRefreshCw,
    LucideUpload,
    LucideX,
  ],
  templateUrl: './fotos-vistoria.html',
})
export class FotosVistoria {
  private readonly http = inject(HttpClient);
  private readonly destruicao = inject(DestroyRef);
  readonly coordenador = inject(CoordenadorComandoService);
  readonly formatarDataHora = formatarDataHora;

  readonly atendimentoId = input.required<string>();
  readonly tipo = input.required<TipoFoto>();
  readonly versaoAtendimento = input('');
  readonly podeEnviar = input(true);
  readonly quantidadeInicial = input(0);
  readonly janelaInicial = input<'aberta' | 'encerrada'>('encerrada');
  readonly orcamentoInicial = input<OrcamentoFotos | null>(null);
  readonly titulo = input('Fotos da vistoria');
  readonly quantidadeAtualizada = output<number>();

  readonly itens = signal<readonly FotoVistoriaResumo[]>([]);
  readonly quantidade = signal(0);
  readonly janela = signal<'aberta' | 'encerrada'>('encerrada');
  readonly orcamento = signal<OrcamentoFotos | null>(null);
  readonly carregado = signal(false);
  readonly carregando = signal(false);
  readonly erro = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);

  readonly arquivo = signal<File | null>(null);
  readonly erroEnvio = signal<string | null>(null);
  readonly enviando = signal(false);

  readonly fotoAberta = signal<FotoVistoriaResumo | null>(null);
  readonly urlFoto = signal<string | null>(null);
  readonly lendo = signal(false);
  readonly erroLeitura = signal<string | null>(null);

  readonly podeEnviarAgora = computed(
    () =>
      this.podeEnviar() &&
      this.janela() === 'aberta' &&
      Boolean(this.versaoAtendimento()) &&
      !this.coordenador.bloqueado(),
  );

  readonly incerto = computed(() => this.coordenador.fase() === 'incerto');

  private ativo = true;

  constructor() {
    effect(() => {
      this.quantidade.set(this.quantidadeInicial());
      this.janela.set(this.janelaInicial());
      this.orcamento.set(this.orcamentoInicial());
    });

    this.destruicao.onDestroy(() => {
      this.ativo = false;
      this.revogarUrl();
    });
  }

  async consultar(): Promise<void> {
    if (this.carregando()) {
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);

    try {
      const lista = await firstValueFrom(
        this.http.get<ListaFotosVistoria>(
          urlApi(`/api/v1/attendances/${this.atendimentoId()}/inspections/${this.tipo()}/photos`),
        ),
      );

      if (!this.ativo) {
        return;
      }

      this.aplicarLista(lista);
    } catch (erro) {
      if (this.ativo) {
        this.erro.set(mensagemDoErro(erro, 'Não foi possível consultar as fotos da vistoria.'));
      }
    } finally {
      if (this.ativo) {
        this.carregando.set(false);
      }
    }
  }

  selecionarArquivo(evento: Event): void {
    const entrada = evento.target as HTMLInputElement;
    const arquivo = entrada.files?.[0] ?? null;
    this.arquivo.set(null);
    this.erroEnvio.set(null);
    this.aviso.set(null);

    if (!arquivo) {
      return;
    }

    const aceitos = ['image/jpeg', 'image/png', 'image/webp'];
    if (!aceitos.includes(arquivo.type)) {
      this.erroEnvio.set(
        'Formato não aceito. Envie JPEG, PNG ou WebP estático; SVG, animação e arquivo corrompido são recusados.',
      );
      entrada.value = '';
      return;
    }

    if (arquivo.size > 5 * 1024 * 1024) {
      this.erroEnvio.set('Arquivo acima de 5 MiB. A vistoria textual continua válida sem esta foto.');
      entrada.value = '';
      return;
    }

    this.arquivo.set(arquivo);
  }

  async enviar(evento: Event): Promise<void> {
    evento.preventDefault();
    const arquivo = this.arquivo();

    if (!arquivo) {
      this.erroEnvio.set('Selecione uma foto JPEG, PNG ou WebP para enviar.');
      return;
    }

    if (!this.podeEnviarAgora()) {
      this.erroEnvio.set(
        'A janela ou o armazenamento não permite novo anexo. A vistoria textual continua disponível.',
      );
      return;
    }

    this.enviando.set(true);
    this.erroEnvio.set(null);
    this.aviso.set(null);

    try {
      await this.coordenador.executar<ConteudoUpload, unknown>({
        arquivo,
        versaoAtendimento: this.versaoAtendimento(),
      }, (chave, conteudo) => this.enviarParaApi(chave, conteudo));

      this.arquivo.set(null);
      this.aviso.set('Foto confirmada. Metadados e vínculo registrados; o texto não foi alterado.');
      await this.consultar();
    } catch (erro) {
      this.erroEnvio.set(mensagemDoErro(erro, 'Não foi possível anexar a foto.'));
    } finally {
      this.enviando.set(false);
    }
  }

  async repetir(): Promise<void> {
    if (!this.incerto()) {
      return;
    }

    this.enviando.set(true);
    this.erroEnvio.set(null);

    try {
      await this.coordenador.repetir<ConteudoUpload, unknown>((chave, conteudo) =>
        this.enviarParaApi(chave, conteudo));
      this.arquivo.set(null);
      this.aviso.set('Repetição confirmada com a mesma chave e o mesmo conteúdo.');
      await this.consultar();
    } catch (erro) {
      this.erroEnvio.set(mensagemDoErro(erro, 'Não foi possível repetir a tentativa de foto.'));
    } finally {
      this.enviando.set(false);
    }
  }

  descartar(): void {
    this.coordenador.descartar();
    this.aviso.set('Tentativa descartada da tela. O resultado no servidor permanece reconciliável.');
  }

  async ler(foto: FotoVistoriaResumo): Promise<void> {
    if (this.lendo()) {
      return;
    }

    this.lendo.set(true);
    this.erroLeitura.set(null);
    this.revogarUrl();

    try {
      const blob = await firstValueFrom(
        this.http.get(
          urlApi(
            `/api/v1/attendances/${this.atendimentoId()}/inspections/${this.tipo()}/photos/${foto.fotoId}/content`,
          ),
          { responseType: 'blob' },
        ),
      );

      if (!this.ativo) {
        return;
      }

      this.urlFoto.set(URL.createObjectURL(blob));
      this.fotoAberta.set(foto);
    } catch (erro) {
      if (this.ativo) {
        this.erroLeitura.set(
          mensagemDoErro(erro, 'Não foi possível ler a foto. Nenhum conteúdo foi exibido.'),
        );
      }
    } finally {
      if (this.ativo) {
        this.lendo.set(false);
      }
    }
  }

  fecharFoto(): void {
    this.fotoAberta.set(null);
    this.revogarUrl();
  }

  fechar(): void {
    this.fotoAberta.set(null);
    this.revogarUrl();
  }

  formatarBytes(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }

    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KiB`;
    }

    return `${(bytes / (1024 * 1024)).toFixed(1)} MiB`;
  }

  private async enviarParaApi(chave: string, conteudo: ConteudoUpload): Promise<unknown> {
    const formulario = new FormData();
    formulario.append('arquivo', conteudo.arquivo, conteudo.arquivo.name);
    formulario.append('versaoAtendimentoEsperada', conteudo.versaoAtendimento);

    return firstValueFrom(
      this.http.post(
        urlApi(
          `/api/v1/attendances/${this.atendimentoId()}/inspections/${this.tipo()}/photos`,
        ),
        formulario,
        { headers: { 'Idempotency-Key': chave } },
      ),
    );
  }

  private aplicarLista(lista: ListaFotosVistoria): void {
    this.itens.set(lista.itens);
    this.quantidade.set(lista.quantidade);
    this.janela.set(lista.janela);
    this.orcamento.set(lista.orcamento);
    this.carregado.set(true);
    this.quantidadeAtualizada.emit(lista.quantidade);
  }

  private revogarUrl(): void {
    const url = this.urlFoto();
    if (url) {
      URL.revokeObjectURL(url);
      this.urlFoto.set(null);
    }
  }
}
