import { Chess, Color, PieceSymbol, Square } from 'chess.js';
import { tryLoadFen } from '../puzzles/puzzle-move.util';

/**
 * Stellungs-Taktik für die Zug-Klasse „Brilliant" im Partie-Rückblick: hängt nach dem Zug eine eigene
 * Figur, wurde also etwas GEOPFERT? Reine Funktionen über chess.js, einzeln mit Stellungen testbar.
 *
 * Die Regel „hängt" ist die von WintrCat/freechess (`src/lib/board.ts`, `isPieceHanging`), mit einem
 * bewussten Unterschied: freechess prüft nach dem Schlagen per Simulation, ob es nicht ein Matt oder den
 * Verlust einer anderen Figur erlaubt. Das braucht es dort, weil freechess nur die Stellung sieht. Wir
 * haben die Engine: ist der Zug der beste oder fast der beste und steht der Ziehende danach nicht
 * schlecht, IST das Opfer korrekt — genau so definiert chess.com „Brilliant". Die ANGREIFER sind aber wie
 * bei freechess die LEGALEN Schlagzüge des Gegners (`legalCapturersOf`): nach einem Abzugsschach darf der
 * Bauer die Figur nicht schlagen, die auf sein Feld gezogen ist, ein gefesselter Läufer auch nicht, und
 * der König nimmt keine gedeckte Dame — mit Pseudo-Angriffen „hing" jede dieser Figuren, und der Zug wäre
 * ein falsches Brilliant. Die VERTEIDIGER bleiben Pseudo-Angriffe (`attackers`, ohne Röntgen): der
 * Ziehende ist danach nicht am Zug, für ihn gibt es keine legalen Züge zu fragen.
 *
 * Alles hier wirft NIE: die Funktionen laufen in einem `computed` hinter der Vorlage, ein Wurf dort ließe
 * den ganzen Rückblick unrendert. Eine unlesbare Stellung heißt „kein Opfer".
 */

/** Eine geopferte Figur: wo sie steht und welche es ist. */
export interface Sacrifice {
  square: Square;
  piece: PieceSymbol;
}

/** Ein Angreifer bzw. Verteidiger eines Feldes. */
export interface Influencer {
  square: Square;
  type: PieceSymbol;
}

/** Figurenwerte wie in freechess. Der König ist unendlich: er ist nie der „billigere" Angreifer, und
 *  ein Verteidiger, der nur König ist, taugt nie als billigerer Zurückschlagender. */
const VALUES: Readonly<Record<PieceSymbol, number>> = { p: 1, n: 3, b: 3, r: 5, q: 9, k: Infinity };

/** Wert einer Figurenart (Bauer 1, Leichtfigur 3, Turm 5, Dame 9, König ∞). */
export function pieceValue(type: PieceSymbol): number {
  return VALUES[type];
}

/** Alle Figuren der Farbe `color`, die das Feld angreifen (Pseudo-Angriffe, ohne Legalität) — für die
 *  eigene Farbe heißt das: decken. */
export function attackersOf(chess: Chess, square: Square, color: Color): Influencer[] {
  return chess.attackers(square, color)
    .map(sq => ({ square: sq, type: chess.get(sq)!.type }));
}

/**
 * Die Figuren der Seite AM ZUG, die auf `square` LEGAL schlagen können — je Figur einmal (die vier
 * Umwandlungen eines Bauern sind ein Angreifer). Im Schach bleiben nur Schlagzüge, die das Schach
 * beheben; eine gefesselte Figur und der König gegen ein gedecktes Feld fallen weg.
 */
export function legalCapturersOf(chess: Chess, square: Square): Influencer[] {
  const seen = new Set<string>();
  const out: Influencer[] = [];
  for (const m of chess.moves({ verbose: true })) {
    if (m.to !== square || !m.captured || seen.has(m.from)) continue;
    seen.add(m.from);
    out.push({ square: m.from, type: m.piece });
  }
  return out;
}

/** Umwandlungszug? Der UCI-Zug trägt dann die neue Figur als fünftes Zeichen. */
export function isPromotion(moveUci: string): boolean {
  return /^[a-h][1-8][a-h][1-8][qrbn]$/i.test(moveUci);
}

/** Steht die Seite am Zug im Schach? Eine unlesbare Stellung zählt als „nein". */
export function inCheck(fen: string): boolean {
  return tryLoadFen(fen)?.inCheck() ?? false;
}

/** UCI eines chess.js-Zugs — dieselbe Form, in der der Server `playedUci` schreibt. */
export function uciOf(move: { from: string; to: string; promotion?: string | null }): string {
  return move.from + move.to + (move.promotion ?? '');
}

/**
 * Wert der mit dem Zug geschlagenen Figur: 0 ohne Schlagen, beim en passant der Bauer, der NICHT auf dem
 * Zielfeld steht. `null` bei unlesbarer Stellung oder unlesbarem Zug — nie ein Wurf.
 */
export function capturedValue(fenBefore: string, moveUci: string): number | null {
  const before = tryLoadFen(fenBefore);
  return before ? capturedOn(before, moveUci) : null;
}

