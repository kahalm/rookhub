import { Chess } from 'chess.js';
import {
  ExpectedMove, judgeMove, LineSolver, resolveExpectedUci, sameMove,
} from './line-solver';

const START = new Chess().fen();
/** Weiß am Zug, BEIDE Springer (b1 und f3) können nach d2 — der Fall für mehrdeutige SAN. */
const TWO_KNIGHTS = 'rnbqkbnr/ppp1pppp/8/3p4/3P4/5N2/PPP1PPPP/RNBQKB1R w KQkq - 0 3';
const CASTLING = 'r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1';
const PROMOTION = '8/4P3/8/8/8/8/8/4K1k1 w - - 0 1';
/** Weiße Dame auf d7, schwarze Dame auf d8 — Qxd8+ ist der eine Damenzug. */
const QUEEN_TAKES = 'rnbqkbnr/pppQpppp/8/8/8/8/PPPP1PPP/RNB1KBNR w KQkq - 0 1';
const BLACK_TO_MOVE = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1';

describe('resolveExpectedUci — SAN ist Anzeige, Felder sind die Wahrheit', () => {
  it('löst SAN mit Unterscheider auf die Felder auf: Nbd2 = b1d2', () => {
    expect(resolveExpectedUci(new Chess(TWO_KNIGHTS), { san: 'Nbd2' })).toBe('b1d2');
    expect(resolveExpectedUci(new Chess(TWO_KNIGHTS), { san: 'Nfd2' })).toBe('f3d2');
  });

  it('nimmt LANG-ALGEBRAISCHE Schreibweise ohne eigene Vorreinigung an (chess.js, nicht-strikt)', () => {
    // Gemessen an chess.js 1.4.0: Nb1d2/b1d2/Nf3e5 gehen durch, ein eigenes normSan braucht es nicht.
    expect(resolveExpectedUci(new Chess(TWO_KNIGHTS), { san: 'Nb1d2' })).toBe('b1d2');
    expect(resolveExpectedUci(new Chess(TWO_KNIGHTS), { san: 'b1d2' })).toBe('b1d2');
  });

  it('0-0 und O-O sind derselbe Zug; Suffixe zählen nicht', () => {
    expect(resolveExpectedUci(new Chess(CASTLING), { san: '0-0' })).toBe('e1g1');
    expect(resolveExpectedUci(new Chess(CASTLING), { san: 'O-O' })).toBe('e1g1');
    expect(resolveExpectedUci(new Chess(CASTLING), { san: 'O-O+' })).toBe('e1g1');
    expect(resolveExpectedUci(new Chess(CASTLING), { san: '0-0-0' })).toBe('e1c1');
    expect(resolveExpectedUci(new Chess(START), { san: 'Nf3?!' })).toBe('g1f3');
  });

  it('Umwandlung: mit und ohne Gleichheitszeichen, die Figur steht im UCI', () => {
    expect(resolveExpectedUci(new Chess(PROMOTION), { san: 'e8=Q' })).toBe('e7e8q');
    expect(resolveExpectedUci(new Chess(PROMOTION), { san: 'e8Q' })).toBe('e7e8q');
    expect(resolveExpectedUci(new Chess(PROMOTION), { san: 'e8=R' })).toBe('e7e8r');
  });

  it('MEHRDEUTIGE SAN ergibt null — geraten wird nicht', () => {
    // chess.js weist `Nd2` ab, solange zwei Springer dorthin können. Genau so soll es sein:
    // es gibt keinen EINEN gemeinten Zug, und ein geratener wäre schlimmer als gar keiner.
    expect(resolveExpectedUci(new Chess(TWO_KNIGHTS), { san: 'Nd2' })).toBeNull();
  });

  it('ein in dieser Stellung illegaler Zug ergibt null — als SAN wie als UCI', () => {
    expect(resolveExpectedUci(new Chess(START), { san: 'e5' })).toBeNull();
    expect(resolveExpectedUci(new Chess(START), { uci: 'e2e5' })).toBeNull();
    expect(resolveExpectedUci(new Chess(START), { uci: '' })).toBeNull();
    expect(resolveExpectedUci(new Chess(START), null)).toBeNull();
  });

  it('UCI wird gegen die Stellung geprüft und sonst unverändert zurückgegeben', () => {
    expect(resolveExpectedUci(new Chess(START), { uci: 'e2e4' })).toBe('e2e4');
    expect(resolveExpectedUci(new Chess(PROMOTION), { uci: 'E7E8Q' })).toBe('e7e8q');
    expect(resolveExpectedUci(new Chess(PROMOTION), { uci: 'e7e8b' })).toBe('e7e8b');
  });

  it('das übergebene Brett bleibt unangetastet (SAN läuft auf einer Kopie)', () => {
    const chess = new Chess(TWO_KNIGHTS);
    resolveExpectedUci(chess, { san: 'Nbd2' });
    expect(chess.fen()).toBe(TWO_KNIGHTS);
  });
});

