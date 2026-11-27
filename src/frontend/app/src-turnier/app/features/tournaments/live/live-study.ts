import { computed, signal } from '@angular/core';
import { Chess, Move } from 'chess.js';
import { AnalysisEngineService } from '@rh/features/analysis/analysis-engine.service';
import { LiveEngineSession } from '@rh/features/games/live-engine-session';
import { BoardArrow, UserBoardMove } from '@rh/shared/pgn-viewer/chess-board.component';
import { parseOneGame, START_FEN } from '@rh/shared/pgn-viewer/pgn-parser';
import { LiveGame } from './live-boards.models';

/** Gemerkte Wahl im Vollbild (je Gerät): Engine an/aus und Zahl der Linien. */
export const LIVE_FS_KEY = 'rookhub_live_fs';
export interface LiveFsPrefs { engine: boolean; lines: number; }

export function readLiveFsPrefs(): LiveFsPrefs {
  try {
    const raw = JSON.parse(localStorage.getItem(LIVE_FS_KEY) ?? 'null') as Partial<LiveFsPrefs> | null;
    const lines = Number(raw?.lines);
    return { engine: raw?.engine !== false, lines: Number.isFinite(lines) && lines >= 1 && lines <= 5 ? Math.round(lines) : 3 };
  } catch { return { engine: true, lines: 3 }; }
}

export function writeLiveFsPrefs(p: LiveFsPrefs): void {
  try { localStorage.setItem(LIVE_FS_KEY, JSON.stringify(p)); } catch { /* gesperrter Speicher: gilt bis zum Neuladen */ }
}

/** Stellung ohne Zugzähler/En-passant — Lichess und chess.js schreiben das e.p.-Feld verschieden. */
function samePosition(a: string, b: string): boolean {
  return a.split(' ').slice(0, 2).join(' ') === b.split(' ').slice(0, 2).join(' ');
}

/** Ergebnis von {@link LiveStudy.update}: nichts Neues, ein Zug angehängt, oder die Zugliste hinkt (PGN neu holen). */
export type LiveStudyUpdate = 'same' | 'appended' | 'stale';

/**
 * Ein Live-Brett im Vollbild (1.036.0, Wunsch 2026-11-27): die Züge der Partie zum Nachspielen, die lokale
 * Browser-Engine ({@link LiveEngineSession}, dieselbe wie auf der Partieseite) und eigene Züge als Nebenvariante.
 *
 * Die Zugliste kommt einmal als PGN vom Server ({@link loadPgn}); danach hängen die Live-Ereignisse (FEN + letzter Zug)
 * je einen Zug an ({@link update}). Passt ein Ereignis nicht (mehrere Züge verpasst, Partie neu), meldet `update`
 * `stale` und der Aufrufer holt das PGN neu.
 *
 * `cursor` null = man folgt der Partie (steht am letzten Zug, neue Züge rücken nach); eine Zahl = man blättert, die
 * Ansicht bleibt stehen. Der erste eigene Zug heftet die Ansicht an den Zug, an dem man steht — die Live-Partie
 * überschreibt die eigenen Züge nie, „Zur Live-Stellung“ ({@link backToLive}) verwirft sie.
 */
export class LiveStudy {
  readonly session: LiveEngineSession;
  /** Abzweig-Kennung für „folgt der Partie“ — bleibt gleich, auch wenn Live-Züge nachrücken. */
  private static readonly FOLLOW = -2;
  private readonly startFen = signal(START_FEN);
  readonly moves = signal<Move[]>([]);
  /** Restzeit nach dem Halbzug (Sekunden) — für die Zugliste. */
  readonly clocks = signal<Record<number, number>>({});
  /** Halbzug am Brett (−1 = Anfangsstellung); `null` = folgt der Live-Partie. */
  readonly cursor = signal<number | null>(null);
  /** Die zuletzt eingetroffene Live-Partie. */
  readonly live = signal<LiveGame | null>(null);
  readonly engineOn = signal(false);
  readonly ownMoves = signal(false);
  /** Das PGN kam an (sonst nur die Live-Stellung, ohne Zugliste). */
  readonly loaded = signal(false);

  readonly following = computed(() => this.cursor() === null);
  readonly index = computed(() => this.cursor() ?? this.moves().length - 1);
  /** Stellung des Partiezugs am Brett. Folgt man der Partie, gilt die Live-FEN (auch wenn die Zugliste noch hinkt). */
  readonly baseFen = computed(() => {
    const live = this.live();
    if (this.following() && live) return live.fen;
    const i = this.index();
    return i < 0 ? this.startFen() : this.moves()[i]?.after ?? this.startFen();
  });
  readonly hasVariation = computed(() => this.session.variation().length > 0 || this.session.canRedo());
  /** Brett zeigt genau die Live-Stellung (folgt, keine eigenen Züge). */
  readonly atLive = computed(() => this.following() && this.session.variation().length === 0);
  readonly fen = computed(() => this.session.fen(this.baseFen()));
  readonly lastMove = computed<[string, string] | undefined>(() => {
    const own = this.session.lastMove();
    if (own) return own;
    const live = this.live();
    if (this.following() && live) return live.lastMove && live.lastMove.length >= 4 ? [live.lastMove.slice(0, 2), live.lastMove.slice(2, 4)] : undefined;
    const m = this.moves()[this.index()];
    return m ? [m.from, m.to] : undefined;
  });
  readonly arrows = computed<BoardArrow[]>(() => this.engineOn() ? this.session.arrows() : []);
  /** Partiezug, der in der Liste markiert ist (−1 = keiner). */
  readonly listIndex = computed(() => this.index());

