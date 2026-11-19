import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SessaoService } from '../../core/sessao/sessao.service';
import { aguardarAtualizacao } from '../../testing/aguardar';
import { PollingRetiradasService } from './polling-retiradas.service';
import { FilaRetiradas } from './fila-retiradas';

function sessao(perfil: 'Administrador' | 'Caixa' | 'Manobrista') {
  return {
    autenticado: () => true,
    possuiPerfil: (perfis: readonly string[]) => perfis.includes(perfil),
    estado: () => ({ funcionario: { nome: 'Pessoa Sintética', perfis: [perfil] } }),
  };
}

function pedido(id = '11111111-1111-1111-1111-111111111111') {
  return {
    id,
    atendimentoId: '22222222-2222-2222-2222-222222222222',
    identificador: 'AT-2026-000041',
    placa: 'BRA2E19',
    modelo: 'Hatch prata',
    vagaCodigo: 'A-03',
    estado: 'aguardando',
    entradaUtc: new Date().toISOString(),
    solicitadoEmUtc: new Date().toISOString(),
    buscaIniciadaEmUtc: null,
    responsavelBusca: null,
    versaoPedido: 'AAAAAAA=',
    totalOriginal: 60,
    pagoLiquido: 0,
    saldo: 60,
    autorizacaoDocumental: 'nao_concedida',
    autorizacaoFinanceira: 'nao_concedida',
  };
}

function configurar(perfil: 'Administrador' | 'Caixa' | 'Manobrista') {
  let acao: (() => Promise<void>) | null = null;
  const polling = {
    iniciar: vi.fn((novaAcao: () => Promise<void>) => {
      acao = novaAcao;
    }),
    atualizarAgora: vi.fn(async () => {
      await acao?.();
    }),
    notificarComando: vi.fn(async () => {
      await acao?.();
    }),
    parar: vi.fn(),
  };

  TestBed.configureTestingModule({
    imports: [FilaRetiradas],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: SessaoService, useValue: sessao(perfil) },
      { provide: PollingRetiradasService, useValue: polling },
    ],
  });

  return { http: TestBed.inject(HttpTestingController), polling };
}

describe('FilaRetiradas', () => {
  beforeEach(() => TestBed.resetTestingModule());

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('carrega a fila paginada com as colunas financeiras do caixa', async () => {
    const { http } = configurar('Administrador');
    const fixture = TestBed.createComponent(FilaRetiradas);
    fixture.detectChanges();
    await fixture.whenStable();

    const requisicao = http.expectOne((req) => req.url.includes('/api/v1/withdrawals?'));
    expect(requisicao.request.method).toBe('GET');
    requisicao.flush({ items: [pedido()], page: 1, pageSize: 20, total: 1 });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.itens().length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('AT-2026-000041');
    expect(fixture.nativeElement.textContent).toContain('Saldo');
    expect(fixture.nativeElement.textContent).toContain('Total');
  });

  it('omite finanças e acesso ao caixa na visão do manobrista', async () => {
    const { http } = configurar('Manobrista');
    const fixture = TestBed.createComponent(FilaRetiradas);
    fixture.detectChanges();
    await fixture.whenStable();

    http
      .expectOne((req) => req.url.includes('/api/v1/withdrawals?'))
      .flush({ items: [pedido()], page: 1, pageSize: 20, total: 1 });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Saldo');
    expect(fixture.nativeElement.textContent).not.toContain('Total');
    expect(fixture.nativeElement.textContent).not.toContain('Registrar pedido');
  });

  it('preserva filtros e página quando a atualização falha', async () => {
    const { http, polling } = configurar('Caixa');
    const fixture = TestBed.createComponent(FilaRetiradas);
    fixture.detectChanges();
    await fixture.whenStable();
    http
      .expectOne((req) => req.url.includes('/withdrawals?'))
      .flush({ items: [pedido()], page: 1, pageSize: 20, total: 1 });
    await fixture.whenStable();

    fixture.componentInstance.alterarFiltro('aguardando');
    const primeiraAtualizacao = http.expectOne(
      (req) => req.url.includes('state=aguardando') && req.url.includes('pageSize=20'),
    );
    primeiraAtualizacao.flush({ items: [], page: 1, pageSize: 20, total: 0 });
    await aguardarAtualizacao(fixture);

    fixture.componentInstance.alterarTamanho('50');
    expect(fixture.componentInstance.filtro()).toBe('aguardando');
    expect(fixture.componentInstance.tamanhoPagina()).toBe(50);
    expect(fixture.componentInstance.pagina()).toBe(1);

    const consulta = http.expectOne(
      (req) => req.url.includes('state=aguardando') && req.url.includes('pageSize=50'),
    );
    consulta.flush(
      { title: 'erro' },
      { status: 500, statusText: 'Erro' },
    );
    await aguardarAtualizacao(fixture);

    expect(fixture.componentInstance.erro()).toContain('Não foi possível atualizar');
    expect(fixture.componentInstance.filtro()).toBe('aguardando');
    expect(fixture.componentInstance.pagina()).toBe(1);
    expect(polling.atualizarAgora).toHaveBeenCalled();
  });

  it('inicia a busca com a versão do pedido e recarrega em conflito', async () => {
    const { http } = configurar('Administrador');
    const fixture = TestBed.createComponent(FilaRetiradas);
    fixture.detectChanges();
    await fixture.whenStable();
    http
      .expectOne((req) => req.url.includes('/withdrawals?'))
      .flush({ items: [pedido()], page: 1, pageSize: 20, total: 1 });
    await fixture.whenStable();

    const componente = fixture.componentInstance;
    const promessa = componente.iniciarBusca(componente.itens()[0]);
    const post = http.expectOne((req) => req.url.endsWith('/search'));
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ versaoEsperada: 'AAAAAAA=' });
    post.flush({ ...pedido(), estado: 'em busca' });
    await new Promise((resolver) => setTimeout(resolver, 0));

    const recarga = http.expectOne((req) => req.url.includes('/withdrawals?'));
    recarga.flush({ items: [pedido()], page: 1, pageSize: 20, total: 1 });
    await promessa;
    await aguardarAtualizacao(fixture);
    expect(componente.aviso()).toContain('Busca iniciada');

    const conflito = componente.iniciarBusca(componente.itens()[0]);
    const postConflito = http.expectOne((req) => req.url.endsWith('/search'));
    postConflito.flush(
      { code: 'stale_withdrawal_version', detail: 'conflito' },
      { status: 409, statusText: 'Conflict' },
    );
    await new Promise((resolver) => setTimeout(resolver, 0));
    const recargaConflito = http.expectOne((req) => req.url.includes('/withdrawals?'));
    recargaConflito.flush({ items: [], page: 1, pageSize: 20, total: 0 });
    await conflito;
    expect(fixture.componentInstance.conflito()).toContain('Conflito');
  });
});
