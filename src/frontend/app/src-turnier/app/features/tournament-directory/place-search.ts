import { DestroyRef, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';
import { GeoPlaceSuggestion } from './tournament-directory.model';

/**
 * Die Ortssuche des Turnierkalenders: entprellte Vorschlaege aus dem Lexikon samt „sucht",
 * „kein Ort gefunden" und „Suche gescheitert". Herausgezogen aus `TournamentDirectoryComponent`
 * (Zerlegung Z28); was ein gewaehlter Ort mit dem FILTER macht, bleibt in der Komponente.
 */
export class PlaceSearch {
  static readonly DebounceMs = 250;

  readonly suggestions = signal<GeoPlaceSuggestion[]>([]);
  readonly searching = signal(false);
  /**
   * Die Ortssuche lief, fand aber nichts bzw. scheiterte. Ohne diese beiden stand nach der Sanduhr
   * einfach nichts da — unbekannter Ort und kaputte Suche sahen gleich aus (Codereview F6-007).
   */
  readonly noMatch = signal(false);
  readonly failed = signal(false);

  private readonly term$ = new Subject<string>();

  constructor(
    private readonly search: (term: string) => Observable<GeoPlaceSuggestion[]>,
    private readonly destroyRef: DestroyRef,
  ) {}

  start(): void {
    // switchMap: bei schnellem Tippen darf nicht die Antwort einer aelteren Anfrage die neuere
    // ueberschreiben.
    this.term$.pipe(
      debounceTime(PlaceSearch.DebounceMs),
      // Der Vergleich muss die ANGEZEIGTEN Vorschlaege einbeziehen: „ab" tippen, auf „a" loeschen
      // (Liste geleert), wieder „b" — derselbe Begriff, aber die Liste ist leer und die Sanduhr
      // steht. Ohne diese Bedingung verwirft distinctUntilChanged die Anfrage und die Sanduhr
      // bleibt fuer immer.
      distinctUntilChanged((a, b) => a === b && this.suggestions().length > 0),
      // Der Fehler wird INNEN abgefangen: draussen beendete er den ganzen Strom, danach setzte
      // jede Eingabe nur noch die Sanduhr, und sie blieb bis zum Neuladen stehen.
      switchMap(term => this.search(term).pipe(
        map(results => ({ results, failed: false })),
        catchError(() => of({ results: [] as GeoPlaceSuggestion[], failed: true })),
      )),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(({ results, failed }) => {
      this.suggestions.set(results);
      this.searching.set(false);
      this.failed.set(failed);
      this.noMatch.set(!failed && results.length === 0);
    });
  }

  /** Neuer Text im Ortsfeld: ab zwei Zeichen wird gesucht, darunter die Liste geleert. */
  input(value: string): void {
    const term = value.trim();
    this.resetState();
    if (term.length < 2) {
      this.suggestions.set([]);
      this.searching.set(false);
      return;
    }
    this.searching.set(true);
    this.term$.next(term);
  }

  /** Vorschlaege und „kein Ort gefunden"/„gescheitert" abraeumen — es steht ein Ort fest. */
  clear(): void {
    this.suggestions.set([]);
    this.resetState();
  }

  private resetState(): void {
    this.noMatch.set(false);
    this.failed.set(false);
  }
}
