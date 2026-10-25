import { fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ClubClient } from '../../core/club-api.service';
import { ScoresheetScan } from '../../core/club.models';
import { ScoresheetUploadStore, ScoresheetUploadStoreDeps } from './scoresheet-upload-store';
import { anonKeys } from './club-anon-keys';

const SCAN = (status: ScoresheetScan['status'], id = 7): ScoresheetScan => ({
  id, status, notationLanguage: 'de', createdAt: new Date().toISOString(), rounds: 1, moveCount: 40, uncertainCount: 0,
  unresolvedCount: 0, white: 'Didi', black: 'Hengl',
});
const file = (n: string) => new File([n], `${n}.jpg`, { type: 'image/jpeg' });

describe('ScoresheetUploadStore', () => {
  let api: jasmine.SpyObj<ClubClient>;
  let deps: ScoresheetUploadStoreDeps;
  let kind: 'pgn' | 'formular';

  beforeEach(() => {
    localStorage.removeItem('lh-anon-scans');
    kind = 'formular';
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['scans', 'scoresheetStatus', 'upload', 'discard']);
    api.scans.and.resolveTo([]);
    api.scoresheetStatus.and.resolveTo({ available: true, dailyLimit: 10, usedToday: 0, languages: [] });
    deps = { client: api, share: null, isManager: false, openScans: jasmine.createSpy('openScans').and.resolveTo([]),
      confirm: jasmine.createSpy('confirm').and.resolveTo(true), kind: () => kind };
  });
  afterEach(() => localStorage.removeItem('lh-anon-scans'));

  it('Seiten und weitere Fotos: Layout, Beschriftung, Deckel', () => {
    const s = new ScoresheetUploadStore(deps);
    expect(s.layout()).toEqual([1]);
    s.addExtra(true);
    s.addExtra();
    expect(s.layout()).toEqual([1, 1, 2]);
    expect(s.extraLabels()).toEqual(['Seite 1, Foto 2', 'Seite 2']);
    s.addExtra();
    s.addExtra();                                                    // höchstens drei Seiten
    expect(s.layout()).toEqual([1, 1, 2, 3]);
    s.removeExtra(0);
    expect(s.layout()).toEqual([1, 2, 3]);
  });

  it('mehrere Fotos auf einmal: das erste wird Hauptfoto, höchstens vier je Seite, Rest mit Hinweis', () => {
    const s = new ScoresheetUploadStore(deps);
    const files = [1, 2, 3, 4, 5, 6].map(n => file(String(n)));
    const input = { files, value: 'x' };
    s.pickMany({ target: input } as unknown as Event);
    expect(input.value).toBe('');
    expect(s.photo()).toBe(files[0]);
    expect(s.layout()).toEqual([1, 1, 1, 1]);
    expect(s.multiNote()).toBe('2 Fotos nicht übernommen — höchstens 4 je Seite.');
  });

  it('Hochladen: schickt Fotos mit Seiten, merkt den Schlüssel ohne Konto, leert die Auswahl und das Feld', fakeAsync(() => {
    const s = new ScoresheetUploadStore({ ...deps, share: 'TOK' });
    api.upload.and.resolveTo({ ref: 'geheim', scan: SCAN('pending') });
    s.photo.set(file('a'));
    s.addExtra();
    s.pickExtra(0, { target: { files: [file('b')] } } as unknown as Event);
    const cleared = jasmine.createSpy('clear');
    void s.upload(cleared);
    flushMicrotasks();
    expect(api.upload).toHaveBeenCalledWith([jasmine.any(File), jasmine.any(File)], 'auto', 'auto', [1, 2]);
    expect(anonKeys('TOK')).toEqual(['geheim']);
    expect(s.scans().map(x => x.ref)).toEqual(['geheim']);
    expect(s.photo()).toBeNull();
    expect(s.extraPhotos()).toEqual([]);
    expect(cleared).toHaveBeenCalled();
    expect(api.scoresheetStatus).toHaveBeenCalled();
    s.dispose();
    flush();
  }));

  it('Hochladen scheitert: Fehlertext, Auswahl bleibt', fakeAsync(() => {
    const s = new ScoresheetUploadStore(deps);
    api.upload.and.rejectWith(new HttpErrorResponse({ status: 500 }));
    const f = file('a');
    s.photo.set(f);
    void s.upload();
    flushMicrotasks();
    expect(s.scanError()).toBeTruthy();
    expect(s.photo()).toBe(f);
    expect(s.uploading()).toBeFalse();
  }));

  it('fragt nach, solange gelesen wird, und hört auf, sobald alles fertig ist; dispose beendet das Nachfragen', fakeAsync(() => {
    api.scans.and.resolveTo([{ ref: '7', scan: SCAN('running') }]);
    const s = new ScoresheetUploadStore(deps);
    void s.loadScans();
    flushMicrotasks();
    expect(api.scoresheetStatus).toHaveBeenCalledTimes(1);           // Art „Partieformular": erst der Status
    tick(3000);
    flushMicrotasks();
    expect(api.scans).toHaveBeenCalledTimes(2);
    api.scans.and.resolveTo([{ ref: '7', scan: SCAN('done') }]);
    tick(3000);
    flushMicrotasks();
    expect(api.scans).toHaveBeenCalledTimes(3);
    tick(6000);
    expect(api.scans).toHaveBeenCalledTimes(3);
    api.scans.and.resolveTo([{ ref: '7', scan: SCAN('running') }]);
    void s.loadScans();
    flushMicrotasks();
    s.dispose();
    tick(6000);
    expect(api.scans).toHaveBeenCalledTimes(4);
  }));

  it('Art „Partiedaten": kein Status-Abruf beim Laden der Formulare', fakeAsync(() => {
    kind = 'pgn';
    const s = new ScoresheetUploadStore(deps);
    void s.loadScans();
    flushMicrotasks();
    expect(api.scoresheetStatus).not.toHaveBeenCalled();
  }));

  it('Verwalter: offene Formulare anderer, verwerfen nach Rückfrage', fakeAsync(() => {
    (deps.openScans as jasmine.Spy).and.resolveTo([{ scan: SCAN('done', 8), viaShareLink: true, mine: false },
      { scan: SCAN('done', 9), viaShareLink: false, mine: true }]);
    api.discard.and.resolveTo();
    const s = new ScoresheetUploadStore({ ...deps, isManager: true });
    void s.loadScans();
    flushMicrotasks();
    expect(s.othersOpen().map(o => o.scan.id)).toEqual([8]);
    void s.discardOther(s.othersOpen()[0]);
    flushMicrotasks();
    expect(api.discard).toHaveBeenCalledWith('8');
    expect(s.othersOpen()).toEqual([]);
  }));

  it('eigenes Formular verwerfen: ein gelesenes erst nach Rückfrage, ein gescheitertes ohne', fakeAsync(() => {
    api.discard.and.resolveTo();
    const s = new ScoresheetUploadStore(deps);
    s.scans.set([{ ref: '1', scan: SCAN('done', 1) }, { ref: '2', scan: SCAN('failed', 2) }]);
    (deps.confirm as jasmine.Spy).and.resolveTo(false);
    void s.discard(s.scans()[0]);
    flushMicrotasks();
    expect(api.discard).not.toHaveBeenCalled();
    void s.discard(s.scans()[1]);
    flushMicrotasks();
    expect(api.discard).toHaveBeenCalledWith('2');
    expect(s.scans().map(x => x.ref)).toEqual(['1']);
  }));

  it('scanLink: mit Konto /verein/formular, über den Teilen-Link /s/…/formular', () => {
    expect(new ScoresheetUploadStore(deps).scanLink({ ref: '7', scan: SCAN('done') })).toEqual(['/verein/formular', '7']);
    expect(new ScoresheetUploadStore({ ...deps, share: 'TOK' }).scanLink({ ref: 'k', scan: SCAN('done') })).toEqual(['/s', 'TOK', 'formular', 'k']);
  });
});
