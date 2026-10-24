import { BookPuzzleDto, SharedPuzzleCounts } from './puzzle.service';
import { findCachedBookPuzzle, getBookOffline } from './book-offline.util';
import { clearDailyElapsed } from './daily-elapsed.util';
import { BookModeHost, BookModeStrategy } from './book-mode-host';

/**
 * Einzelnes Buch-Puzzle (/puzzles/book/:id, auch Tagespuzzle — beides „standalone“, Zerlegung Z18): Laden per Id,
 * Blättern im selben Buch (online über den Server, offline aus dem gespeicherten Buch), Versuch melden inkl.
 * Freundes-Challenge und „Track solves“ des direkt geteilten Einzel-Puzzles.
 */
export class BookStandaloneMode implements BookModeStrategy {
  navLoading = false;
  private solveRecorded = false;       // Solve nur einmal je Puzzle-Durchgang melden (Tagespuzzle-Statistik)
  private challengeReported = false;   // Challenge-Ergebnis nur mit dem ERSTEN gemeldeten Versuch aufloesen
  /** Gesetzt, wenn dieses Buch-Puzzle aus einer Freundes-Challenge geöffnet wurde (?challengeId=…). */
  challengeId: number | null = null;
  private challengeResolved = false;
  /** „Track solves": für jedes direkt geteilte Einzel-Puzzle (= `singlePuzzle`) aktiv — Erstversuche
   *  der Besucher zählen + unter dem Puzzle anzeigen (solved/failed). Failed = alles inkl. Reset/Aufgeben;
   *  nur der erste Versuch zählt. */
  trackSolves = false;
  sharedCounts: SharedPuzzleCounts | null = null;
  private trackRecorded = false;

  constructor(private readonly h: BookModeHost) {}

  /** Neuer Puzzle-Durchgang: Solve/Challenge wieder meldbar. */
  resetAttempt(): void {
    this.solveRecorded = false;
    this.challengeReported = false;
  }

  load(id: number): void {
    const h = this.h;
    const epoch = ++h.loadEpoch;
    h.loadError = false;
    h.retryFn = () => this.load(id);
    h.state = 'LOADING';
    h.resetForLoad();

    // Offline: aus einem lokal gespeicherten Buch bedienen.
    if (!navigator.onLine) {
      const cached = findCachedBookPuzzle(id);
      if (cached) { h.puzzle = cached; h.setupPuzzle(cached); return; }
    }

    // Die Wahl hängt am Kurs der Linie — vor dem ersten Abruf ist nur der bekannt, von dem aus
    // weitergeblättert wurde; sonst gilt die Oberflächensprache und `afterLineLoaded` holt nach.
    const lang = h.requestLang;
    h.puzzleService.getBookPuzzleById(id, lang).subscribe({
      next: puzzle => {
        if (epoch !== h.loadEpoch) return;
        h.puzzle = puzzle;
        h.afterLineLoaded(puzzle, lang);
        h.setupPuzzle(puzzle);
      },
      error: () => {
        if (epoch !== h.loadEpoch) return;
        h.state = 'LOADING';
        h.puzzle = null;
        h.loadError = true;
      }
    });
  }

  /** Nächstes Puzzle aus demselben Buch laden (nur Standalone-Buch-Modus). */
  next(): void {
    if (!this.h.puzzle || this.navLoading) return;
    if (!navigator.onLine) { this.navOffline(false); return; }
    this.navLoading = true;
    this.h.puzzleService.getNextBookPuzzle(this.h.puzzle.id, this.h.requestLang).subscribe({
      next: p => this.goTo(p),
      error: () => { this.navLoading = false; }
    });
  }

  /** Zufälliges Puzzle aus demselben Buch laden (nur Standalone-Buch-Modus). */
  random(): void {
    if (!this.h.puzzle || this.navLoading) return;
    if (!navigator.onLine) { this.navOffline(true); return; }
    this.navLoading = true;
    this.h.puzzleService.getRandomBookPuzzle(this.h.puzzle.id, this.h.requestLang).subscribe({
      next: p => this.goTo(p),
      error: () => { this.navLoading = false; }
    });
  }

  /** Offline: nächstes/zufälliges Puzzle aus dem lokal gespeicherten Buch. */
  private navOffline(random: boolean): void {
    const cur = this.h.puzzle;
    if (!cur) return;
    const book = getBookOffline(cur.bookFileName);
    if (!book || !book.length) {
      this.h.snackbar.info(this.h.translate.instant('book.offlineUnavailable'), { action: 'common.ok', duration: 2500 });
      return;
    }
    let next: BookPuzzleDto;
    if (random) {
      const others = book.filter(p => p.id !== cur.id);
      next = (others.length ? others : book)[Math.floor(Math.random() * (others.length || book.length))];
    } else {
      const i = book.findIndex(p => p.id === cur.id);
      next = book[((i < 0 ? -1 : i) + 1 + book.length) % book.length];   // Loop am Ende
    }
    this.goTo(next);
  }

  private goTo(p: BookPuzzleDto): void {
    const h = this.h;
    ++h.loadEpoch;   // evtl. noch laufenden Ladevorgang entwerten
    this.navLoading = false;
    h.clearSolutionPlay();
    h.router.navigate(['/puzzles/book', p.id]);   // URL aktualisieren (Komponente wird wiederverwendet)
    h.puzzle = p;
    h.courseLang.noteLines(h.langRef, [p]);
    h.setupPuzzle(p);
  }

