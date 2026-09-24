import { Chess, Color, PieceSymbol, Square } from 'chess.js';
import { tryLoadFen } from '../puzzles/puzzle-move.util';

/**
 * Stellungs-Taktik für die Zug-Klasse „Brilliant" im Partie-Rückblick: hängt nach dem Zug eine eigene
 * Figur, wurde also etwas GEOPFERT? Reine Funktionen über chess.js, einzeln mit Stellungen testbar.
 *
 * Die Regel „hängt" ist die von WintrCat/freechess (`src/lib/board.ts`, `isPieceHanging`), samt dessen
 * Schlag-Simulation (`capturable`, seit 0.518.1): ein Stück, dessen Schlagen den Schläger selbst etwas
 * mindestens so Wertvolles kostet, ist kein Opfer, sondern ein Köder. Bis dahin fehlte die Simulation mit
 * der Begründung „wir haben die Engine — ist der Zug der beste, IST das Opfer korrekt". Die Prod-Partie
 * MYXN3hXqz1X2hm7Cx6V47Q widerlegte das: 23.Lxd5 ist der beste Zug, WEIL er die b-Linie öffnet — der Läufer
 * „hängt" gegen exd5, aber dann nimmt Tb2 die Db8. Die Engine sagt „bester Zug", nicht „Opfer". Die ANGREIFER sind aber wie
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
 * diesem Sinn nicht), die nach dem Zug hängt und bei der der Gegner MEHR gewinnt, als der Zug geschlagen
 * hat. Wer einen Turm schlägt und dafür einen Läufer stehen lässt, hat nichts geopfert.
 *
 * „Mehr gewinnt" heißt: der Abtausch auf dem Feld (`exchangeGain`), nicht der Wert der Figur. Bis 0.518.0
 * stand hier der volle Figurenwert, und 22…gxf4 der Prod-Partie MYXN3hXqz1X2hm7Cx6V47Q hieß Brilliant: der
 * Lb7 griff den Ta8 an (Turm 5 > geschlagener Läufer 3). Der Turm ist aber von der Dame b8 gedeckt — nach
 * Lxa8 Dxa8 verliert Schwarz nur die Qualität (2), weniger als der Läufer, den er gerade genommen hat.
 * chess.com nennt den Zug nicht brillant; bei uns ist er seither „Great" (der einzige gute Zug nach dem
 * Patzer 22.Lxb7).
 *
 * Und es muss sich NEHMEN lassen (`capturable`): kostet das Schlagen den Gegner selbst mindestens so viel,
 * ist das Stück ein Köder, kein Opfer.
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

  const hanging: Sacrifice[] = [];
  for (const row of after.board()) {
    for (const cell of row) {
      if (!cell || cell.color !== mover || cell.type === 'p' || cell.type === 'k') continue;
      if (pieceValue(cell.type) <= captured) continue;
      if (hangingOn(before, after, cell.square) && exchangeLossOn(after, cell.square) > captured) {
        hanging.push({ square: cell.square, piece: cell.type });
      }
    }
  }
  if (hanging.length === 0 || !capturable(after, hanging)) return null;
  return hanging.reduce((a, b) => pieceValue(b.piece) > pieceValue(a.piece) ? b : a);
}

/**
 * Lässt sich wenigstens EINES der hängenden Stücke wirklich nehmen? (freechess, `analysis.ts`.) Für jeden
 * legalen Schlagzug des Gegners wird geschlagen und nachgesehen: hängt danach eine Figur des SCHLÄGERS, die
 * mindestens so viel wert ist wie das teuerste Opfer, war das Schlagen ein Fehler — das Stück ein Köder
 * (Abzugsangriff, Fesselung). Ein Opfer unter Turmwert gilt außerdem nicht, wenn das Schlagen ein Matt in
 * einem Zug erlaubt: dann ist es eine Mattdrohung, kein Materialopfer. Ab Turmwert zählt das Matt nicht
 * mehr (wie freechess: eine Dame, die man für ein Matt stehen lässt, IST ein Opfer).
 */
function capturable(after: Chess, sacrifices: readonly Sacrifice[]): boolean {
  const top = Math.max(...sacrifices.map(x => pieceValue(x.piece)));
  for (const sac of sacrifices) {
    for (const capture of after.moves({ verbose: true })) {
      if (capture.to !== sac.square || !capture.captured) continue;
      const test = tryLoadFen(after.fen());
      if (!test) return false;
      test.move(capture);
      const capturer = capture.color;
      let baitTaken = false;
      for (const row of test.board()) {
        for (const cell of row) {
          if (!cell || cell.color !== capturer || cell.type === 'p' || cell.type === 'k') continue;
          if (pieceValue(cell.type) >= top && hangingOn(after, test, cell.square)) { baitTaken = true; break; }
        }
        if (baitTaken) break;
      }
      if (baitTaken) continue;
      if (pieceValue(sac.piece) >= 5 || !test.moves().some(m => m.endsWith('#'))) return true;
    }
  }
  return false;
}

/**
 * Was der Gegner im Abtausch auf dem Feld gewinnt (Static Exchange Evaluation): er schlägt mit seiner
 * billigsten Figur, die Gegenseite schlägt mit ihrer billigsten zurück und so fort — und jede Seite hört auf,
 * sobald weiterschlagen sie Material kostet. Ohne Röntgen, wie die Angreifer und Verteidiger hier überall.
 * Der König schlägt nur auf ein Feld, das niemand mehr deckt (Wert ∞). Ein gedeckter Turm, den ein Läufer
 * angreift, kostet also 2, kein gedeckter 5; ein gedeckter Läufer gegen einen Läufer 0.
 *
 * `values` sind je Seite aufsteigend sortiert: `capturers` = wer als Nächstes auf das Feld schlägt,
 * `owners` = die Seite, der die Figur auf dem Feld gehört.
 */
export function exchangeGain(target: number, capturers: readonly number[], owners: readonly number[]): number {
  if (capturers.length === 0) return 0;
  const [capturer, ...rest] = capturers;
  return Math.max(0, target - exchangeGain(capturer, owners, rest));
}

/** Material, das der Gegner (am Zug) gewinnt, wenn er die Figur auf `square` schlägt — 0, wenn sich das
 *  Schlagen nicht lohnt. */
function exchangeLossOn(after: Chess, square: Square): number {
  const piece = after.get(square);
  if (!piece) return 0;
  const { attackers, defenders } = influenceOn(after, square, piece.color);
  const asc = (list: Influencer[]) => list.map(i => pieceValue(i.type)).sort((a, b) => a - b);
  return exchangeGain(pieceValue(piece.type), asc(attackers), asc(defenders));
}

/**
 * Angreifer und Verteidiger einer Figur der Farbe `color`. Nach dem Zug ist der Gegner am Zug: seine
 * Angreifer sind seine LEGALEN Schlagzüge auf das Feld. Steht dort (Test-Stellung) doch die eigene Seite am
 * Zug, gibt es keine legale Sicht — dann Pseudo.
 */
function influenceOn(after: Chess, square: Square, color: Color): { attackers: Influencer[]; defenders: Influencer[] } {
  const opponent: Color = color === 'w' ? 'b' : 'w';
  return {
    attackers: after.turn() === opponent ? legalCapturersOf(after, square) : attackersOf(after, square, opponent),
    defenders: attackersOf(after, square, color),
  };
}

function hangingOn(before: Chess, after: Chess, square: Square): boolean {
  const piece = after.get(square);
  if (!piece) return false;
  const value = pieceValue(piece.type);
  const { attackers, defenders } = influenceOn(after, square, piece.color);

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
