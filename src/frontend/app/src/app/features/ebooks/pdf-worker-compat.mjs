// Vorschaltdatei für den pdf.js-Worker (angular.json kopiert sie nach /assets/pdfjs, PdfEngineLoader setzt sie als
// workerSrc). Der Legacy-Build von pdf.js 6 rüstet `Promise.withResolvers`/`Promise.try` nur im Hauptteil nach, der
// WORKER ruft sie ungeschützt auf — in älteren WebViews (Boox-E-Reader: Chrome 111; withResolvers erst ab Chrome 119,
// try ab 128) scheiterte so jedes Buch mit „Promise.withResolvers is not a function“, nur Daten im Speicher gingen.
// Dazu `ArrayBuffer.prototype.transfer(ToFixedLength)` (Chrome 114) — ohne blieb der Seiteninhalt leer
// („GetOperatorList … transferToFixedLength is not a function“). Der Kopier-Ersatz löst den alten Puffer nicht ab;
// pdf.js liest ihn danach nicht mehr, also gleichwertig.
// Erst nachrüsten, dann den Worker dynamisch laden (ein statischer Import liefe VOR diesem Code).
if (typeof Promise.withResolvers !== 'function') {
  Promise.withResolvers = function () {
    let resolve, reject;
    const promise = new this((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
  };
}
if (typeof Promise.try !== 'function') {
  Promise.try = function (fn, ...args) {
    return new this(resolve => resolve(fn(...args)));
  };
}
for (const name of ['transfer', 'transferToFixedLength']) {
  if (typeof ArrayBuffer.prototype[name] !== 'function') {
    Object.defineProperty(ArrayBuffer.prototype, name, {
      configurable: true, writable: true,
      value: function (length = this.byteLength) {
        const copy = new ArrayBuffer(length);
        new Uint8Array(copy).set(new Uint8Array(this, 0, Math.min(length, this.byteLength)));
        return copy;
      },
    });
  }
}
await import('./pdf.worker.min.mjs');
