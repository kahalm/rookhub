import { Chess } from 'chess.js';
import { Key } from 'chessground/types';
import { replayIllegalFen } from './illegal-board.util';
import { applyUci, tryLoadFen } from './puzzle-move.util';
import { buildCommentSegments, CommentSegment } from './comment-variation.util';

/**
 * Durchklick-Regeln einer Linie (Brett nach `index` Halbzügen, Kommentar-Segmente), geteilt von den
 * Solvern (`BasePuzzleSolver.reviewGoToCore`, `BookPuzzleComponent.reviewGoTo`) und der Kurs-Durchsicht
 * (`CourseBrowseComponent.goTo`) — vorher dort jeweils ausgeschrieben (Codereview F3-014): eine
 * Regel-Änderung am Solver erreichte die Durchsicht nicht.
 */

/** Brett-Stand nach `index` gespielten Halbzügen einer Linie ab ihrer FEN. */
export interface LineStep {
  /** Auf [0, ucis.length] geklemmter Index. */
  index: number;
  /** Anzeige-FEN (bei illegaler Diagramm-FEN die per Koordinaten nachgespielte). */
  fen: string;
  /** Zuletzt gespielter Zug fürs Highlight; undefined bei Index 0. */
  lastMove?: [Key, Key];
  turnColor: 'white' | 'black';
  isCheck: boolean;
  /** chess.js-Stellung nach den Zügen; `null`, wenn chess.js die FEN verwirft (Chessable-Muster-/Info-
   *  Diagramm ohne König o. Ä.) — dann ist die Stellung rein per Koordinaten nachgespielt, ohne Schach. */
  chess: Chess | null;
}

/**
 * Stellung nach den ersten `index` UCI-Zügen ab `fen`. Legale FEN: über chess.js (ein unspielbarer Zug
 * wirft wie bisher). Von chess.js abgelehnte FEN: {@link replayIllegalFen} (wie im Buch-Solver,
 * `renderStaticInfo`).
 */
export function lineStepAt(fen: string, ucis: readonly string[], index: number): LineStep {
  index = Math.max(0, Math.min(index, ucis.length));
  const chess = tryLoadFen(fen);
  if (!chess) {
    const replay = replayIllegalFen(fen, [...ucis], index);
    return {
      index,
      fen: replay.fen,
      lastMove: replay.lastMove as [Key, Key] | undefined,
      turnColor: replay.whiteToMove ? 'white' : 'black',
      isCheck: false,
      chess: null,
    };
  }
  let last: [Key, Key] | undefined;
  for (let i = 0; i < index; i++) {
    applyUci(chess, ucis[i]);
    last = [ucis[i].substring(0, 2) as Key, ucis[i].substring(2, 4) as Key];
  }
  return {
    index,
    fen: chess.fen(),
    lastMove: last,
    turnColor: chess.turn() === 'w' ? 'white' : 'black',
    isCheck: chess.isCheck(),
    chess,
  };
}

/**
 * Kommentar-Absätze in klickbare Segmente zerlegt (Text + spielbare Zug-Chips), gecacht je Linie +
 * Kommentar-Inhalt — die Auflösung hängt nur von Linien-FEN/-Zügen ab, nicht vom Ply. Ohne FEN bleibt
 * jeder Absatz reiner Text.
 */
export class CommentBlockCache {
  private key = '';
  private blocks: CommentSegment[][] = [];

  /** `ucis` wird nur gelesen, wenn neu aufgelöst werden muss. */
  get(lineId: number, lines: string[], fen: string, ucis: () => string[]): CommentSegment[][] {
    // Trenner, der in keinem Kommentar vorkommt — sonst ergäben ["ab", "c"] und ["a", "bc"] denselben Schlüssel.
    const key = lineId + '|' + lines.join('\u0001');
    if (key !== this.key) {
      this.blocks = fen ? lines.map(l => buildCommentSegments(l, fen, ucis())) : lines.map(l => [{ text: l }]);
      this.key = key;
    }
    return this.blocks;
  }
}
