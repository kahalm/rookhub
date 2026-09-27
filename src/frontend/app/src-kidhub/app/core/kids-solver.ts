import { Chess, Square } from 'chess.js';

/**
 * Eine Aufgabe, wie die Kinderseite sie loest — fuer Lichess-Puzzles UND Kurs-Linien dieselbe Form.
 *
 * - `moves`: die ganze Zugfolge in UCI ab `fen`.
 * - `startPly`: Index des Zuges, der die Aufgabe STELLT (wird vorgespielt); geloest wird ab
 *   `moves[startPly + 1]`. Lichess-Puzzles haben immer `0` (moves[0] ist der Zug des Gegners),
 *   Kurs-Linien tragen ihren eigenen Wert (`-1` = kein Stellungszug, das Kind zieht sofort).
 * - `altMoves`: im Kurs geduldete Alternativen je Halbzug-Index (Chessable-softFail) — kein Fehler,
 *   aber auch nicht der gesuchte Zug.
 */
export interface KidsTask {
  fen: string;
  moves: string[];
  startPly: number;
  altMoves?: Record<number, string[]>;
}

/** Ergebnis eines Kinderzugs. */
export type KidsMoveResult =
  /** Richtig, der Gegner antwortet jetzt (`playReply`). */
  | 'continue'
  /** Richtig, und die Aufgabe ist geloest. */
  | 'solved'
  /** Falscher Zug — die Stellung ist schon wieder zurueckgesetzt. */
  | 'wrong'
  /** Ein im Kurs geduldeter anderer Zug — zurueckgesetzt, zaehlt nicht als Fehler. */
  | 'alternative'
  /** Gar kein erlaubter Zug (kann das Brett eigentlich nicht liefern). */
  | 'illegal';

export interface KidsMove { from: string; to: string; }

/**
 * Die Loese-Logik der Kinderseite, frei von Angular: haelt die Stellung, prueft Zuege gegen die
 * Loesung und spielt die Antworten des Gegners.
 *
 * <p>Absichtlich nachsichtig: im LETZTEN Zug zaehlt jedes Matt, nicht nur das gespeicherte (wie
 * bei Lichess) — ein Kind, das auf einem anderen Weg mattsetzt, hat die Aufgabe geloest. Eine
 * Umwandlung ist immer eine Dame, ausser die Loesung verlangt ausdruecklich etwas anderes (dann
 * gilt der Zug trotzdem, wenn Start- und Zielfeld stimmen — die Kinderseite fragt nicht nach).</p>
 */
export class KidsSolver {
  private readonly chess: Chess;
  private ply: number;
  private setupPlayed: boolean;

  /** Farbe, die das Kind spielt (am Zug nach dem Stellungszug). */
  readonly solverColor: 'white' | 'black';

  constructor(private readonly task: KidsTask) {
    this.chess = new Chess(task.fen);
    const startPly = Math.max(-1, Math.min(task.startPly, task.moves.length - 1));
    // Alles VOR dem Stellungszug wird stumm vorgespult; der Stellungszug selbst wird sichtbar
    // gespielt (playSetup), damit das Kind sieht, was gerade passiert ist.
    for (let i = 0; i < startPly; i++) this.apply(task.moves[i]);
    this.ply = startPly;
    this.setupPlayed = startPly < 0;
    if (this.setupPlayed) this.ply = 0;

    const probe = new Chess(this.chess.fen());
    if (!this.setupPlayed) probe.move(uciToMove(task.moves[startPly]));
    this.solverColor = probe.turn() === 'w' ? 'white' : 'black';
  }

  /** Aktuelle Stellung. */
  fen(): string { return this.chess.fen(); }

  /** Steht der Koenig der Partei am Zug im Schach? */
  inCheck(): boolean { return this.chess.inCheck(); }

  /** Farbe am Zug. */
  turnColor(): 'white' | 'black' { return this.chess.turn() === 'w' ? 'white' : 'black'; }

  /** Ist der Stellungszug noch zu spielen? */
  hasPendingSetup(): boolean { return !this.setupPlayed; }

  /** Spielt den Zug, der die Aufgabe stellt, und gibt ihn fuer die Anzeige zurueck. */
  playSetup(): KidsMove | null {
    if (this.setupPlayed) return null;
    const uci = this.task.moves[this.ply];
    this.apply(uci);
    this.ply++;
    this.setupPlayed = true;
    return { from: uci.slice(0, 2), to: uci.slice(2, 4) };
  }

