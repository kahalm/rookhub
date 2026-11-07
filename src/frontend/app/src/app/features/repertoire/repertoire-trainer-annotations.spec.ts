import { parsePgnText } from '../../shared/pgn-viewer/pgn-parser';
import { buildRepertoireGraph } from './repertoire-tree.util';
import { TrainerClock, TrainerMode, TrainerSession } from './repertoire-trainer-session';

/** Von Hand weitergestellte Uhr (wie in repertoire-trainer-session.spec.ts). */
class ManualClock implements TrainerClock {
  private now = 0;
  private seq = 0;
  private timers: { id: number; at: number; fn: () => void }[] = [];
  setTimeout(fn: () => void, ms: number): unknown {
    const id = ++this.seq;
    this.timers.push({ id, at: this.now + ms, fn });
    return id;
  }
  clearTimeout(handle: unknown): void { this.timers = this.timers.filter(t => t.id !== handle); }
  advance(ms: number): void {
    const until = this.now + ms;
    for (;;) {
      const next = [...this.timers].sort((a, b) => a.at - b.at || a.id - b.id)[0];
      if (!next || next.at > until) break;
      this.timers = this.timers.filter(t => t !== next);
      this.now = next.at;
      next.fn();
    }
    this.now = until;
  }
}

/** Der Fall des Users: Prod-Repertoire 294, Chessable-Linie 34736216 (Schwarz), Kommentare gekürzt. */
const USER_LINE = [
  '[Event "Schandorff"]', '[White "English 2.e4 #2"]', '[Black "15) English and Reti"]', '[ChessableColor "black"]', '',
  '{Einleitung.} 1. c4 e6 {[%alt Nf6 e5 c6]} 2. e4 d5 3. exd5 exd5 4. d4 (4.cxd5 Nf6 5.Bb5+ Nbd7) 4... Nf6 5. Nc3 Bb4',
  '6. Bd3 (6.Nf3 O-O) 6... dxc4 7. Bxc4 O-O 8. Nge2 Nbd7 9. O-O Nb6 10. Bb3 c6 {[%alt Re8 h6 Bd6]} 11. Bg5 Re8',
  '{[%cal Gc8e6][%csl Rd4]The position is balanced, but I would rather be Black because I love to play against an isolated pawn!} *', '',
].join('\n');
const NEXT_LINE = '[Event "R"]\n[White "n"]\n[Black "B"]\n\n1. d4 d5 2. c4 e6 *\n';
/** Eigener Zug mit Pfeil, Gegnerzug mit Feld. */
const MID = '[Event "R"]\n[White "m"]\n[Black "A"]\n\n1. e4 {[%cal Ge4d5]Zentrum.} e5 {[%csl Rf7]} 2. Nf3 Nc6 *\n';

const FINAL = [{ orig: 'c8', dest: 'e6', brush: 'green' }, { orig: 'd4', brush: 'red' }];

function setup(pgn: string, color: 'w' | 'b', mode: TrainerMode = 'quiz') {
  const clock = new ManualClock();
  const s = new TrainerSession({
    changed: () => {}, colorOf: () => color, lineLearned: () => {}, lineReviewed: () => {}, wrongMove: () => {},
    retryAfterWrong: () => {},
  }, clock);
  s.mode = mode;
  s.graph = buildRepertoireGraph(pgn);
  return { s, clock, lines: parsePgnText(pgn) };
}

/** Die Linie mit den richtigen Zügen bis zum „Linie fertig"-Kasten durchspielen (Abfragen und Lernen). */
function playToEnd(s: TrainerSession, clock: ManualClock): void {
  for (let guard = 0; guard < 200 && s.phase !== 'LINE_DONE'; guard++) {
    if (s.phase === 'COMMENT' || (s.phase === 'LEARN_SHOW' && s.learnComment)) s.tap();
    else if (s.phase === 'PLAYING' && !s.oppMoving) {
      const m = s.currentLine!.moves[s.currentPly];
      s.onMove({ orig: m.from as any, dest: m.to as any });
    }
    clock.advance(5000);
  }
}

