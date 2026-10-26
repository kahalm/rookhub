import { PuzzleService, PuzzleStatsDto } from './puzzle.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { ChallengeService } from '../../core/challenge.service';
import { RevengeService } from '../../core/revenge.service';
import { PuzzleRevengeRound } from './puzzle-revenge-round';
import { MoveLogEntry } from './base-puzzle-solver';

/** Ein zu meldender Versuch am Standard-Puzzle (Werte zum Zeitpunkt der Meldung). */
export interface StandardAttempt {
  id: number;
  solved: boolean;
  /** Zu wertende Lösezeit (beim Lösen ggf. wegen überlanger Zeit gekappt, siehe `LongSolveService`). */
  seconds: number;
  moveLog: MoveLogEntry[];
  visualizationLevel: number;
  evalShown: boolean;
  vizShowCount: number;
  hintsUsed: number;
}

/** Womit gemeldet wird — beim Aufruf frisch von der Komponente geholt. */
export interface StandardAttemptDeps {
  puzzleService: PuzzleService;
  offlineQueue: OfflineQueueService;
  challengeService: ChallengeService;
  revengeService: RevengeService;
  loggedIn: boolean;
  revenge: PuzzleRevengeRound;
  /** Stufe, zu der die Statistik NACH der Antwort nachgeladen wird (live gelesen). */
  visualizationMode(): number;
  onEloChange(change: number): void;
  onStats(stats: PuzzleStatsDto): void;
}

/**
 * Versuch am Standard-Puzzle melden: angemeldet bzw. anonym an den Server, offline oder bei Fehler in die
 * Offline-Schlange. Challenge und Revanche werden je genau einmal beansprucht und erst NACH dem gespeicherten
 * Versuch gemeldet (Codereview N9-001).
 */
export function sendStandardAttempt(d: StandardAttemptDeps, a: StandardAttempt): void {
  const { id, solved, seconds } = a;
  const log = a.moveLog.length > 0 ? JSON.stringify(a.moveLog) : undefined;
  const url = d.loggedIn ? `/api/puzzles/${id}/attempt` : `/api/puzzles/${id}/attempt/anonymous`;
  const body: Record<string, unknown> = {
    solved, timeSpentSeconds: seconds, moveLog: log ?? null,
    visualizationLevel: a.visualizationLevel,
    evalShown: a.evalShown, vizShowCount: a.vizShowCount,
    hintsUsed: a.hintsUsed,
    screenWidth: window.innerWidth, screenHeight: window.innerHeight,
  };
  if (!d.loggedIn) body['sessionId'] = d.puzzleService.ensureSessionId();
  // Challenge und Revanche: der erste Versuch entscheidet, gemeldet wird erst NACH dem gespeicherten Versuch
  // (Codereview N9-001). Gleichzeitig abgeschickt prüfte der Server, bevor der Versuch stand — echte Lösungen wurden
  // als „nicht gelöst" gebucht, die Revanche-Glocke fiel aus. Offline oder bei Fehler geht nur der Versuch in die
  // Schlange: beim Nachsenden schließt der Server die Challenge selbst und legt über revengeUserId die Glocke an.
  const challengeId = d.loggedIn ? d.revenge.claimChallenge() : null;
  const revengeTo = d.loggedIn ? d.revenge.claimRevenge(solved, true) : null;
  if (revengeTo != null) body['revengeUserId'] = revengeTo;
  if (!navigator.onLine) {
    // Offline gelöst → für späteres Hochladen vormerken (Stats aktualisieren sich beim Sync).
    d.offlineQueue.enqueue('POST', url, body);
    return;
  }
  if (d.loggedIn) {
    d.puzzleService.recordAttempt(id, solved, seconds, log, a.visualizationLevel, a.evalShown, a.vizShowCount, a.hintsUsed, revengeTo).subscribe({
      next: res => {
        if (res.eloChange != null) d.onEloChange(res.eloChange);
        d.puzzleService.getStats(d.visualizationMode()).subscribe(s => d.onStats(s));
        if (challengeId != null) d.challengeService.resolve(challengeId, solved, seconds).subscribe({ next: () => {}, error: () => {} });
        if (revengeTo != null) d.revengeService.recordResult(revengeTo, id, solved).subscribe({ next: () => {}, error: () => {} });
      },
      error: () => d.offlineQueue.enqueue('POST', url, body),
    });
  } else {
    d.puzzleService.recordAnonymousAttempt(id, solved, seconds, log, a.visualizationLevel, a.evalShown, a.vizShowCount, a.hintsUsed).subscribe({
      next: () => d.puzzleService.getAnonymousStats().subscribe(s => d.onStats(s)),
      error: () => d.offlineQueue.enqueue('POST', url, body),
    });
  }
}
