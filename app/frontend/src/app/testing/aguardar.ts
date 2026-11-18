import { ComponentFixture } from '@angular/core/testing';

/**
 * Aguarda as continuações assíncronas iniciadas por componentes zoneless e
 * força uma nova detecção de mudanças. Usado apenas nos testes de UI.
 */
export async function aguardarAtualizacao<T>(fixture?: ComponentFixture<T>): Promise<void> {
  await new Promise<void>((resolver) => setTimeout(resolver, 0));
  await Promise.resolve();
  fixture?.detectChanges();
  await fixture?.whenStable();
}
