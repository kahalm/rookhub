import {
  EditPly, SHEET_EXTRA, SHEET_MISSING, SHEET_NOTE, SHEET_OPEN, START_FEN, commentsForSave, commentsOf, cropView, fensOf, nextUncertainFrom, fromServer, headersOf, isoDateOf, pliesOfPgn,
  resolveRequest, revalidate, startFenOf, stripSheetNotes, toServer, userPly, writtenIndexAt,
} from './game-edit.util';

function ply(san: string, w: number | null = null, extra: Partial<EditPly> = {}): EditPly {
  return { san, uci: '', w, written: '', match: 'written', uncertain: false, confirmed: false, options: null,
    comment: null, illegal: false, ...extra };
}

describe('game-edit.util', () => {
  it('revalidate: marks everything from the first impossible move as illegal and canonicalises SAN', () => {
    const r = revalidate([ply('e4'), ply('e5'), ply('Nf3+'), ply('Qh5'), ply('Nc6')]);
    expect(r.map(p => p.san)).toEqual(['e4', 'e5', 'Nf3', 'Qh5', 'Nc6']);
    expect(r.map(p => p.illegal)).toEqual([false, false, false, true, true]);
    expect(r[2].uci).toBe('g1f3');
  });

  it('fensOf: one position before every legal move plus the final one', () => {
    const fens = fensOf([ply('e4'), ply('e5'), ply('Ke3', null, { illegal: true })]);
    expect(fens.length).toBe(3);
    expect(fens[1]).toContain(' b KQkq');
  });

  it('writtenIndexAt: own entry, else counted on from the last known one', () => {
    const list = [ply('e4', 0), ply('e5', 1), ply('Nf3', null), ply('Nc6', 3)];
    expect(writtenIndexAt(list, 1)).toBe(1);
    expect(writtenIndexAt(list, 2)).toBe(2);
    expect(writtenIndexAt(list, 4)).toBe(4); // hinter dem Ende: der nächste Eintrag
    expect(writtenIndexAt([], 0)).toBe(0);
  });

  it('resolveRequest: replace consumes the entry, insert keeps it open, delete skips it', () => {
    const list = [ply('e4', 0), ply('e5', 1), ply('Nf3', 2), ply('Nc6', 3)];
    expect(resolveRequest(list, 2, 'replace', 'd4')).toEqual({ prefix: ['e4', 'e5', 'd4'], writtenFrom: 3 });
    expect(resolveRequest(list, 2, 'insert', 'd4')).toEqual({ prefix: ['e4', 'e5', 'd4'], writtenFrom: 2 });
    expect(resolveRequest(list, 2, 'delete')).toEqual({ prefix: ['e4', 'e5'], writtenFrom: 3 });
    // Anhängen am Ende: der erste noch offene Eintrag wird verbraucht.
    expect(resolveRequest(list, 4, 'replace', 'Bb5')).toEqual({ prefix: ['e4', 'e5', 'Nf3', 'Nc6', 'Bb5'], writtenFrom: 5 });
  });

  // Gemeldet 2026-09-27 (Prod-Partie 27, Zug 36): der Spieler hatte Bd5 vergessen, der Auflöser Rb4 als „nicht auf dem
  // Formular" eingefügt. Wer an dem eingefügten Zug etwas tat, las ab Eintrag 73 statt 72 weiter — „Kd7" fiel weg.
  it('resolveRequest: a move without a sheet entry (inserted) consumes none when replaced or deleted', () => {
    const list = [ply('Kf1', 70), ply('Bd5', 71), ply('Rb4', null, { match: 'inserted' }), ply('Kd7', 72), ply('Rb6', 73)];
    expect(writtenIndexAt(list, 2)).toBe(72);                 // der nächste offene Eintrag, nicht 73
    expect(writtenIndexAt(list, 3)).toBe(72);
    expect(resolveRequest(list, 2, 'replace', 'Rb4').writtenFrom).toBe(72);
    expect(resolveRequest(list, 2, 'delete').writtenFrom).toBe(72);
    expect(resolveRequest(list, 2, 'insert', 'Ke2').writtenFrom).toBe(72);
    expect(resolveRequest(list, 3, 'replace', 'Kd7').writtenFrom).toBe(73);   // mit Eintrag: der ist verbraucht
  });

  it('fromServer/toServer: round trip keeps the sheet state, confirmed moves are never uncertain', () => {
    const plies = fromServer([
      { w: 0, written: 'Sf3', san: 'Nf3', uci: 'g1f3', match: 'written', uncertain: false },
      { w: 1, written: 'Qxd4', san: 'd5', uci: 'd7d5', match: 'fuzzy', uncertain: true, confirmed: true },
    ], ['note', null]);
    expect(plies[0].comment).toBe('note');
    expect(plies[1].uncertain).toBeFalse();
    const back = toServer(plies);
    expect(back[1]).toEqual(jasmine.objectContaining({ w: 1, written: 'Qxd4', confirmed: true, uncertain: false }));
  });

  it('userPly: typed-in moves are confirmed', () => {
    expect(userPly('e4', 'e2e4', 0, 'e4')).toEqual(jasmine.objectContaining({ match: 'user', confirmed: true, uncertain: false }));
  });

  it('pliesOfPgn + commentsOf: moves with their comments', () => {
    const pgn = '[Event "x"]\n\n1. e4 {gut} e5 2. Nf3 {sheet: Sf3} *';
    const plies = pliesOfPgn(pgn);
    expect(plies.map(p => p.san)).toEqual(['e4', 'e5', 'Nf3']);
    expect(plies[0].comment).toBe('gut');
    expect(plies[2].comment).toBe('sheet: Sf3');
    expect(commentsOf(pgn, 3)).toEqual(['gut', null, 'sheet: Sf3']);
  });

  // W3 F4-002: Stellungspartie (PGN-Upload mit FEN-Kopf) — die Züge gelten ab der FEN, nicht ab der Grundstellung.
  it('startFenOf + commentsOf + fensOf/revalidate: a game with a FEN header starts from that position', () => {
    const fen = '4k3/8/8/8/8/8/4P3/4K3 w - - 0 1';
    const pgn = `[SetUp "1"]\n[FEN "${fen}"]\n\n1. Kd2 {gut} Kd7 2. e4 {auch} Ke6 *`;
    expect(startFenOf(pgn)).toBe(fen);
    expect(startFenOf(`[FEN "${fen}"]\n\n*`)).toBe(fen);                 // ohne Züge: die Stellung selbst
    expect(startFenOf('1. e4 e5 *')).toBe(START_FEN);
    expect(startFenOf('[FEN "kaputt"]\n\n1. e4 *')).toBe(START_FEN);
    const plies = pliesOfPgn(pgn);
    expect(plies.map(p => p.san)).toEqual(['Kd2', 'Kd7', 'e4', 'Ke6']);
    expect(commentsOf(pgn, 4)).toEqual(['gut', null, 'auch', null]);      // vorher: alle null
    const fens = fensOf(plies, fen);
    expect(fens.length).toBe(5);
    expect(fens[0]).toBe(fen);
    expect(revalidate(plies, fen).map(p => p.illegal)).toEqual([false, false, false, false]);
  });

  it('stripSheetNotes: keeps what the user wrote, drops what the server generated', () => {
    expect(stripSheetNotes(`${SHEET_NOTE}Qxd4`)).toBeNull();
    expect(stripSheetNotes(`gut | ${SHEET_OPEN}Zz9 Yy8`)).toBe('gut');
    expect(stripSheetNotes('nur Text')).toBe('nur Text');
    expect(stripSheetNotes(null)).toBeNull();
  });

  it('commentsForSave: notes bent moves unless confirmed, open entries hang at the last move', () => {
    const list = [
      ply('e4', 0, { comment: 'gut' }),
      ply('e5', 1, { match: 'fuzzy', written: 'e6' }),
      ply('Nf3', 2, { match: 'fuzzy', written: 'Sg3', confirmed: true }),
      ply('Qh5', 3, { illegal: true }),
    ];
    expect(commentsForSave(list, ['Zz9'])).toEqual(['gut', `${SHEET_NOTE}e6`, `${SHEET_OPEN}Zz9`]);
    expect(commentsForSave(list, [])).toEqual(['gut', `${SHEET_NOTE}e6`, null]);
    // Ein eingeschobener (auf dem Formular fehlender) Zug bekommt seinen Hinweis, bestätigt verliert er ihn.
    const inserted = [ply('e4', null, { match: 'inserted' }), ply('e5', null, { match: 'inserted', confirmed: true })];
    expect(commentsForSave(inserted, [])).toEqual([SHEET_MISSING, null]);
    expect(stripSheetNotes(`${SHEET_EXTRA}Nf3 | eigener`)).toBe('eigener');
  });

  it('headersOf + isoDateOf: header values, "?" counts as empty', () => {
    const h = headersOf('[Event "Simultan \\"26\\""]\n[Site "?"]\n[Date "2026.06.05"]\n\n1. e4 *');
    expect(h['Event']).toBe('Simultan "26"');
    expect(h['Site']).toBe('');
    expect(isoDateOf(h['Date'])).toBe('2026-06-05');
    expect(isoDateOf('????.??.??')).toBe('');
  });

  it('cropView: frames the entry with its neighbourhood and marks the entry itself', () => {
    // Eintrag 100..200 × 100..140 auf einem 2000×1000-Foto: seitlich 60 % der Breite, oben/unten 130 % der Höhe dazu.
    const v = cropView([100, 100, 200, 140], 2000, 1000)!;
    // Rahmen 40..260 × 48..192 → 220 × 144 Promille, in Pixeln 440 × 144.
    expect(v.aspect).toBeCloseTo(440 / 144, 2);
    expect(v.imgW).toBeCloseTo(100000 / 220, 2);
    expect(v.imgH).toBeCloseTo(100000 / 144, 2);
    expect(v.left).toBeCloseTo(-40 / 220 * 100, 2);
    expect(v.top).toBeCloseTo(-48 / 144 * 100, 2);
    expect(v.markLeft).toBeCloseTo(60 / 220 * 100, 2);
    expect(v.markTop).toBeCloseTo(52 / 144 * 100, 2);
    expect(v.markW).toBeCloseTo(100 / 220 * 100, 2);
    expect(v.markH).toBeCloseTo(40 / 144 * 100, 2);
  });

  it('cropView: stays inside the photo at the edge, and gives nothing without a usable box or photo size', () => {
    const v = cropView([0, 980, 50, 1000], 1000, 1000)!;
    expect(v.left).toBeCloseTo(0, 5);                // links nicht über den Rand
    expect(v.top + v.imgH).toBeCloseTo(100, 2);      // unten bündig mit dem Foto
    expect(cropView(null, 1000, 1000)).toBeNull();
    expect(cropView([10, 10, 20], 1000, 1000)).toBeNull();
    expect(cropView([30, 10, 20, 40], 1000, 1000)).toBeNull();
    expect(cropView([10, 10, 20, 40], 0, 0)).toBeNull();
  });

  it('nextUncertainFrom: the next open uncertain move from a position, wrapping round, skipping confirmed and illegal', () => {
    const list = [ply('e4'), ply('e5', 1, { uncertain: true }), ply('Nf3', 2, { uncertain: true, confirmed: true }),
      ply('Nc6', 3, { uncertain: true, illegal: true }), ply('Bb5', 4, { uncertain: true })];
    expect(nextUncertainFrom(list, 2)).toBe(4);
    expect(nextUncertainFrom(list, 5)).toBe(1);        // über das Ende hinweg
    expect(nextUncertainFrom([ply('e4'), ply('e5')], 0)).toBeNull();
  });
});
