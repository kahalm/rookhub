import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { LibraryGame, MasterGamePage } from '../guess/library.service';
import { MASTER_SEARCH_DEBOUNCE_MS, MasterGamesComponent } from './master-games.component';
import { OPENING_GROUPS, cleanName, gameYear, openingGroupOf, paramsToQuery, queryToParams, resultLabel } from './master-games.util';
import { expectWholeTileClickable } from '../../testing/tile-hit';

const GAME: LibraryGame = {
  id: 990101, white: 'Carlsen, Magnus', black: 'Anand, Viswanathan', whiteElo: 2862, blackElo: 2775, result: '1/2-1/2',
  event: 'Wch', playedOn: null, playedYear: 2014, eco: 'C67', plyCount: 80, annotator: 'Giri, Anish', commentedPlies: 20,
  commentChars: 3000, languages: 'en', score: 90, sourceTitle: 'CBM 164', inPool: false, requested: false, gameAnalysisId: null,
};

const PAGE = (items: LibraryGame[], total = items.length): MasterGamePage =>
  ({ items, total, page: 1, pageSize: 25, ignored: [], maxPage: 200 });

describe('MasterGamesComponent (1.038.0)', () => {
  let http: HttpTestingController;
  let dialog: jasmine.SpyObj<MatDialog>;

  /** Die Filter kommen aus der Adresse: erst dorthin navigieren, dann die Seite anlegen. */
  function configure(loggedIn: boolean, params: Record<string, string>): Promise<boolean> {
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    TestBed.configureTestingModule({
      imports: [MasterGamesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: loggedIn } },
      ],
    });
    TestBed.overrideProvider(MatDialog, { useValue: dialog });
    http = TestBed.inject(HttpTestingController);
    const qs = new URLSearchParams(params).toString();
    return TestBed.inject(Router).navigateByUrl(qs ? '/?' + qs : '/');
  }

  function mount(): ComponentFixture<MasterGamesComponent> {
    const f = TestBed.createComponent(MasterGamesComponent);
    f.autoDetectChanges();
    return f;
  }

  async function create(loggedIn: boolean, params: Record<string, string> = {}): Promise<ComponentFixture<MasterGamesComponent>> {
    await configure(loggedIn, params);
    return mount();
  }

  afterEach(() => http.verify());

  it('sucht beim Öffnen mit den Filtern aus der Adresse und zeigt Spieler, Ergebnis, Jahr, Turnier, Quelle', async () => {
    const f = await create(true, { player: 'Carlsen', yearFrom: '2010', result: 'draw', eco: 'C60-C99' });
    const req = http.expectOne(r => r.url === '/api/library-games/search');
    expect(req.request.params.get('player')).toBe('Carlsen');
    expect(req.request.params.get('yearFrom')).toBe('2010');
    expect(req.request.params.get('result')).toBe('draw');
    expect(req.request.params.get('eco')).toBe('C60-C99');
    expect(req.request.params.has('q')).toBeFalse();
    req.flush(PAGE([GAME], 1));
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.who')?.textContent).toContain('Carlsen, Magnus – Anand, Viswanathan');
    expect(el.querySelector('.result')?.textContent).toContain('½-½');
    const meta = el.querySelector('.meta')?.textContent ?? '';
    expect(meta).toContain('Wch 2014');
    expect(meta).toContain('C67');
    expect(meta).toContain('masterGames.source');
    expect(f.componentInstance.openingKey()).toBe('ruyLopez');
  });

  it('Getipptes wartet auf die Pause, ein neuer Filter geht auf Seite 1', fakeAsync(() => {
    void configure(true, { page: '3' });
    tick();
    const f = mount();
    http.expectOne(r => r.url === '/api/library-games/search' && r.params.get('page') === '3').flush(PAGE([GAME]));
    f.componentInstance.set({ player: 'Ca' }, true);
    f.componentInstance.set({ player: 'Carl' }, true);
    tick(MASTER_SEARCH_DEBOUNCE_MS - 50);
    http.expectNone('/api/library-games/search');
    tick(100);
    const req = http.expectOne(r => r.url === '/api/library-games/search');
    expect(req.request.params.get('player')).toBe('Carl');
    expect(req.request.params.has('page')).toBeFalse();
    req.flush(PAGE([]));
    tick();
    expect((f.nativeElement as HTMLElement).querySelector('.empty-state')?.textContent).toContain('masterGames.empty');
  }));

  it('Ladefehler zeigt den Fehlerbaustein statt „nichts gefunden“, Wiederholen sucht erneut', async () => {
    const f = await create(true);
    http.expectOne(r => r.url === '/api/library-games/search').flush(null, { status: 500, statusText: 'x' });
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.querySelector('.empty-state')).toBeNull();
    f.componentInstance.reload();
    http.expectOne(r => r.url === '/api/library-games/search').flush(PAGE([GAME]));
    await f.whenStable();
    expect(el.querySelector('.game')).not.toBeNull();
  });

  it('nennt ignorierte Wörter und den Blätter-Deckel', async () => {
    const f = await create(true);
    http.expectOne(r => r.url === '/api/library-games/search')
      .flush({ ...PAGE([GAME], 9000), ignored: ['li'], maxPage: 200 });
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.ignored')?.textContent).toContain('masterGames.ignored');
    expect(el.querySelector('.capped')).not.toBeNull();
    expect(f.componentInstance.shownTotal()).toBe(5000);
  });

  it('Klick auf die Zeile öffnet die Partie im Nachspiel-Dialog; die ganze Zeile ist klickbar', async () => {
    const f = await create(true);
    http.expectOne(r => r.url === '/api/library-games/search').flush(PAGE([GAME]));
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expectWholeTileClickable(el.querySelector('.game')!, el.querySelector('.game-main')!);
    (el.querySelector('.game-main') as HTMLButtonElement).click();
    http.expectOne(r => r.url === `/api/library-games/${GAME.id}/view`).flush({ id: GAME.id, pgn: '1. e4 *', language: 'en', languages: ['en'] });
    // Der Dialog wird erst beim Klick nachgeladen (dynamischer Import).
    for (let i = 0; i < 100 && !dialog.open.calls.any(); i++) await new Promise(r => setTimeout(r, 20));
    expect(dialog.open).toHaveBeenCalled();
    expect(dialog.open.calls.mostRecent().args[1]?.data).toEqual({ pgn: '1. e4 *' });
  });

  it('ohne Anmeldung: Hinweis sichtbar, der Klick führt zur Anmeldung statt die Züge zu laden', async () => {
    const f = await create(false);
    http.expectOne(r => r.url === '/api/library-games/search').flush(PAGE([GAME]));
    await f.whenStable();
    const router = TestBed.inject(Router);
    const nav = spyOn(router, 'navigate').and.resolveTo(true);
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.login-note')?.textContent).toContain('masterGames.loginToView');
    (el.querySelector('.game-main') as HTMLButtonElement).click();
    expect(nav).toHaveBeenCalledWith(['/login'], jasmine.objectContaining({ queryParams: jasmine.any(Object) }));
    http.expectNone(r => r.url.endsWith('/view'));
  });

  it('Eröffnungsauswahl schreibt den ECO-Bereich', async () => {
    const f = await create(true);
    http.expectOne(r => r.url === '/api/library-games/search').flush(PAGE([]));
    f.componentInstance.pickOpening('sicilian');
    const req = http.expectOne(r => r.url === '/api/library-games/search');
    expect(req.request.params.get('eco')).toBe('B20-B99');
    req.flush(PAGE([]));
  });
});

