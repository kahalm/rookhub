/**
 * Sternenjagd: EINE weisse Figur, ein paar Sterne auf dem Brett — jeder Zug muss genau einen Stern fressen, nach so
 * vielen Zuegen, wie Sterne liegen, ist das Brett leer. Rein, ohne Angular: Zugregeln, Loesungszaehler, Generator und
 * die Stufenfolge.
 *
 * <p>Regeln: Sterne stehen im Weg wie Figuren — Turm, Laeufer und Dame koennen nicht ueber einen Stern hinweg ziehen,
 * nur auf ihn (er ist ja zu fressen). Der Springer springt, fuer ihn gibt es keine
 * Blockade. Es werden nur Aufgaben mit GENAU EINER Reihenfolge gestellt (Wunsch 2026-10-10: „nur eindeutige Loesungen
 * aufstellen") — damit ist jeder Stern, der nicht der naechste der Loesung ist, eine Sackgasse, und das darf das Kind
 * sofort erfahren statt erst drei Zuege spaeter.</p>
 *
 * <p>Felder sind Zahlen 0..63: a1 = 0, h1 = 7, a8 = 56.</p>
 */

export type StarPiece = 'R' | 'B' | 'Q' | 'N';

export interface StarPuzzle {
  piece: StarPiece;
  /** Startfeld der Figur. */
  start: number;
  /** Felder der Sterne (ungeordnet). */
  stars: number[];
  /** Eine Reihenfolge, in der alle Sterne gefressen werden — bei `unique` die einzige. */
  solution: number[];
  /** Genau eine Loesung. Nur grosse Aufgaben des freien Spiels sind es nicht (`generateOpenPath`). */
  unique: boolean;
}

export interface StarStage {
  /** 1-basiert. */
  stage: number;
  piece: StarPiece;
  /** Sterne je Aufgabe, in Reihenfolge — die Laenge ist die Zahl der Aufgaben. */
  counts: readonly number[];
}

/** Aufgaben je Stufe: je zwei mit n, n+1 und n+2 Sternen. */
export const STARS_PER_STAGE = 6;

/**
 * Die Stufenfolge (Wunsch 2026-10-10: „steiler — Stufe 1: 2 Puzzles mit 2 Sternen, dann 2 mit 3, dann 2 mit 4"): Turm,
 * Laeufer, Springer, Dame reihum; jede Stufe zieht innerhalb von sich an (n, n, n+1, n+1, n+2, n+2), und alle vier
 * Stufen steigt n um eins — Stufe 1 beginnt bei 2, Stufe 17–20 bei 6 (bis 8 Sterne).
 */
export const STAR_STAGES: readonly StarStage[] = Array.from({ length: 20 }, (_, i) => {
  const base = 2 + Math.floor(i / 4);
  return {
    stage: i + 1,
    piece: (['R', 'B', 'N', 'Q'] as const)[i % 4],
    counts: [base, base, base + 1, base + 1, base + 2, base + 2],
  };
});

const ROOK_DIRS: readonly [number, number][] = [[1, 0], [-1, 0], [0, 1], [0, -1]];
const BISHOP_DIRS: readonly [number, number][] = [[1, 1], [1, -1], [-1, 1], [-1, -1]];
const KNIGHT_STEPS: readonly [number, number][] = [[1, 2], [2, 1], [2, -1], [1, -2], [-1, -2], [-2, -1], [-2, 1], [-1, 2]];
const QUEEN_DIRS: readonly [number, number][] = [...ROOK_DIRS, ...BISHOP_DIRS];

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
  if (piece === 'N') {
    for (const [dx, dy] of KNIGHT_STEPS) {
      const x = fx + dx;
      const y = fy + dy;
      if (x >= 0 && x < 8 && y >= 0 && y < 8) out.push(y * 8 + x);
    }
    return out;
  }
  const dirs = piece === 'R' ? ROOK_DIRS : piece === 'B' ? BISHOP_DIRS : QUEEN_DIRS;
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
export function solveStars(
  piece: StarPiece, start: number, stars: readonly number[], limit = 2, maxNodes = Infinity,
): number[][] | null {
  const found: number[][] = [];
  const left = new Set(stars);
  const path: number[] = [];
  let nodes = 0;
  const walk = (pos: number): void => {
    if (found.length >= limit || nodes > maxNodes) return;
    nodes++;
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
      if (found.length >= limit || nodes > maxNodes) return;
    }
  };
  walk(start);
  // Budget erschoepft: keine Aussage — `null`, damit der Generator die Aufgabe verwirft statt zu raten.
  return nodes > maxNodes ? null : found;
}

