import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  LucideCalendarClock,
  LucideClipboardList,
  LucideLayoutDashboard,
  LucideLogOut,
  LucideParkingSquare,
  LucideReceipt,
  LucideSquareParking,
  LucideUsers,
} from '@lucide/angular';
import { ComandoPendenteService } from './core/comando/comando-pendente.service';
import { SessaoService } from './core/sessao/sessao.service';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    LucideCalendarClock,
    LucideClipboardList,
    LucideLayoutDashboard,
    LucideLogOut,
    LucideParkingSquare,
    LucideReceipt,
    LucideSquareParking,
    LucideUsers,
  ],
  templateUrl: './app.html',
})
export class App {
  readonly sessao = inject(SessaoService);
  readonly comando = inject(ComandoPendenteService);
  private readonly router = inject(Router);

  async sair(): Promise<void> {
    if (this.comando.bloqueado()) {
      return;
    }

    const confirmada = await this.sessao.logout();
    await this.router.navigate(['/login'], {
      queryParams: confirmada ? {} : { logout: 'sem-confirmacao' },
    });
  }
}
