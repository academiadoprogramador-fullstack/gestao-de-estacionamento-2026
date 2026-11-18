import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, OnInit, effect, inject, signal } from '@angular/core';
import { takeUntil } from 'rxjs/operators';
import { Subject, firstValueFrom } from 'rxjs';
import {
  LucideClipboardList,
  LucideCopy,
  LucideEye,
  LucideFileText,
  LucideLoaderCircle,
  LucideSearch,
  LucideX,
} from '@lucide/angular';
import { urlApi } from '../../../environments/environment';
import { mensagemDoErro, Pagina } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';
import { formatarDataHora, formatarValor } from '../../shared/formato';

interface AtendimentoLista {
  readonly id: string;
  readonly identificador: string;
  readonly placa: string;
  readonly modelo: string;
  readonly titular: string;
  readonly estado: string;
  readonly vagaCodigo: string;
  readonly entradaUtc: string;
}

interface AtendimentoDetalhe extends AtendimentoLista {
  readonly tarifaVersao?: number;
  readonly tarifaValorSnapshot?: number;
}

interface EventoHistorico {
  readonly tipo: string;
  readonly atorOpaco: string;
  readonly ocorridoEmUtc: string;
}

interface LeituraSensivel {
  readonly conteudo: string;
}

type TipoBusca = 'placa' | 'identificador';
type TipoLeitura = 'documento' | 'vistoria';

