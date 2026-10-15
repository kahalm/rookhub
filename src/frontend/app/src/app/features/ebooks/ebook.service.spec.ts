import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { EbookService, formatBytes } from './ebook.service';

describe('EbookService', () => {
  let service: EbookService;
  let http: HttpTestingController;
  let user$: BehaviorSubject<{ userId: number } | null>;

  beforeEach(() => {
    user$ = new BehaviorSubject<{ userId: number } | null>(null);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: { currentUser$: user$ } }] });
    service = TestBed.inject(EbookService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('„Nicht erkanntes Diagramm?“ schickt Stelle und Antwort an /api/ebooks/{id}/missed-diagram', () => {
    service.reportMissedDiagram(42, 12, 0.25, 0.5, true).subscribe();
    const req = http.expectOne('/api/ebooks/42/missed-diagram');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ page: 12, x: 0.25, y: 0.5, isDiagram: true });
    req.flush(null);
  });

  it('„Als Kurs anlegen“ schickt POST an /api/ebooks/{id}/course', () => {
    let id = 0;
    service.createCourse(42).subscribe(c => id = c.bookId);
    const req = http.expectOne('/api/ebooks/42/course');
    expect(req.request.method).toBe('POST');
    req.flush({ bookId: 7, displayName: 'Awerbach' });
    expect(id).toBe(7);
  });

  it('schickt nur gesetzte Filter als Query', () => {
    service.list({ search: 'endgame', tag: 'Endspiel', mine: true, sort: 'recent', page: 2 }).subscribe();
    const req = http.expectOne(r => r.url === '/api/ebooks');
    expect(req.request.params.get('search')).toBe('endgame');
    expect(req.request.params.get('tag')).toBe('Endspiel');
    expect(req.request.params.get('mine')).toBe('true');
    expect(req.request.params.get('sort')).toBe('recent');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.has('pageSize')).toBeFalse();
    req.flush({ items: [], total: 0 });

    service.list().subscribe();
    expect(http.expectOne(r => r.url === '/api/ebooks').request.params.keys().length).toBe(0);
  });

  it('ohne Anmeldung (0.778.0): Ausweis wird bei An-/Abmelden vergessen, die Übernahme schickt die Kennung', () => {
    const later = new Date(Date.now() + 3_600_000).toISOString();
    service.accessToken().subscribe();
    http.expectOne('/api/ebooks/access-token').flush({ token: 'anon', expiresAt: later });
    service.accessToken().subscribe();
    http.expectNone('/api/ebooks/access-token');   // noch zwischengespeichert
    user$.next({ userId: 7 });                      // angemeldet → anderer Aufrufer
    let t = '';
    service.accessToken().subscribe(v => t = v);
    http.expectOne('/api/ebooks/access-token').flush({ token: 'user7', expiresAt: later });
    expect(t).toBe('user7');

    service.claimSession('0123456789abcdef0123456789abcdef').subscribe();
    const req = http.expectOne('/api/ebooks/claim-session');
    expect(req.request.body).toEqual({ sessionId: '0123456789abcdef0123456789abcdef' });
    req.flush({ claimed: 1 });
    service.accessToken().subscribe();
    http.expectOne('/api/ebooks/access-token').flush({ token: 'user7b', expiresAt: later });   // nach der Übernahme frisch
  });

  it('holt den Ausweis einmal und teilt ihn, bis er fast abgelaufen ist', () => {
    const seen: string[] = [];
    service.accessToken().subscribe(t => seen.push(t));
    service.accessToken().subscribe(t => seen.push(t));
    const req = http.expectOne('/api/ebooks/access-token');
    req.flush({ token: 'abc', expiresAt: new Date(Date.now() + 3_600_000).toISOString() });
    expect(seen).toEqual(['abc', 'abc']);

    service.accessToken().subscribe(t => seen.push(t));
    http.expectNone('/api/ebooks/access-token');
    expect(seen.length).toBe(3);

    service.forgetToken();
    service.accessToken().subscribe();
    http.expectOne('/api/ebooks/access-token').flush({ token: 'neu', expiresAt: new Date(Date.now() + 3_600_000).toISOString() });
  });

  it('holt einen neuen Ausweis, wenn der alte kurz vor dem Ablauf steht', () => {
    service.accessToken().subscribe();
    http.expectOne('/api/ebooks/access-token').flush({ token: 'alt', expiresAt: new Date(Date.now() + 60_000).toISOString() });
    service.accessToken().subscribe();
    http.expectOne('/api/ebooks/access-token').flush({ token: 'neu', expiresAt: new Date(Date.now() + 3_600_000).toISOString() });
  });

  it('baut Datei- und Titelbild-Adressen mit dem Ausweis', () => {
    expect(service.fileUrl(7, 'a b')).toBe('/api/ebooks/7/file?t=a%20b');
    expect(service.fileUrl(7, 'x', { fileId: 3, download: true })).toBe('/api/ebooks/7/file?t=x&fileId=3&download=true');
    expect(service.coverUrl(7, 'x', 'v1')).toBe('/api/ebooks/7/cover?t=x&v=v1');
    expect(service.epubBaseUrl(7, 'a/b')).toBe('/api/ebooks/7/epub/a%2Fb/');
  });

  it('lädt als Multipart mit Metadaten als CSV hoch', () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'buch.pdf', { type: 'application/pdf' });
    service.upload(file, { title: 'T', tags: ['a', 'b'], groupIds: [1, 2], year: 2001 }).subscribe();
    const req = http.expectOne('/api/ebooks');
    const body = req.request.body as FormData;
    expect(body.get('title')).toBe('T');
    expect(body.get('tags')).toBe('a,b');
    expect(body.get('groupIds')).toBe('1,2');
    expect(body.get('year')).toBe('2001');
    expect((body.get('file') as File).name).toBe('buch.pdf');
    expect(req.request.reportProgress).toBeTrue();
    req.flush({});
  });

  it('formatBytes rundet lesbar', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(999)).toBe('999 B');
    expect(formatBytes(1_500)).toBe('1.5 kB');
    // Binär (c2-eb-upload-limit-315-300): 300 MiB = die Server-Grenze = „300 MB“, nicht „315 MB“.
    expect(formatBytes(300 * 1024 * 1024)).toBe('300 MB');
    expect(formatBytes(25_000_000)).toBe('24 MB');
    expect(formatBytes(1023)).toBe('1,023 B');
    expect(formatBytes(1_470_585_020)).toBe('1.4 GB');
    expect(formatBytes(-1)).toBe('');
  });
});
