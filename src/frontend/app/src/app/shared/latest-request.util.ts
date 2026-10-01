import { Observable, Observer, Subscription } from 'rxjs';

/**
 * Höchstens EIN laufender Request je Auswahl (Thread, Gruppe, Nutzer, Modus, Zeitraum): ein neues
 * {@link run} bricht den vorigen ab — HttpClient storniert dabei den XHR —, damit die späte Antwort
 * einer früheren Auswahl die aktuelle nicht überschreibt (Out-of-order-Race, Codereview F5-004).
 * Für Suchfelder bleibt das Muster Subject + debounce + switchMap mit innerem catchError.
 *
 * Wer die Auswahl ohne neuen Request wechselt (schließen, leeren), ruft {@link cancel} und setzt
 * seinen Lade-Zustand selbst zurück — ein abgebrochener Request ruft weder `next` noch `error`.
 */
export class LatestRequest {
  private sub: Subscription | null = null;

  run<T>(request: Observable<T>, observer: Partial<Observer<T>>): void {
    this.sub?.unsubscribe();
    this.sub = request.subscribe(observer);
  }

  cancel(): void {
    this.sub?.unsubscribe();
    this.sub = null;
  }
}
