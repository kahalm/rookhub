import { Chess, Move } from 'chess.js';

export interface ParsedGame {
  headers: Record<string, string>;
  moves: Move[];
  fens: string[];
  comments: { [moveIndex: number]: string };
}

export const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

/**
 * Strip RAV (Recursive Annotation Variations) from PGN move text.
 * Removes parenthesized variations like (6.a3 Bxc3+ 7.bxc3) while
 * preserving comments in curly braces.
 *
 * Handles Chessbase label pattern: ({A)} or ({B)} where a comment-only
 * variation prematurely closes with ). These are merged into the next
 * real variation.
 */
function stripVariations(pgn: string): string {
  // Fix Chessbase label pattern: ({A}) is a label-only variation where )
  // prematurely closes. Replace ({...}) with ( to keep variation open.
  let fixed = pgn.replace(/\(\s*\{[^}]*\}\s*\)/g, '(');

  let result = '';
  let depth = 0;
  let inComment = false;

  for (let i = 0; i < fixed.length; i++) {
    const ch = fixed[i];
    if (ch === '{') {
      if (depth === 0 && !inComment) result += ch;  // nur die aeussere oeffnende Klammer emittieren
      inComment = true;
    } else if (ch === '}') {
      if (depth === 0 && inComment) result += ch;    // nur schliessen, wenn auch eine offen war (keine Streu-})
      inComment = false;
    } else if (inComment) {
      if (depth === 0) result += ch;
    } else if (ch === '(') {
      depth++;
    } else if (ch === ')') {
      depth = Math.max(0, depth - 1);
    } else if (depth === 0) {
      result += ch;
    }
  }

  // Unausgeglichene oeffnende Kommentar-Klammer schliessen, damit chess.js
  // nicht am offenen { scheitert und das ganze Spiel still verworfen wird.
  if (inComment) result += '}';

  return result;
}

/**
 * Remove NAG symbols (e.g. $1, $14) and Unicode evaluation glyphs (⩲ ± etc.)
 */
/**
 * Faltet jede Hauptlinien-Variante `( … )` als Text-Kommentar `{ … }` an ihre Stelle: Züge (mit ihren
 * Nummern) und Kommentartext bleiben lesbar, Klammern, `[%…]`-Marker und NAGs fallen weg; verschachtelte
 * Varianten werden mit eingeflacht. `extractComments` hängt den Text danach an den Zug, hinter dem die
 * Variante stand — dort macht die Repertoire-Linienansicht ihre Züge klickbar. Gleiche Regel wie
 * `PgnParser.ExtractMoveComments(foldAllVariations: true)` im Backend (Kurs-Import).
 */
export function foldVariationsIntoComments(moveText: string): string {
  // ChessBase-Label-Muster wie in stripVariations behandeln.
  const s = moveText.replace(/\(\s*\{[^}]*\}\s*\)/g, '(');
  let out = '';
  let inComment = false;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (inComment) { out += c; if (c === '}') inComment = false; continue; }
    if (c === '{') { inComment = true; out += c; continue; }
    if (c === ')') continue;                      // verirrte schließende Klammer
    if (c !== '(') { out += c; continue; }
    // Passende schließende Klammer suchen; Klammern in Kommentaren zählen nicht.
    let depth = 0;
    let j = i;
    let cmt = false;
    for (; j < s.length; j++) {
      const d = s[j];
      if (cmt) { if (d === '}') cmt = false; continue; }
      if (d === '{') cmt = true;
      else if (d === '(') depth++;
      else if (d === ')' && --depth === 0) break;
    }
    const text = s.slice(i + 1, j)
      .replace(/\[%[^\]]*\]/g, '')
      .replace(/\$\d+/g, '')
      .replace(/[{}()]/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
    if (text) out += ` {${text}} `;
    i = j;
  }
  return out;
}

function stripNags(moveText: string): string {
  return moveText
    .replace(/\$\d+/g, '')
    .replace(/[⩲⩱±∓⊕⊖∞⩵↑→⇆∆□⊞⊟≤≥⪯⪰]+/g, '');
}

/**
 * Extract comments from cleaned move text and associate with move indices.
 * Comments appear as {text} in PGN. Chessbase annotations like [%csl ...],
 * [%cal ...], [%tqu ...] are stripped from display text.
 * Returns a map of moveIndex -> comment text. Index -1 = before first move.
 */
