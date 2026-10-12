import { fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { ScanPhotos } from './scan-photos';

describe('ScanPhotos', () => {
  it('lädt alle Seiten, wechselt Seiten und holt weitere Fotos erst bei Bedarf', fakeAsync(() => {
    const fetchView = jasmine.createSpy('fetchView').and.resolveTo(new Blob(['v']));
    const ph = new ScanPhotos({ fetchView, setPageSize: () => {} });
    void ph.load(2, () => Promise.resolve(new Blob(['p'])), () => true);
    flushMicrotasks();
    expect(ph.pageNumbers()).toEqual([1, 2]);
    expect(ph.pageUrl(2)).toMatch(/^blob:/);
    expect(ph.photoUrl()).toBe(ph.pageUrl(1));

    ph.viewCounts.set([0, 1]);
    ph.showPage(2);
    expect(ph.photoUrl()).toBe(ph.pageUrl(2));
    expect(ph.viewNumbers()).toEqual([0, 1]);
    void ph.showView(1);
    flushMicrotasks();
    expect(fetchView).toHaveBeenCalledOnceWith(2, 1);
    expect(ph.photoUrl()).not.toBe(ph.pageUrl(2));
    ph.showPage(1);
    expect(ph.shownView()).toBe(0);
    ph.dispose();
  }));
});
