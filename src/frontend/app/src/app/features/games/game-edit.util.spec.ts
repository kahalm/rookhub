import {
  EditPly, SHEET_NOTE, SHEET_OPEN, commentsForSave, commentsOf, fensOf, fromServer, headersOf, isoDateOf, pliesOfPgn,
  resolveRequest, revalidate, stripSheetNotes, toServer, userPly, writtenIndexAt,
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
  });

  it('headersOf + isoDateOf: header values, "?" counts as empty', () => {
    const h = headersOf('[Event "Simultan \\"26\\""]\n[Site "?"]\n[Date "2026.06.05"]\n\n1. e4 *');
    expect(h['Event']).toBe('Simultan "26"');
    expect(h['Site']).toBe('');
    expect(isoDateOf(h['Date'])).toBe('2026-06-05');
    expect(isoDateOf('????.??.??')).toBe('');
  });
});
