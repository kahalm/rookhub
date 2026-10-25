import { EndlessConfig, EndlessSession, EndlessSessionDetailDto, EndlessStorageService } from './endless-storage.service';
import { PuzzleDto } from './puzzle.service';

/** Ein gespieltes Puzzle des laufenden Durchgangs (Lauf-Protokoll). */
export interface EndlessPuzzleAttempt {
  puzzleNumber: number;
  puzzleId: number;
  lichessId: string;
  rating: number;
  solved: boolean;
  themes?: string;
  /** Start-/Endzeit dieses Puzzles als Unix-Millis (fürs serverseitige Logging; reist im Spielstand
   *  mit, damit ein fortgesetzter Lauf sie behält). 0 = unbekannt (Spielstand aus älterem Build) —
   *  dann speichert der Server das Puzzle mit dem Lauf, loggt es aber nicht. */
  startedAt: number;
  endedAt: number;
}

/** Die Kennzahlen des Laufs, die in den Spielstand gehören (gehalten von der Komponente). */
export interface EndlessRunSnapshot {
  lives: number;
  solved: number;
  level: number;
  chainIndex: number;
  seed: string;
  currentMinRating: number;
  maxRatingReached: number;
  sessionSeconds: number;
}

/** Spielstand eines offenen Laufs, wie er lokal und am Server liegt (`activeGameState`). */
export interface EndlessActiveGame extends EndlessRunSnapshot {
  mistakes: number[];
  puzzleAttempts: Omit<EndlessPuzzleAttempt, 'themes'>[];
}

/** Ein Puzzle so, wie es im gespeicherten Spielstand liegt (JSON; ältere Builds ohne Zeiten/Lichess-Id). */
export interface SavedPuzzleAttempt {
  puzzleNumber: number;
  puzzleId: number;
  lichessId?: string;
  rating: number;
  solved: boolean;
  startedAt?: unknown;
  endedAt?: unknown;
}

/** Zusammenfassung des beendeten Laufs über den Absprung in die Analyse (?gameover=1). */
export interface EndlessGameOverSnapshot {
  maxRatingReached?: number;
  solved?: number;
  level?: number;
  lives?: number;
  sessionSeconds?: number;
  isNewHighscore?: boolean;
  currentSessionMistakes?: number[];
  currentSessionPuzzles?: EndlessPuzzleAttempt[];
}

/** Puzzle-Liste aus einem gespeicherten Spielstand (`puzzleAttempts`) wieder aufbauen — für das
 *  Fortsetzen UND das Archivieren eines offenen Laufs. Fehlende Zeiten (älterer Build) → 0. */
export function restoreSessionPuzzles(raw: readonly SavedPuzzleAttempt[] | null | undefined): EndlessPuzzleAttempt[] {
  return (raw ?? []).map(p => ({
    puzzleNumber: p.puzzleNumber,
    puzzleId: p.puzzleId,
    lichessId: p.lichessId ?? '',
    rating: p.rating,
    solved: p.solved,
    themes: undefined,
    startedAt: Number(p.startedAt) || 0,
    endedAt: Number(p.endedAt) || 0,
  }));
}

/** sessionStorage: Zusammenfassung des beendeten Laufs über den Absprung in die Analyse (?gameover=1). */
const GAMEOVER_SNAPSHOT_KEY = 'rookhub_endless_gameover';
/** sessionStorage: Server-Id des aufgezeichneten Laufs, falls sie erst nach dem Absprung eintrifft. */
const PENDING_SESSION_ID_KEY = 'rookhub_endless_pending_sid';

/**
 * Lauf-Protokoll eines Endlos-Durchgangs (Zerlegung Z19): Fehler-Ratings und gespielte Puzzles, der daraus gebaute
 * Spielstand (lokal + Server) und die Aufzeichnung des beendeten Laufs — genau EINMAL, egal über welchen Pfad der
 * Lauf endet. Reine Klasse ohne Angular; den Speicher bekommt sie vom Aufrufer.
 */
export class EndlessSessionRecorder {
  /** Rating der Puzzles, an denen ein Leben verloren ging (in Reihenfolge). */
  mistakes: number[] = [];
  /** Gespielte Puzzles des Laufs — auch die vor einem Fortsetzen gespielten. */
  puzzles: EndlessPuzzleAttempt[] = [];
  /** Der laufende/zuletzt beendete Lauf wurde bereits aufgezeichnet. Verhindert Doppel-Posts, wenn mehrere
   *  Pfade (Game-Over → „Weiter“, Analyse-Absprung, Verlassen der Seite) dieselbe Session aufzeichnen wollen. */
  recorded = false;

  constructor(private readonly storage: EndlessStorageService) {}

  /** Frischer Lauf: leeres Protokoll, noch nicht aufgezeichnet. */
  startFresh(): void {
    this.mistakes = [];
    this.puzzles = [];
    this.recorded = false;
  }

  /** Fortgesetzter Lauf: Protokoll aus dem gespeicherten Spielstand; läuft weiter → noch nicht aufgezeichnet. */
  resumeFrom(game: { mistakes?: number[]; puzzleAttempts?: SavedPuzzleAttempt[] }): void {
    this.mistakes = game.mistakes ?? [];
    this.puzzles = restoreSessionPuzzles(game.puzzleAttempts);
    this.recorded = false;
  }

  /** Abgeschlossener Lauf aus der History: Protokoll aus dem Server-Detail; nie (neu) aufzeichnen. */
  showDetail(detail: EndlessSessionDetailDto): void {
    this.mistakes = (detail.mistakeAtRatings || '')
      .split(',').map(Number).filter(n => !isNaN(n));
    this.puzzles = (detail.puzzles ?? []).map((p, i) => ({
      puzzleNumber: i + 1,
      puzzleId: p.puzzleId,
      lichessId: p.lichessId ?? '',
      rating: p.rating,
      solved: p.solved,
      startedAt: 0,
      endedAt: 0,
    }));
    this.recorded = true;
  }

