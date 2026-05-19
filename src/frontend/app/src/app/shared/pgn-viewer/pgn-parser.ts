import { Chess, Move } from 'chess.js';

export interface ParsedGame {
  headers: Record<string, string>;
  moves: Move[];
  fens: string[];
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
      if (depth === 0) result += ch;
      inComment = true;
    } else if (ch === '}') {
      if (depth === 0) result += ch;
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

export function parsePgnText(pgnText: string): ParsedGame[] {
  const rawGames = pgnText.split(/\n\n(?=\[Event )/);
  const parsed: ParsedGame[] = [];

  for (const raw of rawGames) {
    const trimmed = raw.trim();
    if (!trimmed) continue;

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

      // Clean move text: strip variations, NAGs, and special annotations
      moveText = stripVariations(moveText);
      moveText = stripNags(moveText);

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

      parsed.push({ headers: gameHeaders, moves, fens });
    } catch {
      // Skip unparseable games
    }
  }

  return parsed;
}
