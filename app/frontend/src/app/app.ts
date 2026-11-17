import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { LucideLayoutDashboard, LucideParkingSquare } from '@lucide/angular';
import { SessaoService } from './core/sessao/sessao.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, LucideLayoutDashboard, LucideParkingSquare],
  templateUrl: './app.html',
})
export class App {
  readonly sessao = inject(SessaoService);
}
