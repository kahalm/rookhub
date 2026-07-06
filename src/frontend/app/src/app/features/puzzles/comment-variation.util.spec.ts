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
});
