import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { ActivatedRoute } from '@angular/router';
import {
  AdminApiService,
  AttendanceDetail,
  AttendanceHistoryItem,
  AttendanceItem,
} from '../admin/admin-api.service';
import { AuthService } from '../auth/auth.service';

@Component({
  standalone: true,
  selector: 'app-attendances-page',
  imports: [FormField, DecimalPipe],
  template: `
    <section class="space-y-6" aria-labelledby="attendances-title">
      <header>
        <h1 id="attendances-title" class="text-2xl font-semibold">Atendimentos</h1>
        <p class="mt-2 text-slate-600">Busque pela placa ou pelo identificador público exato.</p>
      </header>
      @if (error()) {
        <p role="alert" class="rounded border border-red-300 bg-red-50 p-3 text-red-800">
          {{ error() }}
        </p>
      }
      @if (message()) {
        <p
          role="status"
          aria-live="polite"
          class="rounded border border-emerald-300 bg-emerald-50 p-3 text-emerald-900"
        >
          {{ message() }}
        </p>
      }
      <form class="flex flex-wrap items-end gap-3" (submit)="search($event)" novalidate>
        <label class="grid gap-1 text-sm"
          >Placa<input
            class="rounded border border-slate-300 px-3 py-2"
            [formField]="queryForm.plate"
            autocomplete="off"
            [attr.aria-invalid]="queryForm.plate().invalid() ? 'true' : null"
        /></label>
        <label class="grid gap-1 text-sm"
          >Identificador público<input
            class="rounded border border-slate-300 px-3 py-2"
            [formField]="queryForm.identifier"
            autocomplete="off"
            [attr.aria-invalid]="queryForm.identifier().invalid() ? 'true' : null"
        /></label>
        <button
          class="rounded bg-slate-900 px-4 py-2 font-medium text-white disabled:opacity-50"
          [disabled]="busy()"
        >
          {{ busy() ? 'Buscando…' : 'Buscar' }}
        </button>
      </form>
      @if (queryErrors().length) {
        <ul
          role="alert"
          aria-label="Resumo de erros"
          class="list-disc pl-5 text-sm text-red-700"
          id="attendance-error-summary"
          tabindex="-1"
        >
          @for (item of queryErrors(); track item) {
            <li>{{ item }}</li>
          }
        </ul>
      }
      <section aria-labelledby="results-title">
        <h2 id="results-title" class="text-lg font-semibold">Resultados</h2>
        @if (searched() && !items().length && !busy()) {
          <p class="mt-2 text-slate-600" role="status">Nenhum atendimento encontrado.</p>
        }
        @if (items().length) {
          <ul class="mt-2 divide-y divide-slate-200 rounded border border-slate-200">
            @for (item of items(); track item.id) {
              <li class="flex flex-wrap items-center justify-between gap-3 p-3">
                <div>
                  <p class="font-medium">
                    {{ item.publicIdentifier }} · {{ item.plate }} · {{ item.model }}
                  </p>
                  <p class="text-sm text-slate-600">
                    {{ item.parkingSpaceCode }} · {{ item.state }} · {{ item.enteredAtUtc }}
                  </p>
                </div>
                <button
                  class="rounded border border-slate-400 px-3 py-2 font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                  (click)="open(item)"
                >
                  Abrir detalhe
                </button>
              </li>
            }
          </ul>
          <nav class="mt-3 flex items-center gap-3" aria-label="Paginação dos resultados">
            <button
              class="rounded border px-3 py-2 disabled:opacity-50"
              [disabled]="page() <= 1 || busy()"
              (click)="searchPage(page() - 1)"
            >
              Anterior
            </button>
            <span aria-live="polite">Página {{ page() }} · {{ total() }} atendimento(s)</span>
            <button
              class="rounded border px-3 py-2 disabled:opacity-50"
              [disabled]="page() * 20 >= total() || busy()"
              (click)="searchPage(page() + 1)"
            >
              Próxima
            </button>
          </nav>
        }
      </section>
      @if (detail(); as selected) {
        <section
          class="space-y-4 rounded-xl border border-slate-300 p-4"
          aria-labelledby="detail-title"
          aria-live="polite"
        >
          <div class="flex flex-wrap items-start justify-between gap-2">
            <h2 id="detail-title" class="text-lg font-semibold">
              Detalhe · {{ selected.publicIdentifier }}
            </h2>
            <button class="rounded border px-3 py-2" (click)="closeDetail()">Fechar detalhe</button>
          </div>
          <dl class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <div>
              <dt class="text-sm text-slate-600">Placa</dt>
              <dd>{{ selected.plate }}</dd>
            </div>
            <div>
              <dt class="text-sm text-slate-600">Modelo</dt>
              <dd>{{ selected.model }}</dd>
            </div>
            <div>
              <dt class="text-sm text-slate-600">Titular</dt>
              <dd>{{ selected.ownerName }}</dd>
            </div>
            <div>
              <dt class="text-sm text-slate-600">Vaga</dt>
              <dd>{{ selected.parkingSpaceCode }}</dd>
            </div>
            <div>
              <dt class="text-sm text-slate-600">Estado</dt>
              <dd>{{ selected.state }}</dd>
            </div>
            <div>
              <dt class="text-sm text-slate-600">Entrada (UTC)</dt>
              <dd>{{ selected.enteredAtUtc }}</dd>
            </div>
            @if (selected.dailyRateSnapshot !== undefined) {
              <div>
                <dt class="text-sm text-slate-600">Tarifa diária registrada</dt>
                <dd>{{ selected.dailyRateSnapshot | number: '1.2-2' }}</dd>
              </div>
            }
          </dl>
          <div class="flex flex-wrap gap-2">
            <button
              class="rounded border border-slate-500 px-3 py-2"
              (click)="readInspection()"
              [disabled]="sensitiveBusy()"
            >
              Ler vistoria de entrada
            </button>
            @if (canReadDocument()) {
              <button
                class="rounded border border-slate-500 px-3 py-2"
                (click)="readDocument()"
                [disabled]="sensitiveBusy()"
              >
                Ler documento do titular
              </button>
            }
          </div>
          @if (sensitiveValue()) {
            <section class="rounded bg-amber-50 p-3" aria-live="polite">
              <h3 class="font-semibold">{{ sensitiveLabel() }}</h3>
              <p class="mt-1 whitespace-pre-wrap">{{ sensitiveValue() }}</p>
            </section>
          }
          <section aria-labelledby="history-title">
            <h3 id="history-title" class="font-semibold">Histórico de eventos</h3>
            @if (!history().length) {
              <p class="mt-2 text-slate-600" role="status">
                Nenhum evento de histórico disponível.
              </p>
            }
            @if (history().length) {
              <ol class="mt-2 divide-y divide-slate-200">
                @for (event of history(); track $index) {
                  <li class="py-2">
                    <span class="font-medium">{{ event.action }}</span> ·
                    {{ event.occurredAtUtc }} · operador {{ event.actorId }}
                  </li>
                }
              </ol>
              <nav class="mt-2 flex items-center gap-3" aria-label="Paginação do histórico">
                <button
                  class="rounded border px-3 py-2 disabled:opacity-50"
                  [disabled]="historyPage() <= 1 || historyBusy()"
                  (click)="loadHistory(historyPage() - 1)"
                >
                  Anterior
                </button>
                <span aria-live="polite"
                  >Página {{ historyPage() }} · {{ historyTotal() }} evento(s)</span
                >
                <button
                  class="rounded border px-3 py-2 disabled:opacity-50"
                  [disabled]="historyPage() * 20 >= historyTotal() || historyBusy()"
                  (click)="loadHistory(historyPage() + 1)"
                >
                  Próxima
                </button>
              </nav>
            }
          </section>
        </section>
      }
    </section>
  `,
})
export class AttendancesPage implements OnInit, OnDestroy {
  private readonly api = inject(AdminApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private destroyed = false;
  private searchRevision = 0;
  private detailRevision = 0;
  private historyRevision = 0;
  readonly query = signal({ plate: '', identifier: '' });
  readonly queryForm = form(this.query, (path) => {
    maxLength(path.plate, 80);
    maxLength(path.identifier, 40);
  });
  readonly queryErrors = signal<string[]>([]);
  readonly items = signal<AttendanceItem[]>([]);
  readonly detail = signal<AttendanceDetail | null>(null);
  readonly history = signal<AttendanceHistoryItem[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly historyPage = signal(1);
  readonly historyTotal = signal(0);
  readonly busy = signal(false);
  readonly historyBusy = signal(false);
  readonly sensitiveBusy = signal(false);
  readonly searched = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly sensitiveValue = signal('');
  readonly sensitiveLabel = signal('');
  readonly canReadDocument = () =>
    this.auth.session()?.roles.some((role) => role === 'caixa' || role === 'admin') ?? false;

  ngOnInit(): void {
    const identifier = this.route.snapshot.queryParamMap.get('identifier');
    if (identifier) {
      this.query.update((value) => ({ ...value, identifier }));
      queueMicrotask(() => void this.searchPage(1));
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    ++this.searchRevision;
    this.closeDetail();
  }

  async search(event: Event): Promise<void> {
    event.preventDefault();
    this.queryErrors.set([]);
    if (this.queryForm.plate().invalid() || this.queryForm.identifier().invalid()) {
      this.queryErrors.set(['O critério de busca excede o tamanho permitido.']);
      setTimeout(() => document.getElementById('attendance-error-summary')?.focus(), 0);
      return;
    }
    const query = this.query();
    if (!query.plate.trim() && !query.identifier.trim()) {
      this.queryErrors.set(['Informe uma placa ou um identificador público.']);
      setTimeout(() => document.getElementById('attendance-error-summary')?.focus(), 0);
      return;
    }
    if (query.plate.trim() && query.identifier.trim()) {
      this.queryErrors.set(['Informe apenas um critério de busca por vez.']);
      setTimeout(() => document.getElementById('attendance-error-summary')?.focus(), 0);
      return;
    }
    await this.searchPage(1);
  }

  async searchPage(page: number): Promise<void> {
    const query = this.query();
    if (this.destroyed) return;
    const revision = ++this.searchRevision;
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.closeDetail();
    this.items.set([]);
    if (!query.plate.trim() && !query.identifier.trim()) {
      this.busy.set(false);
      this.error.set('Informe uma placa ou identificador para buscar.');
      return;
    }
    try {
      const response = await this.api.searchAttendances(query.plate, query.identifier, page);
      if (this.destroyed || revision !== this.searchRevision) return;
      this.items.set(response.items);
      this.page.set(response.page);
      this.total.set(response.total);
      this.searched.set(true);
    } catch (error) {
      if (!this.destroyed && revision === this.searchRevision)
        this.error.set(this.apiError(error, 'Não foi possível buscar atendimentos.'));
    } finally {
      if (!this.destroyed && revision === this.searchRevision) this.busy.set(false);
    }
  }

  async open(item: AttendanceItem): Promise<void> {
    if (this.destroyed) return;
    ++this.searchRevision;
    this.busy.set(false);
    this.closeDetail();
    const revision = this.detailRevision;
    this.error.set('');
    this.message.set('');
    this.sensitiveValue.set('');
    this.detail.set(null);
    this.history.set([]);
    try {
      const [detail, history] = await Promise.all([
        this.api.getAttendance(item.id),
        this.api.getAttendanceHistory(item.id, 1),
      ]);
      if (!this.isCurrentDetail(revision)) return;
      this.detail.set(detail);
      this.history.set(history.items);
      this.historyPage.set(history.page);
      this.historyTotal.set(history.total);
    } catch (error) {
      if (this.isCurrentDetail(revision))
        this.error.set(this.apiError(error, 'Não foi possível abrir o detalhe e histórico.'));
    }
  }

  closeDetail(): void {
    ++this.detailRevision;
    ++this.historyRevision;
    this.detail.set(null);
    this.history.set([]);
    this.sensitiveValue.set('');
    this.sensitiveLabel.set('');
    this.sensitiveBusy.set(false);
    this.historyBusy.set(false);
  }

  async loadHistory(page: number): Promise<void> {
    const detail = this.detail();
    if (!detail || this.destroyed) return;
    const selection = this.detailRevision;
    const revision = ++this.historyRevision;
    this.historyBusy.set(true);
    this.error.set('');
    try {
      const response = await this.api.getAttendanceHistory(detail.id, page);
      if (!this.isCurrentDetail(selection) || revision !== this.historyRevision) return;
      this.history.set(response.items);
      this.historyPage.set(response.page);
      this.historyTotal.set(response.total);
    } catch (error) {
      if (this.isCurrentDetail(selection) && revision === this.historyRevision)
        this.error.set(this.apiError(error, 'Não foi possível carregar o histórico.'));
    } finally {
      if (this.isCurrentDetail(selection) && revision === this.historyRevision)
        this.historyBusy.set(false);
    }
  }

  async readDocument(): Promise<void> {
    if (!this.canReadDocument()) return;
    await this.readSensitive('document', 'Documento do titular');
  }

  async readInspection(): Promise<void> {
    await this.readSensitive('entry-inspection', 'Vistoria de entrada');
  }

  private async readSensitive(
    field: 'document' | 'entry-inspection',
    label: string,
  ): Promise<void> {
    const detail = this.detail();
    if (!detail || this.sensitiveBusy() || this.destroyed) return;
    const revision = this.detailRevision;
    this.sensitiveBusy.set(true);
    this.error.set('');
    this.sensitiveValue.set('');
    try {
      const response =
        field === 'document'
          ? await this.api.readAttendanceDocument(detail.id)
          : await this.api.readEntryInspection(detail.id);
      if (!this.isCurrentDetail(revision)) return;
      this.sensitiveLabel.set(label);
      this.sensitiveValue.set(response.value);
      this.message.set('Leitura sensível registrada e exibida somente nesta tela.');
    } catch (error) {
      if (this.isCurrentDetail(revision))
        this.error.set(this.apiError(error, `Não foi possível ler ${label.toLowerCase()}.`));
    } finally {
      if (this.isCurrentDetail(revision)) this.sensitiveBusy.set(false);
    }
  }

  private isCurrentDetail(revision: number): boolean {
    return !this.destroyed && revision === this.detailRevision;
  }

  private apiError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && error.status === 404)
      return 'O atendimento não foi encontrado.';
    if (error instanceof HttpErrorResponse && error.status === 403)
      return 'Seu perfil não tem permissão para esta operação.';
    return fallback;
  }
}
