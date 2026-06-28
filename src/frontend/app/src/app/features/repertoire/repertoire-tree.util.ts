import { Chess } from 'chess.js';

/**
 * Repertoire-PGN (inkl. Varianten + [%alt]-Annotationen) → Stellungs-Graph für den Trainer.
 *
 * Statt eines Knoten-Baums bauen wir eine Map „Stellung → von hier aus im Repertoire gespielte
 * Züge". Dadurch teilen Transpositionen automatisch dieselbe Stellung. Schlüssel ist die
 * NORMALISIERTE FEN (nur die ersten 4 Felder: Figuren, Zugrecht, Rochade, en passant — ohne
 * Halbzug-/Zugzähler), damit über Zugfolgen hinweg dieselbe Stellung denselben Key hat.
 */

/** Ein im Repertoire an einer Stellung vorgesehener Zug + die geduldeten Alternativen ([%alt]). */
export interface RepMove {
  san: string;
  alts: string[];
}

export interface RepertoireGraph {
  /** normFen → Liste der von dort gespielten Repertoire-Züge (Reihenfolge: Hauptzug zuerst). */
  moves: Map<string, RepMove[]>;
  /** normFen der Startstellung der ersten Linie (für Default-Farb-Erkennung). */
  rootFen: string;
  /** Geschätzte Trainingsfarbe ('w'/'b') — aus [%alt] bzw. Heuristik; im Trainer überschreibbar. */
  guessedColor: 'w' | 'b';
}

/** FEN auf die 4 stellungsrelevanten Felder kürzen (ohne Zugzähler). */
export function normFen(fen: string): string {
  return fen.split(' ').slice(0, 4).join(' ');
}

/** Welche Seite ist in dieser (norm)FEN am Zug? */
export function sideToMove(fen: string): 'w' | 'b' {
  return fen.split(' ')[1] === 'b' ? 'b' : 'w';
}

const STARTFEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/** [%alt a b c] aus einem Kommentar-Token extrahieren. */
function extractAlts(commentToken: string): string[] {
  const m = /\[%alt\s+([^\]]+)\]/.exec(commentToken);
  if (!m) return [];
  return m[1].trim().split(/\s+/).filter(Boolean);
}

/** Zugnummer/Resultat aus einem Token strippen; gibt die reine SAN zurück (oder '' wenn keine). */
function cleanMoveToken(t: string): string {
  if (t === '*' || t === '1-0' || t === '0-1' || t === '1/2-1/2') return '';
  // führende Zugnummer "12." / "12..." entfernen
  const s = t.replace(/^\d+\.(\.\.)?/, '').trim();
  if (s === '' || /^\d+$/.test(s)) return '';
  return s;
}

function tokenize(movetext: string): string[] {
  // Kommentare {…}, Klammern, NAGs $n je als eigenes Token; sonst alle Nicht-Whitespace-Läufe.
  return movetext.match(/\{[^}]*\}|\(|\)|\$\d+|[^\s()]+/g) ?? [];
}

