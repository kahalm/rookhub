/**
 * Sternenjagd: EINE weisse Figur, ein paar Sterne auf dem Brett — jeder Zug muss genau einen Stern fressen, nach so
 * vielen Zuegen, wie Sterne liegen, ist das Brett leer. Rein, ohne Angular: Zugregeln, Loesungszaehler, Generator und
 * die Stufenfolge.
 *
 * <p>Regeln: Sterne stehen im Weg wie Figuren — Turm, Laeufer und Dame koennen nicht ueber einen Stern hinweg ziehen,
 * nur auf ihn (er ist ja zu fressen). Springer und Koenig springen bzw. gehen ein Feld, fuer sie gibt es keine
 * Blockade. Es werden nur Aufgaben mit GENAU EINER Reihenfolge gestellt (Wunsch 2026-10-10: „nur eindeutige Loesungen
 * aufstellen") — damit ist jeder Stern, der nicht der naechste der Loesung ist, eine Sackgasse, und das darf das Kind
 * sofort erfahren statt erst drei Zuege spaeter.</p>
 *
 * <p>Felder sind Zahlen 0..63: a1 = 0, h1 = 7, a8 = 56.</p>
 */

export type StarPiece = 'R' | 'B' | 'Q' | 'N' | 'K';

export interface StarPuzzle {
  piece: StarPiece;
  /** Startfeld der Figur. */
  start: number;
  /** Felder der Sterne (ungeordnet). */
  stars: number[];
  /** Die eine Reihenfolge, in der alle Sterne gefressen werden. */
  solution: number[];
}

export interface StarStage {
  /** 1-basiert. */
  stage: number;
  piece: StarPiece;
  stars: number;
}

/** Aufgaben je Stufe. */
export const STARS_PER_STAGE = 5;

/**
 * Die Stufenfolge: erst wenige Sterne mit geraden Figuren, dann Springer und Koenig, die Zahl der Sterne waechst.
 * Feste Liste statt Formel — so laesst sie sich lesen und umsortieren.
 */
export const STAR_STAGES: readonly StarStage[] = ([
  ['R', 2], ['B', 2], ['R', 3], ['Q', 3], ['N', 2],
  ['B', 3], ['K', 3], ['N', 3], ['R', 4], ['Q', 4],
  ['B', 4], ['K', 4], ['N', 4], ['R', 5], ['Q', 5],
  ['B', 5], ['N', 5], ['K', 5], ['Q', 6], ['N', 6],
] as const).map(([piece, stars], i) => ({ stage: i + 1, piece, stars }));

const ROOK_DIRS: readonly [number, number][] = [[1, 0], [-1, 0], [0, 1], [0, -1]];
const BISHOP_DIRS: readonly [number, number][] = [[1, 1], [1, -1], [-1, 1], [-1, -1]];
const KNIGHT_STEPS: readonly [number, number][] = [[1, 2], [2, 1], [2, -1], [1, -2], [-1, -2], [-2, -1], [-2, 1], [-1, 2]];
const KING_STEPS: readonly [number, number][] = [...ROOK_DIRS, ...BISHOP_DIRS];

/** „e4" fuer Feld 28. */
export function squareName(sq: number): string {
  return String.fromCharCode(97 + (sq % 8)) + String(Math.floor(sq / 8) + 1);
}

/** Feld fuer „e4", `-1` fuer Unsinn. */
export function squareIndex(name: string): number {
  if (!/^[a-h][1-8]$/.test(name)) return -1;
  return (name.charCodeAt(1) - 49) * 8 + (name.charCodeAt(0) - 97);
}

/**
 * Wohin die Figur von `from` ziehen kann, wenn auf `stars` Sterne liegen: jedes Feld, das sie erreicht — leer oder mit
 * Stern. Hinter einem Stern geht es fuer Turm/Laeufer/Dame nicht weiter.
 */
export function reachable(piece: StarPiece, from: number, stars: ReadonlySet<number>): number[] {
  const fx = from % 8;
  const fy = Math.floor(from / 8);
  const out: number[] = [];
  const steps = piece === 'N' ? KNIGHT_STEPS : piece === 'K' ? KING_STEPS : null;
  if (steps) {
    for (const [dx, dy] of steps) {
      const x = fx + dx;
      const y = fy + dy;
      if (x >= 0 && x < 8 && y >= 0 && y < 8) out.push(y * 8 + x);
    }
    return out;
  }
  const dirs = piece === 'R' ? ROOK_DIRS : piece === 'B' ? BISHOP_DIRS : KING_STEPS;
  for (const [dx, dy] of dirs) {
    let x = fx + dx;
    let y = fy + dy;
    while (x >= 0 && x < 8 && y >= 0 && y < 8) {
      const sq = y * 8 + x;
      out.push(sq);
      if (stars.has(sq)) break;
      x += dx;
      y += dy;
    }
  }
  return out;
}