describe('sameMove — Felder entscheiden, die Umwandlungsfigur nur, wenn sie genannt ist', () => {
  it('ohne Umwandlungsfigur im Nutzerzug passt jede', () => {
    expect(sameMove('e7e8', 'e7e8q')).toBeTrue();
    expect(sameMove('e7e8', 'e7e8r')).toBeTrue();
  });

  it('eine ANDERE genannte Figur ist ein anderer Zug', () => {
    expect(sameMove('e7e8r', 'e7e8q')).toBeFalse();
    expect(sameMove('e7e8q', 'e7e8q')).toBeTrue();
  });

  it('andere Felder sind immer falsch', () => {
    expect(sameMove('d2d4', 'e2e4')).toBeFalse();
    expect(sameMove('e2e4', 'e2e4')).toBeTrue();
    expect(sameMove('', 'e2e4')).toBeFalse();
    expect(sameMove('e2e4', '')).toBeFalse();
  });

  it('deckt die frühere Sonderregel des Aufgabenblatts ab', () => {
    // Alt: `expected === played || (expected.length === 5 && expected.startsWith(orig + dest))`.
    // Das Blatt urteilt OHNE die Dialog-Figur (die Lösung setzt sie) — und dann sind beide Regeln
    // Zeile für Zeile dasselbe.
    const worksheetRule = (expected: string, orig: string, dest: string) =>
      expected === orig + dest || (expected.length === 5 && expected.startsWith(orig + dest));
    const cases: [string, string, string][] = [
      ['e2e4', 'e2', 'e4'], ['e2e4', 'd2', 'd4'], ['e7e8q', 'e7', 'e8'],
      ['e7e8r', 'e7', 'e8'], ['e7e8q', 'f7', 'f8'], ['g1f3', 'g1', 'f3'],
    ];
    for (const [expected, orig, dest] of cases) {
      expect(sameMove(orig + dest, expected)).toBe(worksheetRule(expected, orig, dest), `${orig}${dest} gegen ${expected}`);
    }
  });
});

