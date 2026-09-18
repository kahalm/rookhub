import { extractSanTokens, resolveVariation, buildCommentSegments, splitBranches } from './comment-variation.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('comment-variation.util', () => {
  it('extractSanTokens: liest nur die reinen Züge (ohne Zugnummern)', () => {
    expect(extractSanTokens('Besser war 2.Nf3 und dann 2...Nc6.')).toEqual(['Nf3', 'Nc6']);
    expect(extractSanTokens('… 2…Kc7 …')).toEqual(['Kc7']);   // typografisches Ellipsis
    expect(extractSanTokens('kein Zug hier')).toEqual([]);
  });

  it('resolveVariation: findet die Hauptlinien-Stellung, aus der die Folge legal ist', () => {
    // Hauptlinie 1.e4 e5 → nach e5 (Weiß am Zug, Vollzug 2) sind Nf3 (Weiß) + Nc6 (Schwarz) legal.
    const steps = resolveVariation(START, ['e2e4', 'e7e5'], ['Nf3', 'Nc6']);
    expect(steps.map(s => s.san)).toEqual(['Nf3', 'Nc6']);
    expect(steps[0].from).toBe('g1');
    expect(steps[0].to).toBe('f3');
    expect(steps[1].from).toBe('b8');
    expect(steps[1].to).toBe('c6');
    // FEN nach Nf3 hat den Springer auf f3 und Schwarz am Zug.
    expect(steps[0].fen.split(' ')[1]).toBe('b');
  });

  it('resolveVariation: illegaler Zug bricht den Präfix ab', () => {
    // Nf3 legal, dann „Kd4" (illegal) → nur der erste Schritt zählt.
    const steps = resolveVariation(START, ['e2e4', 'e7e5'], ['Nf3', 'Kd4']);
    expect(steps.map(s => s.san)).toEqual(['Nf3']);
  });

  it('resolveVariation: nirgends spielbar → leer', () => {
    expect(resolveVariation(START, ['e2e4', 'e7e5'], ['Kd4'])).toEqual([]);
  });

  it('buildCommentSegments: klickbare Zug-Chips + Text; nicht spielbare Züge bleiben Text', () => {
    const segs = buildCommentSegments('Besser war 2.Nf3 dann 2...Nc6.', START, ['e2e4', 'e7e5']);
    // Reihenfolge: Text, Zug, Text, Zug, Text
    expect(segs[0].text).toBe('Besser war ');
    expect(segs[1].move).toBe('2.Nf3');
    expect(segs[1].fen).toBeTruthy();
    expect(segs[1].from).toBe('g1');
    expect(segs[2].text).toBe(' dann ');
    expect(segs[3].move).toBe('2...Nc6');
    expect(segs[4].text).toBe('.');

    // Nicht spielbarer „Zug" bleibt Text (kein move-Segment).
    const segs2 = buildCommentSegments('Nicht Kd4 spielbar.', START, ['e2e4', 'e7e5']);
    expect(segs2.every(s => !s.move)).toBeTrue();
    expect(segs2.map(s => s.text).join('')).toBe('Nicht Kd4 spielbar.');
  });

  it('splitBranches: teilt an Zugnummer-Rücksprüngen (das „2. 39" ist ein neuer Zweig)', () => {
    // Zweig A endet bei Zug 43; „40.b5" springt zurück → neuer Zweig B.
    expect(splitBranches('39...d4 40.Kf4 43.h4 a4 . White wins after 40.b5 a3'))
      .toEqual([['d4', 'Kf4', 'h4', 'a4'], ['b5', 'a3']]);
    // Ein einzelner durchgehender Zweig bleibt einer.
    expect(splitBranches('2.Nf3 Nc6 3.Bb5')).toEqual([['Nf3', 'Nc6', 'Bb5']]);
  });

  it('buildCommentSegments: ein zweiter, unabhängiger Zweig wird ebenfalls klickbar', () => {
    // Flach würde „2.Bc4" als Fortsetzung nach 3.Nxe5 illegal (Schwarz am Zug) und ginge als Text verloren.
    const segs = buildCommentSegments('2.Nf3 Nc6 3.Nxe5 sonst 2.Bc4.', START, ['e2e4', 'e7e5']);
    const moves = segs.filter(s => s.move).map(s => s.move);
    expect(moves).toEqual(['2.Nf3', 'Nc6', '3.Nxe5', '2.Bc4']);
    // Der Zweig-B-Zug hat eine echte Vorschau-Stellung.
    const bc4 = segs.find(s => s.move === '2.Bc4')!;
    expect(bc4.fen).toBeTruthy();
    expect(bc4.from).toBe('f1');
    expect(bc4.to).toBe('c4');
  });

  it('deutsche Notation (D/T/L/S) wird erkannt und normalisiert', () => {
    // Sf3 = Nf3, Sc6 = Nc6.
    const segs = buildCommentSegments('Besser 2.Sf3 Sc6.', START, ['e2e4', 'e7e5']);
    const moves = segs.filter(s => s.move);
    expect(moves.map(s => s.move)).toEqual(['2.Sf3', 'Sc6']);   // Chip zeigt den Original-Wortlaut
    expect(moves[0].from).toBe('g1');
    expect(moves[0].to).toBe('f3');
    // Deutsche Umwandlung a8=D → a8=Q.
    const promo = resolveVariation('8/P7/8/8/8/8/8/k6K w - - 0 1', [], ['a8=D']);
    expect(promo.map(s => s.san)).toEqual(['a8=D']);
    expect(promo[0].fen.split(' ')[0]).toContain('Q');   // Dame steht auf dem Brett
  });

  it('Alternativen („c5, oder a5") sind getrennte Zweige, KEINE Weiß-dann-Schwarz-Folge', () => {
    expect(splitBranches('a move like c5, or a5 as neither')).toEqual([['c5'], ['a5']]);

    // Weiß am Zug, Bauern a4+c4 (plus schwarzer a7-Bauer, der die alte Fehl-Deutung erlaubte).
    const fen = '7k/p7/8/8/P1P5/8/8/7K w - - 0 1';
    const segs = buildCommentSegments('… nach einem Zug wie c5, oder a5 …', fen, []);
    const c5 = segs.find(s => s.move === 'c5')!;
    const a5 = segs.find(s => s.move === 'a5')!;
    expect(c5.from).toBe('c4');
    expect(a5.from).toBe('a4');   // Weißzug a4–a5 (NICHT der schwarze a7-Bauer)
  });

  it('Feldangaben („auf d6", „on d6", „e5-Feld") sind keine Züge', () => {
    const main = ['e2e4', 'e7e5'];
    const chips = (t: string) => buildCommentSegments(t, START, main).filter(s => s.move).map(s => s.move);
    for (const t of ['Der Springer auf d6 stört nicht.', 'The knight on d6 is strong.', 'Das e5-Feld ist schwach.',
      'the d6 square', 'Der d6-Bauer bleibt rückständig.', 'Über d5 kommt der Springer auf f4.']) {
      expect(chips(t)).withContext(t).toEqual([]);
      expect(buildCommentSegments(t, START, main).map(s => s.move ?? s.text).join('')).withContext(t).toBe(t);
    }
    expect(extractSanTokens('Springer auf d6, danach d5')).toEqual(['d5']);
    // Züge bleiben Züge: ohne Präposition, mit Zugnummer, und nach „nach" (= „nach dem Zug").
    expect(chips('Schwarz spielt d6.')).toEqual(['d6']);
    expect(chips('Auf 2.d4 folgt exd4.')).toEqual(['2.d4', 'exd4']);
    expect(chips('nach d4 steht Weiß gut')).toEqual(['d4']);
  });

  it('Komma vor nummeriertem Zug ist Fortsetzung, kein Zweig-Bruch', () => {
    expect(splitBranches('40.b5 a3, 41.b6 a2')).toEqual([['b5', 'a3', 'b6', 'a2']]);
  });

  it('ILLEGALE Diagramm-FEN (Chessable-Muster ohne König): kein Wurf, alles bleibt Text', () => {
    // Echte Info-Linie „📝64" aus Buch 16 (Stellung ohne Könige) samt Original-Kommentar. chess.js
    // verwirft die FEN — der Kommentar MUSS trotzdem als Text herauskommen (vorher warf der Parser
    // in den commentBlocks-Getter und riss die halbe Solver-Ansicht mit).
    const fen = 'rn6/pp6/1P6/8/Q7/8/8/8 w - - 0 1';
    const text = 'If the rook captures the queen, 1...Rxa7 2.bxa7 comes with a double threat of 3.a8=Q and 3.axb8=Q .';
    const segs = buildCommentSegments(text, fen, []);
    expect(segs.every(s => !s.move)).toBeTrue();          // nichts klickbar (nicht validierbar)
    expect(segs.map(s => s.text).join('')).toBe(text);    // aber der volle Text ist da
    expect(resolveVariation(fen, [], ['Rxa7', 'bxa7'])).toEqual([]);
  });
});

