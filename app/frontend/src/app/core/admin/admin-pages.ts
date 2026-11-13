import { HttpErrorResponse } from '@angular/common/http';
import { Component, InjectionToken, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import {
  email,
  form,
  FormField,
  maxLength,
  minLength,
  pattern,
  required,
  submit,
} from '@angular/forms/signals';
import {
  AdminApiService,
  DailyRate,
  EmployeeItem,
  EmployeeRole,
  RateConfiguration,
  SpaceItem,
} from './admin-api.service';
import { AuthService } from '../auth/auth.service';
import {
  EMPTY,
  Subject,
  Subscription,
  catchError,
  exhaustMap,
  finalize,
  fromEvent,
  map,
  merge,
  startWith,
  switchMap,
  tap,
  timer,
} from 'rxjs';

const roles: EmployeeRole[] = ['manobrista', 'caixa', 'admin'];
export const SPACE_AVAILABILITY_POLL_INTERVAL_MS = new InjectionToken<number>(
  'SPACE_AVAILABILITY_POLL_INTERVAL_MS',
  { factory: () => 15_000 },
);

@Component({
  standalone: true,
  selector: 'app-employees-page',
  imports: [FormField],
  template: `
    <section aria-labelledby="employees-title" class="space-y-8">
      <div>
        <h1 id="employees-title" class="text-2xl font-semibold">Funcionários</h1>
        <p class="mt-2 text-slate-600">Cadastre contas e mantenha perfis e estado de acesso.</p>
      </div>
      <form
        class="grid gap-4 rounded-xl border border-slate-200 p-5 md:grid-cols-2"
        (submit)="create($event)"
        novalidate
      >
        <h2 class="text-lg font-semibold md:col-span-2">Novo funcionário</h2>
        <label class="grid gap-1 text-sm"
          >E-mail<input
            class="rounded border border-slate-300 px-3 py-2"
            type="email"
            autocomplete="off"
            [attr.aria-invalid]="employeeForm.email().invalid() ? 'true' : null"
            [attr.aria-describedby]="validationErrors().length ? 'employee-error-summary' : null"
            [formField]="employeeForm.email"
        /></label>
        <label class="grid gap-1 text-sm"
          >Senha inicial<input
            class="rounded border border-slate-300 px-3 py-2"
            type="password"
            autocomplete="new-password"
            [attr.aria-invalid]="employeeForm.initialPassword().invalid() ? 'true' : null"
            [attr.aria-describedby]="validationErrors().length ? 'employee-error-summary' : null"
            [formField]="employeeForm.initialPassword"
        /></label>
        <fieldset class="flex flex-wrap gap-4 text-sm md:col-span-2">
          <legend class="mb-2 font-medium">Perfis (selecione ao menos um)</legend>
          @for (role of availableRoles; track role) {
            <label class="flex items-center gap-2"
              ><input
                type="checkbox"
                [checked]="createRoles().includes(role)"
                (change)="toggleCreateRole(role, $event)"
              />{{ role }}</label
            >
          }
        </fieldset>
        @if (message()) {
          <p class="text-sm text-emerald-800 md:col-span-2" role="status">{{ message() }}</p>
        }
        @if (error()) {
          <p class="text-sm text-red-700 md:col-span-2" role="alert">{{ error() }}</p>
        }
        @if (validationErrors().length) {
          <ul
            id="employee-error-summary"
            class="list-disc pl-5 text-sm text-red-700 md:col-span-2"
            aria-label="Resumo de erros"
            role="alert"
            tabindex="-1"
          >
            @for (message of validationErrors(); track message) {
              <li>{{ message }}</li>
            }
          </ul>
        }
        <button
          class="w-fit rounded bg-emerald-800 px-4 py-2 font-medium text-white disabled:opacity-50"
          [disabled]="busy() || !createRoles().length"
        >
          {{ busy() ? 'Salvando…' : 'Cadastrar funcionário' }}
        </button>
      </form>
      <div class="space-y-3">
        <h2 class="text-lg font-semibold">Acessos cadastrados</h2>
        @for (employee of employees(); track employee.id) {
          <article
            class="grid gap-3 rounded-xl border border-slate-200 p-4 md:grid-cols-[1fr_auto]"
          >
            <div>
              <h3 class="font-medium">{{ employee.email }}</h3>
              <p class="text-sm text-slate-600">{{ employee.isActive ? 'Ativo' : 'Desativado' }}</p>
              <fieldset class="mt-2 flex flex-wrap gap-4 text-sm">
                <legend class="sr-only">Perfis de {{ employee.email }}</legend>
                @for (role of availableRoles; track role) {
                  <label class="flex items-center gap-2"
                    ><input
                      type="checkbox"
                      [checked]="
                        editRoles()[employee.id]?.includes(role) ?? employee.roles.includes(role)
                      "
                      (change)="toggleEmployeeRole(employee, role, $event)"
                    />{{ role }}</label
                  >
                }
              </fieldset>
            </div>
            <div class="flex items-center gap-2">
              <button
                type="button"
                class="rounded border border-slate-300 px-3 py-2 text-sm"
                [disabled]="busy() || !editRoles()[employee.id]?.length"
                (click)="saveAccess(employee)"
              >
                Salvar perfis</button
              ><button
                type="button"
                class="rounded border border-slate-300 px-3 py-2 text-sm"
                [disabled]="busy()"
                (click)="setActive(employee, !employee.isActive)"
              >
                {{ employee.isActive ? 'Desativar' : 'Ativar' }}
              </button>
            </div>
          </article>
        } @empty {
          <p class="text-slate-600">Nenhum funcionário nesta página.</p>
        }
        <div class="flex items-center gap-3">
          <button
            class="rounded border px-3 py-1"
            [disabled]="page() <= 1"
            (click)="load(page() - 1)"
          >
            Anterior</button
          ><span>Página {{ page() }} · {{ total() }} funcionário(s)</span
          ><button
            class="rounded border px-3 py-1"
            [disabled]="page() * 20 >= total()"
            (click)="load(page() + 1)"
          >
            Próxima
          </button>
        </div>
      </div>
    </section>
  `,
})
export class EmployeesPage implements OnInit {
  private readonly api = inject(AdminApiService);
  readonly availableRoles = roles;
  readonly employeeModel = signal({ email: '', initialPassword: '' });
  readonly employeeForm = form(this.employeeModel, (path) => {
    required(path.email, { message: 'Informe o e-mail.' });
    email(path.email, { message: 'Informe um e-mail válido.' });
    required(path.initialPassword, { message: 'Informe a senha inicial.' });
    minLength(path.initialPassword, 6, { message: 'Use ao menos 6 caracteres.' });
    pattern(path.initialPassword, /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{6,128}$/, {
      message: 'Inclua letra maiúscula, minúscula, número e símbolo.',
    });
    maxLength(path.initialPassword, 128);
  });
  readonly employees = signal<EmployeeItem[]>([]);
  readonly createRoles = signal<EmployeeRole[]>(['caixa']);
  readonly editRoles = signal<Record<string, EmployeeRole[]>>({});
  readonly page = signal(1);
  readonly total = signal(0);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly validationErrors = signal<string[]>([]);
  ngOnInit(): void {
    void this.load(1);
  }
  toggleCreateRole(role: EmployeeRole, event: Event): void {
    this.createRoles.update((value) => this.toggleRole(value, role, event));
  }
  toggleEmployeeRole(employee: EmployeeItem, role: EmployeeRole, event: Event): void {
    const current = this.editRoles()[employee.id] ?? [...employee.roles];
    this.editRoles.update((value) => ({
      ...value,
      [employee.id]: this.toggleRole(current, role, event),
    }));
  }
  async create(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set('');
    this.message.set('');
    this.validationErrors.set([]);
    this.busy.set(true);
    try {
      await submit(this.employeeForm, async () => {
        if (!this.createRoles().length) {
          this.error.set('Selecione ao menos um perfil.');
          return;
        }
        await this.api.createEmployee(
          this.employeeModel().email,
          this.employeeModel().initialPassword,
          this.createRoles(),
        );
        this.employeeModel.set({ email: '', initialPassword: '' });
        this.createRoles.set(['caixa']);
        this.message.set('Funcionário cadastrado. A senha não foi armazenada na tela.');
        await this.load(this.page());
      });
    } catch (error) {
      this.error.set(
        this.errorFor(error, 'Não foi possível cadastrar. Verifique e-mail, senha e perfis.'),
      );
    } finally {
      this.busy.set(false);
      this.collectValidationErrors();
    }
  }
  async saveAccess(employee: EmployeeItem): Promise<void> {
    await this.update(employee, employee.isActive, this.editRoles()[employee.id] ?? employee.roles);
  }
  async setActive(employee: EmployeeItem, isActive: boolean): Promise<void> {
    await this.update(employee, isActive, this.editRoles()[employee.id] ?? employee.roles);
  }
  async load(page: number): Promise<void> {
    this.error.set('');
    this.busy.set(true);
    try {
      const result = await this.api.listEmployees(page);
      this.employees.set(result.items);
      this.page.set(result.page);
      this.total.set(result.total);
      this.editRoles.set({});
    } catch (error) {
      this.error.set(this.errorFor(error, 'Não foi possível carregar funcionários.'));
    } finally {
      this.busy.set(false);
    }
  }
  private async update(
    employee: EmployeeItem,
    isActive: boolean,
    selectedRoles: EmployeeRole[],
  ): Promise<void> {
    if (!selectedRoles.length) {
      this.error.set('Cada funcionário deve manter ao menos um perfil.');
      return;
    }
    this.error.set('');
    this.message.set('');
    this.busy.set(true);
    try {
      await this.api.updateEmployee(employee, isActive, selectedRoles);
      this.message.set('Acesso atualizado. Sessões anteriores são revogadas pelo servidor.');
      await this.load(this.page());
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        await this.load(this.page());
        this.error.set(
          'O acesso mudou em outra sessão. A lista foi atualizada; revise antes de tentar novamente.',
        );
      } else {
        this.error.set(this.errorFor(error, 'Não foi possível atualizar o acesso.'));
      }
    } finally {
      this.busy.set(false);
    }
  }
  private toggleRole(current: EmployeeRole[], role: EmployeeRole, event: Event): EmployeeRole[] {
    return (event.target as HTMLInputElement).checked
      ? [...new Set([...current, role])]
      : current.filter((item) => item !== role);
  }
  private errorFor(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && error.status === 409
      ? 'Conflito de cadastro; atualize a lista e tente novamente.'
      : fallback;
  }
  private collectValidationErrors(): void {
    const messages = [
      ...this.employeeForm
        .email()
        .errors()
        .map((item) => item.message),
      ...this.employeeForm
        .initialPassword()
        .errors()
        .map((item) => item.message),
    ].filter((message): message is string => typeof message === 'string');
    this.validationErrors.set(messages);
    if (messages.length)
      setTimeout(() => document.getElementById('employee-error-summary')?.focus(), 0);
  }
}