  /**
   * Meldet einen Lösungsversuch ans Backend — nur im Standalone-Buch-Modus und nur eingeloggt
   * (Basis für die Tagespuzzle-Visualisierung auf Discord). Pro Puzzle nur einmal.
   */
  recordAttempt(solved: boolean): void {
    const h = this.h;
    const puzzle = h.puzzle;
    if (!h.standalone || !puzzle) return;
    // Seit v0.309.0 wird JEDER Fehlversuch gemeldet (Fehlzug/Mouseslip/Restart → je ein rotes ✗
    // in der Löser-Anzeige); nur der Solve bleibt einmalig je Durchgang. Die Aufrufer stellen
    // sicher, dass ein Fehler nicht doppelt zählt (State-Guards in resetPuzzle/mouseslip).
    if (solved) {
      if (this.solveRecorded) return;
      this.solveRecorded = true;
    }
    // Ergebnis an eine ggf. offene Freundes-Challenge zurückmelden (nur Standalone; der ERSTE
    // gemeldete Versuch entscheidet — wie die bisherige Erstversuch-Semantik der Challenge).
    // Gemeldet wird erst NACH dem gespeicherten Versuch (Codereview N9-001): vorher ging /resolve
    // sogar VOR dem Versuch hinaus, der Server fand keinen gelösten Versuch und buchte echte Lösungen
    // als „nicht gelöst". Offline oder bei Fehler schließt der Server die Challenge beim Nachsenden.
    let challengeId: number | null = null;
    if (!this.challengeReported) {
      this.challengeReported = true;
      challengeId = this.claimChallenge();
    }
    const dailyDate = h.isDaily ? h.daily.date : null;
    if (h.auth.isLoggedIn) {
      // Versuch wird erfasst → gemerkte Tagespuzzle-Zwischenzeit verfällt (Erstversuch ist gewertet).
      if (dailyDate) clearDailyElapsed(dailyDate);
      const url = `/api/book-puzzles/${puzzle.id}/attempt`;
      // `mode` = Spielweise: hier wird KEINE Visualisierungsstufe gespeichert, der Server müsste
      // sonst auf „training" raten. Offline-Body identisch zum Online-Body, sonst geht die
      // Spielweise beim Nachsenden verloren.
      const mode = h.attemptSolveMode;
      const seconds = h.solveSeconds;
      const body = { solved, timeSeconds: seconds, hintsUsed: h.hintsUsed, mode };
      if (!navigator.onLine) { h.offlineQueue.enqueue('POST', url, body); return; }
      h.puzzleService.recordBookAttempt(puzzle.id, solved, seconds, h.hintsUsed, mode)
        .subscribe({
          next: () => {
            if (challengeId != null) h.challengeService.resolve(challengeId, solved, seconds).subscribe({ next: () => {}, error: () => {} });
          },
          error: () => h.offlineQueue.enqueue('POST', url, body),
        });
    } else if (solved) {
      // Anonym (nicht eingeloggt): nur Solves zählen fürs Tagespuzzle mit (namenlos) — die
      // gemerkte Zwischenzeit kumuliert daher bis zum ersten SOLVE weiter.
      if (dailyDate) clearDailyElapsed(dailyDate);
      const url = `/api/book-puzzles/${puzzle.id}/attempt/anonymous`;
      const mode = h.attemptSolveMode;
      const body = { solved, timeSeconds: h.solveSeconds, sessionId: h.puzzleService.ensureSessionId(), mode };
      if (!navigator.onLine) { h.offlineQueue.enqueue('POST', url, body); return; }
      h.puzzleService.recordBookAttemptAnonymous(puzzle.id, solved, h.solveSeconds, mode)
        .subscribe({ error: () => h.offlineQueue.enqueue('POST', url, body) });
    }
  }

  /** Beansprucht genau einmal die offene Buch-Challenge für diesen Versuch; die Meldung folgt erst nach dem
   *  gespeicherten Versuch (fire-and-forget). `null` = nichts zu melden. */
  private claimChallenge(): number | null {
    if (this.challengeId == null || this.challengeResolved) return null;
    this.challengeResolved = true;
    return this.challengeId;
  }

  /** „Track solves": meldet den ERSTEN Versuch des Besuchers (solved/failed) und aktualisiert die
   *  angezeigten Zähler. Nur einmal pro geöffnetem Puzzle (serverseitig zusätzlich erstversuch-dedupliziert).
   *  `solved=false` deckt Fehlzug, Aufgeben und Reset ab. */
  recordTrack(solved: boolean): void {
    const puzzle = this.h.puzzle;
    if (!this.trackSolves || this.trackRecorded || !puzzle) return;
    this.trackRecorded = true;
    // Genutzte Tipp-Stufe (0–3) mitschicken; serverseitig geklemmt. So zeigt der Zähler künftig auch,
    // wie viele Löser ohne/mit 1/2/3 Tipps gelöst haben.
    this.h.puzzleService.trackSharedAttempt(puzzle.id, solved, this.h.hintsUsed).subscribe({
      next: c => this.sharedCounts = c,
      error: () => { this.trackRecorded = false; }   // bei Fehler erneut versuchbar
    });
  }

  /** Erstversuch-Zähler des geteilten Puzzles holen (beim Öffnen). */
  loadSharedCounts(id: number): void {
    this.h.puzzleService.getSharedCounts(id).subscribe({ next: c => this.sharedCounts = c, error: () => {} });
  }
}
