import { DestroyRef, Signal, inject, signal } from '@angular/core';

/** Breitbild-Stufe (UI-Sweep u1, 0.762.0): dieselbe Grenze wie `@media (min-width: 2400px)` in den Stilen. */
export const WIDE_MEDIA_QUERY = '(min-width: 2400px)';

/**
 * Signal, das anzeigt, ob die Media-Query gerade zutrifft, und bei Größenänderung mitläuft. Im Injektionskontext
 * aufrufen (Feld-Initialisierer); der Listener wird mit der Komponente abgemeldet. Ohne `matchMedia` (SSR, alte
 * Testumgebung) bleibt es `false`.
 */
export function mediaSignal(query: string = WIDE_MEDIA_QUERY): Signal<boolean> {
  const mql = typeof matchMedia === 'function' ? matchMedia(query) : null;
  const value = signal(!!mql?.matches);
  // Außerhalb eines Injektionskontexts (Komponente per `new` im Unit-Test) nur der Startwert — ohne Listener, der
  // nie abgemeldet würde.
  let destroyRef: DestroyRef | null = null;
  try { destroyRef = inject(DestroyRef); } catch { /* kein Injektionskontext */ }
  if (mql && destroyRef) {
    const onChange = (e: MediaQueryListEvent) => value.set(e.matches);
    mql.addEventListener?.('change', onChange);
    destroyRef.onDestroy(() => mql.removeEventListener?.('change', onChange));
  }
  return value.asReadonly();
}
