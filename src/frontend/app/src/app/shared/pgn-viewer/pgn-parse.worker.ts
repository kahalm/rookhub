/// <reference lib="webworker" />
import { parseOneGame } from './pgn-parser';
import type { PgnParseRequest, PgnParseResponse } from './pgn-parse-pool';

/** Liest einen Stapel Partien (Abschnitte aus `splitPgnGames`) abseits des Hauptthreads — siehe `pgn-parse-pool.ts`. */
addEventListener('message', ({ data }: MessageEvent<PgnParseRequest>) => {
  const out = data.games.map(g => parseOneGame(g, data.opts));
  postMessage({ id: data.id, out } satisfies PgnParseResponse);
});
