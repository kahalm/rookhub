// Maia-3 im Browser: ein KLASSISCHER Worker als statische Datei (angular.json kopiert sie nach
// /assets/maia/), kein gebündelter TS-Worker. Bewusst winzig — alles Schachliche (Kodierung,
// Spiegeln, Zugwahl) liegt in TypeScript (maia-encoding.ts), hier läuft nur das Netz.
//
// onnxruntime-web wird NIE in TypeScript importiert: die drei Laufzeit-Dateien liegen als Assets
// unter /assets/ort/ (wie Stockfish unter /assets/stockfish/) und kommen per importScripts.
// Unter der Produktions-CSP nachgemessen (script-src 'self' 'wasm-unsafe-eval'; worker-src 'self'):
// Sitzung aufbauen ~1,2 s, ein Zug ~0,15 s.
//
// Nachrichten hin:   { type: 'init', model: ArrayBuffer }   (transferiert — kein zweites 45-MB-Exemplar)
//                    { type: 'infer', id, tokens: ArrayBuffer, elo }
// Nachrichten zurück: { type: 'ready' } · { type: 'result', id, logits: ArrayBuffer }
//                    { type: 'error', id?, message } — ohne id = die Sitzung kam nicht zustande.
importScripts('/assets/ort/ort.wasm.min.js');
ort.env.wasm.wasmPaths = '/assets/ort/';
ort.env.wasm.numThreads = 1;          // ohne COOP/COEP gibt es keinen SharedArrayBuffer
let session = null;
self.onmessage = async (e) => {
  const m = e.data;
  try {
    if (m.type === 'init') {
      session = await ort.InferenceSession.create(m.model, { executionProviders: ['wasm'] });
      self.postMessage({ type: 'ready' });
    } else if (m.type === 'infer') {
      const out = await session.run({
        tokens: new ort.Tensor('float32', new Float32Array(m.tokens), [1, 64, 12]),
        elo_self: new ort.Tensor('float32', Float32Array.from([m.elo]), [1]),
        elo_oppo: new ort.Tensor('float32', Float32Array.from([m.elo]), [1]),
      });
      const logits = new Float32Array(out.logits_move.data);
      self.postMessage({ type: 'result', id: m.id, logits: logits.buffer }, [logits.buffer]);
    }
  } catch (err) {
    self.postMessage({ type: 'error', id: m.id, message: String((err && err.message) || err) });
  }
};