@Component({
  standalone: true,
  selector: 'app-spaces-page',
  imports: [FormField],
  template: `
    <section aria-labelledby="spaces-title" class="space-y-6">
      <div>
        <h1 id="spaces-title" class="text-2xl font-semibold">Vagas</h1>
        <p class="mt-2 text-slate-600">
          A disponibilidade exibida é informativa e não reserva a vaga.
        </p>
      </div>
      @if (canManage()) {
        <form
          class="flex flex-wrap items-end gap-3 rounded-xl border p-4"
          (submit)="create($event)"
          novalidate
        >
          <label class="grid gap-1 text-sm"
            >Código<input
              class="rounded border px-3 py-2"
              [attr.aria-invalid]="spaceForm.code().invalid() ? 'true' : null"
              [attr.aria-describedby]="validationErrors().length ? 'space-error-summary' : null"
              [formField]="spaceForm.code" /></label
          ><button class="rounded bg-emerald-800 px-4 py-2 text-white" [disabled]="busy()">
            Cadastrar vaga
          </button>
        </form>
      }
      @if (validationErrors().length) {
        <ul
          id="space-error-summary"
          class="list-disc pl-5 text-sm text-red-700"
          aria-label="Resumo de erros"
          role="alert"
          tabindex="-1"
        >
          @for (message of validationErrors(); track message) {
            <li>{{ message }}</li>
          }
        </ul>
      }
      <form class="flex flex-wrap gap-3" (submit)="search($event)">
        <label class="grid gap-1 text-sm"
          >Código<input class="rounded border px-3 py-2" [formField]="filterForm.code" /></label
        ><label class="grid gap-1 text-sm"
          >Situação<select class="rounded border px-3 py-2" [formField]="filterForm.status">
            <option value="">Todas</option>
            <option value="free">Livre</option>
            <option value="occupied">Ocupada</option>
          </select></label
        ><button class="self-end rounded border px-4 py-2">Filtrar</button>
      </form>
      @if (error()) {
        <p role="alert" class="text-red-700">{{ error() }}</p>
      }
      @if (message()) {
        <p role="status" class="text-emerald-800">{{ message() }}</p>
      }
      <ul class="divide-y divide-slate-200">
        @for (space of spaces(); track space.id) {
          <li class="flex justify-between py-3">
            <span class="font-medium">{{ space.code }}</span
            ><span>{{ space.isOccupied ? 'Ocupada' : 'Livre' }}</span>
          </li>
        } @empty {
          <li class="py-3 text-slate-600">Nenhuma vaga encontrada.</li>
        }
      </ul>
      <div class="flex items-center gap-3">
        <button
          class="rounded border px-3 py-1"
          [disabled]="page() <= 1"
          (click)="load(page() - 1)"
        >
          Anterior</button
        ><span>Página {{ page() }} · {{ total() }} vaga(s)</span
        ><button
          class="rounded border px-3 py-1"
          [disabled]="page() * 20 >= total()"
          (click)="load(page() + 1)"
        >
          Próxima
        </button>
      </div>
    </section>
  `,
})
export class SpacesPage implements OnInit, OnDestroy {
  private readonly api = inject(AdminApiService);
  private readonly auth = inject(AuthService);
  private readonly pollIntervalMs = inject(SPACE_AVAILABILITY_POLL_INTERVAL_MS);
  private readonly refreshRequests = new Subject<void>();
  private pollingSubscription?: Subscription;
  private requestInFlight = false;
  private refreshQueued = false;
  readonly canManage = () => this.auth.session()?.roles.includes('admin') ?? false;
  readonly spaceModel = signal({ code: '' });
  readonly spaceForm = form(this.spaceModel, (path) => {
    required(path.code, { message: 'Informe o código.' });
    maxLength(path.code, 32);
    pattern(path.code, /^[A-Za-z0-9-]+$/, { message: 'Use letras, números e hífen.' });
  });
  readonly filterModel = signal({ code: '', status: '' });
  readonly filterForm = form(this.filterModel);
  readonly spaces = signal<SpaceItem[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly validationErrors = signal<string[]>([]);
  ngOnInit(): void {
    const visibleTriggers = fromEvent(document, 'visibilitychange').pipe(
      startWith(null),
      switchMap(() =>
        document.visibilityState === 'hidden'
          ? EMPTY
          : merge(
              timer(0, this.pollIntervalMs),
              fromEvent(globalThis, 'focus'),
              this.refreshRequests,
            ),
      ),
    );
    this.pollingSubscription = visibleTriggers
      .pipe(
        exhaustMap(() => {
          this.requestInFlight = true;
          this.busy.set(true);
          this.error.set('');
          return this.api
            .listSpaces(this.filterModel().code, this.filterModel().status, this.page())
            .pipe(
              tap((result) => {
                this.spaces.set(result.items);
                this.page.set(result.page);
                this.total.set(result.total);
              }),
              catchError(() => {
                this.error.set('Não foi possível consultar as vagas.');
                return EMPTY;
              }),
              finalize(() => {
                this.requestInFlight = false;
                this.busy.set(false);
                if (this.refreshQueued) {
                  this.refreshQueued = false;
                  queueMicrotask(() => this.requestRefresh());
                }
              }),
            );
        }),
      )
      .subscribe();
  }
  ngOnDestroy(): void {
    this.pollingSubscription?.unsubscribe();
    this.refreshRequests.complete();
  }
  search(event: Event): void {
    event.preventDefault();
    this.load(1);
  }
  async create(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set('');
    this.message.set('');
    this.validationErrors.set([]);
    this.busy.set(true);
    try {
      await submit(this.spaceForm, async () => {
        await this.api.createSpace(this.spaceModel().code);
        this.spaceModel.set({ code: '' });
        this.message.set('Vaga cadastrada.');
        await this.load(1);
      });
    } catch (error) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 409
          ? 'Esse código já está cadastrado.'
          : 'Não foi possível cadastrar a vaga.',
      );
    } finally {
      this.busy.set(false);
      this.collectValidationErrors();
    }
  }
  load(page: number): void {
    this.page.set(page);
    this.requestRefresh();
  }
  private requestRefresh(): void {
    if (document.visibilityState === 'hidden') return;
    if (this.requestInFlight) {
      this.refreshQueued = true;
      return;
    }
    this.refreshRequests.next();
  }
  private collectValidationErrors(): void {
    const messages = this.spaceForm
      .code()
      .errors()
      .map((item) => item.message)
      .filter((message): message is string => typeof message === 'string');
    this.validationErrors.set(messages);
    if (messages.length)
      setTimeout(() => document.getElementById('space-error-summary')?.focus(), 0);
  }
}