  /** Ist das Kind am Zug (und die Aufgabe noch offen)? */
  isSolverTurn(): boolean {
    return this.setupPlayed && !this.isFinished() && this.turnColor() === this.solverColor;
  }

  /** Alle Zuege gespielt? */
  isFinished(): boolean { return this.setupPlayed && this.ply >= this.task.moves.length; }

  /** Der gesuchte naechste Zug (fuer den Tipp). */
  hint(): KidsMove | null {
    if (!this.isSolverTurn()) return null;
    const uci = this.task.moves[this.ply];
    return { from: uci.slice(0, 2), to: uci.slice(2, 4) };
  }

  /** Erlaubte Zuege je Startfeld — fuer das Brett (nur, wenn das Kind am Zug ist). */
  dests(): Map<string, string[]> {
    const map = new Map<string, string[]>();
    if (!this.isSolverTurn()) return map;
    for (const m of this.chess.moves({ verbose: true })) {
      const list = map.get(m.from) ?? [];
      if (!list.includes(m.to)) list.push(m.to);
      map.set(m.from, list);
    }
    return map;
  }

  /** Prueft den Zug des Kindes; bei einem Fehler steht danach wieder die alte Stellung. */
  tryMove(from: string, to: string, promotion?: string): KidsMoveResult {
    if (!this.isSolverTurn()) return 'illegal';
    const expected = this.task.moves[this.ply];
    const wantedPromotion = expected.length > 4 ? expected[4] : undefined;
    const isPawnToLastRank = this.isPromotionMove(from, to);
    const promo = isPawnToLastRank
      ? (expected.slice(0, 4) === from + to && wantedPromotion ? wantedPromotion : (promotion ?? 'q'))
      : undefined;

    try {
      this.chess.move({ from: from as Square, to: to as Square, promotion: promo });
    } catch {
      return 'illegal';
    }

    const isLast = this.ply === this.task.moves.length - 1;
    const matches = expected.slice(0, 4) === from + to;
    if (matches || (isLast && this.chess.isCheckmate())) {
      this.ply++;
      return this.ply >= this.task.moves.length ? 'solved' : 'continue';
    }

    this.chess.undo();
    const uci = from + to + (promo ?? '');
    const alts = this.task.altMoves?.[this.ply] ?? [];
    return alts.includes(uci) || alts.includes(from + to) ? 'alternative' : 'wrong';
  }

  /** Spielt die Antwort des Gegners (nach `continue`). */
  playReply(): KidsMove | null {
    if (!this.setupPlayed || this.isFinished() || this.turnColor() === this.solverColor) return null;
    const uci = this.task.moves[this.ply];
    this.apply(uci);
    this.ply++;
    return { from: uci.slice(0, 2), to: uci.slice(2, 4) };
  }

  /** Index des zuletzt gespielten Halbzugs in `moves` (fuer Kurs-Kommentare). */
  lastPly(): number { return this.ply - 1; }

  private isPromotionMove(from: string, to: string): boolean {
    const piece = this.chess.get(from as Square);
    return !!piece && piece.type === 'p' && (to[1] === '8' || to[1] === '1');
  }

  private apply(uci: string): void {
    this.chess.move(uciToMove(uci));
  }
}

function uciToMove(uci: string): { from: Square; to: Square; promotion?: string } {
  return {
    from: uci.slice(0, 2) as Square,
    to: uci.slice(2, 4) as Square,
    promotion: uci.length > 4 ? uci[4] : undefined,
  };
}

/** Zugfolge aus dem Server-Format (Leerzeichen-getrennt). */
export function splitMoves(moves: string): string[] {
  return moves.split(/\s+/).filter(m => m.length >= 4);
}

/** `AltMoves` eines Kurs-Puzzles (roher JSON-String `{ "ply": ["uci", …] }`) lesen. */
export function parseAltMoves(raw: string | null | undefined): Record<number, string[]> | undefined {
  if (!raw) return undefined;
  try {
    const parsed = JSON.parse(raw) as Record<string, string[]>;
    const out: Record<number, string[]> = {};
    for (const [k, v] of Object.entries(parsed)) {
      const ply = Number(k);
      if (Number.isInteger(ply) && Array.isArray(v)) out[ply] = v.filter(x => typeof x === 'string');
    }
    return out;
  } catch {
    return undefined;
  }
}
