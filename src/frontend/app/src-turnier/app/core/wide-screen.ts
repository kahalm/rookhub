import { DestroyRef, Signal, inject, signal } from '@angular/core';
import { WIDE_MEDIA_QUERY } from '@rh/shared/layout/wide-media';

/** Breitbild-Stufe (UI-Sweep u1, DESIGN 4.1): ab hier dürfen Seiten Ansichten nebeneinander zeigen, darunter nie.
 *  Dieselbe Grenze wie RookHubs `mediaSignal()`; hier mit Rückruf, weil die Turnierseite beim Kippen nachlädt. */
export const WIDE_SCREEN_QUERY = WIDE_MEDIA_QUERY;

/**
 * Ob das Fenster gerade in der Breitbild-Stufe ist — als Signal, das beim Ziehen über die Grenze kippt.
 * Nur im Injektionskontext aufrufen (Feld-Initialisierer). `onChange` läuft NACH dem Kippen, z. B. um nachzuladen,
 * was die neue Anordnung zusätzlich zeigt. Ohne `matchMedia` (manche Testumgebungen) immer `false`.
 */
export function wideScreen(onChange?: (wide: boolean) => void): Signal<boolean> {
  const query = typeof window !== 'undefined' ? window.matchMedia?.(WIDE_SCREEN_QUERY) : undefined;
  const wide = signal(query?.matches ?? false);
  if (query) {
    const listener = (e: MediaQueryListEvent) => { wide.set(e.matches); onChange?.(e.matches); };
    query.addEventListener('change', listener);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', listener));
  }
  return wide.asReadonly();
}
