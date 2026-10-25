import { DestroyRef, Injectable, Injector, inject } from '@angular/core';
import { localStore, readRaw } from './local-json-store';
import { ANON_PUZZLE_SESSION_KEY, readAnonSessionId } from './anon-session';

/**
 * Der Nachlauf einer Anmeldung: Präferenzen vom Server, vorgemerkten Discord-Link einlösen, anonyme Sitzungen
 * (Punktepartie, Puzzles, Endless, E-Books) ins Konto übernehmen. Aus `AuthService` herausgelöst (Zerlegung Z34);
 * die Fassade ruft ihn an denselben Stellen und in derselben Reihenfolge wie vorher.
 *
 * <p><b>Die dynamischen `import()` bleiben bewusst</b> — es ist die EINZIGE Stelle, an der `core/` nach `features/`
 * greift, und sie hat zwei Gründe: (1) Zyklus-Brecher: `PreferencesService`, `GuessService`,
 * `EndlessStorageService` und `EbookService` importieren selbst `AuthService` (ein statischer Import schlösse den
 * Modul-Kreis). (2) Bündelgröße: die Feature-Dienste bleiben aus dem Start-Bündel ALLER fünf Oberflächen (die
 * `auth.service.ts` teilen). Ein Umdrehen („Features melden sich beim core an") verlangte eine Registrierung in den
 * fünf `app.config.ts` und lüde die Dienste beim Start — mehr Risiko als Nutzen für denselben Ablauf.</p>
 */
@Injectable({ providedIn: 'root' })
export class LoginFollowUpService {
  private readonly injector = inject(Injector);

  /**
   * Ob der Injektor schon abgeraeumt ist. Gebraucht, weil jede Stelle hier einen DYNAMISCHEN
   * Import macht und den Dienst erst im `then` aus dem Injektor holt — bis dahin kann der
   * Injektor weg sein.
   */
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.destroyed = true);
  }

  /**
   * Einen Dienst holen, der hinter einem dynamischen Import liegt — und dabei aushalten, dass der
   * Injektor in der Zwischenzeit abgeraeumt wurde.
   *
   * <p>Ein `import(...)` loest asynchron auf, und niemand bricht es ab. Meldet sich jemand an und
   * verlaesst die Seite sofort wieder, kommt das `then` NACH dem Abbau des Injektors an, und
   * `injector.get` wirft NG0205. Latent seit 0.413.0, an fuenf Stellen dieser Datei.</p>
   *
   * <p><b>Was hier NICHT der Beleg ist, obwohl es danach aussieht:</b> im Karma-Protokoll steht
   * NG0205 zu Dutzenden — auch in gruenen Laeufen, gezaehlt 72-mal in einem. Das ist der
   * TestBed-Abbau, kein Fehlschlag, und es hat mit dieser Reparatur nichts zu tun. Wer den Race
   * beheben will, sucht ihn im Browser (Anmelden, sofort weg), nicht im Testprotokoll.</p>
   *
   * <p>Bewusst kein `try/catch` um den ganzen Aufruf: das verschluckte auch echte Fehler AUS dem
   * geholten Dienst. Geprueft wird nur das eine, was hier schiefgehen kann.</p>
   */
  private withService<T>(get: () => T, use: (service: T) => void): void {
    if (this.destroyed) return;
    use(get());
  }

  /** Präferenzen des (neuen) Kontos vom Server holen (überschreibt den localStorage). */
  loadPreferences(): void {
    import('./preferences.service').then(m =>
      this.withService(() => this.injector.get(m.PreferencesService), p => p.loadFromServer()));
  }

  /** Nachlauf nach Anmelden/Registrieren über die Maske — Reihenfolge wie vorher in `AuthService.storeUser`. */
  afterLogin(): void {
    this.claimAnonymousPuzzleSession();
    this.consumeStashedDiscordLink();
    // Sync user preferences from server (overwrites localStorage)
    this.loadPreferences();
  }

  /**
   * Löst einen über einen Bot-Link (?dl=) vorgemerkten Discord-Token ein, sobald
   * sich ein anonymer User ein-/registriert — so wird die beim Klick hinterlegte
   * Discord-ID automatisch mit dem neuen Account verknüpft.
   */
  private consumeStashedDiscordLink(): void {
    import('./discord-link.service').then(m =>
      this.withService(() => this.injector.get(m.DiscordLinkService), d => d.consumeStashed()));
  }

  private claimAnonymousPuzzleSession(): void {
    // Punktepartie: eigene Kennung (GuessService.AnonKey), unabhängig von der Puzzle-Sitzung — ein Besucher
    // kann nur geraten und nie ein Puzzle gelöst haben (Codereview N11-003).
    if (readRaw(localStore(), 'rookhub_guess_session'))
      import('../features/guess/guess.service').then(m =>
        this.withService(() => this.injector.get(m.GuessService), guess => guess.claimAnonymous().subscribe()));
    // Mit der Rückfallebene von anon-session: bei gesperrtem Speicher liefen die Versuche unter einer
    // Speicher-Kennung — auch die gehören jetzt ins Konto.
    const sessionId = readAnonSessionId(ANON_PUZZLE_SESSION_KEY);
    if (!sessionId) return;
    // Lazy import to avoid circular dependency
    import('../features/puzzles/puzzle.service').then(m =>
      this.withService(() => this.injector.get(m.PuzzleService), puzzleService => {
        puzzleService.claimSession().subscribe();
        puzzleService.claimBookPuzzleSession().subscribe();
      }));
    // Also claim endless puzzle progress
    import('../features/puzzles/endless-storage.service').then(m =>
      this.withService(() => this.injector.get(m.EndlessStorageService),
        endlessStorage => endlessStorage.claimEndlessSession().subscribe()));
    // E-Books ohne Anmeldung (0.778.0): eigene Uploads + Lesestände derselben Kennung ins Konto.
    import('../features/ebooks/ebook.service').then(m =>
      this.withService(() => this.injector.get(m.EbookService), ebooks => ebooks.claimSession(sessionId).subscribe()));
  }
}
