import { Injectable } from '@angular/core';
import type * as PdfJs from 'pdfjs-dist';

/**
 * Wo die pdf.js-Dateien liegen (angular.json kopiert sie aus `node_modules/pdfjs-dist` dorthin) — und zwar der
 * LEGACY-Build (`legacy/build/`): der Standard-Build von pdf.js 6 ruft `Promise.try` und `Promise.withResolvers` ohne
 * Rückfall auf, und in Browsern ohne die beiden (Chrome < 128, Safari < 18.2, Firefox < 134) stirbt `getDocument` still im
 * Hintergrund — der Leser zeigte nur den Ladebalken, die Datei wurde nie angefordert (Dev, 13.10.2026). Der Legacy-Build
 * bringt die Polyfills mit; geprüft im selben Headless-Chromium, in dem der Standard-Build hing.
 */
export const PDFJS_ASSET_BASE = '/assets/pdfjs';

export type PdfJsLib = typeof PdfJs;
export type PdfDocument = PdfJs.PDFDocumentProxy;
export type PdfPage = PdfJs.PDFPageProxy;
export type PdfLoadingTask = PdfJs.PDFDocumentLoadingTask;

/**
 * Lädt pdf.js zur Laufzeit aus den eigenen Assets (dynamischer Import mit Laufzeit-Adresse) statt es ins
 * Bundle zu nehmen: ~1 MB, das nur der Leser braucht — so landet es weder im Start-Bundle noch im Vorab-Cache
 * des Service Workers (der holt JEDEN Lazy-Chunk bei jeder Version neu). Als Dienst, damit Specs ihn ersetzen.
 */
@Injectable({ providedIn: 'root' })
export class PdfEngineLoader {
  private lib: Promise<PdfJsLib> | null = null;

  load(): Promise<PdfJsLib> {
    if (!this.lib) {
      const url = `${PDFJS_ASSET_BASE}/pdf.min.mjs`;
      this.lib = (import(/* @vite-ignore */ /* webpackIgnore: true */ url) as Promise<PdfJsLib>).then(lib => {
        lib.GlobalWorkerOptions.workerSrc = `${PDFJS_ASSET_BASE}/pdf.worker.min.mjs`;
        return lib;
      });
      this.lib.catch(() => { this.lib = null; });   // beim nächsten Versuch neu laden
    }
    return this.lib;
  }

  /** Die Parameter, mit denen jedes Dokument geöffnet wird: Schriften/CMaps/WASM aus den Assets, Bereichsanfragen. */
  documentParams(url: string): Record<string, unknown> {
    return {
      url,
      cMapUrl: `${PDFJS_ASSET_BASE}/cmaps/`,
      cMapPacked: true,
      standardFontDataUrl: `${PDFJS_ASSET_BASE}/standard_fonts/`,
      wasmUrl: `${PDFJS_ASSET_BASE}/wasm/`,
      iccUrl: `${PDFJS_ASSET_BASE}/iccs/`,
      // Nur holen, was gerade gebraucht wird (Bereichsanfragen) — ein 800-MB-Buch wird nicht erst ganz geladen.
      rangeChunkSize: 256 * 1024,
      disableAutoFetch: true,
      disableStream: true,
    };
  }
}