describe('master-games.util', () => {
  it('Adresse hin und zurück, Unbekanntes fällt weg', () => {
    const q = paramsToQuery({ q: 'x', player: 'Tal', yearFrom: '1950', yearTo: 'abc', result: 'maybe', sort: 'newest', page: '2' });
    expect(q).toEqual(jasmine.objectContaining({ q: 'x', player: 'Tal', yearFrom: 1950, yearTo: null, result: '', sort: 'newest', page: 2 }));
    expect(queryToParams(q)).toEqual({ q: 'x', player: 'Tal', yearFrom: '1950', sort: 'newest', page: '2' });
    expect(queryToParams(paramsToQuery({}))).toEqual({});
  });

  it('Eröffnungsgruppen: eindeutige Schlüssel, gültige Bereiche, Rückweg über das ECO-Feld', () => {
    expect(new Set(OPENING_GROUPS.map(o => o.key)).size).toBe(OPENING_GROUPS.length);
    for (const o of OPENING_GROUPS) expect(o.eco).toMatch(/^[A-E]\d\d(-[A-E]\d\d)?$/);
    expect(openingGroupOf(' b20-b99 ')).toBe('sicilian');
    expect(openingGroupOf('B90')).toBe('');
  });

  it('Anzeige: Platzhalter sind keine Namen, Jahr aus Spalte oder Datum, Ergebnis kurz', () => {
    expect(cleanName('?')).toBe('');
    expect(gameYear({ ...GAME, playedYear: null, playedOn: '1999-03-04' })).toBe('1999');
    expect(resultLabel('*')).toBe('');
    expect(resultLabel('1-0')).toBe('1-0');
  });
});
