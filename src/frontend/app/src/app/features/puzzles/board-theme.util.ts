export type ThemeMode = 'fixed' | 'random' | 'crazy';

export const BOARD_THEMES: { key: string; name: string; light: string; dark: string; img?: string }[] = [
  { key: 'brown', name: 'Brown', light: '#f0d9b5', dark: '#b58863' },
  { key: 'blue', name: 'Blue', light: '#d4e3ed', dark: '#5882a1' },
  { key: 'green', name: 'Green', light: '#eeeed2', dark: '#769656' },
  { key: 'gray', name: 'Gray', light: '#f0f0f0', dark: '#8a8a8a' },
  { key: 'wood', name: 'Wood', light: '#e6d1a0', dark: '#8b5e3c' },
  { key: 'realwood', name: 'Holz', light: '#d8b98a', dark: '#8a5a33', img: '/board/wood4.jpg' },
  { key: 'water', name: 'Wasser', light: '#6f93b8', dark: '#3c5a78', img: '/board/blue3.jpg' },
  { key: 'marble', name: 'Marmor', light: '#e8e8e8', dark: '#9a9a9a', img: '/board/marble.jpg' },
  { key: 'metal', name: 'Metall', light: '#cfcfcf', dark: '#7a7a7a', img: '/board/metal.jpg' },
  { key: 'leather', name: 'Leder', light: '#a87c4f', dark: '#5a3d23', img: '/board/leather.jpg' },
  { key: 'maple', name: 'Ahorn', light: '#e8cfa0', dark: '#b5895a', img: '/board/maple.jpg' },
];

export const PIECE_SETS = [
  { key: 'cburnett', name: 'Classic', preview: '/piece/cburnett/wN.svg' },
  { key: 'merida', name: 'Merida', preview: '/piece/merida/wN.svg' },
  { key: 'fantasy', name: 'Fantasy', preview: '/piece/fantasy/wN.svg' },
  { key: 'spatial', name: 'Spatial', preview: '/piece/spatial/wN.svg' },
  { key: 'celtic', name: 'Celtic', preview: '/piece/celtic/wN.svg' },
  { key: 'chessnut', name: 'Chessnut', preview: '/piece/chessnut/wN.svg' },
  { key: 'rhosgfx', name: 'RhosGFX', preview: '/piece/rhosgfx/wN.svg' },
];

