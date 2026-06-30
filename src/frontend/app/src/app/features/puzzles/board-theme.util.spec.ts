import {
  BOARD_THEMES, PIECE_SETS, randomBoardTheme, randomPieceSet,
  applyThemeMode, clearCrazyStyles, applyVisualizationHide, clearVisualizationHide,
  parseShareViewParams
} from './board-theme.util';

// Mini-ParamMap-Stub (nur .get wird gebraucht).
function qp(map: Record<string, string>) {
  return { get: (k: string) => (k in map ? map[k] : null) };
}

describe('parseShareViewParams', () => {
  it('crazy=1 setzt themeMode crazy', () => {
    expect(parseShareViewParams(qp({ crazy: '1' })).themeMode).toBe('crazy');
  });
  it('crazy!=1 setzt keinen themeMode', () => {
    expect(parseShareViewParams(qp({ crazy: '0' })).themeMode).toBeUndefined();
    expect(parseShareViewParams(qp({})).themeMode).toBeUndefined();
  });
  it('visualmode 0–4 wird als Zahl übernommen', () => {
    for (const n of ['0', '1', '2', '3', '4']) {
      expect(parseShareViewParams(qp({ visualmode: n })).visualization).toBe(Number(n));
    }
  });
  it('visualmode außerhalb 0–4 / unsinnig wird ignoriert', () => {
    expect(parseShareViewParams(qp({ visualmode: '5' })).visualization).toBeUndefined();
    expect(parseShareViewParams(qp({ visualmode: '-1' })).visualization).toBeUndefined();
    expect(parseShareViewParams(qp({ visualmode: 'x' })).visualization).toBeUndefined();
  });
  it('kombiniert crazy + visualmode', () => {
    const r = parseShareViewParams(qp({ crazy: '1', visualmode: '3' }));
    expect(r.themeMode).toBe('crazy');
    expect(r.visualization).toBe(3);
  });
});

describe('board-theme.util', () => {
  afterEach(() => {
    clearCrazyStyles();
    clearVisualizationHide();
  });

  it('randomBoardTheme / randomPieceSet liefern einen gültigen Schlüssel aus der Liste', () => {
    const bt = randomBoardTheme();
    const ps = randomPieceSet();
    expect(BOARD_THEMES.some(t => t.key === bt)).toBeTrue();
    expect(PIECE_SETS.some(p => p.key === ps)).toBeTrue();
  });

  describe('applyThemeMode', () => {
    it('fixed gibt die gespeicherten Werte unverändert zurück und legt keine Crazy-Styles an', () => {
      const res = applyThemeMode('fixed', 'green', 'merida');
      expect(res).toEqual({ boardTheme: 'green', pieceSet: 'merida' });
      expect(document.getElementById('crazy-board-css')).toBeNull();
    });

    it('random liefert gültige Schlüssel (nicht _crazy)', () => {
      const res = applyThemeMode('random', 'brown', 'cburnett');
      expect(BOARD_THEMES.some(t => t.key === res.boardTheme)).toBeTrue();
      expect(PIECE_SETS.some(p => p.key === res.pieceSet)).toBeTrue();
    });

    it('crazy gibt _crazy zurück und injiziert die Crazy-Style-Tags', () => {
      const res = applyThemeMode('crazy', 'brown', 'cburnett');
      expect(res).toEqual({ boardTheme: '_crazy', pieceSet: '_crazy' });
      expect(document.getElementById('crazy-board-css')).not.toBeNull();
      expect(document.getElementById('crazy-piece-css')).not.toBeNull();
    });

    it('fixed räumt zuvor angelegte Crazy-Styles wieder ab', () => {
      applyThemeMode('crazy', 'brown', 'cburnett');
      applyThemeMode('fixed', 'brown', 'cburnett');
      expect(document.getElementById('crazy-board-css')).toBeNull();
      expect(document.getElementById('crazy-piece-css')).toBeNull();
    });
  });

  describe('applyVisualizationHide', () => {
    it('Level 2 ersetzt weiße + schwarze Figuren (zwei Regeln)', () => {
      applyVisualizationHide(2);
      const css = document.getElementById('viz-hide-css')!.textContent!;
      expect(css).toContain('piece.white');
      expect(css).toContain('piece.black');
    });
    it('Level 4 macht Figuren unsichtbar (opacity 0)', () => {
      applyVisualizationHide(4);
      expect(document.getElementById('viz-hide-css')!.textContent).toContain('opacity: 0');
    });
    it('clearVisualizationHide entfernt das Style-Tag', () => {
      applyVisualizationHide(3);
      clearVisualizationHide();
      expect(document.getElementById('viz-hide-css')).toBeNull();
    });
  });
});