describe('comment-variation.util — echte Chessable-Linie (Kurs 128648, oid 20733162)', () => {
  // Hauptlinie + Kommentare, wie der RookHub-Import sie ab Pipeline 19 speichert. Die Soll-Stellungen
  // sind Chessables EIGENE data-fen je klickbarem Variantenzug (aus der Kursseite mitgeschnitten).
  const MAIN = ('e2e4 e7e6 g1f3 d7d5 e4e5 c7c5 b2b4 c5b4 a2a3 b4a3 c1a3 f8a3 b1a3 g8e7 a3b5 e8g8 b5a7 c8d7')
    .split(' ');
  // chess.js und Chessable setzen das En-passant-Feld unterschiedlich; verglichen wird ohne es.
  const noEp = (fen: string | undefined) => { const f = (fen ?? '').split(' '); f[3] = '-'; return f.join(' '); };
  const moves = (text: string) => buildCommentSegments(text, START, MAIN).filter(s => s.move);

  it('Varianten zu 3.e5: alle Züge klickbar, Stellungen wie auf Chessable', () => {
    const text = '3.Nc3 Nf6 4.e5 Nfd7 5.d4 werden wir in einem späteren Kapitel der Steinitz Variante sehen. '
      + '3.exd5 exd5 4.d4 geht über in die Abtauschvariante, mit der wir uns später beschäftigen werden. '
      + '3.d3 ist hier in der Zugfolge 2.d3 d5 3.Nf3 analysiert.';
    const m = moves(text);
    expect(m.map(s => s.move)).toEqual(
      ['3.Nc3', 'Nf6', '4.e5', 'Nfd7', '5.d4', '3.exd5', 'exd5', '4.d4', '3.d3', '2.d3', 'd5', '3.Nf3']);
    expect(m.map(s => noEp(s.fen))).toEqual([
      'rnbqkbnr/ppp2ppp/4p3/3p4/4P3/2N2N2/PPPP1PPP/R1BQKB1R b KQkq - 1 3',
      'rnbqkb1r/ppp2ppp/4pn2/3p4/4P3/2N2N2/PPPP1PPP/R1BQKB1R w KQkq - 2 4',
      'rnbqkb1r/ppp2ppp/4pn2/3pP3/8/2N2N2/PPPP1PPP/R1BQKB1R b KQkq - 0 4',
      'rnbqkb1r/pppn1ppp/4p3/3pP3/8/2N2N2/PPPP1PPP/R1BQKB1R w KQkq - 1 5',
      'rnbqkb1r/pppn1ppp/4p3/3pP3/3P4/2N2N2/PPP2PPP/R1BQKB1R b KQkq - 0 5',
      'rnbqkbnr/ppp2ppp/4p3/3P4/8/5N2/PPPP1PPP/RNBQKB1R b KQkq - 0 3',
      'rnbqkbnr/ppp2ppp/8/3p4/8/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 4',
      'rnbqkbnr/ppp2ppp/8/3p4/3P4/5N2/PPP2PPP/RNBQKB1R b KQkq - 0 4',
      'rnbqkbnr/ppp2ppp/4p3/3p4/4P3/3P1N2/PPP2PPP/RNBQKB1R b KQkq - 0 3',
      'rnbqkbnr/pppp1ppp/4p3/8/4P3/3P4/PPP2PPP/RNBQKBNR b KQkq - 0 2',
      'rnbqkbnr/ppp2ppp/4p3/3p4/4P3/3P4/PPP2PPP/RNBQKBNR w KQkq - 0 3',
      'rnbqkbnr/ppp2ppp/4p3/3p4/4P3/3P1N2/PPP2PPP/RNBQKB1R b KQkq - 1 3',
    ]);
  });

  it('Variante zu 4.b4: nach dem Verweis „2.Sf3" wird „4.c3 … 5.d4" an Zug 4 aufgelöst', () => {
    const text = 'Dieses Gambit ist die einzige unabhängige Variante, die nach 2.Sf3 entstehen kann. '
      + '4.c3 ist ein viel besserer Zug, aber führt auch nur zu einer Transposition: '
      + '4...Nc6 5.d4 würde zur Vorstoßvariante überleiten, die wir uns später in eigenen Kapiteln anschauen werden.';
    const m = moves(text);
    expect(m.map(s => s.move)).toEqual(['2.Sf3', '4.c3', '4...Nc6', '5.d4']);
    expect(m.map(s => noEp(s.fen))).toEqual([
      'rnbqkbnr/pppp1ppp/4p3/8/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2',   // Hauptzug 2.Nf3 (deutsch „Sf3")
      'rnbqkbnr/pp3ppp/4p3/2ppP3/8/2P2N2/PP1P1PPP/RNBQKB1R b KQkq - 0 4',
      'r1bqkbnr/pp3ppp/2n1p3/2ppP3/8/2P2N2/PP1P1PPP/RNBQKB1R w KQkq - 1 5',
      'r1bqkbnr/pp3ppp/2n1p3/2ppP3/3P4/2P2N2/PP3PPP/RNBQKB1R b KQkq - 0 5',
    ]);
  });

  it('Einleitung: „2.d4" ist wie auf Chessable klickbar', () => {
    const m = moves('Es gibt sicher nichts weniger kritisches als wenn unser Gegner nicht ausnutzt, dass er 2.d4 spielen kann.');
    expect(m.map(s => s.move)).toEqual(['2.d4']);
    expect(noEp(m[0].fen)).toBe(noEp('rnbqkbnr/pppp1ppp/4p3/8/3PP3/8/PPP2PPP/RNBQKBNR b KQkq d3 0 2'));
  });

  it('„Springer auf d6" ist eine Feldangabe, kein klickbarer Zug', () => {
    const t = 'Wir haben keine Angst vor dem Springer auf d6, denn wir werden mit unserem typischen ...f7-f6-Hebel immer das Zentrum sprengen können.';
    expect(moves(t)).toEqual([]);
  });

  it('die Zweigbildung selbst bleibt unverändert (Folge mit Nummernlücke ist EIN Zweig)', () => {
    expect(splitBranches('nach 2.Sf3 kann 4.c3 4...Sc6 5.d4')).toEqual([['Sf3', 'c3', 'Sc6', 'd4']]);
  });

  it('Vorwärts-Sprung außerhalb der Hauptlinie wird nicht geraten', () => {
    // Hauptlinie nur 1.e4 e5: „9.Lb5" hat keine Stellung → bleibt Text, auch wenn Lb5 anderswo ginge.
    const segs = buildCommentSegments('erst 2.Sf3 und später 9.Lb5', START, ['e2e4', 'e7e5']);
    expect(segs.filter(s => s.move).map(s => s.move)).toEqual(['2.Sf3']);
    expect(segs.map(s => s.move ?? s.text).join('')).toBe('erst 2.Sf3 und später 9.Lb5');
  });

  it('Vorwärts-Sprung, dessen Rest an seiner Stellung illegal ist, bleibt Text', () => {
    // „4.Dh5" ist nach 3...c5 kein legaler Zug (Läufer/Bauern im Weg) → kein Chip.
    const m = moves('nach 2.Sf3 wäre 4.Dh5 Unsinn');
    expect(m.map(s => s.move)).toEqual(['2.Sf3']);
  });
});

