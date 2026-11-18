import { Injectable, signal } from '@angular/core';

/**
 * Estado global, somente em memória, de comando crítico pendente ou incerto.
 * Bloqueia logout e navegação interna enquanto o resultado é desconhecido.
 */
@Injectable({ providedIn: 'root' })
export class ComandoPendenteService {
  private readonly estadoInterno = signal(false);

  readonly bloqueado = this.estadoInterno.asReadonly();

  definirBloqueado(bloqueado: boolean): void {
    this.estadoInterno.set(bloqueado);
  }
}
