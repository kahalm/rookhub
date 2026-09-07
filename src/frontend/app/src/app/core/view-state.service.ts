import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';

/**
 * Der Anzeige-Zustand einer Seite je NUTZER — nicht je Browser.
 *
 * <p>Die Filterleiste des Turnierkalenders lag nur im `localStorage`: der Umkreis, den man am
 * Rechner eingestellt hat, war am Handy weg. Es ist die Einstellung eines Nutzers.</p>
 *
 * <p>Fehler sind hier bewusst STILL (`null` bzw. „nicht gespeichert"): der Zustand ist eine
 * Bequemlichkeit, kein Inhalt. Faellt der Server aus, traegt die geraetelokale Kopie weiter —
 * eine Fehlermeldung „deine Filtereinstellung konnte nicht gespeichert werden" waere die Sorte
 * Meldung, die man wegklickt.</p>
 */
@Injectable({ providedIn: 'root' })
export class ViewStateService {
  private readonly http = inject(HttpClient);

  /** Der gespeicherte Zustand, oder `null` (kein gespeicherter Zustand / nicht erreichbar). */
  get<T>(key: string): Observable<T | null> {
    // 204 (nichts gespeichert) kommt mit LEEREM Rumpf — `observe: response`, weil ein leerer
    // Rumpf sonst als `null` nicht von einem Fehler zu unterscheiden waere.
    return this.http.get<T>(`/api/view-state/${key}`, { observe: 'response' }).pipe(
      map(res => res.body ?? null),
      catchError(() => of(null)),
    );
  }

  /** Zustand speichern. Der Rumpf ist der Zustand selbst. */
  save(key: string, state: unknown): Observable<boolean> {
    return this.http.put(`/api/view-state/${key}`, state).pipe(
      map(() => true),
      catchError(() => of(false)),
    );
  }
}
