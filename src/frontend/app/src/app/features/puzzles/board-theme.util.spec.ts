import {
  BOARD_THEMES, PIECE_SETS, randomBoardTheme, randomPieceSet,
  applyThemeMode, clearCrazyStyles, applyVisualizationHide, clearVisualizationHide,
  parseShareViewParams, paintCrazyPieces, clearCrazyPieces
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
  it('anarchy=max setzt Crazy-Brett UND erzwingt En passant', () => {
    const r = parseShareViewParams(qp({ anarchy: 'max' }));
    expect(r.themeMode).toBe('crazy');
    expect(r.enPassantForced).toBeTrue();
  });
  it('ohne anarchy kein enPassantForced', () => {
    expect(parseShareViewParams(qp({})).enPassantForced).toBeUndefined();
    expect(parseShareViewParams(qp({ anarchy: 'mild' })).enPassantForced).toBeUndefined();
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

    it('crazy gibt _crazy zurück und injiziert das Brett-Style-Tag (Figuren werden pro Stück gemalt)', () => {
      const res = applyThemeMode('crazy', 'brown', 'cburnett');
      expect(res).toEqual({ boardTheme: '_crazy', pieceSet: '_crazy' });
      expect(document.getElementById('crazy-board-css')).not.toBeNull();
      // Kein globales Figuren-CSS mehr — die Figuren bekommen Inline-Styles via paintCrazyPieces().
      expect(document.getElementById('crazy-piece-css')).toBeNull();
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

  describe('paintCrazyPieces (jede Figur einzeln gewürfelt)', () => {
    function board(...specs: string[]): HTMLElement {
      const root = document.createElement('div');
      const cg = document.createElement('cg-board');
      for (const s of specs) {
        const p = document.createElement('piece');
        p.className = s;
        cg.appendChild(p);
      }
      root.appendChild(cg);
      return root;
    }
    const setKeys = PIECE_SETS.map(p => p.key);
    function setOf(el: HTMLElement): string | null {
      const m = el.style.backgroundImage.match(/\/piece\/([^/]+)\//);
      return m ? m[1] : null;
    }

    it('gibt jeder Figur ein gültiges Figurenset als Inline-Hintergrund mit korrektem Rollen-Code', () => {
      const root = board('white pawn', 'black knight');
      paintCrazyPieces(root);
      const [wp, bn] = Array.from(root.querySelectorAll<HTMLElement>('piece'));
      expect(setKeys).toContain(setOf(wp)!);
      expect(wp.style.backgroundImage).toContain('/wP.svg');
      expect(bn.style.backgroundImage).toContain('/bN.svg');
    });

    it('hält das Set je Element stabil über mehrere Aufrufe (WeakMap)', () => {
      const root = board('white pawn');
      paintCrazyPieces(root);
      const first = setOf(root.querySelector('piece') as HTMLElement);
      paintCrazyPieces(root);
      expect(setOf(root.querySelector('piece') as HTMLElement)).toBe(first);
    });

    it('würfelt unabhängig pro Element (zwei gleiche Bauern können verschiedene Sets haben)', () => {
      // Über viele gleiche Bauern ist die Wahrscheinlichkeit, dass NICHT alle identisch sind, ~1.
      const root = board(...Array(12).fill('white pawn'));
      paintCrazyPieces(root);
      const sets = new Set(Array.from(root.querySelectorAll<HTMLElement>('piece')).map(setOf));
      expect(sets.size).toBeGreaterThan(1);
    });

    it('lässt Geister-Figuren (Drag-Klon) unangetastet', () => {
      const root = board('white pawn ghost');
      paintCrazyPieces(root);
      expect((root.querySelector('piece') as HTMLElement).style.backgroundImage).toBe('');
    });

    it('clearCrazyPieces entfernt die Inline-Hintergründe wieder', () => {
      const root = board('white pawn', 'black queen');
      paintCrazyPieces(root);
      clearCrazyPieces(root);
      root.querySelectorAll<HTMLElement>('piece').forEach(el => expect(el.style.backgroundImage).toBe(''));
    });
  });
});