/** Normalisiert eine SAN für den Vergleich (entfernt +, #, !, ?). */
export function normSan(san: string): string {
  return san.replace(/[+#!?]+$/g, '').trim();
}

/**
 * Parst die kombinierte Repertoire-PGN (mehrere Spiele) in einen Stellungs-Graph.
 * PGN-Header werden ignoriert; jede Linie wird ab ihrer FEN (oder Grundstellung) eingespielt.
 */
export function buildRepertoireGraph(pgn: string): RepertoireGraph {
  const moves = new Map<string, RepMove[]>();
  let rootFen = normFen(STARTFEN);
  let rootSet = false;

  // PGN in Spiele zerlegen: ein neues Spiel beginnt bei einem [Event …]-Header nach Movetext.
  const games = splitGames(pgn);

  for (const game of games) {
    const startFen = game.fen || STARTFEN;
    if (!rootSet) { rootFen = normFen(startFen); rootSet = true; }
    const tokens = tokenize(game.movetext);
    parseSeq(tokens, { i: 0 }, startFen, moves);
  }

  return { moves, rootFen, guessedColor: guessColor(moves, rootFen) };
}

/** Rekursiver Movetext-Parser. Schreibt Züge in `moves`; Varianten verzweigen ab der Stellung
 *  VOR dem vorangehenden Zug. */
function parseSeq(tokens: string[], cur: { i: number }, fen: string, moves: Map<string, RepMove[]>): void {
  let posFen = fen;                 // volle FEN vor dem nächsten Zug
  let prevBeforeFen: string | null = null;   // volle FEN vor dem zuletzt geparsten Zug (für Varianten)
  let lastRef: RepMove | null = null;        // zuletzt eingetragener Zug (für [%alt]-Kommentar)

  const chess = new Chess();

  while (cur.i < tokens.length) {
    const t = tokens[cur.i];

    if (t === ')') { cur.i++; return; }

    if (t === '(') {
      cur.i++;
      if (prevBeforeFen) parseSeq(tokens, cur, prevBeforeFen, moves);
      else skipVariation(tokens, cur);
      continue;
    }

    if (t.startsWith('{')) {
      const alts = extractAlts(t);
      if (alts.length && lastRef) {
        for (const a of alts) {
          const na = normSan(a);
          if (na && !lastRef.alts.includes(na)) lastRef.alts.push(na);
        }
      }
      cur.i++;
      continue;
    }

    if (t.startsWith('$')) { cur.i++; continue; }   // NAG

    const san = cleanMoveToken(t);
    cur.i++;
    if (!san) continue;

    // Zug anwenden, um die Folge-FEN zu erhalten.
    let afterFen: string;
    try {
      chess.load(posFen);
      const mv = chess.move(san);
      if (!mv) { skipToVariationEnd(tokens, cur); continue; }
      afterFen = chess.fen();
    } catch {
      // Illegaler Zug (kaputte Variante) → Rest dieser Ebene überspringen.
      skipToVariationEnd(tokens, cur);
      continue;
    }

    const key = normFen(posFen);
    let list = moves.get(key);
    if (!list) { list = []; moves.set(key, list); }
    let ref = list.find(m => m.san === normSan(san));
    if (!ref) { ref = { san: normSan(san), alts: [] }; list.push(ref); }
    lastRef = ref;

    prevBeforeFen = posFen;
    posFen = afterFen;
  }
}

/** Überspringt bis zum Ende der aktuellen Varianten-Ebene (zugehörige schließende Klammer). */
function skipToVariationEnd(tokens: string[], cur: { i: number }): void {
  let depth = 0;
  while (cur.i < tokens.length) {
    const t = tokens[cur.i];
    if (t === '(') depth++;
    else if (t === ')') { if (depth === 0) return; depth--; }
    cur.i++;
  }
}

function skipVariation(tokens: string[], cur: { i: number }): void {
  let depth = 0;
  while (cur.i < tokens.length) {
    const t = tokens[cur.i++];
    if (t === '(') depth++;
    else if (t === ')') { if (depth === 0) return; depth--; }
  }
}

interface ParsedGame { fen: string | null; movetext: string; }

function splitGames(pgn: string): ParsedGame[] {
  const text = pgn.replace(/\r\n?/g, '\n');
  const out: ParsedGame[] = [];
  let fen: string | null = null;
  const mtLines: string[] = [];
  let inMoves = false;

  const flush = () => {
    const mt = mtLines.join(' ').trim();
    if (mt) out.push({ fen, movetext: mt });
    fen = null; mtLines.length = 0; inMoves = false;
  };

  for (const raw of text.split('\n')) {
    const line = raw.trim();
    const hdr = /^\[\s*(\w+)\s+"(.*)"\s*\]$/.exec(line);
    if (hdr) {
      if (inMoves) flush();
      if (hdr[1].toLowerCase() === 'fen') fen = hdr[2];
      continue;
    }
    if (line === '') continue;
    mtLines.push(line);
    inMoves = true;
  }
  flush();
  return out;
}

/** Trainingsfarbe schätzen: bevorzugt aus [%alt] (nur die trainierte Seite trägt geduldete Züge);
 *  sonst Heuristik (wer am Wurzel-Zug NICHT zuerst zieht). */
function guessColor(moves: Map<string, RepMove[]>, rootFen: string): 'w' | 'b' {
  let w = 0, b = 0;
  for (const [fen, list] of moves) {
    if (list.some(m => m.alts.length > 0)) {
      if (sideToMove(fen) === 'w') w++; else b++;
    }
  }
  if (w > 0 || b > 0) return w >= b ? 'w' : 'b';
  // Heuristik: zieht am Wurzel die weiße Seite (Gegner), trainieren wir Schwarz.
  return sideToMove(rootFen) === 'w' ? 'b' : 'w';
}

/** Eine Trainingskarte: Stellung (du am Zug) + erwarteter Hauptzug + akzeptierte Alternativen. */
export interface RepCard {
  cardKey: string;     // normFen vor dem Zug
  fenBefore: string;   // normFen (= cardKey)
  expected: string;    // Haupt-SAN (normalisiert)
  accepted: string[];  // weitere akzeptierte (geduldete) SANs (normalisiert)
}

/** Erzeugt aus dem Graph die Karten für die gegebene Trainingsfarbe. */
export function cardsForColor(graph: RepertoireGraph, color: 'w' | 'b'): RepCard[] {
  const cards: RepCard[] = [];
  for (const [fen, list] of graph.moves) {
    if (sideToMove(fen) !== color || list.length === 0) continue;
    const main = list[0];
    // Akzeptiert: [%alt] des Hauptzugs + weitere an dieser Stellung gelistete eigene Züge.
    const accepted = new Set<string>(main.alts);
    for (let k = 1; k < list.length; k++) accepted.add(list[k].san);
    accepted.delete(main.san);
    cards.push({ cardKey: fen, fenBefore: fen, expected: main.san, accepted: [...accepted] });
  }
  return cards;
}
