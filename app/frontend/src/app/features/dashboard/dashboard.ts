import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Router, RouterLink } from '@angular/router';
import {
  LucideCircleAlert,
  LucideLayoutDashboard,
  LucideLoaderCircle,
  LucideParkingSquare,
  LucideShieldCheck,
  LucideUserRound,
} from '@lucide/angular';
import { urlApi } from '../../../environments/environment';
import { mensagemDeFalha } from '../../core/http/erro-api';
import { SessaoService } from '../../core/sessao/sessao.service';

interface AreaTrabalho {
  readonly nome: string;
  readonly perfis: readonly string[];
  readonly versaoAcesso: number;
  readonly expiraEmUtc: string;
}

@Component({
  selector: 'app-dashboard',
  imports: [
    RouterLink,
    LucideCircleAlert,
    LucideLayoutDashboard,
    LucideLoaderCircle,
    LucideParkingSquare,
    LucideShieldCheck,
    LucideUserRound,
  ],
  templateUrl: './dashboard.html',
})
export class Dashboard implements OnInit {
  private readonly sessao = inject(SessaoService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly estado = this.sessao.estado;
  readonly area = signal<AreaTrabalho | null>(null);
  readonly carregando = signal(true);
  readonly erro = signal<string | null>(null);
  readonly ehAdministrador = computed(() => this.sessao.possuiPerfil(['Administrador']));

  ngOnInit(): void {
    void this.carregarArea();
  }

  async carregarArea(): Promise<void> {
    this.carregando.set(true);
    this.erro.set(null);

    try {
      const area = await firstValueFrom(
        this.http.get<AreaTrabalho>(urlApi('/api/v1/area-trabalho')),
      );
      this.area.set(area);
    } catch (erro) {
      this.erro.set(mensagemDeFalha(erro));
      if (!this.sessao.autenticado()) {
        await this.router.navigate(['/login']);
      }
    } finally {
      this.carregando.set(false);
    }
  }


}