describe('judgeMove — Urteil ohne Anwenden', () => {
  const judge = (fen: string, expected: ExpectedMove | null, alts: ExpectedMove[], orig: string, dest: string, promo?: string) =>
    judgeMove(new Chess(fen), expected, alts, orig, dest, promo);

  it('Nbd2 erwartet, der Nutzer zieht b1→d2 ⇒ correct', () => {
    expect(judge(TWO_KNIGHTS, { san: 'Nbd2' }, [], 'b1', 'd2')).toBe('correct');
    expect(judge(TWO_KNIGHTS, { san: 'Nbd2' }, [], 'f3', 'd2')).toBe('wrong');
  });

  it('Qxd8+ erwartet, der Nutzer zieht die Dame ⇒ correct (das + zählt nicht)', () => {
    expect(judge(QUEEN_TAKES, { san: 'Qxd8+' }, [], 'd7', 'd8')).toBe('correct');
  });

  it('Umwandlung: ohne gewählte Figur passt e7e8 auf e7e8q, mit Dame gegen e7e8r nicht', () => {
    expect(judge(PROMOTION, { uci: 'e7e8q' }, [], 'e7', 'e8')).toBe('correct');
    expect(judge(PROMOTION, { uci: 'e7e8q' }, [], 'e7', 'e8', 'q')).toBe('correct');
    expect(judge(PROMOTION, { uci: 'e7e8r' }, [], 'e7', 'e8', 'q')).toBe('wrong');
  });

  it('eine Alternative darf als SAN dastehen', () => {
    expect(judge(START, { san: 'e4' }, [{ san: 'd4' }], 'd2', 'd4')).toBe('alternative');
    expect(judge(START, { san: 'e4' }, [{ san: 'd4' }], 'e2', 'e4')).toBe('correct');
    expect(judge(START, { san: 'e4' }, [{ san: 'd4' }], 'g1', 'f3')).toBe('wrong');
  });

  it('ein in der Stellung nicht spielbarer Zug ⇒ illegal', () => {
    expect(judge(START, { san: 'e4' }, [], 'e2', 'e5')).toBe('illegal');
    expect(judge(START, { san: 'e4' }, [], 'e3', 'e4')).toBe('illegal');   // leeres Startfeld
  });

  it('eine Figur der anderen Farbe ⇒ not-your-turn (nicht „illegal")', () => {
    expect(judge(START, { san: 'e4' }, [], 'e7', 'e5')).toBe('not-your-turn');
  });

  it('ein unauflösbarer Erwartungszug macht KEINEN Zug richtig', () => {
    // Nd2 ist mehrdeutig. Der Nutzerzug bleibt legal und bekommt „wrong"; wer unterscheiden muss,
    // fragt resolveExpectedUci/expectedUci vorher — dort steht null.
    expect(judge(TWO_KNIGHTS, { san: 'Nd2' }, [], 'b1', 'd2')).toBe('wrong');
    expect(resolveExpectedUci(new Chess(TWO_KNIGHTS), { san: 'Nd2' })).toBeNull();
  });
});