  constructor(engineFactory: () => AnalysisEngineService = () => new AnalysisEngineService(), prefs: LiveFsPrefs = { engine: true, lines: 3 }) {
    this.session = new LiveEngineSession(engineFactory, 22, prefs.lines);
    this.session.setPaused(!prefs.engine);
    this.engineOn.set(prefs.engine);
  }

  /** PGN der Partie (`/api/live-boards/pgn/…`) übernehmen; `false` = nicht lesbar. Holt danach die Live-Stellung nach. */
  loadPgn(pgn: string): boolean {
    const parsed = parseOneGame(pgn);
    if (!parsed) return false;
    this.startFen.set(parsed.game.fens[0] ?? START_FEN);
    this.moves.set(parsed.game.moves);
    this.clocks.set({ ...(parsed.game.clocks ?? {}) });
    const c = this.cursor();
    if (c !== null && c >= parsed.game.moves.length - 1 && !this.hasVariation()) this.cursor.set(null);
    this.loaded.set(true);
    const live = this.live();
    if (live) this.update(live); else this.syncEngine();
    return true;
  }

  /** Neue Live-Fassung der Partie. Hängt den letzten Zug an, wenn er genau an die Zugliste passt. */
  update(game: LiveGame): LiveStudyUpdate {
    this.live.set(game);
    const moves = this.moves();
    const lastFen = moves.length ? moves[moves.length - 1].after : this.startFen();
    let result: LiveStudyUpdate = 'stale';
    if (samePosition(lastFen, game.fen)) {
      result = 'same';
    } else if (this.loaded() && game.lastMove && game.lastMove.length >= 4) {
      try {
        const c = new Chess(lastFen);
        const u = game.lastMove;
        const mv = c.move({ from: u.slice(0, 2), to: u.slice(2, 4), promotion: u.length > 4 ? u[4] : undefined });
        if (mv && samePosition(mv.after, game.fen)) {
          this.moves.set([...moves, mv]);
          const mover = mv.color === 'w' ? game.white : game.black;
          if (mover.clockSeconds !== null && mover.clockSeconds !== undefined) {
            this.clocks.set({ ...this.clocks(), [moves.length]: mover.clockSeconds });
          }
          result = 'appended';
        }
      } catch { /* passt nicht → stale */ }
    }
    this.syncEngine();
    return result;
  }

  /** Zu einem Partiezug springen (−1 = Anfang); der letzte Zug heißt „der Partie folgen“. Eigene Züge fallen weg. */
  goTo(i: number): void {
    const last = this.moves().length - 1;
    const target = Math.max(-1, Math.min(i, last));
    if (this.hasVariation()) this.session.reset(this.baseFen());
    this.cursor.set(target >= last ? null : target);
    this.syncEngine();
  }

  first(): void { this.goTo(-1); }
  last(): void { this.goTo(this.moves().length - 1); }
  prev(): void {
    if (this.session.variation().length) { this.session.undo(this.baseFen()); return; }
    this.goTo(this.index() - 1);
  }
  next(): void {
    if (this.session.canRedo()) { this.session.redo(this.baseFen()); return; }
    if (this.session.variation().length) return;
    this.goTo(this.index() + 1);
  }

  /** Eigener Zug am Brett: die Ansicht bleibt ab jetzt an diesem Partiezug stehen. */
  play(move: UserBoardMove): void {
    if (this.cursor() === null) {
      // Abzweig festhalten. Hinkt die Zugliste der Live-Stellung hinterher, zweigt man trotzdem an der Live-FEN ab.
      const live = this.live();
      const listFen = this.moves().length ? this.moves()[this.moves().length - 1].after : this.startFen();
      if (!live || samePosition(listFen, live.fen)) {
        this.cursor.set(this.moves().length - 1);
        this.syncEngine();   // Abzweig-Index jetzt setzen — später verwirft `sync` die eigenen Züge sonst als „anderer Zug“
      }
    }
    this.session.play(move, this.baseFen());
  }

  /** Eigene Züge verwerfen und wieder der Partie folgen. */
  backToLive(): void {
    this.session.reset(this.baseFen());
    this.cursor.set(null);
    this.syncEngine();
  }

  setEngine(on: boolean): void {
    this.engineOn.set(on);
    this.session.setPaused(!on);
    this.syncEngine();
  }

  setOwnMoves(on: boolean): void {
    this.ownMoves.set(on);
    if (!on && this.hasVariation()) this.session.reset(this.baseFen());
  }

  setLines(n: number): void { this.session.setLineCount(n); }

  destroy(): void { this.session.destroy(); }

  /** Engine auf die Stellung am Brett — `sync` verwirft eigene Züge nur, wenn sich der Partiezug ändert. */
  private syncEngine(): void {
    this.session.sync(this.following() ? LiveStudy.FOLLOW : this.index(), this.baseFen());
  }
}
