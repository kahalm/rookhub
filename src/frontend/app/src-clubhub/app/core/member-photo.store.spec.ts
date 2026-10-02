import { TestBed } from '@angular/core/testing';
import { ClubApiService } from './club-api.service';
import { MemberPhotoStore } from './member-photo.store';

/** Ein Versprechen, das der Test von außen einlöst. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

/** Wartet, bis die Antworten der eingelösten Versprechen verarbeitet sind. */
async function flush(): Promise<void> {
  for (let i = 0; i < 5; i++) await Promise.resolve();
}

describe('MemberPhotoStore (Vorschaubilder der Karteiblätter)', () => {
  let api: jasmine.SpyObj<ClubApiService>;
  let store: MemberPhotoStore;
  let revoke: jasmine.Spy;
  const blob = () => new Blob(['jpeg'], { type: 'image/jpeg' });

  beforeEach(() => {
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['memberPhotoBlob']);
    api.memberPhotoBlob.and.callFake(async () => blob());
    let n = 0;
    spyOn(URL, 'createObjectURL').and.callFake(() => `blob:bild-${++n}`);
    revoke = spyOn(URL, 'revokeObjectURL');
    TestBed.configureTestingModule({ providers: [{ provide: ClubApiService, useValue: api }] });
    store = TestBed.inject(MemberPhotoStore);
  });

  it('holt je Blatt und Marke das VORSCHAUBILD genau einmal — auch wenn Kartei und Blatt es gleichzeitig wollen', async () => {
    expect(store.url(7, 100)).toBeNull();
    store.request(7, 100);
    store.request(7, 100);
    expect(store.url(7, 100)).toBeNull();                                                // unterwegs
    await flush();
    expect(api.memberPhotoBlob.calls.allArgs()).toEqual([[7, true, 100]]);
    expect(store.url(7, 100)).toBe('blob:bild-1');
    store.request(7, 100);                                                               // schon da → kein Abruf
    expect(api.memberPhotoBlob).toHaveBeenCalledTimes(1);
  });

  it('ohne Marke (kein Bild) passiert nichts', () => {
    store.request(7, null);
    store.request(7, undefined);
    expect(api.memberPhotoBlob).not.toHaveBeenCalled();
    expect(store.url(7, null)).toBeNull();
  });

  it('ein ersetztes Bild hat eine neue Marke: es wird neu geholt, die alte Adresse freigegeben', async () => {
    store.request(7, 100);
    await flush();
    store.request(7, 200);
    expect(revoke).toHaveBeenCalledWith('blob:bild-1');
    expect(store.url(7, 100)).toBeNull();
    await flush();
    expect(store.url(7, 200)).toBe('blob:bild-2');
    expect(api.memberPhotoBlob.calls.allArgs()).toEqual([[7, true, 100], [7, true, 200]]);
  });

  it(`höchstens ${MemberPhotoStore.Parallel} Abrufe gleichzeitig — die Kartei zeigt viele Bilder auf einmal`, async () => {
    const waiting = Array.from({ length: 6 }, () => deferred<Blob>());
    let i = 0;
    api.memberPhotoBlob.and.callFake(() => waiting[i++].promise);
    for (let id = 1; id <= 6; id++) store.request(id, 1);
    expect(api.memberPhotoBlob).toHaveBeenCalledTimes(4);
    waiting[0].resolve(blob());
    await flush();
    expect(api.memberPhotoBlob).toHaveBeenCalledTimes(5);                                // einer fertig → der nächste rückt nach
    waiting.slice(1).forEach(w => w.resolve(blob()));
    await flush();
    await flush();
    expect(api.memberPhotoBlob).toHaveBeenCalledTimes(6);
    expect([1, 2, 3, 4, 5, 6].every(id => store.url(id, 1) !== null)).toBeTrue();
  });

  it('ein gescheiterter Abruf bleibt nicht hängen: der nächste Anlauf holt neu', async () => {
    api.memberPhotoBlob.and.returnValues(Promise.reject(new Error('offline')), Promise.resolve(blob()));
    store.request(7, 100);
    await flush();
    expect(store.url(7, 100)).toBeNull();
    store.request(7, 100);
    await flush();
    expect(store.url(7, 100)).toBe('blob:bild-1');
  });

  it('Abmelden räumt alles ab — auch ein Bild, dessen Antwort erst danach kommt, bleibt draußen', async () => {
    const late = deferred<Blob>();
    api.memberPhotoBlob.and.returnValues(Promise.resolve(blob()), late.promise);
    store.request(1, 1);
    await flush();
    store.request(2, 1);
    store.clear();
    expect(revoke).toHaveBeenCalledWith('blob:bild-1');
    expect(store.url(1, 1)).toBeNull();
    late.resolve(blob());
    await flush();
    expect(store.url(2, 1)).toBeNull();
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
  });
});