function randomItem<T>(arr: readonly T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

export function randomBoardTheme(): string {
  return randomItem(BOARD_THEMES).key;
}

export function randomPieceSet(): string {
  return randomItem(PIECE_SETS).key;
}

// --- Share-Link-Anzeige-Overrides ---

export interface ShareViewParams {
  themeMode?: ThemeMode;
  visualization?: number;
  /** Anarchy-Modus (`?anarchy=max`): wenn ein En-passant-Schlag möglich ist, ist er verpflichtend. */
  enPassantForced?: boolean;
  /** Crazy-Figuren-Modus (`?anarchy=max+1` → 'square'): das Feld bestimmt den Figurenstil. */
  crazyPieceMode?: CrazyPieceMode;
}

/**
 * Liest optionale Anzeige-Overrides aus den Query-Parametern eines (geteilten) Puzzle-Links:
 *  - `crazy=1`        → Brett-Theme-Modus „crazy"
 *  - `visualmode=N`   → Visualisierungs-Stufe N (0–4)
 *  - `anarchy=max`    → Crazy-Brett + en passant verpflichtend („en passant is forced")
 *  - `anarchy=max+1`  → wie max, aber das FELD bestimmt den Figurenstil (crazyPieceMode='square')
 * Rein lesend; verändert KEINE gespeicherten Nutzereinstellungen (transient pro Aufruf).
 */
export function parseShareViewParams(q: { get(key: string): string | null }): ShareViewParams {
  const out: ShareViewParams = {};
  if (q.get('crazy') === '1') out.themeMode = 'crazy';
  // `+` wird in Query-Strings teils als Leerzeichen dekodiert → beide Schreibweisen akzeptieren.
  const anarchy = q.get('anarchy');
  const isMaxPlus = anarchy === 'max+1' || anarchy === 'max 1';
  if (anarchy === 'max' || isMaxPlus) { out.themeMode = 'crazy'; out.enPassantForced = true; }
  if (isMaxPlus) out.crazyPieceMode = 'square';
  const vm = q.get('visualmode');
  if (vm != null && /^[0-4]$/.test(vm)) out.visualization = Number(vm);
  return out;
}

// --- Crazy Mode ---

// Only simple (flat-color) themes are used for crazy squares
const SIMPLE_THEMES = BOARD_THEMES.filter(t => !t.img);

/** Generate an 8x8 SVG data-URI where each square has a random color from the simple themes. */
function generateCrazyBoardSvg(): string {
  let rects = '';
  for (let row = 0; row < 8; row++) {
    for (let col = 0; col < 8; col++) {
      const theme = randomItem(SIMPLE_THEMES);
      const isLight = (row + col) % 2 === 0;
      const fill = isLight ? theme.light : theme.dark;
      rects += `<rect x="${col}" y="${row}" width="1" height="1" fill="${fill}"/>`;
    }
  }
  const svg = `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 8 8' shape-rendering='crispEdges'>${rects}</svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

/** CSS for crazy board — overrides the cg-board background with an 8x8 SVG. */
function applyCrazyBoard(): void {
  let style = document.getElementById('crazy-board-css') as HTMLStyleElement | null;
  if (!style) {
    style = document.createElement('style');
    style.id = 'crazy-board-css';
    document.head.appendChild(style);
  }
  const dataUri = generateCrazyBoardSvg();
  style.textContent = `.board-theme-_crazy cg-board { background-image: url("${dataUri}"); background-size: cover; }`;
}

/** Figurenrollen + Datei-Codes/Farb-Präfixe für die Crazy-Figuren-Zufallswahl. */
const PIECE_ROLES = ['pawn', 'knight', 'bishop', 'rook', 'queen', 'king'] as const;
const PIECE_CODES: Record<string, string> = { pawn: 'P', knight: 'N', bishop: 'B', rook: 'R', queen: 'Q', king: 'K' };
const COLOR_PREFIX: Record<string, string> = { white: 'w', black: 'b' };

/**
 * Crazy-Figuren-Modus:
 *  - 'piece'  (Default, `?anarchy=max`): jede Figur würfelt ihr eigenes Set, behält es beim Ziehen.
 *  - 'square' (`?anarchy=max+1`): das FELD bestimmt das Set — eine Figur nimmt das Set ihres aktuellen
 *    Feldes an (beim Ziehen wechselt also ihr Aussehen passend zum Zielfeld).
 */
export type CrazyPieceMode = 'piece' | 'square';

/** Pro DOM-Figur ein einzeln gewürfeltes Figurenset (stabil, solange chessground das Element
 *  wiederverwendet). GC räumt entfernte Figuren automatisch ab. */
const crazyPieceSets = new WeakMap<Element, string>();

const FILES = ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'];
/** 64 zufällige Set-Zuordnungen je Feld (lazily erzeugt, stabil über die Sitzung). */
let crazySquareSets: Record<string, string> | null = null;
function squareSets(): Record<string, string> {
  if (crazySquareSets) return crazySquareSets;
  const m: Record<string, string> = {};
  for (const f of FILES) for (let r = 1; r <= 8; r++) m[`${f}${r}`] = randomItem(PIECE_SETS).key;
  return (crazySquareSets = m);
}

/** Feld eines <piece>-Elements aus seinem `transform: translate(x,y)` ableiten (chessground positioniert
 *  Figuren per Pixel-Translate; Feldgröße = Brettbreite/8). Null, wenn (noch) nicht ableitbar. */
export function squareOfPiece(el: HTMLElement, boardWidth: number, orientation: 'white' | 'black'): string | null {
  const m = /translate\(\s*([-\d.]+)px[,\s]+([-\d.]+)px/.exec(el.style.transform || '');
  if (!m || boardWidth <= 0) return null;
  const sq = boardWidth / 8;
  const fi = Math.round(parseFloat(m[1]) / sq);
  const ti = Math.round(parseFloat(m[2]) / sq);   // 0 = oberste Reihe
  if (fi < 0 || fi > 7 || ti < 0 || ti > 7) return null;
  return orientation === 'white' ? `${FILES[fi]}${8 - ti}` : `${FILES[7 - fi]}${ti + 1}`;
}

/**
 * Malt im Crazy-Modus jede Figur per Inline-Hintergrund (chessground setzt das beim Render nicht selbst,
 * daher nach jedem Render vom Board-Component aufrufen). `mode='piece'` → eigenes Set je Figur (WeakMap);
 * `mode='square'` → Set nach aktuellem Feld (für `?anarchy=max+1`, braucht die Brett-`orientation`).
 */
export function paintCrazyPieces(root: HTMLElement, mode: CrazyPieceMode = 'piece', orientation: 'white' | 'black' = 'white'): void {
  const boardWidth = (root.querySelector('cg-board') as HTMLElement | null)?.clientWidth ?? root.clientWidth;
  root.querySelectorAll<HTMLElement>('cg-board piece').forEach(el => {
    if (el.classList.contains('ghost')) return;   // Drag-/Animations-Klon nicht anfassen
    const color = el.classList.contains('white') ? 'white' : el.classList.contains('black') ? 'black' : null;
    const role = PIECE_ROLES.find(r => el.classList.contains(r));
    if (!color || !role) return;
    let set: string | undefined;
    if (mode === 'square') {
      const sq = squareOfPiece(el, boardWidth, orientation);
      if (sq) set = squareSets()[sq];
    }
    if (!set) {   // Default (per Figur) bzw. Fallback, solange das Feld nicht ableitbar ist
      set = crazyPieceSets.get(el);
      if (!set) { set = randomItem(PIECE_SETS).key; crazyPieceSets.set(el, set); }
    }
    const url = `url("/piece/${set}/${COLOR_PREFIX[color]}${PIECE_CODES[role]}.svg")`;
    if (el.style.backgroundImage !== url) el.style.backgroundImage = url;
  });
}

/** Entfernt die im Crazy-Modus gesetzten Inline-Hintergründe wieder (Wechsel zurück zu fixed/random). */
export function clearCrazyPieces(root: HTMLElement): void {
  root.querySelectorAll<HTMLElement>('cg-board piece').forEach(el => { el.style.backgroundImage = ''; });
}

export function clearCrazyStyles(): void {
  document.getElementById('crazy-board-css')?.remove();
  document.getElementById('crazy-piece-css')?.remove();
}

// --- Visualization Hide (Level 2–4) ---

function checkerSvg(fill: string, stroke: string): string {
  const svg = `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><circle cx='50' cy='50' r='40' fill='${fill}' stroke='${stroke}' stroke-width='4'/></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}
const WHITE_CHECKER = checkerSvg('#e8e8e8', '#999');
const BLACK_CHECKER = checkerSvg('#333', '#111');

/**
 * Inject/update a <style> tag that hides or replaces pieces when .viz-hidden is set.
 * Level 2: colored checkers (white/black).  Level 3: all-black checkers.  Level 4: invisible.
 */
export function applyVisualizationHide(level: number): void {
  let style = document.getElementById('viz-hide-css') as HTMLStyleElement | null;
  if (!style) {
    style = document.createElement('style');
    style.id = 'viz-hide-css';
    document.head.appendChild(style);
  }
  let css = '';
  if (level === 2) {
    css = `.viz-hidden cg-board piece.white { background-image: url("${WHITE_CHECKER}") !important; }\n`
        + `.viz-hidden cg-board piece.black { background-image: url("${BLACK_CHECKER}") !important; }\n`;
  } else if (level === 3) {
    css = `.viz-hidden cg-board piece { background-image: url("${BLACK_CHECKER}") !important; }\n`;
  } else if (level >= 4) {
    css = `.viz-hidden cg-board piece { opacity: 0 !important; }\n`;
  }
  style.textContent = css;
}

export function clearVisualizationHide(): void {
  document.getElementById('viz-hide-css')?.remove();
}

/**
 * Apply a theme mode and return the boardTheme + pieceSet to use.
 * - fixed: uses the provided saved values, clears crazy styles
 * - random: picks a random theme + set, clears crazy styles
 * - crazy: generates unique per-square/per-piece styles, returns '_crazy' keys
 */
export function applyThemeMode(
  mode: ThemeMode,
  savedBoard: string,
  savedPiece: string
): { boardTheme: string; pieceSet: string } {
  if (mode === 'fixed') {
    clearCrazyStyles();
    return { boardTheme: savedBoard, pieceSet: savedPiece };
  }
  if (mode === 'random') {
    clearCrazyStyles();
    return { boardTheme: randomBoardTheme(), pieceSet: randomPieceSet() };
  }
  // crazy: zufälliges Brett-SVG; die Figuren werden pro Stück vom Board-Component via
  // paintCrazyPieces() inline gewürfelt (nicht mehr per Rolle+Farbe-CSS).
  applyCrazyBoard();
  return { boardTheme: '_crazy', pieceSet: '_crazy' };
}
