import { lineKeyFromSans } from './repertoire-line-key.util';

describe('lineKeyFromSans', () => {
  it('is deterministic for the same move sequence', () => {
    expect(lineKeyFromSans(['e4', 'e5', 'Nf3'])).toBe(lineKeyFromSans(['e4', 'e5', 'Nf3']));
  });

  it('differs for different move sequences', () => {
    expect(lineKeyFromSans(['e4', 'e5'])).not.toBe(lineKeyFromSans(['e4', 'c5']));
  });

  it('normalizes SAN (strips check/mate/annotation) so equivalent lines share a key', () => {
    expect(lineKeyFromSans(['Qh5+', 'Ke7', 'Qxf7#'])).toBe(lineKeyFromSans(['Qh5', 'Ke7', 'Qxf7']));
    expect(lineKeyFromSans(['e4!', 'e5?'])).toBe(lineKeyFromSans(['e4', 'e5']));
  });

  it('is order-sensitive', () => {
    expect(lineKeyFromSans(['e4', 'e5'])).not.toBe(lineKeyFromSans(['e5', 'e4']));
  });

  it('produces a stable "l"-prefixed base36 key (incl. empty line)', () => {
    expect(lineKeyFromSans([])).toMatch(/^l[0-9a-z]+$/);
    expect(lineKeyFromSans(['e4'])).toMatch(/^l[0-9a-z]+$/);
  });

  // WICHTIG: Der Schlüssel ist ein SPRACHÜBERGREIFENDER VERTRAG — der Server bildet ihn in
  // ChessableTrainedLineService.LineKeyFromSans nach, und persistierte Schlüssel
  // (RepertoireCardState.CardKey, RepertoireFlashcardMark.LineKey) hängen daran. Ohne feste
  // Literale hier prüfen die Tests nur, dass die Implementierung mit sich selbst übereinstimmt:
  // eine Änderung an cyrb53/normSan bliebe auf BEIDEN Seiten grün und liefe still ins Leere.
  // Dieselben Vektoren stehen in tests/RookHub.Api.Tests/ChessableTrainedLineServiceTests.cs —
  // ändert sich hier einer, muss dort derselbe brechen.
  it('matches the pinned cross-language vectors (server mirror + stored keys)', () => {
    expect(lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5'])).toBe('l2eac1aan9n2');
    expect(lineKeyFromSans(['d4', 'd5', 'c4', 'e6', 'Nc3', 'Nf6', 'Bg5', 'Be7'])).toBe('lgaojnnug81');
    expect(lineKeyFromSans(['e4', 'c6', 'd4', 'd5', 'exd5', 'cxd5'])).toBe('l1pzvjpxtqs9');
    expect(lineKeyFromSans(['e4', 'e6', 'd4', 'd5', 'Nd2', 'c5', 'exd5', 'Qxd5+'])).toBe('l1sv2o9a142o');
    expect(lineKeyFromSans(['O-O', 'O-O-O'])).toBe('l65y1fu91w7');
    expect(lineKeyFromSans(['e4'])).toBe('lbve4kkccho');
    // Chessable-Schreibweisen müssen auf dieselben Schlüssel fallen wie die kanonischen.
    expect(lineKeyFromSans(['e4', 'd5', 'exd5', 'c6', 'dxc6', 'b5', 'c7', 'a5', 'c8Q']))
      .toBe(lineKeyFromSans(['e4', 'd5', 'exd5', 'c6', 'dxc6', 'b5', 'c7', 'a5', 'c8=Q']));
    expect(lineKeyFromSans(['a2', 'b1Q+'])).toBe('l27uzjsul7qp');
    expect(lineKeyFromSans(['0-0'])).toBe('lf2l4fs48fe');
    expect(lineKeyFromSans(['e4', 'e5', 'Nf3', 'Nc6', 'Bc4', 'Bc5', 'O-O', 'Nf6'])).toBe('lcvspxwtgmj');
  });
});
