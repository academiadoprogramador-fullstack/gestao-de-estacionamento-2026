import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { form, FormField, maxLength, required, submit } from '@angular/forms/signals';
import { CanDeactivateFn, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AdminApiService, CheckInCreated, SpaceItem } from '../admin/admin-api.service';

interface CheckInModel {
  plate: string;
  model: string;
  ownerName: string;
  documentNumber: string;
  entryInspection: string;
  parkingSpaceId: string;
}
interface PendingCheckIn {
  content: CheckInModel;
  idempotencyKey: string;
  spaceCode: string;
}

const emptyCheckIn = (): CheckInModel => ({
  plate: '',
  model: '',
  ownerName: '',
  documentNumber: '',
  entryInspection: '',
  parkingSpaceId: '',
});

@Component({
  standalone: true,
  selector: 'app-check-in-page',
  imports: [FormField, RouterLink],
  template: `
    <section aria-labelledby="check-in-title" class="space-y-6">
      <div>
        <h1 id="check-in-title" class="text-2xl font-semibold">Registrar entrada</h1>
        <p class="mt-2 text-slate-600">
          Informe os dados do veículo e a vistoria textual de entrada.
        </p>
      </div>
      @if (availabilityError()) {
        <p class="rounded border border-red-300 bg-red-50 p-3 text-red-800" role="alert">
          {{ availabilityError() }}
        </p>
      }
      @if (!availabilityError() && !spaces().length) {
        <p class="rounded border border-amber-300 bg-amber-50 p-3 text-amber-950" role="status">
          Não há vagas livres disponíveis para selecionar. A lista é informativa; o servidor
          confirma a vaga durante o registro.
        </p>
      }
      @if (error()) {
        <p class="rounded border border-red-300 bg-red-50 p-3 text-red-800" role="alert">
          {{ error() }}
        </p>
      }
      @if (unknownAttempt()) {
        <section
          class="rounded border border-amber-400 bg-amber-50 p-4 text-amber-950"
          role="alert"
          aria-live="assertive"
        >
          <h2 class="font-semibold">Resultado ainda não confirmado</h2>
          <p class="mt-2">
            A conexão caiu antes de sabermos se a entrada foi confirmada. Os dados e a chave
            permanecem somente nesta memória. Não edite, recarregue nem saia desta tela; tente
            novamente com a mesma solicitação para consultar o resultado idempotente.
          </p>
          <button
            class="mt-3 rounded bg-amber-900 px-4 py-2 font-medium text-white disabled:opacity-50"
            type="button"
            [disabled]="busy()"
            (click)="retryUnknownAttempt()"
          >
            {{ busy() ? 'Consultando…' : 'Tentar novamente com a mesma solicitação' }}
          </button>
        </section>
      }
      @if (result(); as created) {
        <section
          class="rounded-xl border border-emerald-300 bg-emerald-50 p-5"
          aria-labelledby="check-in-result-title"
          aria-live="polite"
        >
          <h2 id="check-in-result-title" class="font-semibold">Entrada confirmada</h2>
          <dl class="mt-3 grid gap-2 text-sm sm:grid-cols-2">
            <div>
              <dt class="text-slate-600">Identificador</dt>
              <dd class="font-semibold">{{ created.publicIdentifier }}</dd>
            </div>
            <div>
              <dt class="text-slate-600">Estado</dt>
              <dd>{{ created.state }}</dd>
            </div>
            <div>
              <dt class="text-slate-600">Entrada (UTC)</dt>
              <dd>{{ created.enteredAtUtc }}</dd>
            </div>
            <div>
              <dt class="text-slate-600">Vaga</dt>
              <dd>{{ selectedSpaceCode() }}</dd>
            </div>
          </dl>
          <a
            class="mt-4 inline-block font-medium text-emerald-900 underline underline-offset-4"
            routerLink="/attendances"
            [queryParams]="{ identifier: created.publicIdentifier }"
            >Consultar este atendimento</a
          >
        </section>
      }
      <form (submit)="create($event)" novalidate>
        <fieldset
          class="grid gap-4 md:grid-cols-2"
          [disabled]="busy() || unknownAttempt() !== null"
        >
          <label class="grid gap-1 text-sm"
            >Placa<input
              class="rounded border border-slate-300 px-3 py-2"
              autocomplete="off"
              aria-required="true"
              [attr.aria-invalid]="checkInForm.plate().invalid() ? 'true' : null"
              [formField]="checkInForm.plate"
          /></label>
          <label class="grid gap-1 text-sm"
            >Modelo<input
              class="rounded border border-slate-300 px-3 py-2"
              autocomplete="off"
              aria-required="true"
              [attr.aria-invalid]="checkInForm.model().invalid() ? 'true' : null"
              [formField]="checkInForm.model"
          /></label>
          <label class="grid gap-1 text-sm"
            >Nome do titular<input
              class="rounded border border-slate-300 px-3 py-2"
              autocomplete="off"
              aria-required="true"
              [attr.aria-invalid]="checkInForm.ownerName().invalid() ? 'true' : null"
              [formField]="checkInForm.ownerName"
          /></label>
          <label class="grid gap-1 text-sm"
            >Documento do titular<input
              class="rounded border border-slate-300 px-3 py-2"
              autocomplete="off"
              aria-required="true"
              [attr.aria-invalid]="checkInForm.documentNumber().invalid() ? 'true' : null"
              [formField]="checkInForm.documentNumber"
          /></label>
          <label class="grid gap-1 text-sm md:col-span-2"
            >Vistoria de entrada<textarea
              class="min-h-28 rounded border border-slate-300 px-3 py-2"
              aria-required="true"
              [attr.aria-invalid]="checkInForm.entryInspection().invalid() ? 'true' : null"
              [formField]="checkInForm.entryInspection"
            ></textarea>
          </label>
          <label class="grid gap-1 text-sm md:col-span-2"
            >Vaga livre<select
              class="rounded border border-slate-300 px-3 py-2"
              aria-required="true"
              [attr.aria-invalid]="checkInForm.parkingSpaceId().invalid() ? 'true' : null"
              [formField]="checkInForm.parkingSpaceId"
            >
              <option value="">Selecione uma vaga</option>
              @for (space of spaces(); track space.id) {
                <option [value]="space.id">{{ space.code }}</option>
              }
              @if (selectedSpaceSnapshot() && !spaces().some(isSameSpace)) {
                <option [value]="selectedSpaceSnapshot()!.id">
                  {{ selectedSpaceSnapshot()!.code }} (selecionada)
                </option>
              }
            </select></label
          >
          <div class="flex items-center gap-3 md:col-span-2">
            <button
              class="rounded border px-3 py-1"
              type="button"
              [disabled]="loadingSpaces() || spacePage() <= 1"
              (click)="loadSpaces(spacePage() - 1)"
            >
              Vagas anteriores
            </button>
            <span aria-live="polite"
              >Página {{ spacePage() }} · {{ spaceTotal() }} vaga(s) livres</span
            >
            <button
              class="rounded border px-3 py-1"
              type="button"
              [disabled]="loadingSpaces() || spacePage() * 100 >= spaceTotal()"
              (click)="loadSpaces(spacePage() + 1)"
            >
              Mais vagas
            </button>
            <button
              class="rounded border px-3 py-1"
              type="button"
              [disabled]="loadingSpaces()"
              (click)="loadSpaces()"
            >
              Atualizar vagas
            </button>
          </div>
          @if (fieldErrors().length) {
            <ul
              class="list-disc pl-5 text-sm text-red-700 md:col-span-2"
              aria-label="Resumo de erros"
              id="check-in-error-summary"
              tabindex="-1"
              role="alert"
            >
              @for (message of fieldErrors(); track message) {
                <li>{{ message }}</li>
              }
            </ul>
          }
          <button
            class="w-fit rounded bg-emerald-800 px-5 py-3 font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
            type="submit"
            [disabled]="busy() || loadingSpaces() || !spaces().length"
          >
            {{ busy() ? 'Registrando…' : 'Confirmar entrada' }}
          </button>
        </fieldset>
      </form>
    </section>
  `,
})
export class CheckInPage implements OnInit, OnDestroy {
  private readonly api = inject(AdminApiService);
  private spacesSubscription?: Subscription;
  private destroyed = false;
  readonly pendingAttempt = signal<PendingCheckIn | null>(null);
  readonly model = signal<CheckInModel>(emptyCheckIn());
  readonly checkInForm = form(this.model, (path) => {
    required(path.plate, { message: 'Informe a placa.' });
    maxLength(path.plate, 80);
    required(path.model, { message: 'Informe o modelo.' });
    maxLength(path.model, 120);
    required(path.ownerName, { message: 'Informe o nome do titular.' });
    maxLength(path.ownerName, 200);
    required(path.documentNumber, { message: 'Informe o documento do titular.' });
    maxLength(path.documentNumber, 80);
    required(path.entryInspection, { message: 'Descreva a vistoria de entrada.' });
    maxLength(path.entryInspection, 4000);
    required(path.parkingSpaceId, { message: 'Selecione uma vaga.' });
  });
  readonly spaces = signal<SpaceItem[]>([]);
  readonly spacePage = signal(1);
  readonly spaceTotal = signal(0);
  readonly selectedSpaceSnapshot = signal<SpaceItem | null>(null);
  readonly selectedSpaceCode = signal('');
  readonly result = signal<CheckInCreated | null>(null);
  readonly unknownAttempt = signal<PendingCheckIn | null>(null);
  readonly busy = signal(false);
  readonly loadingSpaces = signal(false);
  readonly error = signal('');
  readonly availabilityError = signal('');
  readonly fieldErrors = signal<string[]>([]);
  private readonly beforeUnload = (event: BeforeUnloadEvent): void => {
    if (this.pendingAttempt() || this.unknownAttempt()) {
      event.preventDefault();
      event.returnValue = '';
    }
  };

