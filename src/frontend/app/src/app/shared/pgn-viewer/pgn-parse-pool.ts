import { Move } from 'chess.js';
import { ParsePgnOptions, ParsedGameWithSource, parseOneGame, parsePgnTextWithSourceAsync, splitPgnGames } from './pgn-parser';

/**
 * Großes PGN parallel lesen (Wunsch 2026-10-19: Repertoire 16, 4,9 MB / 817 Abschnitte, zählte ~5 s hoch): die Abschnitte
 * gehen in Stapeln an bis zu {@link MAX_WORKERS} Web Worker, die gleichzeitig rechnen; das Ergebnis steht in der
 * Reihenfolge des Textes (Index je Abschnitt), der Hauptthread bleibt frei. ~80 % der Zeit ist chess.js (jeder Zug wird
 * regelgerecht gespielt) — das teilt sich gut auf.
 *
 * Rückfall auf {@link parsePgnTextWithSourceAsync} (Portionen im Hauptthread): ohne `Worker` (alte WebViews, Tests), bei
 * wenigen Abschnitten (Worker-Start lohnt nicht) oder wenn kein Worker startet. Scheitert ein Worker unterwegs, liest der
 * Hauptthread seine Stapel selbst — es fehlt nie ein Abschnitt. Ergebnis identisch zum sequentiellen Lesen.
 */

export interface PgnParseRequest { id: number; games: string[]; opts?: ParsePgnOptions; }
export interface PgnParseResponse { id: number; out: (ParsedGameWithSource | null)[]; }

/** Was der Pool von einem Worker braucht (im Test ersetzbar). */
export interface PgnWorkerLike {
  onmessage: ((e: MessageEvent<PgnParseResponse>) => void) | null;
  onerror: ((e: ErrorEvent) => void) | null;
  postMessage(msg: PgnParseRequest): void;
  terminate(): void;
}

export const MAX_WORKERS = 4;
/** Darunter liest der Hauptthread — der Start der Worker kostet mehr, als er spart. */
export const MIN_GAMES_FOR_WORKERS = 120;
/** Abschnitte je Nachricht: klein genug für gleichmäßige Verteilung und flüssigen Fortschritt. */
export const BATCH = 20;

export function defaultWorkerCount(): number {
  const cores = typeof navigator !== 'undefined' ? navigator.hardwareConcurrency || 2 : 2;
  return Math.max(1, Math.min(MAX_WORKERS, cores - 1));
}

function createDefaultWorker(): PgnWorkerLike | null {
  if (typeof Worker === 'undefined') return null;
  try {
    return new Worker(new URL('./pgn-parse.worker', import.meta.url), { type: 'module' }) as unknown as PgnWorkerLike;
  } catch {
    return null;
  }
}

/** Nach `postMessage` sind die Züge reine Objekte — die Methoden von chess.js' `Move` (isCapture …) wieder anhängen. */
function rehydrate(p: ParsedGameWithSource | null): ParsedGameWithSource | null {
  if (p) for (const m of p.game.moves) Object.setPrototypeOf(m, Move.prototype);
  return p;
}

export async function parsePgnTextParallel(pgnText: string, opts?: ParsePgnOptions,
  onProgress?: (done: number, total: number) => void,
  deps: { createWorker?: () => PgnWorkerLike | null; workers?: number } = {}): Promise<ParsedGameWithSource[]> {
  const games = splitPgnGames(pgnText);
  const want = Math.min(deps.workers ?? defaultWorkerCount(), Math.ceil(games.length / BATCH));
  if (games.length < MIN_GAMES_FOR_WORKERS || want < 1) return parsePgnTextWithSourceAsync(pgnText, opts, onProgress);
  const create = deps.createWorker ?? createDefaultWorker;
  const pool: PgnWorkerLike[] = [];
  for (let i = 0; i < want; i++) {
    const w = create();
    if (!w) break;
    pool.push(w);
  }
  if (pool.length === 0) return parsePgnTextWithSourceAsync(pgnText, opts, onProgress);

  const results: (ParsedGameWithSource | null)[] = new Array(games.length).fill(null);
  const batches: number[] = [];
  for (let start = 0; start < games.length; start += BATCH) batches.push(start);
  let next = 0;
  let finished = 0;
  let done = 0;

  await new Promise<void>(resolve => {
    const complete = (start: number, out: (ParsedGameWithSource | null)[]) => {
      for (let k = 0; k < out.length; k++) results[start + k] = rehydrate(out[k]);
      done += out.length;
      finished++;
      onProgress?.(done, games.length);
      if (finished === batches.length) resolve();
    };
    const parseHere = (start: number) =>
      complete(start, games.slice(start, start + BATCH).map(g => parseOneGame(g, opts)));
    const feed = (w: PgnWorkerLike) => {
      if (next >= batches.length) return;
      const start = batches[next++];
      w.onmessage = e => { complete(start, e.data.out); feed(w); };
      w.onerror = e => {
        e.preventDefault?.();
        // Dieser Worker ist raus — seinen Stapel liest der Hauptthread, der Rest geht an die übrigen (oder hierher).
        w.terminate();
        const i = pool.indexOf(w);
        if (i >= 0) pool.splice(i, 1);
        parseHere(start);
        if (pool.length === 0) while (next < batches.length) parseHere(batches[next++]);
      };
      w.postMessage({ id: start, games: games.slice(start, start + BATCH), opts });
    };
    for (const w of [...pool]) feed(w);
  });
  for (const w of pool) w.terminate();
  return results.filter((p): p is ParsedGameWithSource => p !== null);
}
