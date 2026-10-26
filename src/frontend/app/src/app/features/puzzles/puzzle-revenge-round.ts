import { HttpClient } from '@angular/common/http';

/**
 * Freundes-Challenge und Revanche-Runde des Standard-Lösers (ohne Angular): welche Challenge bzw. welcher Freund
 * gemeldet wird (je genau einmal), die Warteschlange der offenen Revenge-Puzzles des Freundes und der Stand für
 * die Glückwunsch-/Feuerwerk-Karte. Gemeldet wird von der Komponente erst NACH dem gespeicherten Versuch.
 */
export class PuzzleRevengeRound {
  /** Gesetzt, wenn dieses Puzzle aus einer Freundes-Challenge geöffnet wurde (?challengeId=…). */
  challengeId: number | null = null;
  private challengeResolved = false;

  /** Gesetzt, wenn dieses Puzzle als Revanche an einem gescheiterten Puzzle dieses Users geöffnet wurde (?revengeUserId=…). */
  userId: number | null = null;
  notified = false;
  /** Warteschlange der noch offenen Revenge-Puzzle-Ids des Freundes (ohne das aktuelle). */
  private queue: number[] = [];
  /** Revanche-Runde durch → Glückwunsch-/Feuerwerk-Card statt nächstem Puzzle. */
  complete = false;
  friendName = '';
  solvedCount = 0;
  totalCount = 0;
  /** Indizes für die Feuerwerk-Funken im Template. */
  readonly fireworkDots = Array.from({ length: 28 }, (_, i) => i);

  constructor(private readonly http: HttpClient) {}

  /** Beansprucht genau einmal die offene Challenge für diesen (ersten) Versuch; die Meldung folgt erst nach dem
   *  gespeicherten Versuch (fire-and-forget). `null` = nichts zu melden. Sofort beansprucht, damit ein späteres,
   *  fremdes Puzzle derselben Seite die Challenge nicht mit seinem Ergebnis meldet. */
  claimChallenge(): number | null {
    if (this.challengeId == null || this.challengeResolved) return null;
    this.challengeResolved = true;
    return this.challengeId;
  }

  /** Beansprucht genau einmal je Puzzle die Revanche-Meldung an den Freund, dessen gescheitertes Puzzle gerade
   *  gerächt wurde, und zählt den Solve sofort für die Abschlusskarte. Liefert den Freund oder `null`. */
  claimRevenge(solved: boolean, hasPuzzle: boolean): number | null {
    if (this.userId == null || this.notified || !hasPuzzle) return null;
    this.notified = true;
    if (solved) this.solvedCount++;
    return this.userId;
  }

  /** Offene Revenge-Puzzles des Freundes laden und als Warteschlange für die Revanche-Runde merken.
   *  `currentIds` liefert beim Eintreffen die Ids, die gerade geladen werden/sind (die kommen nicht in die Schlange). */
  loadQueue(friendUserId: number, currentIds: () => (number | null | undefined)[]): void {
    this.http.get<{ displayName: string | null; username: string; puzzles: { puzzleId: number; solvedByViewer: boolean }[] }>(
      `/api/friends/${friendUserId}/revenge`
    ).subscribe({
      next: d => {
        this.friendName = d.displayName || d.username;
        const openIds = d.puzzles.filter(p => !p.solvedByViewer).map(p => p.puzzleId);
        this.totalCount = openIds.length;
        // Das aktuell geladene Puzzle wird zuerst gespielt → aus der Restschlange nehmen.
        const current = currentIds();
        this.queue = openIds.filter(id => !current.includes(id));
      },
      error: () => {}
    });
  }

  /** Nächste offene Puzzle-Id der Runde; ist die Schlange leer, gilt die Runde als abgeschlossen (`undefined`). */
  takeNext(): number | undefined {
    const nextId = this.queue.shift();
    if (nextId == null) this.complete = true;
    return nextId;
  }
}
