// Paritäts-Fixture für den Maia-Dienst der API (MaiaService, C#): dieselben Stellungen durch die JS-Fassung des
// Analysebretts (maia-encoding.ts + onnxruntime-web, wie maia-worker.js) und die besten Züge samt Wahrscheinlichkeit
// nach tests/RookHub.Api.Tests/Fixtures/maia-parity.json schreiben. Der C#-Test MaiaServiceTests vergleicht dagegen.
//
// Aufruf (im Repo-Wurzelordner, Frontend-Abhängigkeiten installiert, Modell geholt per
// `sh src/frontend/app/maia-model/fetch.sh`):
//   node tools/maia-parity/generate.mjs
// Neu erzeugen nur, wenn sich Modell-Pin, Kodierung oder die Stellungsliste ändern.
import { build } from '../../src/frontend/app/node_modules/esbuild/lib/main.js';
import { readFileSync, writeFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const app = join(root, 'src/frontend/app');
const tmp = mkdtempSync(join(tmpdir(), 'maia-parity-'));
try {
  // Die echte Kodierung des Analysebretts (TypeScript + chess.js) zu einem Modul bündeln.
  const out = join(tmp, 'enc.mjs');
  await build({
    entryPoints: [join(app, 'src/app/features/analysis/maia/maia-encoding.ts')],
    bundle: true, format: 'esm', platform: 'node', outfile: out, logLevel: 'error',
    nodePaths: [join(app, 'node_modules')],
  });
  const enc = await import(pathToFileURL(out).href);
  const require = createRequire(join(app, 'package.json'));
  const ort = require('onnxruntime-web');
  ort.env.wasm.numThreads = 1;
  const model = readFileSync(join(app, 'maia-model/maia3_simplified.onnx'));
  const session = await ort.InferenceSession.create(model, { executionProviders: ['wasm'] });

  const cases = [
    { fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', eloSelf: 1600, eloOppo: 1600 },
    { fen: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', eloSelf: 1600, eloOppo: 1600 },
    { fen: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', eloSelf: 1100, eloOppo: 2200 },
    // Vereinspartie 259 nach 44.g3 (Befund: 44...h3 fehlte in der Erklärung).
    { fen: '8/1b6/p5kb/8/2NB2pp/P5P1/1P6/5K2 b - - 0 44', eloSelf: 1600, eloOppo: 1600 },
    { fen: '8/1b6/p5kb/8/2NB2pp/P5P1/1P6/5K2 b - - 0 44', eloSelf: 2000, eloOppo: 1800 },
    // Rochade beider Seiten möglich, Weiß am Zug.
    { fen: 'r3k2r/pppq1ppp/2npbn2/2b1p3/2B1P3/2NPBN2/PPPQ1PPP/R3K2R w KQkq - 4 8', eloSelf: 1800, eloOppo: 1800 },
    // Schwarz rochiert (gespiegelt), en passant möglich.
    { fen: 'r3k2r/ppp2ppp/2n5/3pP3/8/8/PPP2PPP/R3K2R w KQkq d6 0 12', eloSelf: 1500, eloOppo: 1500 },
    // Umwandlung für Weiß und (gespiegelt) für Schwarz.
    { fen: '8/4P1k1/8/8/8/8/1p4K1/8 w - - 0 60', eloSelf: 1600, eloOppo: 1600 },
    { fen: '8/4P1k1/8/8/8/8/1p4K1/8 b - - 0 60', eloSelf: 1600, eloOppo: 1600 },
    // Schach: nur wenige legale Züge.
    { fen: 'rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3', eloSelf: 1600, eloOppo: 1600 },
  ];

  const results = [];
  for (const c of cases) {
    const e = enc.encodePosition(c.fen);
    const res = await session.run({
      tokens: new ort.Tensor('float32', e.tokens, [1, 64, 12]),
      elo_self: new ort.Tensor('float32', Float32Array.from([c.eloSelf]), [1]),
      elo_oppo: new ort.Tensor('float32', Float32Array.from([c.eloOppo]), [1]),
    });
    const policy = enc.policyFromLogits(res.logits_move.data, e);
    results.push({
      ...c,
      legal: e.legal.length,
      top: policy.slice(0, 8).map(m => ({ uci: m.uci, p: Number(m.p.toFixed(6)) })),
    });
  }
  const target = join(root, 'tests/RookHub.Api.Tests/Fixtures/maia-parity.json');
  writeFileSync(target, JSON.stringify({ model: '405bf76c', cases: results }, null, 2) + '\n');
  console.log(`maia-parity: ${results.length} Stellungen → ${target}`);
  for (const r of results) console.log(r.fen, r.eloSelf, r.top.slice(0, 4).map(m => `${m.uci} ${(m.p * 100).toFixed(1)}%`).join(', '));
} finally {
  rmSync(tmp, { recursive: true, force: true });
}
