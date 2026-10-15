import { PDFJS_ASSET_BASE, PDF_WORKER_SRC } from './pdf-engine';
import { renderPixelRatio } from './pdf-viewer.component';

describe('PdfEngineLoader — Worker-Adresse', () => {
  // Der Worker läuft über die Vorschaltdatei mit den Nachrüstungen (Promise.withResolvers/try, ArrayBuffer#transfer):
  // direkt auf pdf.worker.min.mjs ging in der Chrome-111-WebView des Boox-E-Readers kein Buch auf.
  it('startet den Worker über pdf-worker-compat.mjs, nicht direkt über pdf.worker.min.mjs', () => {
    expect(PDF_WORKER_SRC).toBe(`${PDFJS_ASSET_BASE}/pdf-worker-compat.mjs`);
  });
});

describe('PDF-Leinwand: Pixeldichte × UI-Skalierung', () => {
  it('rechnet die UI-Skalierung ein (sonst unscharf bei 200 %), bleibt bei höchstens 3', () => {
    expect(renderPixelRatio(1, 100)).toBe(1);
    expect(renderPixelRatio(1, 200)).toBe(2);
    expect(renderPixelRatio(1.5, 150)).toBe(2.25);
    expect(renderPixelRatio(2.5, 200)).toBe(3);
    expect(renderPixelRatio(undefined, 100)).toBe(1);
  });
});