@Component({
  standalone: true,
  selector: 'app-daily-rates-page',
  imports: [FormField, DecimalPipe, DatePipe],
  template: `
    <section aria-labelledby="rates-title" class="space-y-6">
      <div>
        <h1 id="rates-title" class="text-2xl font-semibold">Tarifa diária</h1>
        <p class="mt-2 text-slate-600">
          Cada publicação cria uma nova versão; entradas existentes preservam seu snapshot.
        </p>
      </div>
      <div class="rounded-xl border border-slate-200 p-4" aria-live="polite">
        <h2 class="font-semibold">Vigente</h2>
        @if (configuration()?.currentRate; as rate) {
          <p class="mt-2">
            {{ rate.amount | number: '1.2-2' }} · publicada em
            {{ rate.publishedAtUtc | date: 'short' }}
          </p>
        } @else {
          <p class="mt-2 text-slate-600">Nenhuma tarifa publicada.</p>
        }
      </div>
      <button
        class="rounded border px-3 py-2"
        type="button"
        [disabled]="busy()"
        (click)="load(ratePage())"
      >
        {{ busy() ? 'Atualizando…' : 'Atualizar tarifas' }}
      </button>
      <form class="flex flex-wrap items-end gap-3" (submit)="publish($event)" novalidate>
        <label class="grid gap-1 text-sm"
          >Novo valor diário<input
            class="rounded border px-3 py-2"
            type="number"
            step="0.01"
            [attr.aria-invalid]="rateForm.amount().invalid() ? 'true' : null"
            [attr.aria-describedby]="validationErrors().length ? 'rate-error-summary' : null"
            [formField]="rateForm.amount" /></label
        ><button
          class="rounded bg-emerald-800 px-4 py-2 text-white"
          [disabled]="busy() || !configuration()"
        >
          Publicar tarifa
        </button>
      </form>
      @if (error()) {
        <p role="alert" class="text-red-700">{{ error() }}</p>
      }
      @if (message()) {
        <p role="status" class="text-emerald-800">{{ message() }}</p>
      }
      @if (validationErrors().length) {
        <ul
          id="rate-error-summary"
          class="list-disc pl-5 text-sm text-red-700"
          aria-label="Resumo de erros"
          role="alert"
          tabindex="-1"
        >
          @for (message of validationErrors(); track message) {
            <li>{{ message }}</li>
          }
        </ul>
      }
      <h2 class="text-lg font-semibold">Versões anteriores</h2>
      <ol class="divide-y divide-slate-200">
        @for (rate of rates(); track rate.id) {
          <li class="py-3">
            {{ rate.amount | number: '1.2-2' }} · {{ rate.publishedAtUtc | date: 'short' }}
          </li>
        } @empty {
          <li class="py-3 text-slate-600">Nenhuma versão publicada.</li>
        }
      </ol>
      <div class="flex items-center gap-3">
        <button
          class="rounded border px-3 py-1"
          [disabled]="ratePage() <= 1 || busy()"
          (click)="load(ratePage() - 1)"
        >
          Versões anteriores
        </button>
        <span aria-live="polite">Página {{ ratePage() }} · {{ rateTotal() }} versão(ões)</span>
        <button
          class="rounded border px-3 py-1"
          [disabled]="ratePage() * 20 >= rateTotal() || busy()"
          (click)="load(ratePage() + 1)"
        >
          Mais versões
        </button>
      </div>
    </section>
  `,
})
export class DailyRatesPage implements OnInit {
  private readonly api = inject(AdminApiService);
  readonly rateModel = signal({ amount: '' });
  readonly rateForm = form(this.rateModel, (path) => {
    required(path.amount, { message: 'Informe o valor.' });
    pattern(path.amount, /^(?=.*[1-9])\d+(?:\.\d{1,2})?$/, {
      message: 'Informe valor positivo com até duas casas decimais.',
    });
  });
  readonly configuration = signal<RateConfiguration | null>(null);
  readonly rates = signal<DailyRate[]>([]);
  readonly ratePage = signal(1);
  readonly rateTotal = signal(0);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly validationErrors = signal<string[]>([]);
  ngOnInit(): void {
    void this.load();
  }
  async load(page = this.ratePage()): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const [config, history] = await Promise.all([
        this.api.currentRate(),
        this.api.listRates(page, 20),
      ]);
      this.configuration.set(config);
      this.rates.set(history.items);
      this.ratePage.set(history.page);
      this.rateTotal.set(history.total);
    } catch {
      this.error.set('Não foi possível carregar a configuração de tarifa.');
    } finally {
      this.busy.set(false);
    }
  }
  async publish(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set('');
    this.message.set('');
    this.validationErrors.set([]);
    this.busy.set(true);
    try {
      await submit(this.rateForm, async () => {
        const config = this.configuration();
        if (!config) return;
        await this.api.publishRate(Number(this.rateModel().amount), config.version);
        this.rateModel.set({ amount: '' });
        this.message.set('Nova versão publicada.');
        await this.load();
      });
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        await this.load();
        this.error.set(
          'A versão vigente mudou. A configuração foi atualizada; revise e publique novamente.',
        );
      } else {
        this.error.set(
          'Não foi possível publicar. Informe um valor positivo com até duas casas decimais.',
        );
      }
    } finally {
      this.busy.set(false);
      this.collectValidationErrors();
    }
  }
  private collectValidationErrors(): void {
    const messages = this.rateForm
      .amount()
      .errors()
      .map((item) => item.message)
      .filter((message): message is string => typeof message === 'string');
    if (!messages.length) {
      const value = this.rateModel().amount.trim();
      if (!value) messages.push('Informe o valor.');
      else if (!/^(?=.*[1-9])\d+(?:\.\d{1,2})?$/.test(value)) {
        messages.push('Informe valor positivo com até duas casas decimais.');
      }
    }
    this.validationErrors.set(messages);
    if (messages.length)
      setTimeout(() => document.getElementById('rate-error-summary')?.focus(), 0);
  }
}
