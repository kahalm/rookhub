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

export function parsePgnText(pgnText: string): ParsedGame[] {
  if (pgnText.length > MAX_PGN_CHARS) {
    pgnText = pgnText.slice(0, MAX_PGN_CHARS);
  }
  const rawGames = pgnText.split(/\n\n(?=\[Event )/).slice(0, MAX_GAMES);
  const parsed: ParsedGame[] = [];

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

      // Clean move text: strip variations and NAGs
      moveText = stripVariations(moveText);
      moveText = stripNags(moveText);

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

      parsed.push({ headers: gameHeaders, moves, fens, comments });
    } catch (err) {
      // Unparsebares Spiel ueberspringen, aber fuer Diagnose sichtbar machen
      // statt es voellig stumm zu verwerfen.
      console.warn('pgn-parser: skipping unparseable game', err);
    }
  }

  return parsed;
}