  /** Ein Leben verloren am Puzzle mit diesem Rating. */
  addMistake(rating: number): void {
    this.mistakes.push(rating);
  }

  /** Hängt das Puzzle (mit Start-/Endzeit) an die Liste an. `startedAt` 0 = noch nicht begonnen → jetzt. */
  pushPuzzle(puzzle: PuzzleDto, solved: boolean, startedAt: number, now = Date.now()): void {
    this.puzzles.push({
      puzzleNumber: this.puzzles.length + 1,
      puzzleId: puzzle.id,
      lichessId: puzzle.lichessId,
      rating: puzzle.rating,
      solved,
      themes: puzzle.themes,
      startedAt: startedAt > 0 ? startedAt : now,
      endedAt: now,
    });
  }

  /** Spielstand des offenen Laufs. `resumeAt` = Kettenposition, an der ein Fortsetzen beginnt: sobald das aktuelle
   *  Puzzle ein Ergebnis hat (gelöst, Fehler, Zurücksetzen), rufen die Aufrufer mit `chainIndex + 1` — sonst
   *  spielte ein Neuladen/Analyse-Rückkehr dasselbe Puzzle erneut (doppelt gezählt bzw. ein zweites Leben). */
  activeGame(run: EndlessRunSnapshot, resumeAt = run.chainIndex): EndlessActiveGame {
    return {
      lives: run.lives,
      solved: run.solved,
      level: run.level + (resumeAt - run.chainIndex),
      chainIndex: resumeAt,
      seed: run.seed,
      currentMinRating: run.currentMinRating,
      maxRatingReached: run.maxRatingReached,
      sessionSeconds: run.sessionSeconds,
      mistakes: this.mistakes,
      puzzleAttempts: this.puzzles.map(p => ({
        puzzleNumber: p.puzzleNumber,
        puzzleId: p.puzzleId,
        lichessId: p.lichessId,
        rating: p.rating,
        solved: p.solved,
        startedAt: p.startedAt,
        endedAt: p.endedAt,
      })),
    };
  }

  /** Sichert den Spielstand lokal und am Server (siehe {@link activeGame}). */
  sync(run: EndlessRunSnapshot, config: EndlessConfig, highscore: number, resumeAt = run.chainIndex): void {
    const gameState = this.activeGame(run, resumeAt);
    this.storage.saveActiveGameLocal(gameState);
    this.storage.saveProgressToServer(config, highscore, gameState);
  }

  /** Zeichnet den beendeten Lauf genau EINMAL auf (idempotent): lokale History + Server (mit allen Puzzles samt
   *  Start-/Lösungszeit; Einträge ohne Startzeit speichert der Server, loggt sie aber nicht). Die Server-Id wird
   *  für die Rückkehr aus der Analyse (?gameover=1) zusätzlich in sessionStorage gemerkt.
   *  @returns die neue lokale History, oder `null`, wenn der Lauf schon aufgezeichnet war. */
  recordOnce(makeSession: () => EndlessSession, history: EndlessSession[], onServerId: (id: number) => void): EndlessSession[] | null {
    if (this.recorded) return null;
    this.recorded = true;
    const session = makeSession();
    const next = this.storage.recordSession(history, session);
    this.storage.recordSessionToServer(session, this.puzzles).subscribe(id => {
      if (id) {
        onServerId(id);
        try { sessionStorage.setItem(PENDING_SESSION_ID_KEY, String(id)); } catch { /* gesperrt: best effort */ }
      }
    });
    return next;
  }

  /** Session-Eintrag des beendeten Laufs (Seed + geordnete Ketten-IDs → späteres Replay). */
  static session(config: EndlessConfig, totalSolved: number, maxRating: number, durationSeconds: number,
                 mistakes: number[], seed: string, chainPuzzleIds: string | undefined): EndlessSession {
    return {
      timestamp: Date.now(),
      config: { ...config },
      totalSolved,
      maxRating,
      durationSeconds,
      mistakeAtRatings: mistakes,
      seed,
      chainPuzzleIds,
    };
  }

  /** Zusammenfassung des beendeten Laufs für die Rückkehr aus der Analyse sichern (?gameover=1). */
  static saveGameOverSnapshot(snap: EndlessGameOverSnapshot): void {
    try { sessionStorage.setItem(GAMEOVER_SNAPSHOT_KEY, JSON.stringify(snap)); } catch { /* gesperrt: best effort */ }
  }

  /** Gesicherte Zusammenfassung holen und entfernen; `null`, wenn keine (lesbare) da ist. */
  static takeGameOverSnapshot(): EndlessGameOverSnapshot | null {
    const raw = sessionStorage.getItem(GAMEOVER_SNAPSHOT_KEY);
    if (!raw) return null;
    try {
      const s = JSON.parse(raw);
      sessionStorage.removeItem(GAMEOVER_SNAPSHOT_KEY);
      return s;
    } catch { return null; }
  }

  /** Server-Id eines Laufs, die erst nach dem Absprung in die Analyse eintraf (einmalig);
   *  `undefined` = keine gemerkt, `null` = unlesbar. */
  static takePendingSessionId(): number | null | undefined {
    const pending = sessionStorage.getItem(PENDING_SESSION_ID_KEY);
    if (!pending) return undefined;
    const id = parseInt(pending, 10) || null;
    sessionStorage.removeItem(PENDING_SESSION_ID_KEY);
    return id;
  }
}