describe('Pfeile/Felder aus dem PGN-Kommentar im Repertoire-Trainer (0.887.0, ab 0.893.0 nur am Linienende)', () => {
  it('der Parser reicht [%cal]/[%csl] je Halbzug durch, der Kommentartext bleibt ohne die Befehle', () => {
    const [g] = parsePgnText(USER_LINE);
    const last = g.moves.length - 1;
    expect(g.moves[last].san).toBe('Re8');
    expect(g.shapes?.[last]).toEqual(FINAL as any);
    expect(g.comments[last].startsWith('The position is balanced')).toBeTrue();
    expect(g.comments[last]).not.toContain('[%');
    expect(g.shapes?.[last - 2]).toBeUndefined();                 // {[%alt …]} ist kein Pfeil
  });

  it('Schlussstellung (Fall des Users, Schwarz, Abfragen): c8e6 grün + d4 rot, bis zur nächsten Linie', () => {
    const { s, clock, lines } = setup(USER_LINE + '\n' + NEXT_LINE, 'b');
    s.start(lines);
    playToEnd(s, clock);
    expect(s.phase).toBe('LINE_DONE');
    expect(s.boardShapes).toEqual(FINAL as any);
    clock.advance(60_000);                                         // bleibt stehen
    expect(s.boardShapes).toEqual(FINAL as any);
    s.continueLine();
    expect(s.qIndex).toBe(1);
    expect(s.boardShapes).toEqual([]);
  });

  it('Lernmodus: auch dort stehen die Marker in der Schlussstellung', () => {
    const { s, clock, lines } = setup(USER_LINE, 'b', 'learn');
    s.start(lines);
    playToEnd(s, clock);
    expect(s.phase).toBe('LINE_DONE');
    expect(s.boardShapes).toEqual(FINAL as any);
  });

  it('während des Spiels keine Kommentar-Formen (0.893.0) — weder nach dem eigenen noch nach dem Gegnerzug', () => {
    const { s, clock, lines } = setup(MID, 'w');
    s.start(lines);
    expect(s.boardShapes).toEqual([]);
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(s.boardShapes).toEqual([]);                             // 1. e4 hätte den Pfeil e4d5
    clock.advance(300);
    expect(s.oppMoving).toBeTrue();
    expect(s.boardShapes).toEqual([]);
    clock.advance(1000);                                           // 1… e5 gespielt (Feld f7 im Kommentar)
    expect(s.oppMoving).toBeFalse();
    expect(s.boardShapes).toEqual([]);
  });

  it('Tipp-Pfeil und Fehlzug bleiben während des Spiels (ohne Kommentar-Formen)', () => {
    const { s, clock, lines } = setup(MID, 'w');
    s.start(lines);
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    clock.advance(2000);
    s.hint();
    expect(s.boardShapes).toEqual([{ orig: 'g1', brush: 'green' }] as any);
    s.hint();
    expect(s.boardShapes).toEqual([{ orig: 'g1', dest: 'f3', brush: 'green' }] as any);
    s.onMove({ orig: 'a2' as any, dest: 'a3' as any });           // Fehlzug bleibt stehen
    expect(s.wrongShown).toBeTrue();
    expect(s.boardShapes).toEqual([{ orig: 'a2', dest: 'a3', brush: 'red' }] as any);
    s.tap();                                                       // zurückgenommen: der laufende Tipp bleibt
    expect(s.boardShapes).toEqual([{ orig: 'g1', dest: 'f3', brush: 'green' }] as any);
  });

  it('Lernmodus: der vorgezeigte Zug zeigt KEINEN Pfeil mehr, der Kommentartext steht ohne [%…]', () => {
    const { s, lines } = setup(MID, 'w', 'learn');
    s.start(lines);
    expect(s.phase).toBe('LEARN_SHOW');
    expect(s.learnComment).toBe('Zentrum.');
    expect(s.boardShapes).toEqual([]);
    s.tap();
    expect(s.boardShapes).toEqual([]);
  });

  it('Nachbetrachtung: die Formen folgen der gewählten Stellung', () => {
    const { s, clock, lines } = setup(MID, 'w');
    s.start(lines);
    playToEnd(s, clock);
    expect(s.phase).toBe('LINE_DONE');
    expect(s.boardShapes).toEqual([]);                             // Schluss 2… Nc6 ohne Marker
    s.reviewTo(1);
    expect(s.boardShapes).toEqual([{ orig: 'e4', dest: 'd5', brush: 'green' }] as any);
    s.reviewTo(2);
    expect(s.boardShapes).toEqual([{ orig: 'f7', brush: 'red' }] as any);
    s.reviewTo(0);
    expect(s.boardShapes).toEqual([]);
  });

  it('dieselbe Markierung bleibt dasselbe Array (OnPush-Input zeichnet nicht dauernd neu)', () => {
    const { s, clock, lines } = setup(USER_LINE, 'b');
    s.start(lines);
    playToEnd(s, clock);
    expect(s.boardShapes).toBe(s.boardShapes);
  });
});