/** Die Sterne, die von `from` aus mit EINEM Zug zu fressen sind. */
export function reachableStars(piece: StarPiece, from: number, stars: ReadonlySet<number>): number[] {
  return reachable(piece, from, stars).filter(sq => stars.has(sq));
}

/**
 * Reihenfolgen, in denen alle Sterne gefressen werden — hoechstens `limit` (mehr als zwei braucht niemand: eins heisst
 * eindeutig). Liefert die Loesungen selbst, damit der Generator die eine gleich mitnehmen kann.
 */
export function solveStars(piece: StarPiece, start: number, stars: readonly number[], limit = 2): number[][] {
  const found: number[][] = [];
  const left = new Set(stars);
  const path: number[] = [];
  const walk = (pos: number): void => {
    if (found.length >= limit) return;
    if (left.size === 0) {
      found.push([...path]);
      return;
    }
    for (const sq of reachableStars(piece, pos, left)) {
      left.delete(sq);
      path.push(sq);
      walk(sq);
      path.pop();
      left.add(sq);
      if (found.length >= limit) return;
    }
  };
  walk(start);
  return found;
}

/** Zufallsquelle 0 ≤ x < 1 — in Tests fest. */
export type Rng = () => number;

/** Kleiner, fester Zufall fuer Tests (mulberry32). */
export function seededRng(seed: number): Rng {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function pick<T>(list: readonly T[], rng: Rng): T {
  return list[Math.floor(rng() * list.length)];
}

/** Hoechstens so viele Versuche je Aufgabe; danach gilt die erste eindeutige, die gefunden wurde. */
const MAX_ATTEMPTS = 600;

/**
 * Eine Aufgabe mit `count` Sternen und GENAU einer Loesung. Gebaut wird ein zufaelliger Weg der Figur (Felder verschieden,
 * nie zurueck aufs Startfeld), auf dessen Zielfeldern die Sterne liegen; danach zaehlt `solveStars`, ob es genau eine
 * Reihenfolge gibt. Bevorzugt werden Aufgaben, bei denen am Anfang mehr als ein Stern erreichbar ist — sonst ist
 * die erste Entscheidung keine. `null` nur, wenn gar keine eindeutige Aufgabe gefunden wurde.
 */
export function generateStarPuzzle(piece: StarPiece, count: number, rng: Rng = Math.random): StarPuzzle | null {
  let fallback: StarPuzzle | null = null;
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
    const start = Math.floor(rng() * 64);
    const used = new Set<number>([start]);
    const path: number[] = [];
    let pos = start;
    for (let i = 0; i < count; i++) {
      // Weg ohne Rücksicht auf spaetere Sterne: ob er aufgeht, entscheidet erst `solveStars` (mit Blockaden).
      const options = reachable(piece, pos, new Set(path)).filter(sq => !used.has(sq));
      if (options.length === 0) break;
      pos = pick(options, rng);
      used.add(pos);
      path.push(pos);
    }
    if (path.length < count) continue;
    const solutions = solveStars(piece, start, path, 2);
    if (solutions.length !== 1) continue;
    const puzzle: StarPuzzle = { piece, start, stars: [...path].sort((a, b) => a - b), solution: solutions[0] };
    if (count < 2 || reachableStars(piece, start, new Set(path)).length >= 2) return puzzle;
    fallback ??= puzzle;
  }
  return fallback;
}

/** Die Stellung fuer das Brett: nur die weisse Figur (kein Koenig — das Brett prueft keine Schachregeln). */
export function starFen(piece: StarPiece, square: number): string {
  const rows: string[] = [];
  for (let y = 7; y >= 0; y--) {
    let row = '';
    let empty = 0;
    for (let x = 0; x < 8; x++) {
      if (y * 8 + x === square) {
        if (empty) row += String(empty);
        empty = 0;
        row += piece;
      } else {
        empty++;
      }
    }
    if (empty) row += String(empty);
    rows.push(row);
  }
  return `${rows.join('/')} w - - 0 1`;
}

/** Ein Stern als SVG fuer Chessgrounds `customSvg` (Raster 0..100 ueber dem Feld). */
export const STAR_SVG = (() => {
  const pts: string[] = [];
  for (let i = 0; i < 10; i++) {
    const r = i % 2 === 0 ? 34 : 14;
    const a = -Math.PI / 2 + (i * Math.PI) / 5;
    pts.push(`${(50 + r * Math.cos(a)).toFixed(1)},${(53 + r * Math.sin(a)).toFixed(1)}`);
  }
  return `<polygon points="${pts.join(' ')}" fill="#ffc83d" stroke="#b7791f" stroke-width="3.5" stroke-linejoin="round"/>`;
})();
