import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from '../../core/auth.service';
import { flushBeforeLogout } from '../../core/logout-flush';
import { OfflineService } from '../../core/offline.service';
import { EBOOK_CALC_DEBOUNCE_MS, EBOOK_CALC_META_KEY, EBOOK_CALC_PREFIX, EbookCalcSyncService } from './ebook-calc-sync.service';

/** E-Book-Kalkulation im Konto (1.019.0): vorher nur lokal und beim Abmelden weg. */
describe('EbookCalcSyncService', () => {
  let auth: { isLoggedIn: boolean };
  let http: HttpTestingController;
  let svc: EbookCalcSyncService;

  function setup(loggedIn: boolean): void {
    auth = { isLoggedIn: loggedIn };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
    });
    http = TestBed.inject(HttpTestingController);
    svc = TestBed.inject(EbookCalcSyncService);
  }

  const clear = () => { localStorage.removeItem(EBOOK_CALC_PREFIX + 11); localStorage.removeItem(EBOOK_CALC_META_KEY); };
  beforeEach(clear);
  afterEach(() => { http.verify(); clear(); });

  it('Server gewinnt beim Öffnen', () => {
    localStorage.setItem(EBOOK_CALC_PREFIX + 11, 'LOKAL');
    setup(true);
    let got: string | null = 'x';
    svc.fetch(3, 11).subscribe(t => got = t);
    http.expectOne('/api/ebooks/3/diagrams/11/calc').flush({ tree: 'SERVER', at: 50 });
    expect(got).toBe('SERVER');
    expect(svc.local(11)).toBe('SERVER');
  });

  it('jüngerer ungesicherter Gerätestand geht hoch statt überschrieben zu werden', fakeAsync(() => {
    setup(true);
    svc.save(3, 11, 'NEU');                  // Upload erst nach der Pause
    let got: string | null = 'x';
    svc.fetch(3, 11).subscribe(t => got = t);
    http.expectOne(r => r.method === 'GET').flush({ tree: 'ALT', at: 1 });
    expect(got).toBeNull();
    expect(svc.local(11)).toBe('NEU');
    const put = http.expectOne(r => r.method === 'PUT' && r.url === '/api/ebooks/3/diagrams/11/calc');
    expect(put.request.body.tree).toBe('NEU');
    put.flush({ tree: 'NEU', at: put.request.body.at });
    tick(EBOOK_CALC_DEBOUNCE_MS);
    http.expectOne(r => r.method === 'PUT').flush({ tree: 'NEU', at: 1 });   // der gesammelte Upload
  }));

  it('speichert gesammelt nach der Pause; danach nichts mehr ungesichert', fakeAsync(() => {
    setup(true);
    svc.save(3, 11, 'A');
    svc.save(3, 11, 'B');
    tick(EBOOK_CALC_DEBOUNCE_MS);
    const put = http.expectOne(r => r.method === 'PUT');
    expect(put.request.body.tree).toBe('B');
    put.flush({ tree: 'B', at: put.request.body.at });
    expect(JSON.parse(localStorage.getItem(EBOOK_CALC_META_KEY)!)['11'].dirty).toBeFalse();
  }));

  it('vor dem Abmelden: Ungesichertes mit dem Token der endenden Sitzung, danach ist das Gerät geräumt', () => {
    setup(true);
    svc.save(3, 11, 'ARBEIT');
    flushBeforeLogout('tok-alt');
    const post = http.expectOne('/api/ebooks/calc/bulk');
    expect(post.request.headers.get('Authorization')).toBe('Bearer tok-alt');
    expect(post.request.body).toEqual([{ diagramId: 11, tree: 'ARBEIT', at: jasmine.any(Number) }]);
    post.flush({ saved: 1 });
    TestBed.inject(OfflineService).clearOnLogout();
    expect(svc.local(11)).toBeNull();
    expect(localStorage.getItem(EBOOK_CALC_META_KEY)).toBeNull();
  });

  it('Gast: nur lokal', fakeAsync(() => {
    setup(false);
    expect(svc.save(3, 11, 'G')).toBeTrue();
    svc.fetch(3, 11).subscribe();
    tick(EBOOK_CALC_DEBOUNCE_MS);
    http.expectNone(() => true);
    expect(svc.local(11)).toBe('G');
  }));
});