  ngOnInit(): void {
    this.loadSpaces();
    globalThis.addEventListener('beforeunload', this.beforeUnload);
  }
  ngOnDestroy(): void {
    this.destroyed = true;
    this.spacesSubscription?.unsubscribe();
    globalThis.removeEventListener('beforeunload', this.beforeUnload);
    this.pendingAttempt.set(null);
    this.unknownAttempt.set(null);
    this.model.set(emptyCheckIn());
  }

  confirmNavigationAway(): boolean {
    if (this.pendingAttempt()) {
      this.error.set(
        'Aguarde a resposta da entrada antes de sair ou encerrar a sessão. O resultado ainda não é conhecido.',
      );
      return false;
    }
    return (
      !this.unknownAttempt() ||
      globalThis.confirm(
        'O resultado do check-in é desconhecido. Sair pode perder os dados e a chave necessários para uma repetição segura. Deseja sair mesmo assim?',
      )
    );
  }

  isSameSpace = (space: SpaceItem): boolean => space.id === this.selectedSpaceSnapshot()?.id;

  loadSpaces(page = this.spacePage()): void {
    if (this.destroyed) return;
    const selectedId = this.model().parkingSpaceId;
    const previouslySelected = this.spaces().find((space) => space.id === selectedId);
    if (previouslySelected) this.selectedSpaceSnapshot.set(previouslySelected);
    this.spacesSubscription?.unsubscribe();
    this.loadingSpaces.set(true);
    this.availabilityError.set('');
    this.spacesSubscription = this.api.listSpaces('', 'free', page, 100).subscribe({
      next: (response) => {
        const items = response.items.filter((item) => !item.isOccupied);
        this.spaces.set(items);
        this.spacePage.set(response.page);
        this.spaceTotal.set(response.total);
        const selected = items.find((item) => item.id === selectedId);
        if (selected) this.selectedSpaceSnapshot.set(selected);
      },
      error: () => {
        this.loadingSpaces.set(false);
        this.availabilityError.set(
          'Não foi possível carregar as vagas. Tente consultar novamente antes de registrar a entrada.',
        );
      },
      complete: () => this.loadingSpaces.set(false),
    });
  }