describe('comment-variation.util — in Prosa ERWÄHNTER Zug (Kurs 421, oid 73000259)', () => {
  // Echte Linie „Mapuranga vs. Dehtiarov" samt Kommentar, wie importiert. Nach 70…c6 waere „Rd5+"
  // der Halbzug 140 — den beansprucht „71.Rc3+" selbst, also ist Rd5+ nur erwaehnt („Preventing
  // Rd5+ runs into mate!"). Frueher haengte der Parser Rd5+ an die Variante; danach war Schwarz am
  // Zug, 71.Rc3+ wurde illegal, und die Mattfuehrung blieb unklickbar (gemeldet 2026-09-18).
  const FEN = '8/2p5/5Kp1/2k4p/5R1n/2R2P1r/8/8 b - - 0 68';
  const MAIN = 'c5d5 c3d3 d5c5 f6e6 h3h1 d3d5 c5c6 f4c4 c6b6 e6d7'.split(' ');
  const TEXT = '70...c6 Preventing Rd5+ runs into mate! 71.Rc3+ Kb5 72.Rb3+ Kc5 73.Rbb4!! '
    + 'Do your puzzles and you can find incredible mating patterns!';

  it('setzt die Variante bei 71.Rc3+ fort und laesst die Erwaehnung Text', () => {
    const segs = buildCommentSegments(TEXT, FEN, MAIN);
    expect(segs.filter(s => s.move).map(s => s.move))
      .toEqual(['70...c6', '71.Rc3+', 'Kb5', '72.Rb3+', 'Kc5', '73.Rbb4']);
    // Rd5+ bleibt im Fliesstext stehen (nicht klickbar), der Wortlaut aendert sich nicht.
    expect(segs.filter(s => s.text).map(s => s.text).join('')).toContain('Preventing Rd5+ runs into mate!');
    expect(segs.map(s => s.move ?? s.text).join('')).toBe(TEXT);
  });

  it('die Vorschau endet in der Mattstellung nach 73.Rbb4', () => {
    const last = buildCommentSegments(TEXT, FEN, MAIN).filter(s => s.move).at(-1);
    expect(last?.fen?.split(' ')[0]).toBe('8/8/2p1K1p1/2k4p/1R3R1n/5P1r/8/8');
  });

  it('ohne widersprechende Zugnummer bleibt ein numerloser Zug klickbar', () => {
    // „besser war Rd5+" ohne folgende Nummer: nichts widerlegt ihn → weiterhin ein Chip.
    const segs = buildCommentSegments('70...c6 besser war Rd5+', FEN, MAIN);
    expect(segs.filter(s => s.move).map(s => s.move)).toEqual(['70...c6', 'Rd5+']);
  });
});