function extractComments(cleanedMoveText: string): { [moveIndex: number]: string } {
  const comments: { [moveIndex: number]: string } = {};
  const segments = cleanedMoveText.split(/(\{[^}]*\})/);
  let moveIndex = -1;

  for (const segment of segments) {
    if (segment.startsWith('{')) {
      let text = segment.slice(1, -1).trim();
      // Strip Chessbase annotations
      text = text.replace(/\[%[^\]]*\]/g, '').trim();
      if (text) {
        comments[moveIndex] = comments[moveIndex]
          ? comments[moveIndex] + ' ' + text
          : text;
      }
    } else {
      // Count chess moves in non-comment text
      const withoutNumbers = segment.replace(/\d+\.{1,3}/g, ' ');
      const withoutResult = withoutNumbers.replace(/\b(1-0|0-1|1\/2-1\/2|\*)\b/g, ' ');
      const tokens = withoutResult.trim().split(/\s+/).filter(t => t.length > 0);
      moveIndex += tokens.length;
    }
  }

  return comments;
}

// Eingabe-Limits, damit ein riesiges/kombiniertes PGN den synchronen
// Parser-Lauf auf dem UI-Thread nicht einfriert.
const MAX_PGN_CHARS = 2_000_000;   // ~2 MB pro Viewer-Session
const MAX_GAMES = 500;
const MAX_GAME_CHARS = 200_000;    // pathologisch grosse Einzelpartie ueberspringen

export interface ParsePgnOptions {
  /** Varianten nicht verwerfen, sondern als Text in den Kommentar ihres Zugs falten (siehe
   *  {@link foldVariationsIntoComments}). Nur für Ansichten, die Kommentar-Züge klickbar machen. */
  foldVariations?: boolean;
}

export function parsePgnText(pgnText: string, opts?: ParsePgnOptions): ParsedGame[] {
  return parsePgnTextWithSource(pgnText, opts).map(p => p.game);
}

/** Ein geparstes Spiel + sein unveränderter Originaltext (mit Varianten, Kommentaren und Markern). */
export interface ParsedGameWithSource { game: ParsedGame; raw: string; }

/**
 * Wie {@link parsePgnText}, liefert zu jedem Spiel aber auch dessen Originaltext. Übersprungene
 * (leere/zu große/unlesbare) Spiele fehlen in BEIDEN — Index `i` gehört also immer zusammen, auch
 * wenn ein Spiel mittendrin nicht gelesen werden konnte.
 */
export function parsePgnTextWithSource(pgnText: string, opts?: ParsePgnOptions): ParsedGameWithSource[] {
  if (pgnText.length > MAX_PGN_CHARS) {
    pgnText = pgnText.slice(0, MAX_PGN_CHARS);
  }
  const rawGames = pgnText.split(/\n\n(?=\[Event )/).slice(0, MAX_GAMES);
  const parsed: ParsedGameWithSource[] = [];

  for (const raw of rawGames) {
    const trimmed = raw.trim();
    if (!trimmed || trimmed.length > MAX_GAME_CHARS) continue;

    try {
      // Separate headers from move text
      // Use line-anchored regex so ] inside comments like {[%tqu ...]} is not matched
      const headerRegex = /^\[.*\]\s*$/gm;
      const headers: string[] = [];
      let moveText = trimmed;
      let lastHeaderEnd = 0;

      let match;
      while ((match = headerRegex.exec(trimmed)) !== null) {
        headers.push(match[0]);
        lastHeaderEnd = match.index + match[0].length;
      }

      if (headers.length > 0) {
        moveText = trimmed.substring(lastHeaderEnd);
      }

      // Clean move text: strip (or fold) variations and NAGs
      if (opts?.foldVariations) moveText = foldVariationsIntoComments(moveText);
      moveText = stripVariations(moveText);
      moveText = stripNags(moveText);
      // Direkt aufeinanderfolgende Kommentare zu EINEM zusammenfassen: chess.js lehnt „{a} {b}" ab und das
      // ganze Spiel fiele weg. Sie entstehen beim Einfalten und stehen auch so in manchen Quellen
      // („{[%cal …]} {Text}"); extractComments hätte sie ohnehin mit Leerzeichen verbunden.
      moveText = moveText.replace(/\}\s*\{/g, ' ');

      // Extract comments before feeding to chess.js (which strips them)
      const comments = extractComments(moveText);

      // Reconstruct cleaned PGN
      const cleanedPgn = headers.join('\n') + '\n\n' + moveText;

      const chess = new Chess();
      chess.loadPgn(cleanedPgn);

      const gameHeaders = chess.getHeaders();
      const moves = chess.history({ verbose: true });

      // Use FEN header as start position if present
      const startFen = gameHeaders['FEN'] || START_FEN;
      const fens: string[] = [startFen];
      for (const move of moves) {
        fens.push(move.after);
      }

      parsed.push({ game: { headers: gameHeaders, moves, fens, comments }, raw: trimmed });
    } catch (err) {
      // Unparsebares Spiel ueberspringen, aber fuer Diagnose sichtbar machen
      // statt es voellig stumm zu verwerfen.
      console.warn('pgn-parser: skipping unparseable game', err);
    }
  }

  return parsed;
}