/** Richtung eines Zugs: bei Turm/Laeufer/Dame die Linie (Vorzeichen), beim Springer der Sprung selbst. */
function direction(piece: StarPiece, from: number, to: number): string {
  const dx = (to % 8) - (from % 8);
  const dy = Math.floor(to / 8) - Math.floor(from / 8);
  return piece === 'N' ? `${dx},${dy}` : `${Math.sign(dx)},${Math.sign(dy)}`;
}

/**
 * Nach jedem gefressenen Stern geht es in eine ANDERE Richtung weiter — zurueck oder abgebogen, nie geradeaus
 * (Wunsch 2026-10-10). Geradeaus waere fuer Turm/Laeufer/Dame derselbe Strahl, und den sieht ein Kind ohne Nachdenken.
 */
export function turnsEveryMove(piece: StarPiece, start: number, solution: readonly number[]): boolean {
  let prev: string | null = null;
  let pos = start;
  for (const sq of solution) {
    const dir = direction(piece, pos, sq);
    if (dir === prev) return false;
    prev = dir;
    pos = sq;
  }
  return true;
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
const MAX_ATTEMPTS = 3000;

/**
 * Eine Aufgabe mit `count` Sternen und GENAU einer Loesung. Gebaut wird ein zufaelliger Weg der Figur (Felder verschieden,
 * nie zurueck aufs Startfeld), auf dessen Zielfeldern die Sterne liegen; danach zaehlt `solveStars`, ob es genau eine
 * Reihenfolge gibt und sie nach jedem Stern die Richtung wechselt (`turnsEveryMove`). Bevorzugt werden Aufgaben,
 * bei denen am Anfang mehr als ein Stern erreichbar ist — sonst ist
 * die erste Entscheidung keine. `null` nur, wenn gar keine eindeutige Aufgabe gefunden wurde.
 */
export function generateStarPuzzle(
  piece: StarPiece, count: number, rng: Rng = Math.random, budgetMs = GENERATE_BUDGET_MS,
): StarPuzzle | null {
  if (count > maxStars(piece)) return null;
  if (count > RANDOM_WALK_MAX) {
    // Eindeutig nur, solange die Kette es schafft — darueber gleich die offene Aufgabe mit dem ganzen Budget.
    if (count <= chainMax(piece)) {
      const chain = generateChain(piece, count, rng, budgetMs / 2);
      if (chain) return chain;
      return generateOpenPath(piece, count, rng, budgetMs / 2);
    }
    return generateOpenPath(piece, count, rng, budgetMs);
  }
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
    const solutions = solveStars(piece, start, path, 2, DFS_MAX_NODES);
    if (!solutions || solutions.length !== 1 || !turnsEveryMove(piece, start, solutions[0])) continue;
    const puzzle: StarPuzzle = { piece, start, stars: [...path].sort((a, b) => a - b), solution: solutions[0], unique: true };
    if (count < 2 || reachableStars(piece, start, new Set(path)).length >= 2) return puzzle;
    fallback ??= puzzle;
  }
  return fallback;
}

/** Bis zu so vielen Sternen reicht der Zufallsweg; darueber werden eindeutige Aufgaben damit zu selten. */
export const RANDOM_WALK_MAX = 8;
/** Hoechstzahl Sterne im freien Spiel (alle Felder ausser dem Startfeld). */
export const MAX_STARS = 63;

/** Bis hierher findet `generateChain` eindeutige Aufgaben (gemessen 2026-10-10); darueber versucht sie es nicht mehr. */
function chainMax(piece: StarPiece): number {
  return { R: 14, B: 12, N: 25, Q: 10 }[piece];
}

/** Schritte je Anlauf der offenen Aufgabe — haengt die Suche fest, lieber neu anfangen (anderes Feld) als weiterbohren. */
const OPEN_STEPS_PER_TRY = 20_000;

/** Mehr geht mit dieser Figur nicht: der Laeufer bleibt auf seiner Farbe (32 Felder, eins davon ist das Startfeld). */
export function maxStars(piece: StarPiece): number {
  return piece === 'B' ? 31 : MAX_STARS;
}
/** So lange darf das Wuerfeln einer grossen Aufgabe dauern — es laeuft im Browser, das Brett soll nicht haengen. */
export const GENERATE_BUDGET_MS = 600;
/** Deckel fuer die Loesungssuche: ohne ihn lief die Dame mit 20 Sternen minutenlang. */
const DFS_MAX_NODES = 200_000;
/** So oft wird beim Legen eine Falle erlaubt (ein zweiter sichtbarer Stern, nach dem es nicht weitergeht). */
const TRAP_CHANCE = 0.4;