describe('LineSolver — Linie, Zählstand, Gegnerzüge', () => {
  const line = (...ucis: string[]): ExpectedMove[] => ucis.map(uci => ({ uci }));

  it('spielt die Linie durch: eigener Zug, Gegnerantwort aus der LINIE, nächster eigener Zug', () => {
    const solver = new LineSolver({ line: line('e2e4', 'e7e5', 'g1f3', 'b8c6') });

    expect(solver.solverColor).toBe('w');
    expect(solver.userToMove).toBeTrue();
    expect(solver.expectedUci()).toBe('e2e4');

    expect(solver.judge('e2', 'e4')).toBe('correct');
    solver.playExpected();
    expect(solver.userToMove).toBeFalse();
    expect(solver.judge('g1', 'f3')).toBe('not-your-turn');

    expect(solver.opponentReply()).toEqual(['e7e5']);
    expect(solver.ply).toBe(2);
    expect(solver.userToMove).toBeTrue();

    expect(solver.judge('g1', 'f3')).toBe('correct');
    solver.playExpected();
    solver.opponentReply();
    expect(solver.done).toBeTrue();
    expect(solver.expected).toBeNull();
    expect(solver.expectedUci()).toBeNull();
    expect(solver.judge('d2', 'd4')).toBe('not-your-turn');
  });

  it('Startstellung mit SCHWARZ am Zug: der Löser ist Schwarz, ohne dass es jemand sagt', () => {
    const solver = new LineSolver({ fen: BLACK_TO_MOVE, line: [{ san: 'e5' }, { san: 'Nf3' }] });

    expect(solver.solverColor).toBe('b');
    expect(solver.userToMove).toBeTrue();
    expect(solver.expectedUci()).toBe('e7e5');
    expect(solver.judge('e7', 'e5')).toBe('correct');

    solver.playExpected();
    expect(solver.opponentReply()).toEqual(['g1f3']);
    expect(solver.done).toBeTrue();
  });

  it('startPly spielt vor; solverColor richtet sich nach der Stellung DANACH', () => {
    const solver = new LineSolver({ line: line('e2e4', 'e7e5', 'g1f3'), startPly: 1 });

    expect(solver.ply).toBe(1);
    expect(solver.solverColor).toBe('b');
    expect(solver.chess.history()).toEqual(['e4']);
    expect(solver.judge('e7', 'e5')).toBe('correct');
  });

  it('undo(2) nimmt zwei Halbzüge samt Zählstand zurück', () => {
    const solver = new LineSolver({ line: line('e2e4', 'e7e5', 'g1f3') });
    solver.playExpected();
    solver.opponentReply();
    expect(solver.ply).toBe(2);

    expect(solver.undo(2)).toBe(2);

    expect(solver.ply).toBe(0);
    expect(solver.chess.fen()).toBe(START);
    expect(solver.lastMove()).toBeUndefined();
    expect(solver.undo(5)).toBe(0);   // nichts mehr da
  });

  it('ein FREIER Zug rückt den Zählstand nicht vor — und sein undo dreht ihn nicht zurück', () => {
    const solver = new LineSolver({ line: line('e2e4', 'e7e5') });
    solver.playExpected();
    solver.opponentReply();
    expect(solver.ply).toBe(2);

    expect(solver.playFree('g1', 'f3')).not.toBeNull();
    expect(solver.ply).toBe(2);
    expect(solver.playFree('g1', 'f3')).toBeNull();   // Feld besetzt → illegal

    solver.undo();
    expect(solver.ply).toBe(2);
    expect(solver.lastMove()).toEqual(['e7', 'e5']);
  });

  it('geduldete Alternativen hängen am HALBZUG, nicht an der Linie', () => {
    const solver = new LineSolver({
      line: line('e2e4', 'e7e5', 'g1f3'),
      alts: { 0: [{ san: 'd4' }], 2: [{ san: 'Bc4' }] },
    });

    expect(solver.judge('d2', 'd4')).toBe('alternative');
    solver.playExpected();
    solver.opponentReply();
    expect(solver.judge('f1', 'c4')).toBe('alternative');
    expect(solver.judge('d2', 'd4')).toBe('wrong');   // Alternative des ERSTEN Halbzugs zählt hier nicht
  });

  it('playExpected meldet eine unauflösbare Linie, statt etwas zu spielen', () => {
    const solver = new LineSolver({ fen: TWO_KNIGHTS, line: [{ san: 'Nd2' }] });

    expect(solver.playExpected()).toBeNull();
    expect(solver.ply).toBe(0);
    expect(solver.chess.fen()).toBe(TWO_KNIGHTS);
  });

  it('reset stellt die Ausgangsstellung samt Vorspiel wieder her', () => {
    const solver = new LineSolver({ line: line('e2e4', 'e7e5', 'g1f3'), startPly: 1 });
    const after = solver.chess.fen();
    solver.playExpected();
    solver.playFree('g1', 'f3');

    solver.reset();

    expect(solver.chess.fen()).toBe(after);
    expect(solver.ply).toBe(1);
    expect(solver.userToMove).toBeTrue();
  });

  it('eine unbrauchbare FEN ergibt das Ersatzbrett und sagt es', () => {
    const solver = new LineSolver({ fen: 'unsinn' });
    expect(solver.startFenAccepted).toBeFalse();
    expect(solver.chess.fen()).toBe(START);

    const ok = new LineSolver({ fen: BLACK_TO_MOVE });
    expect(ok.startFenAccepted).toBeTrue();
  });

  it('dests und lastMove bedienen das Brett direkt', () => {
    const solver = new LineSolver({ line: line('e2e4') });
    expect(solver.dests().get('e2')).toEqual(['e3', 'e4']);
    solver.playExpected();
    expect(solver.lastMove()).toEqual(['e2', 'e4']);
    expect(solver.chess.turn()).toBe('b');
  });

  it('erkennt Matt über das gelesene Brett (die Löser fragen chess selbst)', () => {
    // Narrenmatt: die Linie endet mit Matt, `chess` ist ausdrücklich lesbar.
    const solver = new LineSolver({ line: line('f2f3', 'e7e5', 'g2g4', 'd8h4') });
    while (!solver.done && solver.playExpected() !== null) { /* durchspielen */ }
    expect(solver.done).toBeTrue();
    expect(solver.chess.isCheckmate()).toBeTrue();
  });
});