  async create(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || this.unknownAttempt() || this.loadingSpaces() || !this.spaces().length)
      return;
    this.error.set('');
    this.fieldErrors.set([]);
    this.result.set(null);
    this.busy.set(true);
    try {
      await submit(this.checkInForm, async () => {
        const content = this.model();
        const selectedSpace =
          this.spaces().find((item) => item.id === content.parkingSpaceId) ??
          (this.selectedSpaceSnapshot()?.id === content.parkingSpaceId
            ? this.selectedSpaceSnapshot()
            : null);
        if (!selectedSpace || selectedSpace.isOccupied) {
          this.error.set(
            'A vaga selecionada não está mais disponível. Atualize a lista e selecione outra.',
          );
          this.loadSpaces();
          return;
        }
        this.selectedSpaceSnapshot.set(selectedSpace);
        await this.sendAttempt({
          content: { ...content },
          idempotencyKey: crypto.randomUUID(),
          spaceCode: selectedSpace.code,
        });
      });
    } catch (error) {
      this.error.set(this.errorMessage(error));
    } finally {
      this.busy.set(false);
      this.collectFieldErrors();
      if (this.fieldErrors().length)
        setTimeout(() => document.getElementById('check-in-error-summary')?.focus(), 0);
    }
  }

  async retryUnknownAttempt(): Promise<void> {
    const attempt = this.unknownAttempt();
    if (!attempt || this.busy()) return;
    await this.sendAttempt(attempt);
  }

  private async sendAttempt(attempt: PendingCheckIn): Promise<void> {
    if (this.destroyed) return;
    this.pendingAttempt.set(attempt);
    this.busy.set(true);
    this.error.set('');
    try {
      const created = await this.api.createCheckIn(attempt.content, attempt.idempotencyKey);
      if (this.destroyed) return;
      this.unknownAttempt.set(null);
      this.selectedSpaceCode.set(attempt.spaceCode);
      this.result.set(created);
      this.model.set(emptyCheckIn());
      this.loadSpaces();
    } catch (error) {
      if (this.destroyed) return;
      this.error.set(this.errorMessage(error));
      if (this.isDefinitiveRejection(error)) this.unknownAttempt.set(null);
      else if (this.isUnknownOutcome(error) || this.unknownAttempt())
        this.unknownAttempt.set(attempt);
      if (error instanceof HttpErrorResponse && (error.status === 404 || error.status === 409))
        this.loadSpaces();
    } finally {
      if (!this.destroyed) {
        this.pendingAttempt.set(null);
        this.busy.set(false);
      }
    }
  }

  private collectFieldErrors(): void {
    const messages = [
      ...this.checkInForm
        .plate()
        .errors()
        .map((item) => item.message),
      ...this.checkInForm
        .model()
        .errors()
        .map((item) => item.message),
      ...this.checkInForm
        .ownerName()
        .errors()
        .map((item) => item.message),
      ...this.checkInForm
        .documentNumber()
        .errors()
        .map((item) => item.message),
      ...this.checkInForm
        .entryInspection()
        .errors()
        .map((item) => item.message),
      ...this.checkInForm
        .parkingSpaceId()
        .errors()
        .map((item) => item.message),
    ].filter((message): message is string => typeof message === 'string');
    this.fieldErrors.set(messages);
  }

  private errorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse))
      return 'Não foi possível confirmar a entrada. Os dados permanecem somente nesta tela.';
    if (error.status === 0)
      return 'Não foi possível confirmar a resposta. Verifique a conexão; não inicie outra entrada antes de consultar o atendimento.';
    const code = error.error?.code;
    if (error.status === 422 || code === 'daily_rate_not_configured')
      return 'A tarifa diária ainda não foi configurada. Solicite ao administrador a publicação de uma tarifa.';
    if (error.status === 409 && code === 'space_occupied')
      return 'A vaga acabou de ser ocupada por outra operação. Selecione uma vaga livre atualizada.';
    if (error.status === 404)
      return 'A vaga não foi encontrada. Atualize as vagas e tente novamente.';
    if (error.status === 400) return 'Revise os campos obrigatórios e tente novamente.';
    return 'A entrada não pôde ser registrada. Verifique os dados e tente novamente.';
  }

  private isUnknownOutcome(error: unknown): boolean {
    return (
      !(error instanceof HttpErrorResponse) ||
      error.status === 0 ||
      error.status === 408 ||
      error.status >= 500
    );
  }

  private isDefinitiveRejection(error: unknown): boolean {
    if (!(error instanceof HttpErrorResponse)) return false;
    const codes: Record<number, string> = {
      400: 'invalid_check_in',
      404: 'space_not_found',
      409: 'space_occupied',
      422: 'daily_rate_not_configured',
    };
    return codes[error.status] !== undefined && error.error?.code === codes[error.status];
  }
}

export const pendingCheckInCanDeactivate: CanDeactivateFn<CheckInPage> = (component) =>
  component.confirmNavigationAway();
