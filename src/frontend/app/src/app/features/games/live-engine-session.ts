import { computed, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { BoardArrow, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { AnalysisEngineService, RemoteAnalyseTransport, RemoteEngine } from '../analysis/analysis-engine.service';
import { EngineDisplayLine, toDisplayLines, uciLineToSan } from '../analysis/engine-lines.util';

/** Ein eigener Zug in der Nebenvariante: SAN fürs Lesen, UCI für die Zugnummern-Kette, FEN für Brett und Engine. */
export interface LiveMove { san: string; uci: string; fen: string; from: string; to: string; }

/** So viele Linien rechnet die Live-Engine — wie der Vorgabewert des Analysebretts. */
export const LIVE_LINES = 3;
/** So viele Halbzüge je Linie werden gezeigt. */
export const LIVE_LINE_PLIES = 10;

/**
 * Live-Engine auf der Partieseite (seit 0.525.0, gewünscht 2026-09-24): eine Engine rechnet die Stellung auf dem
 * Brett, und man kann selbst ziehen — eine Nebenvariante ab dem aktuellen Zug der Partie. Rein bis auf die Engine
 * (per Fabrik hereingereicht, in Tests ersetzbar) — wie `MistakesSession` hält sie ihren Zustand in Signalen, die
 * Seite bindet ihr Brett daran.
 *
 * Die Engine ist eine EIGENE Instanz (`AnalysisEngineService` hat keinen Konstruktor und läuft per `new` ein
 * zweites Mal, siehe Vergleichsmodus des Analysebretts) — sie gehört dieser Seite, und `destroy()` beendet sie.
 *
 * Wer in der Partie blättert, verlässt die Nebenvariante: `sync` sieht einen anderen Partiezug und wirft sie weg.
 */
export class LiveEngineSession {
  private readonly engine: AnalysisEngineService;
  private readonly sub: Subscription;
  private analyzedFen = '';
  /** Stellung, an der die Nebenvariante abzweigt (Partie-FEN beim ersten eigenen Zug). */
  private baseFen = '';

  /** Partiezug, an dem die Nebenvariante hängt (`currentMoveIndex`, −1 = Start). */
  readonly baseIndex = signal(-1);
  /** Alle Züge der Nebenvariante — auch die, hinter die man mit ← zurückgegangen ist (→ holt sie wieder). */
  private readonly moves = signal<LiveMove[]>([]);
  /** Wie viele davon gerade auf dem Brett stehen. */
  private readonly cursor = signal(0);
  /** Die Züge, die gerade auf dem Brett stehen. */
  readonly variation = computed(() => this.moves().slice(0, this.cursor()));
  /** Gibt es hinter der Stellung noch eigene Züge (→ / „Zug vor")? */
  readonly canRedo = computed(() => this.cursor() < this.moves().length);
  readonly lines = signal<EngineDisplayLine[]>([]);
  readonly depth = signal(0);
  /** Bester Zug der gerechneten Stellung (UCI) — für den Pfeil. */
  private readonly bestUci = signal<string | null>(null);
  /** Name der externen Engine; `null` = Stockfish im Browser. */
  readonly engineName = signal<string | null>(null);

  readonly lastMove = computed<[string, string] | undefined>(() => {
    const v = this.variation();
    return v.length ? [v[v.length - 1].from, v[v.length - 1].to] : undefined;
  });
  readonly arrows = computed<BoardArrow[]>(() => {
    const u = this.bestUci();
    return u && u.length >= 4 ? [{ from: u.slice(0, 2), to: u.slice(2, 4), brush: 'blue' }] : [];
  });
  /** Die eigene Nebenvariante mit Zugnummern („23. Qe3 Qd6 24. Bxa8"). */
  readonly variationSan = computed(() => {
    const v = this.variation();
    return v.length ? uciLineToSan(this.baseFen, v.map(m => m.uci), v.length) : '';
  });

  constructor(engineFactory: () => AnalysisEngineService = () => new AnalysisEngineService(), depthCap = 22) {
    this.engine = engineFactory();
    this.engine.setMultiPv(LIVE_LINES);
    this.engine.setDepth(depthCap);
    this.sub = this.engine.analysis$.subscribe(s => {
      if (!s.fen || s.fen !== this.analyzedFen) return;
      this.depth.set(s.depth);
      this.lines.set(toDisplayLines(s.fen, s.lines, LIVE_LINE_PLIES));
      this.bestUci.set(s.lines[0]?.pvUci[0] ?? null);
    });
  }

  /** Stellung auf dem Brett: das Ende der Nebenvariante, sonst die Partie. */
  fen(gameFen: string): string {
    const v = this.variation();
    return v.length ? v[v.length - 1].fen : gameFen;
  }

  /**
   * Mit der Partie abgleichen (die Seite ruft das bei jeder Prüfung): anderer Partiezug → Nebenvariante weg;
   * neue Stellung → rechnen. Billig, solange sich nichts geändert hat.
   */
  sync(gameIndex: number, gameFen: string): void {
    if (gameIndex !== this.baseIndex()) {
      this.baseIndex.set(gameIndex);
      if (this.moves().length) { this.moves.set([]); this.cursor.set(0); }
    }
    this.analyze(this.fen(gameFen));
  }

  /** Eigener Zug auf dem Brett — hängt sich an die Stelle der Nebenvariante, an der man gerade steht (was dahinter
   *  lag, fällt weg, wie beim Analysebrett ohne Varianten-Baum). */
  play(move: UserBoardMove, gameFen: string): void {
    if (this.cursor() === 0) this.baseFen = gameFen;
    const uci = move.from + move.to + (this.isPromotion(move) ? 'q' : '');
    this.moves.set([...this.variation(), { san: move.san, uci, fen: move.fen, from: move.from, to: move.to }]);
    this.cursor.update(c => c + 1);
    this.analyze(move.fen);
  }

  /** Einen eigenen Zug zurück (←) — er bleibt gemerkt, → holt ihn wieder. */
  undo(gameFen: string): void {
    if (this.cursor() === 0) return;
    this.cursor.update(c => c - 1);
    this.analyze(this.fen(gameFen));
  }

  /** Einen eigenen Zug wieder vor (→). */
  redo(gameFen: string): void {
    if (!this.canRedo()) return;
    this.cursor.update(c => c + 1);
    this.analyze(this.fen(gameFen));
  }

  /** Zurück zur Partie. */
  reset(gameFen: string): void {
    this.moves.set([]);
    this.cursor.set(0);
    this.analyze(gameFen);
  }

  /** Externe Engine (Lichess-Anbindung) statt Stockfish im Browser; die laufende Stellung rechnet neu. */
  useRemote(info: RemoteEngine, transport: RemoteAnalyseTransport): void {
    this.engine.setRemoteEngine(info, transport);
    this.engineName.set(info.name);
    const fen = this.analyzedFen;
    this.analyzedFen = '';
    if (fen) this.analyze(fen);
  }

  destroy(): void {
    this.sub.unsubscribe();
    this.engine.stop();
    this.engine.destroy();
  }

  private analyze(fen: string): void {
    if (!fen || fen === this.analyzedFen) return;
    this.analyzedFen = fen;
    this.lines.set([]);
    this.depth.set(0);
    this.bestUci.set(null);
    void this.engine.analyze(fen);
  }

  /** Das Brett wandelt immer in eine Dame um (`UserBoardMove.san` endet dann auf „=Q"). */
  private isPromotion(move: UserBoardMove): boolean {
    return /=[QRBN]/.test(move.san);
  }
}