/**
 * Grosse Aufgaben (mehr als `RANDOM_WALK_MAX` Sterne) werden RUECKWAERTS gelegt: vom letzten Stern aus wird jeweils
 * das Feld davor gesucht, von dem aus von den noch liegenden Sternen genau der naechste zu sehen ist. Ein Stern, der
 * spaeter (zeitlich frueher) dazukommt, aendert an den schon gelegten Zuegen nichts — die sind ja dann gefressen.
 * So ist die Loesung eindeutig, ohne dass gesucht werden muss. Damit es nicht nur „den einen sichtbaren Stern
 * finden" ist, darf an einer Stelle ein zweiter Stern sichtbar sein, wenn er eine FALLE ist: nach ihm ist kein Stern
 * mehr erreichbar (`TRAP_CHANCE`). Der Richtungswechsel gilt schon beim Legen.
 */
function generateChain(piece: StarPiece, count: number, rng: Rng, budgetMs: number): StarPuzzle | null {
  const deadline = Date.now() + budgetMs;
  let first = true;
  while (first || Date.now() < deadline) {
    first = false;
    // seq[0] = letzter Stern; davor wird angehaengt (rueckwaerts in der Zeit).
    const seq = [Math.floor(rng() * 64)];
    const remaining = new Set<number>(seq);
    let ok = true;
    for (let k = 0; k < count && ok; k++) {
      const target = seq[seq.length - 1];
      const after = seq.length >= 2 ? seq[seq.length - 2] : null;
      const blockers = new Set(remaining);
      blockers.delete(target);
      const clean: number[] = [];
      const traps: number[] = [];
      for (const x of reachable(piece, target, blockers)) {
        if (remaining.has(x)) continue;
        if (after !== null && direction(piece, x, target) === direction(piece, target, after)) continue;
        const seen = reachableStars(piece, x, remaining);
        if (seen.length === 1) clean.push(x);
        else if (seen.length === 2) {
          const other = seen[0] === target ? seen[1] : seen[0];
          const rest = new Set(remaining);
          rest.delete(other);
          if (reachableStars(piece, other, rest).length === 0) traps.push(x);
        }
      }
      const pool = traps.length && rng() < TRAP_CHANCE ? traps : clean.length ? clean : traps;
      if (!pool.length) { ok = false; break; }
      const x = pick(pool, rng);
      if (k < count - 1) remaining.add(x);
      seq.push(x);
    }
    if (!ok) continue;
    const start = seq[seq.length - 1];
    const solution = seq.slice(0, -1).reverse();
    return { piece, start, stars: [...solution].sort((a, b) => a - b), solution, unique: true };
  }
  return null;
}

/**
 * Wenn sich keine eindeutige Aufgabe findet (viele Sterne — mit 20 Sternen ist an fast jeder Stelle mehr als einer zu
 * sehen), eine mit MEHREREN Loesungen: rueckwaerts gelegt wie `generateChain`, aber ohne Sichtbarkeits-Regel und mit
 * Zuruecksetzen, wenn es nicht weitergeht. Der Richtungswechsel gilt fuer die gelegte Loesung; gespielt zaehlt jeder
 * Weg, der alle Sterne frisst (die Seite prueft eine Sackgasse mit `solveStars`).
 */
export function generateOpenPath(piece: StarPiece, count: number, rng: Rng, budgetMs: number): StarPuzzle | null {
  if (count > maxStars(piece)) return null;
  const deadline = Date.now() + budgetMs;
  let first = true;
  while (first || Date.now() < deadline) {
    first = false;
    const seq = [Math.floor(rng() * 64)];
    const remaining = new Set<number>(seq);
    let steps = 0;
    const extend = (): boolean => {
      if (seq.length === count + 1) return true;
      if (++steps > OPEN_STEPS_PER_TRY || (steps % 512 === 0 && Date.now() > deadline)) return false;
      const target = seq[seq.length - 1];
      const after = seq.length >= 2 ? seq[seq.length - 2] : null;
      const blockers = new Set(remaining);
      blockers.delete(target);
      const options = reachable(piece, target, blockers).filter(x => !remaining.has(x)
        && (after === null || direction(piece, x, target) !== direction(piece, target, after)));
      // Felder mit wenigen Fortsetzungen zuerst (Warnsdorff) — sonst bleibt am Ende ein Feld unerreichbar liegen.
      const scored = options.map(x => ({ x, n: reachable(piece, x, remaining).length, r: rng() }))
        .sort((a, b) => a.n - b.n || a.r - b.r);
      for (const { x } of scored) {
        const last = seq.length === count;
        if (!last) remaining.add(x);
        seq.push(x);
        if (extend()) return true;
        seq.pop();
        if (!last) remaining.delete(x);
        if (steps > OPEN_STEPS_PER_TRY || Date.now() > deadline) return false;
      }
      return false;
    };
    if (!extend()) continue;
    const start = seq[seq.length - 1];
    const solution = seq.slice(0, -1).reverse();
    return { piece, start, stars: [...solution].sort((a, b) => a - b), solution, unique: false };
  }
  return null;
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