@Component({
  selector: 'app-atendimentos',
  imports: [
    LucideClipboardList,
    LucideCopy,
    LucideEye,
    LucideFileText,
    LucideLoaderCircle,
    LucideSearch,
    LucideX,
  ],
  templateUrl: './atendimentos.html',
})
export class Atendimentos implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly sessao = inject(SessaoService);
  private readonly destruicao = inject(DestroyRef);

  readonly formatarDataHora = formatarDataHora;
  readonly formatarValor = formatarValor;
  readonly ehOperador = () => this.sessao.possuiPerfil(['Administrador', 'Caixa']);

  readonly tipoBusca = signal<TipoBusca>('placa');
  readonly termo = signal('');
  readonly resultados = signal<readonly AtendimentoLista[]>([]);
  readonly total = signal(0);
  readonly pagina = signal(1);
  readonly tamanhoPagina = 20;
  readonly buscou = signal(false);
  readonly buscando = signal(false);
  readonly erroBusca = signal<string | null>(null);

  readonly selecionado = signal<string | null>(null);
  readonly detalhe = signal<AtendimentoDetalhe | null>(null);
  readonly historico = signal<readonly EventoHistorico[]>([]);
  readonly carregandoDetalhe = signal(false);
  readonly erroDetalhe = signal<string | null>(null);

  readonly leitura = signal<{ tipo: TipoLeitura; conteudo: string } | null>(null);
  readonly lendo = signal<TipoLeitura | null>(null);
  readonly erroLeitura = signal<string | null>(null);
  readonly mensagemCopia = signal<string | null>(null);

  private contexto = 0;
  private sinalAtual: Subject<void> | null = null;

  constructor() {
    effect(() => {
      if (!this.sessao.autenticado()) {
        this.invalidar();
        this.resultados.set([]);
        this.selecionado.set(null);
        this.detalhe.set(null);
        this.historico.set([]);
        this.leitura.set(null);
      }
    });

    this.destruicao.onDestroy(() => {
      this.invalidar();
      this.resultados.set([]);
      this.detalhe.set(null);
      this.historico.set([]);
      this.leitura.set(null);
    });
  }

  ngOnInit(): void {
    // Nenhuma consulta automática: a busca exige exatamente um filtro.
  }

  selecionarTipo(tipo: TipoBusca): void {
    this.tipoBusca.set(tipo);
    this.termo.set('');
    this.erroBusca.set(null);
  }

  async buscar(evento?: Event): Promise<void> {
    evento?.preventDefault();
    const termo = this.termo().trim();
    this.erroBusca.set(null);

    if (!termo) {
      this.erroBusca.set(
        this.tipoBusca() === 'placa'
          ? 'Informe uma placa para buscar.'
          : 'Informe um identificador para buscar.',
      );
      return;
    }

    const token = this.iniciarContexto();
    const sinal = this.sinalAtual!;
    this.buscando.set(true);
    this.buscou.set(true);
    this.iniciarFechamentoDetalhe();

    try {
      const parametros = new URLSearchParams({
        page: String(this.pagina()),
        pageSize: String(this.tamanhoPagina),
      });

      if (this.tipoBusca() === 'placa') {
        parametros.set('plate', termo);
      } else {
        parametros.set('identifier', termo);
      }

      const resposta = await firstValueFrom(
        this.http
          .get<Pagina<AtendimentoLista>>(urlApi(`/api/v1/attendances?${parametros}`))
          .pipe(takeUntil(sinal)),
      );

      if (token !== this.contexto) {
        return;
      }

      this.resultados.set(resposta.items);
      this.total.set(resposta.total);
    } catch (erro) {
      if (token === this.contexto) {
        this.erroBusca.set(mensagemDoErro(erro, 'Não foi possível buscar atendimentos.'));
      }
    } finally {
      if (token === this.contexto) {
        this.buscando.set(false);
      }
    }
  }

  async selecionar(id: string): Promise<void> {
    const token = this.iniciarContexto();
    const sinal = this.sinalAtual!;
    this.selecionado.set(id);
    this.detalhe.set(null);
    this.historico.set([]);
    this.leitura.set(null);
    this.erroDetalhe.set(null);
    this.erroLeitura.set(null);
    this.mensagemCopia.set(null);
    this.carregandoDetalhe.set(true);

    try {
      const [detalhe, historico] = await Promise.all([
        firstValueFrom(
          this.http
            .get<AtendimentoDetalhe>(urlApi(`/api/v1/attendances/${id}`))
            .pipe(takeUntil(sinal)),
        ),
        firstValueFrom(
          this.http
            .get<Pagina<EventoHistorico>>(
              urlApi(`/api/v1/attendances/${id}/history?page=1&pageSize=20`),
            )
            .pipe(takeUntil(sinal)),
        ),
      ]);

      if (token !== this.contexto) {
        return;
      }

      this.detalhe.set(detalhe);
      this.historico.set(historico.items);
    } catch (erro) {
      if (token === this.contexto) {
        this.erroDetalhe.set(mensagemDoErro(erro, 'Não foi possível carregar o atendimento.'));
      }
    } finally {
      if (token === this.contexto) {
        this.carregandoDetalhe.set(false);
      }
    }
  }

  fechar(): void {
    this.invalidar();
    this.iniciarFechamentoDetalhe();
  }

  async ler(tipo: TipoLeitura): Promise<void> {
    const id = this.selecionado();
    if (!id) {
      return;
    }

    const token = this.iniciarContexto();
    const sinal = this.sinalAtual!;
    this.lendo.set(tipo);
    this.erroLeitura.set(null);
    this.leitura.set(null);
    this.mensagemCopia.set(null);

    const caminho =
      tipo === 'documento'
        ? `/api/v1/attendances/${id}/document`
        : `/api/v1/attendances/${id}/entry-inspection`;

    try {
      const resposta = await firstValueFrom(
        this.http.get<LeituraSensivel>(urlApi(caminho)).pipe(takeUntil(sinal)),
      );

      if (token !== this.contexto) {
        return;
      }

      this.leitura.set({ tipo, conteudo: resposta.conteudo });
    } catch (erro) {
      if (token === this.contexto) {
        this.erroLeitura.set(mensagemDoErro(erro, 'Não foi possível ler o conteúdo solicitado.'));
      }
    } finally {
      if (token === this.contexto) {
        this.lendo.set(null);
      }
    }
  }

  async copiarIdentificador(identificador: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(identificador);
      this.mensagemCopia.set('Identificador copiado.');
    } catch {
      this.mensagemCopia.set(
        'Não foi possível copiar automaticamente. Selecione o identificador em texto e copie manualmente.',
      );
    }
  }

  irParaPagina(novaPagina: number): void {
    const totalPaginas = Math.max(1, Math.ceil(this.total() / this.tamanhoPagina));
    if (novaPagina < 1 || novaPagina > totalPaginas) {
      return;
    }

    this.pagina.set(novaPagina);
    void this.buscar();
  }

  private iniciarContexto(): number {
    this.contexto += 1;
    this.sinalAtual?.next();
    this.sinalAtual = new Subject<void>();
    return this.contexto;
  }

  private invalidar(): void {
    this.contexto += 1;
    this.sinalAtual?.next();
    this.sinalAtual = null;
  }

  private iniciarFechamentoDetalhe(): void {
    this.selecionado.set(null);
    this.detalhe.set(null);
    this.historico.set([]);
    this.leitura.set(null);
    this.erroDetalhe.set(null);
    this.erroLeitura.set(null);
  }
}
