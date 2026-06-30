import {
  BOARD_THEMES, PIECE_SETS, randomBoardTheme, randomPieceSet,
  applyThemeMode, clearCrazyStyles, applyVisualizationHide, clearVisualizationHide,
  parseShareViewParams, paintCrazyPieces, clearCrazyPieces, squareOfPiece
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
  it('anarchy=max setzt Crazy-Brett UND erzwingt En passant (Modus piece = Default)', () => {
    const r = parseShareViewParams(qp({ anarchy: 'max' }));
    expect(r.themeMode).toBe('crazy');
    expect(r.enPassantForced).toBeTrue();
    expect(r.crazyPieceMode).toBeUndefined();   // 'piece' bleibt Default
  });
  it('anarchy=max+1 (auch „max 1" wegen +-Dekodierung) → zusätzlich crazyPieceMode square', () => {
    for (const v of ['max+1', 'max 1']) {
      const r = parseShareViewParams(qp({ anarchy: v }));
      expect(r.themeMode).toBe('crazy');
      expect(r.enPassantForced).toBeTrue();
      expect(r.crazyPieceMode).toBe('square');
    }
  });
  it('ohne anarchy kein enPassantForced', () => {
    expect(parseShareViewParams(qp({})).enPassantForced).toBeUndefined();
    expect(parseShareViewParams(qp({ anarchy: 'mild' })).enPassantForced).toBeUndefined();
  });
});

describe('squareOfPiece (Feld aus transform)', () => {
  function pieceAt(transform: string): HTMLElement {
    const el = document.createElement('piece');
    el.style.transform = transform;
    return el;
  }
  it('rechnet translate→Feld bei Orientierung Weiß (a8 oben links, sq=100 bei 800px)', () => {
    expect(squareOfPiece(pieceAt('translate(0px, 0px)'), 800, 'white')).toBe('a8');
    expect(squareOfPiece(pieceAt('translate(700px, 700px)'), 800, 'white')).toBe('h1');
    expect(squareOfPiece(pieceAt('translate(400px, 600px)'), 800, 'white')).toBe('e2');
  });
  it('spiegelt bei Orientierung Schwarz', () => {
    expect(squareOfPiece(pieceAt('translate(0px, 0px)'), 800, 'black')).toBe('h1');
    expect(squareOfPiece(pieceAt('translate(700px, 700px)'), 800, 'black')).toBe('a8');
  });
  it('liefert null ohne transform oder bei Breite 0', () => {
    expect(squareOfPiece(pieceAt(''), 800, 'white')).toBeNull();
    expect(squareOfPiece(pieceAt('translate(0px,0px)'), 0, 'white')).toBeNull();
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

    it('square-Modus: zwei Figuren auf DEMSELBEN Feld bekommen dasselbe Set (Feld bestimmt den Stil)', () => {
      const root = document.createElement('div');
      const cg = document.createElement('cg-board');
      cg.style.cssText = 'display:block;width:800px;height:800px;position:relative';
      const p1 = document.createElement('piece'); p1.className = 'white pawn'; p1.style.transform = 'translate(0px, 0px)';
      const p2 = document.createElement('piece'); p2.className = 'white knight'; p2.style.transform = 'translate(0px, 0px)';  // gleiches Feld a8
      cg.append(p1, p2); root.appendChild(cg); document.body.appendChild(root);
      try {
        expect(cg.clientWidth).toBe(800);   // Layout vorhanden → Feld ableitbar
        paintCrazyPieces(root, 'square', 'white');
        const set1 = p1.style.backgroundImage.match(/\/piece\/([^/]+)\//)![1];
        const set2 = p2.style.backgroundImage.match(/\/piece\/([^/]+)\//)![1];
        expect(set1).toBe(set2);                                   // gleiches Feld → gleiches Set
        expect(p1.style.backgroundImage).toContain('/wP.svg');     // Rolle/Farbe trotzdem korrekt
        expect(p2.style.backgroundImage).toContain('/wN.svg');
      } finally { document.body.removeChild(root); }
    });
  });
});
