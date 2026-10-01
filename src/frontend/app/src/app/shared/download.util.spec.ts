import { DOWNLOAD_URL_TTL_MS, downloadBlob } from './download.util';

describe('downloadBlob', () => {
  // Den Klick abfangen: der Testlaeufer soll nichts herunterladen. Festgehalten wird, ob das
  // Element beim Klick im Baum hing — Firefox loest einen Klick auf ein loses Element nicht aus.
  let clicks: { download: string; href: string; attached: boolean }[];
  beforeEach(() => {
    clicks = [];
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
      clicks.push({ download: this.download, href: this.href, attached: document.body.contains(this) });
    });
  });

  it('klickt ein EINGEHAENGTES Element mit Dateiname und raeumt es wieder weg', () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:abc');
    spyOn(URL, 'revokeObjectURL');
    const before = document.body.childElementCount;

    expect(downloadBlob(new Blob(['x'], { type: 'text/plain' }), 'game.pgn')).toBeTrue();

    expect(clicks).toEqual([{ download: 'game.pgn', href: 'blob:abc', attached: true }]);
    expect(document.body.childElementCount).toBe(before);
  });

  it('gibt die Object-URL erst nach dem Klick verzoegert frei, nicht sofort', () => {
    jasmine.clock().install();
    try {
      const blob = new Blob(['x'], { type: 'text/plain' });
      const createUrl = spyOn(URL, 'createObjectURL').and.returnValue('blob:abc');
      const revokeUrl = spyOn(URL, 'revokeObjectURL');

      downloadBlob(blob, 'game.pgn');

      expect(createUrl).toHaveBeenCalledWith(blob);
      expect(revokeUrl).not.toHaveBeenCalled();
      jasmine.clock().tick(DOWNLOAD_URL_TTL_MS - 1);
      expect(revokeUrl).not.toHaveBeenCalled();
      jasmine.clock().tick(1);
      expect(revokeUrl).toHaveBeenCalledOnceWith('blob:abc');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('meldet false, wenn der Browser keine Blob-URL anlegen kann', () => {
    spyOn(URL, 'createObjectURL').and.throwError('kein Blob');
    expect(downloadBlob(new Blob(['x']), 'game.pgn')).toBeFalse();
    expect(clicks).toEqual([]);
  });
});
