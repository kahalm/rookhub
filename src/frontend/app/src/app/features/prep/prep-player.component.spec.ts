import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { AuthService } from '@rh/core/auth.service';
import { MyGamesService } from '@lh/core/my-games.service';
import { PrepApiService } from './prep-api.service';
import { PrepPlayerComponent } from './prep-player.component';
import { PrepCardJson } from './prep.models';

function card(over: Partial<PrepCardJson> = {}): PrepCardJson {
  return {
    id: 31252, fide: '1503014', name: 'Carlsen, Magnus', n: 500, games: 8530, loaded: 500, limited: true, limit: 500, max: 3000,
    since: '2015.10.11', twin: null, twinIncluded: false, src: { Mega: 300, Lumbra: 120, 'Mega+Lumbra': 80 }, recent: [],
    white: { n: 250, first: [['e4', 150, 60]], lines: [] }, black_e4: { n: 120, first: [], lines: [] },
    black_d4: { n: 100, first: [], lines: [] }, black_other: { n: 30, first: [], lines: [] }, accounts: [], online: 0, onlineUnsure: 0,
    ...over,
  };
}

describe('PrepPlayerComponent', () => {
  let harness: RouterTestingHarness;
  let api: jasmine.SpyObj<PrepApiService>;
  let perms: Set<string>;
  const el = () => harness.routeNativeElement as HTMLElement;

  async function create(c: PrepCardJson): Promise<void> {
    perms = new Set(['prep.view']);
    api = jasmine.createSpyObj<PrepApiService>('PrepApiService', ['card', 'profile', 'recent', 'tree', 'pgn', 'search']);
    (api as unknown as { lastQuery: () => string }).lastQuery = () => 'Carlsen';
    api.card.and.resolveTo(c);
    TestBed.configureTestingModule({
      imports: [PrepPlayerComponent],
      providers: [provideRouter([{ path: 'prep/:id', component: PrepPlayerComponent }, { path: 'prep', children: [] }]),
        provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations(), { provide: PrepApiService, useValue: api },
        { provide: AuthService, useValue: { has: (p: string) => perms.has(p), isLoggedIn: true } },
        { provide: MyGamesService, useValue: { available: false, rookHubUrl: null } }],
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/prep/31252', PrepPlayerComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await harness.fixture.whenStable();
      harness.detectChanges();
    }
  }

  afterEach(() => document.head.querySelectorAll('link[data-prep-card]').forEach(l => l.remove()));

  it('öffnet die Karte des Spielers als Teil der Seite, mit dem, was geladen ist', async () => {
    await create(card());
    expect(api.card).toHaveBeenCalledWith(31252, { all: false, twin: false });
    const dlg = el().querySelector('dialog.card')!;
    expect(dlg.classList).toContain('inline');
    expect((dlg as HTMLDialogElement).open).toBeTrue();
    expect(el().querySelector('h2')?.textContent).toContain('Carlsen, Magnus');
    expect(el().querySelector('.card-meta')?.textContent).toContain('Mega 300');            // die Quellen
    expect(el().querySelector('.card-meta a')?.getAttribute('href')).toBe('https://ratings.fide.com/profile/1503014');
    expect(el().querySelector('.prep-scope')?.textContent).toContain('prep.loadedSome');
    expect(el().querySelector('.prep-scope button')?.textContent).toContain('prep.loadAll');
    expect(el().querySelector('mat-checkbox')).toBeNull();                                 // kein Zwilling, kein Schalter
    expect(el().querySelector('lh-online-accounts')).toBeNull();                           // keine Konten, nichts Leeres
    expect(el().querySelector('.check')).toBeNull();                                       // ohne prep.manage kein „unsichere"
    expect(el().querySelector('a.back')?.getAttribute('href')).toBe('/prep?q=Carlsen');
    expect(document.head.querySelector('link[data-prep-card]')?.getAttribute('href')).toBe('prep-card.css');
  });

  it('„alle laden" fragt mit all=true neu und sagt danach, dass mehr nicht geht', async () => {
    await create(card());
    api.card.and.resolveTo(card({ loaded: 3000, limit: 3000, since: '1999.10.29', n: 3000 }));
    (el().querySelector('.prep-scope button') as HTMLButtonElement).click();
    await settle();
    expect(api.card).toHaveBeenCalledWith(31252, { all: true, twin: false });
    expect(el().querySelector('.prep-scope button')).toBeNull();
    expect(el().querySelector('.prep-scope')?.textContent).toContain('prep.capped');
  });

  it('alles geladen: kein Knopf', async () => {
    await create(card({ games: 51, loaded: 51, n: 51, limited: false, since: null }));
    expect(el().querySelector('.prep-scope')?.textContent).toContain('prep.loadedAll');
    expect(el().querySelector('.prep-scope button')).toBeNull();
  });

  it('Namens-Zwilling: Schalter mit Partienzahl, Vorgabe aus, an = mit twin=true', async () => {
    await create(card({ twin: { id: 77, name: 'Carlsen, Magnus', games: 12 } }));
    const box = el().querySelector('mat-checkbox input') as HTMLInputElement;
    expect(box).not.toBeNull();
    expect(box.checked).toBeFalse();
    box.click();
    await settle();
    expect(api.card).toHaveBeenCalledWith(31252, { all: false, twin: true });
  });

  it('Konten erscheinen, wenn die API sie liefert; ohne FIDE-ID kein FIDE-Link', async () => {
    await create(card({ fide: null, accounts: [{ site: 'lichess', user: 'magnus', url: 'https://lichess.org/@/magnus', conf: 'sicher' }] }));
    expect(el().querySelector('.card-meta a')).toBeNull();
    expect(el().querySelector('lh-online-accounts')?.textContent).toContain('magnus');
    expect(el().querySelector('lh-online-accounts .chk-btn')).toBeNull();                 // die Prüfung gehört zu LeagueHub
  });
});