/**
 * Hängt die Figur auf `square` in der Stellung NACH dem Zug? Regeln in dieser Reihenfolge (freechess):
 *
 * a) Stand dort vorher eine GEGNERISCHE Figur mindestens gleichen Werts, ist es ein Abtausch → nein.
 * b) Ein Turm, der eine Leichtfigur geschlagen hat, und genau EIN Angreifer, und der ist eine Leichtfigur
 *    → nein (Txf6 gegen ein einmal gedecktes Feld gibt die Qualität nicht her, der Turm schlägt ja zurück).
 * c) Greift sie etwas BILLIGERES an → ja, egal wie oft sie gedeckt ist.
 * d) Mehr Angreifer als Verteidiger → ja, außer (1) sie ist billiger als jeder Angreifer und ein Verteidiger
 *    ist billiger als der billigste Angreifer (wer schlägt, verliert selbst mehr), oder (2) ein Bauer deckt
 *    (dann wäre der BAUER das Opfer, nicht diese Figur).
 * e) sonst nein.
 */
export function isPieceHanging(fenBefore: string, fenAfter: string, square: string): boolean {
  const before = tryLoadFen(fenBefore);
  const after = tryLoadFen(fenAfter);
  return !!before && !!after && hangingOn(before, after, square as Square);
}

/**
 * Das Opfer eines Zuges: die TEUERSTE eigene Figur (Springer bis Dame — Bauern und König opfert man in
 * diesem Sinn nicht), die nach dem Zug hängt und mehr wert ist als das, was der Zug geschlagen hat. Wer
 * einen Turm schlägt und dafür einen Läufer stehen lässt, hat nichts geopfert.
 *
 * Bewusst über ALLE eigenen Figuren, nicht nur die gezogene: beim Legall-Matt zieht der Springer, geopfert
 * wird die Dame, die stehen bleibt. `null`, wenn nichts hängt oder eine Stellung unlesbar ist.
 */
export function sacrificedPiece(fenBefore: string, fenAfter: string, moveUci: string): Sacrifice | null {
  const before = tryLoadFen(fenBefore);
  const after = tryLoadFen(fenAfter);
  if (!before || !after) return null;
  const captured = capturedOn(before, moveUci);
  if (captured === null) return null;
  const mover = before.turn();

  let best: Sacrifice | null = null;
  for (const row of after.board()) {
    for (const cell of row) {
      if (!cell || cell.color !== mover || cell.type === 'p' || cell.type === 'k') continue;
      if (pieceValue(cell.type) <= captured) continue;
      if (best && pieceValue(best.piece) >= pieceValue(cell.type)) continue;
      if (hangingOn(before, after, cell.square)) best = { square: cell.square, piece: cell.type };
    }
  }
  return best;
}

function hangingOn(before: Chess, after: Chess, square: Square): boolean {
  const piece = after.get(square);
  if (!piece) return false;
  const value = pieceValue(piece.type);
  // Nach dem Zug ist der Gegner am Zug: seine Angreifer sind seine legalen Schlagzüge auf das Feld.
  // Steht dort (Test-Stellung) doch die eigene Seite am Zug, gibt es keine legale Sicht — dann Pseudo.
  const opponent: Color = piece.color === 'w' ? 'b' : 'w';
  const attackers = after.turn() === opponent
    ? legalCapturersOf(after, square) : attackersOf(after, square, opponent);
  const defenders = attackersOf(after, square, piece.color);

  // a) Abtausch: das Geschlagene war mindestens so viel wert.
  const previous = before.get(square);
  if (previous && previous.color !== piece.color && pieceValue(previous.type) >= value) return false;

  // b) Turm gegen Leichtfigur, einmal von einer Leichtfigur gedeckt.
  if (piece.type === 'r' && previous && pieceValue(previous.type) === 3
      && attackers.length === 1 && pieceValue(attackers[0].type) === 3) return false;

  // c) Ein billigerer Angreifer gewinnt Material, egal wie oft gedeckt ist.
  if (attackers.some(a => pieceValue(a.type) < value)) return true;

  // d) Überzahl der Angreifer.
  if (attackers.length > defenders.length) {
    const cheapest = Math.min(...attackers.map(a => pieceValue(a.type)));
    if (value < cheapest && defenders.some(d => pieceValue(d.type) < cheapest)) return false;
    if (defenders.some(d => d.type === 'p')) return false;
    return true;
  }
  return false;
}

function capturedOn(before: Chess, moveUci: string): number | null {
  const m = /^([a-h][1-8])([a-h][1-8])/.exec(moveUci ?? '');
  if (!m) return null;
  const from = m[1] as Square;
  const to = m[2] as Square;
  const moved = before.get(from);
  if (!moved) return null;
  const target = before.get(to);
  // Eigene Figur auf dem Zielfeld = Rochade in König-schlägt-Turm-Schreibweise (e1h1), kein Schlagen.
  if (target) return target.color !== moved.color ? pieceValue(target.type) : 0;
  // En passant: der Bauer zieht schräg auf ein LEERES Feld — geschlagen wird der Bauer daneben.
  if (moved.type === 'p' && from[0] !== to[0]) return pieceValue('p');
  return 0;
}