/** Kommentare der zuletzt gespielten Züge für „Kommentare zeigen" (0.893.0). */
describe('TrainerSession.playedComments (Kommentare zeigen, 0.893.0)', () => {
  const COMM = '[Event "R"]\n[White "c"]\n[Black "A"]\n\n1. e4 {[%cal Ge4d5]Zentrum.} e5 {[%csl Rf7]Antwort [%clk 0:01:00] des Gegners.} 2. Nf3 Nc6 *\n';

  it('Abfragen: eigener Zug sofort, nach dem Gegnerzug beide — ohne [%…], ohne Halt', () => {
    const { s, clock, lines } = setup(COMM, 'w');
    s.start(lines);
    expect(s.playedComments).toEqual([]);
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    expect(s.playedComments.map(c => [c.label, c.paragraphs.join()])).toEqual([['1. e4', 'Zentrum.']]);
    clock.advance(2000);
    expect(s.phase).toBe('PLAYING');                               // kein Halt
    const all = s.playedComments;
    expect(all.map(c => c.ply)).toEqual([0, 1]);
    expect(all[1].paragraphs.join()).not.toContain('[%');
    expect(all[1].paragraphs.join()).toContain('des Gegners.');
    expect(s.playedComments).toBe(all);                            // gleiche Liste = gleiches Array (OnPush)
    s.onMove({ orig: 'g1' as any, dest: 'f3' as any });
    expect(s.playedComments.map(c => c.ply)).toEqual([1]);         // 2. Nf3 ohne Kommentar → nur noch die Antwort davor
  });

  it('am Linienende keine (die Nachbetrachtung zeigt den Kommentar je Stellung)', () => {
    const { s, clock, lines } = setup(COMM, 'w');
    s.start(lines);
    playToEnd(s, clock);
    expect(s.phase).toBe('LINE_DONE');
    expect(s.playedComments).toEqual([]);
    s.reviewTo(1);
    expect(s.reviewComment).toBe('Zentrum.');
  });

  it('Lernmodus: der Kommentar des vorgezeigten Zugs steht nur in der Kontextkarte, der Gegnerkommentar nur im Halt', () => {
    const { s, clock, lines } = setup(COMM, 'w', 'learn');
    s.start(lines);
    expect(s.phase).toBe('LEARN_SHOW');
    expect(s.playedComments).toEqual([]);                          // Halbzug 0 = currentComment (Kontextkarte)
    s.tap();                                                       // zurückgenommen
    s.onMove({ orig: 'e2' as any, dest: 'e4' as any });
    clock.advance(2000);                                           // 1… e5 mit Kommentar → Halt
    expect(s.phase).toBe('COMMENT');
    expect(s.holdComment).toContain('des Gegners.');
    expect(s.playedComments.map(c => c.ply)).toEqual([0]);         // der eigene Zug, nicht die Antwort doppelt
  });
});

