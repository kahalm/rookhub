import { PDFJS_ASSET_BASE, PDF_WORKER_SRC } from './pdf-engine';

describe('PdfEngineLoader — Worker-Adresse', () => {
  // Der Worker läuft über die Vorschaltdatei mit den Nachrüstungen (Promise.withResolvers/try, ArrayBuffer#transfer):
  // direkt auf pdf.worker.min.mjs ging in der Chrome-111-WebView des Boox-E-Readers kein Buch auf.
  it('startet den Worker über pdf-worker-compat.mjs, nicht direkt über pdf.worker.min.mjs', () => {
    expect(PDF_WORKER_SRC).toBe(`${PDFJS_ASSET_BASE}/pdf-worker-compat.mjs`);
  });
});
