import { BATCH, MIN_GAMES_FOR_WORKERS, PgnParseRequest, PgnParseResponse, PgnWorkerLike, parsePgnTextParallel } from './pgn-parse-pool';
import { parseOneGame, parsePgnTextWithSource } from './pgn-parser';

const OPENINGS = ['1. e4 e5 2. Nf3 Nc6 3. Bb5 a6', '1. d4 d5 2. c4 e6 (2... c6 3. Nf3) 3. Nc3 Nf6', '1. c4 e5 {Englisch} 2. Nc3 Nf6',
  '1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6'];
function pgnOf(n: number): string {
  return Array.from({ length: n }, (_, i) =>
    `[Event "Rep"]\n[White "Linie ${i}"]\n[Black "Kapitel ${i % 7}"]\n[Result "*"]\n\n${OPENINGS[i % OPENINGS.length]} *\n`).join('\n');
}

/** Fake-Worker: antwortet asynchron (in zufälliger Reihenfolge über die Worker hinweg) mit dem echten Parser. */
class FakeWorker implements PgnWorkerLike {
  onmessage: ((e: MessageEvent<PgnParseResponse>) => void) | null = null;
  onerror: ((e: ErrorEvent) => void) | null = null;
  terminated = false;
  calls = 0;
  constructor(private delay: number, private failOnCall = -1) {}
  postMessage(msg: PgnParseRequest): void {
    const call = this.calls++;
    setTimeout(() => {
      if (this.terminated) return;
      if (call === this.failOnCall) { this.onerror?.({ preventDefault() {} } as ErrorEvent); return; }
      // wie nach postMessage: reine Objekte ohne Prototyp
      const out = JSON.parse(JSON.stringify(msg.games.map(g => parseOneGame(g, msg.opts))));
      this.onmessage?.({ data: { id: msg.id, out } } as MessageEvent<PgnParseResponse>);
    }, this.delay);
  }
  terminate(): void { this.terminated = true; }
}

const sansOf = (r: { game: { moves: { san: string }[] } }[]) => r.map(p => p.game.moves.map(m => m.san).join(' '));

describe('parsePgnTextParallel', () => {
  const N = MIN_GAMES_FOR_WORKERS + 3 * BATCH + 7;
  const pgn = pgnOf(N);
  const expected = parsePgnTextWithSource(pgn, { foldVariations: true, unbounded: true });

  it('liefert dasselbe wie das sequentielle Lesen, in Reihenfolge des Textes — auch wenn Worker verschieden schnell sind', async () => {
    const workers = [new FakeWorker(5), new FakeWorker(0), new FakeWorker(2)];
    let k = 0;
    const progress: number[] = [];
    const r = await parsePgnTextParallel(pgn, { foldVariations: true }, d => progress.push(d), { createWorker: () => workers[k++], workers: 3 });
    expect(r.length).toBe(N);
    expect(sansOf(r)).toEqual(sansOf(expected));
    expect(r.map(p => p.raw)).toEqual(expected.map(p => p.raw));
    expect(r[1].game.comments).toEqual(expected[1].game.comments);
    expect(progress[progress.length - 1]).toBe(N);
    expect(workers.every(w => w.terminated)).toBeTrue();
    expect(workers.every(w => w.calls > 0)).withContext('alle Worker bekommen Arbeit').toBeTrue();
  });

  it('Züge haben nach der Übergabe wieder die Methoden von chess.js', async () => {
    let k = 0;
    const r = await parsePgnTextParallel(pgn, {}, undefined, { createWorker: () => (k++ < 2 ? new FakeWorker(0) : null), workers: 2 });
    const capture = r[3].game.moves.find(m => m.san === 'cxd4')!;
    expect(capture.isCapture()).toBeTrue();
  });

  it('fällt ein Worker aus, liest der Hauptthread seinen Stapel — es fehlt nichts', async () => {
    const workers = [new FakeWorker(0, 1), new FakeWorker(1)];
    let k = 0;
    const r = await parsePgnTextParallel(pgn, { foldVariations: true }, undefined, { createWorker: () => workers[k++], workers: 2 });
    expect(sansOf(r)).toEqual(sansOf(expected));
  });

  it('fallen alle Worker aus, liest der Hauptthread den Rest', async () => {
    const r = await parsePgnTextParallel(pgn, { foldVariations: true }, undefined, { createWorker: () => new FakeWorker(0, 0), workers: 2 });
    expect(sansOf(r)).toEqual(sansOf(expected));
  });

  it('ohne Worker oder bei wenigen Abschnitten: Lesen im Hauptthread', async () => {
    const create = jasmine.createSpy('create').and.returnValue(null);
    expect((await parsePgnTextParallel(pgn, { foldVariations: true }, undefined, { createWorker: create })).length).toBe(N);
    const small = pgnOf(10);
    const never = jasmine.createSpy('never');
    expect((await parsePgnTextParallel(small, {}, undefined, { createWorker: never })).length).toBe(10);
    expect(never).not.toHaveBeenCalled();
  });

  it('mit echtem Web Worker: dasselbe Ergebnis wie sequentiell', async () => {
    if (typeof Worker === 'undefined') { pending('kein Worker in dieser Umgebung'); return; }
    const r = await parsePgnTextParallel(pgn, { foldVariations: true }, undefined, { workers: 2 });
    expect(sansOf(r)).toEqual(sansOf(expected));
    expect(r.map(p => p.game.fens.length)).toEqual(expected.map(p => p.game.fens.length));
  });
});
