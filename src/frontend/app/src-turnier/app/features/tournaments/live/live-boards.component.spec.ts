import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { SnackbarService } from '@rh/core/snackbar.service';
import { LiveBoardsComponent, samePlayer } from './live-boards.component';
import { EVENT_SOURCE_FACTORY } from './live-boards.service';
import { FakeEventSource } from './fake-event-source';
import { LiveGame, LiveStatus } from './live-boards.models';
import { LIVE_EVAL_KEY, LIVE_LOST_GRACE_MS, LIVE_VIEW_KEYS, LIVE_VIEW_SAVE_MS, readViewKey, writeViewKey } from './live-boards.util';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

const STATUS: LiveStatus = {
  linked: true, manual: false, tourId: 'IjDgZVUR', tourName: '1. Bundesliga Austria 2026/2027',
  url: 'https://lichess.org/broadcast/-/IjDgZVUR', currentRound: 1,
  rounds: [{ round: 1, lichessRoundId: 'L3MX2oE3', name: 'Round 1', startsAt: null, ongoing: true, finished: false }],
};

function game(id: string, white: string, black: string, result = '*'): LiveGame {
  return {
    id, boardTag: '1.1', fen: 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', lastMove: 'e2e4', plies: 1, result,
    eval: null, finished: result !== '*', whiteToMove: false,
    white: { name: white, title: 'GM', elo: 2616, fideId: null, team: null, clockSeconds: 5400 },
    black: { name: black, title: null, elo: 2462, fideId: null, team: null, clockSeconds: 5390 },
  };
}

const SNAPSHOT = {
  state: 'live', round: 1, games: [game('b1', 'Aditya Mittal', 'Hagner, Bennet', '1-0'), game('b2', 'Schmider, Niklas', 'Vaibhav, Suri')],
  layout: {
    team: true, boards: [], unassigned: [],
    matches: [{
      matchNumber: 1, home: 'Pinggau-Friedberg', away: 'SK Schwaz', homeScore: null, awayScore: null, liveHome: 1, liveAway: 0,
      boards: [
        { board: 1, white: 'Aditya Mittal', black: 'Hagner, Bennet', result: '1-0', gameId: 'b1', homeIsWhite: true },
        { board: 2, white: 'Schmider, Niklas', black: 'Vaibhav, Suri', result: '*', gameId: 'b2', homeIsWhite: false },
      ],
    }],
  },
};

describe('LiveBoardsComponent', () => {
  let fixture: ComponentFixture<LiveBoardsComponent>;
  let http: HttpTestingController;
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let signedIn = false;

  async function setup(params: Record<string, string> = {}, routeParams: Record<string, string> = { id: '1404438' }): Promise<void> {
    FakeEventSource.reset();
    query = new BehaviorSubject(convertToParamMap(params));
    await TestBed.configureTestingModule({
      imports: [LiveBoardsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: EVENT_SOURCE_FACTORY, useValue: FakeEventSource.factory },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap(routeParams)), queryParamMap: query } },
      ],
    }).compileComponents();
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.callFake(() => signedIn);
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LiveBoardsComponent);
    fixture.autoDetectChanges();
  }

  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    signedIn = false;
    try { localStorage.removeItem(LIVE_EVAL_KEY); localStorage.removeItem(LIVE_VIEW_KEYS); } catch { /* egal */ }
  });

  it('Gast, verknüpft: verbindet den Strom der laufenden Runde und zeigt die Bretter der Begegnung mit Stand', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    expect(es.url).toBe('/api/live-boards/1404438/rounds/1/stream?ngsw-bypass=1');
    es.emit('snapshot', SNAPSHOT);
    await fixture.whenStable();
    expect(el().querySelectorAll('app-live-board-card').length).toBe(2);
    expect(el().querySelector('.mh-score')!.textContent!.replace(/\s/g, '')).toBe('1:0');
    expect(el().querySelector('.live-state')!.classList).toContain('is-live');
    http.verify();
  });

  it('Mannschaft gewählt: nur ihre Begegnung, Bretter aus ihrer Sicht (Schwarz unten, wo sie Schwarz hat)', async () => {
    await setup({ team: 'SK Schwaz' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', SNAPSHOT);
    await fixture.whenStable();
    const flips = fixture.componentInstance.matches()[0].boards.map(b => b.flipped);
    expect(flips).toEqual([true, false]);   // Brett 1: Schwaz hat Schwarz → gedreht; Brett 2: Schwaz hat Weiß
  });

  it('Engine-Balken: Schalter merkt sich die Wahl und verbindet mit eval=true neu (der Server bindet bool, „1“ gab 400)', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const first = FakeEventSource.last();
    fixture.componentInstance.setEval(true);
    expect(first.closed).toBeTrue();
    expect(FakeEventSource.last().url).toBe('/api/live-boards/1404438/rounds/1/stream?eval=true&ngsw-bypass=1');
    expect(localStorage.getItem(LIVE_EVAL_KEY)).toBe('1');
  });

  it('nicht verknüpft: leiser Hinweis, Gast bekommt „Anmelden" statt des Felds, kein Strom', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush({ ...STATUS, linked: false, rounds: [], currentRound: null });
    await fixture.whenStable();
    expect(el().querySelector('.live-hint')).not.toBeNull();
    expect(el().querySelector('.link-form')).toBeNull();
    expect(FakeEventSource.all.length).toBe(0);
  });

  it('nicht verknüpft, angemeldet: Link verknüpfen (PUT) und danach verbinden', async () => {
    signedIn = true;
    await setup();
    http.expectOne('/api/live-boards/1404438').flush({ ...STATUS, linked: false, rounds: [], currentRound: null });
    await fixture.whenStable();
    expect(el().querySelector('.link-form')).not.toBeNull();
    fixture.componentInstance.linkUrl.set('https://lichess.org/broadcast/1-bundesliga-austria-20262027/IjDgZVUR');
    fixture.componentInstance.link();
    const req = http.expectOne('/api/live-boards/1404438/link');
    expect(req.request.method).toBe('PUT');
    req.flush(STATUS);
    await fixture.whenStable();
    expect(FakeEventSource.all.length).toBe(1);
  });

  // Wunsch 2026-11-26: eine bestimmte Ansicht an Gäste teilen — der Link trägt Runde, Mannschaft, Spieler und Balken.
  it('Adresse ?eval=1: Balken an (Strom mit eval=true), ohne die Gerätevorliebe zu überschreiben', async () => {
    await setup({ eval: '1', round: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    expect(FakeEventSource.last().url).toBe('/api/live-boards/1404438/rounds/1/stream?eval=true&ngsw-bypass=1');
    expect(localStorage.getItem(LIVE_EVAL_KEY)).toBeNull();
  });

  it('Balken umschalten schreibt eval in die Adresse (ersetzt den Eintrag, kein neuer Verlaufsschritt)', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.componentInstance.setEval(true);
    expect(nav).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { eval: 1 }, queryParamsHandling: 'merge', replaceUrl: true }));
  });

  const TOKEN = 'Ab3dEf7hJk';
  const KEY = 'K'.repeat(24);
  const viewOf = (over: Record<string, unknown> = {}) => ({ round: 1, team: 'SK Schwaz', player: null, add: [], ext: [], eval: false, ...over });

  it('lange Adresse bleibt gültig: Runde immer ausdrücklich, Mannschaft und Balken mit', async () => {
    await setup({ team: 'Schachklub Schwaz' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(c.shareUrl()).toBe(`${location.origin}/tournaments/1404438/live?round=1&team=Schachklub%20Schwaz`);
    c.evalOn.set(true);
    expect(c.shareUrl()).toBe(`${location.origin}/tournaments/1404438/live?round=1&team=Schachklub%20Schwaz&eval=1`);
  });

  // Wunsch 2026-11-26: „nicht mit round=2&team=…, sondern einfach einen Hash — Änderungen sollen alle mit dem Link sehen“
  it('Link teilen: legt die geteilte Ansicht an, merkt den Schlüssel, kopiert den kurzen Link und wechselt auf /live/<token>', async () => {
    await setup({ team: 'SK Schwaz', round: '1', add: '2.1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const write = jasmine.createSpy('writeText').and.resolveTo();
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText: write } as unknown as Clipboard);
    spyOn(window, 'matchMedia').and.returnValue({ matches: false } as MediaQueryList);
    const copied = spyOn(TestBed.inject(SnackbarService), 'copy');
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    (el().querySelector('.live-share') as HTMLButtonElement).click();
    const req = http.expectOne('/api/live-boards/views');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ tournamentId: '1404438', view: viewOf({ add: ['2.1'] }) });
    req.flush({ token: TOKEN, tournamentId: '1404438', view: viewOf({ add: ['2.1'] }), canEdit: true, editKey: KEY });
    await fixture.whenStable();
    expect(write).toHaveBeenCalledWith(`${location.origin}/live/${TOKEN}`);
    expect(copied).toHaveBeenCalledWith('tournaments.live.shareCopied');
    expect(readViewKey(TOKEN)).toBe(KEY);
    expect(nav).toHaveBeenCalledWith(['/live', TOKEN], { replaceUrl: true });
  });

  it('Link teilen: scheitert das Anlegen, geht die lange Adresse', async () => {
    await setup({ team: 'SK Schwaz', round: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const write = jasmine.createSpy('writeText').and.resolveTo();
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText: write } as unknown as Clipboard);
    spyOn(window, 'matchMedia').and.returnValue({ matches: false } as MediaQueryList);
    fixture.componentInstance.share();
    http.expectOne('/api/live-boards/views').flush({}, { status: 429, statusText: 'Too Many' });
    await fixture.whenStable();
    expect(write).toHaveBeenCalledWith(`${location.origin}/tournaments/1404438/live?round=1&team=SK%20Schwaz`);
  });

  async function openView(view = viewOf(), canEdit = false): Promise<void> {
    await setup({}, { token: TOKEN });
    const req = http.expectOne(`/api/live-boards/views/${TOKEN}`);
    expect(req.request.headers.get('X-Live-View-Key')).toBe(canEdit ? KEY : null);
    req.flush({ token: TOKEN, tournamentId: '1404438', view, canEdit });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
  }

  it('/live/<token> als Gast: Ansicht vom Server, EIN Strom, Änderungen des Besitzers kommen ohne Neuverbinden an', async () => {
    await openView();
    const c = fixture.componentInstance;
    expect(FakeEventSource.all.length).toBe(1);
    const es = FakeEventSource.last();
    expect(es.url).toBe(`/api/live-boards/views/${TOKEN}/stream?ngsw-bypass=1`);
    es.emit('view', { token: TOKEN, tournamentId: '1404438', view: viewOf() });
    es.emit('snapshot', TWO_MATCHES);
    await fixture.whenStable();
    expect(el().querySelectorAll('app-live-board-card').length).toBe(2);
    expect(el().querySelector('.view-note')!.textContent).toContain('tournaments.live.sharedViewer');
    es.emit('view', { token: TOKEN, tournamentId: '1404438', view: viewOf({ add: ['2.2'] }) });  // Besitzer fügt ein Brett hinzu
    await fixture.whenStable();
    expect(c.added()).toEqual(['2.2']);
    expect(el().querySelectorAll('app-live-board-card').length).toBe(3);
    expect(FakeEventSource.all.length).toBe(1);
    const write = jasmine.createSpy('writeText').and.resolveTo();
    spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText: write } as unknown as Clipboard);
    spyOn(window, 'matchMedia').and.returnValue({ matches: false } as MediaQueryList);
    c.share();                                                                   // teilt einfach den kurzen Link
    expect(write).toHaveBeenCalledWith(`${location.origin}/live/${TOKEN}`);
    http.verify();
  });

  it('/live/<token> Zuschauer: eigener Balken nur für ihn (eval im Strom), andere Änderung → eigene lange Ansicht', async () => {
    await openView();
    const c = fixture.componentInstance;
    c.setEval(true);
    expect(FakeEventSource.last().url).toBe(`/api/live-boards/views/${TOKEN}/stream?eval=true&ngsw-bypass=1`);
    FakeEventSource.last().emit('view', { token: TOKEN, tournamentId: '1404438', view: viewOf({ eval: false }) });
    expect(c.evalOn()).toBeTrue();                                               // die Ansicht überschreibt seinen Schalter nicht
    const nav = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    c.setFilter('team', 'SK Absam');
    expect(TestBed.inject(Router).serializeUrl(nav.calls.mostRecent().args[0] as never))
      .toBe('/tournaments/1404438/live?round=1&team=SK%20Absam&eval=1');
    http.verify();                                                               // kein PUT
  });

  it('/live/<token> Besitzer: Änderung sofort sichtbar, entprellt EIN PUT mit Schlüssel, Hinweis „alle sehen mit“', fakeAsync(() => {
    writeViewKey(TOKEN, KEY);
    void openView(viewOf(), true);
    flush();
    const c = fixture.componentInstance;
    expect(el().querySelector('.view-note')!.textContent).toContain('tournaments.live.sharedOwner');
    const nav = spyOn(TestBed.inject(Router), 'navigate');
    c.setAdded(['2.1'], []);
    c.setAdded(['2.1', '2.2'], []);
    expect(c.added()).toEqual(['2.1', '2.2']);
    FakeEventSource.last().emit('view', { token: TOKEN, tournamentId: '1404438', view: viewOf() });   // alter Stand: ignoriert
    expect(c.added()).toEqual(['2.1', '2.2']);
    tick(LIVE_VIEW_SAVE_MS);
    const req = http.expectOne(`/api/live-boards/views/${TOKEN}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('X-Live-View-Key')).toBe(KEY);
    expect(req.request.body).toEqual({ view: viewOf({ add: ['2.1', '2.2'] }) });
    req.flush({ token: TOKEN, tournamentId: '1404438', view: viewOf({ add: ['2.1', '2.2'] }), canEdit: true });
    expect(nav).not.toHaveBeenCalled();
    expect(FakeEventSource.all.length).toBe(1);
    http.verify();
    fixture.destroy();
    discardPeriodicTasks();
    flush();
  }));

  it('/live/<token> unbekannt: leiser Hinweis statt Ladefehler', async () => {
    await setup({}, { token: TOKEN });
    http.expectOne(`/api/live-boards/views/${TOKEN}`).flush({}, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    expect(el().textContent).toContain('tournaments.live.viewNotFound');
    expect(FakeEventSource.all.length).toBe(0);
  });

  it('Link teilen am Handy: Teilen-Blatt des Systems', async () => {
    await setup({ player: 'Mittal, Aditya', round: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    spyOn(window, 'matchMedia').and.returnValue({ matches: true } as MediaQueryList);
    const nav = navigator as Navigator & { share?: (d: ShareData) => Promise<void> };
    const had = Object.getOwnPropertyDescriptor(nav, 'share');
    const shareSpy = jasmine.createSpy('share').and.resolveTo();
    Object.defineProperty(nav, 'share', { value: shareSpy, configurable: true });
    try {
      spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
      fixture.componentInstance.share();
      http.expectOne('/api/live-boards/views').flush({ token: TOKEN, tournamentId: '1404438', view: viewOf(), canEdit: true, editKey: KEY });
      expect(shareSpy).toHaveBeenCalledWith({ url: `${location.origin}/live/${TOKEN}` });
    } finally {
      if (had) Object.defineProperty(nav, 'share', had); else delete (nav as { share?: unknown }).share;
    }
  });

  // ── 1.025.0: weitere Bretter (Wunsch 2026-11-26) ────────────────────────────────────────────────────────────────
  const TWO_MATCHES = {
    ...SNAPSHOT,
    games: [...SNAPSHOT.games, game('c1', 'Muster, Max', 'Gast, Erster'), game('c2', 'Zweiter, Gast', 'Muster, Moritz')],
    layout: {
      ...SNAPSHOT.layout,
      matches: [
        { ...SNAPSHOT.layout.matches[0], homeElo: 2539, homeEloBoards: 2, awayElo: 2462, awayEloBoards: 1 },
        {
          matchNumber: 2, home: 'SK Absam', away: 'SK Wörgl', homeScore: null, awayScore: null, liveHome: 0, liveAway: 0,
          boards: [
            { board: 1, white: 'Muster, Max', black: 'Gast, Erster', result: '*', gameId: 'c1', homeIsWhite: true },
            { board: 2, white: 'Zweiter, Gast', black: 'Muster, Moritz', result: '*', gameId: 'c2', homeIsWhite: false },
          ],
        },
      ],
    },
  };

  it('Adresse ?add=&ext=: Gast sieht die Mannschaft, die hinzugefügten Bretter und die fremde Partie über EINEN Strom', async () => {
    await setup({ team: 'SK Schwaz', round: '1', add: '2.2,9.9', ext: 'fnmEB1kH.RD8sbYw1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    expect(FakeEventSource.all.length).toBe(1);
    const es = FakeEventSource.last();
    // Filter der Ansicht geht mit (1.027.0: der Server bewertet nur, was dieser Zuschauer sieht)
    expect(es.url).toBe('/api/live-boards/1404438/rounds/1/stream?team=SK%20Schwaz&add=2.2,9.9&extra=fnmEB1kH.RD8sbYw1&ngsw-bypass=1');
    es.emit('snapshot', TWO_MATCHES);
    es.emit('ext-snapshot', { round: 'fnmEB1kH', tourName: 'FIDE World Championship 2026', roundName: 'Game 3',
      url: 'https://lichess.org/broadcast/-/-/fnmEB1kH', state: 'live', games: [game('RD8sbYw1', 'Gukesh D', 'Carlsen, Magnus')] });
    await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(c.addedBoards().map(b => b.game.id)).toEqual(['c2']);              // 9.9 gibt es nicht → still weg
    expect(c.addedBoards()[0].caption).toBe('SK Absam – SK Wörgl');
    expect(el().querySelectorAll('app-live-board-card').length).toBe(4);      // 2 Schwaz + 1 hinzugefügt + 1 WM
    expect(el().textContent).toContain('FIDE World Championship 2026 · Game 3');
    expect(el().querySelectorAll('.lb-remove').length).toBe(2);
    expect(c.shareUrl()).toBe(`${location.origin}/tournaments/1404438/live?round=1&team=SK%20Schwaz&add=2.2,9.9&ext=fnmEB1kH.RD8sbYw1`);
    http.verify();
  });

  it('Analyse-Knopf: eigene Runde und fremde Partie verlinken /analysis?live=roundId.gameId auf RookHub', async () => {
    await setup({ team: 'SK Schwaz', ext: 'fnmEB1kH.RD8sbYw1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(c.analysisUrl('L3MX2oE3', 'Game0001')).toBeNull();                 // localhost: keine Partnerseite → kein Knopf
    (c as unknown as { rookhub: string }).rookhub = 'https://rookhub.oberschmid.homes';
    expect(c.ownRoundId()).toBe('L3MX2oE3');
    expect(c.analysisUrl(c.ownRoundId(), 'b1abcdef')).toBe('https://rookhub.oberschmid.homes/analysis?live=L3MX2oE3.b1abcdef');
    expect(c.analysisUrl('fnmEB1kH', 'RD8sbYw1')).toBe('https://rookhub.oberschmid.homes/analysis?live=fnmEB1kH.RD8sbYw1');
    expect(c.analysisUrl('fnmEB1kH', 'b1')).toBeNull();
  });

  it('Entfernen: ✕ nimmt das Brett aus der Adresse; Dialog-Ergebnis schreibt add/ext', async () => {
    await setup({ team: 'SK Schwaz', add: '2.1,2.2', ext: 'fnmEB1kH.RD8sbYw1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const c = fixture.componentInstance;
    c.removeAdded('2.1');
    expect(nav).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { add: '2.2', ext: 'fnmEB1kH.RD8sbYw1' } }));
    c.removeExt('fnmEB1kH.RD8sbYw1');
    expect(nav.calls.mostRecent().args[1]!.queryParams).toEqual({ add: '2.1,2.2', ext: null });
    spyOn(TestBed.inject(MatDialog), 'open').and.returnValue({ afterClosed: () => of({ added: ['2.1'], ext: ['fnmEB1kH.Other001'] }) } as never);
    c.openAdd();
    expect(nav.calls.mostRecent().args[1]!.queryParams).toEqual({ add: '2.1', ext: 'fnmEB1kH.Other001' });
  });

  it('Dialog-Auswahl: mit Mannschaftsfilter die Bretter der anderen Begegnungen, ohne Filter keine (alle sind zu sehen)', async () => {
    await setup({ team: 'SK Schwaz' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', TWO_MATCHES);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(c.addRows().map(r => r.key)).toEqual(['2.1', '2.2']);
    expect(el().querySelector('.live-add')).not.toBeNull();
    query.next(convertToParamMap({}));
    await fixture.whenStable();
    expect(c.addRows()).toEqual([]);
    expect(el().querySelector('.live-add')).not.toBeNull();                  // fremde Übertragungen gehen immer
  });

  it('fremde Runde unbekannt: Hinweis statt Brett, Entfernen nimmt die ganze Runde heraus', async () => {
    await setup({ ext: 'Unknown1.Game0001' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', SNAPSHOT);
    FakeEventSource.last().emit('ext-error', { round: 'Unknown1', code: 'live_ext_not_found' });
    await fixture.whenStable();
    expect(fixture.componentInstance.extRounds()[0].error).toBe('tournaments.live.extError.notFound');
  });

  it('Elo-Schnitt im Kopf der Begegnung, Tooltip mit Bretterzahl', async () => {
    await setup({ team: 'SK Schwaz' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', TWO_MATCHES);
    await fixture.whenStable();
    const elos = [...el().querySelectorAll('.mh-elo')].map(e => e.textContent!.trim());
    expect(elos).toEqual(['Ø 2539', 'Ø 2462']);
  });

  // Wunsch 2026-11-26: „Verbindung unterbrochen“ blinkte bei jedem kurzen Abriss — erst nach 10 s zeigen.
  it('kurzer Abriss: „Live“ bleibt stehen, erst nach der Gnadenfrist „unterbrochen“; zurück vorher → nichts zu sehen', fakeAsync(() => {
    void setup();
    flush();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    flush();
    const c = fixture.componentInstance;
    const es = FakeEventSource.last();
    es.emit('snapshot', SNAPSHOT);
    expect(c.state()).toBe('live');
    es.emit('state', { state: 'reconnecting' });
    tick(LIVE_LOST_GRACE_MS - 1000);
    expect(c.state()).toBe('live');
    es.emit('state', { state: 'live' });                                       // zurück vor Ablauf
    tick(5000);
    expect(c.state()).toBe('live');
    es.fail(true);                                                             // aufgegeben → offline
    tick(LIVE_LOST_GRACE_MS - 1);
    expect(c.state()).toBe('live');
    expect(el().querySelectorAll('app-live-board-card').length).toBe(2);       // Bretter bleiben mit letztem Stand
    tick(1);
    expect(c.state()).toBe('offline');
    fixture.destroy();
    discardPeriodicTasks();
    flush();
  }));

  // Wunsch 2026-11-26: „die Bretter in einer Wurscht, damit ich 8 auf einmal gut sehe“
  it('EIN Raster: Begegnungen, hinzugefügte und fremde Bretter ohne Abschnittsgrenze, Herkunft an der Karte', async () => {
    await setup({ ext: 'fnmEB1kH.RD8sbYw1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    es.emit('snapshot', TWO_MATCHES);
    es.emit('ext-snapshot', { round: 'fnmEB1kH', tourName: 'FIDE World Championship 2026', roundName: 'Game 3',
      url: 'https://lichess.org/broadcast/-/-/fnmEB1kH', state: 'live', games: [game('RD8sbYw1', 'Gukesh D', 'Carlsen, Magnus')] });
    await fixture.whenStable();
    const grids = el().querySelectorAll('.board-grid');
    expect(grids.length).toBe(1);
    expect(grids[0].querySelectorAll('app-live-board-card').length).toBe(5);
    expect(el().querySelectorAll('app-live-board-card').length).toBe(5);
    expect(el().querySelectorAll('section').length).toBe(0);
    const captions = [...el().querySelectorAll('.lb-caption')].map(c => c.textContent!.trim());
    expect(captions).toEqual(['· Pinggau-Friedberg – SK Schwaz', '· Pinggau-Friedberg – SK Schwaz', '· SK Absam – SK Wörgl',
      '· SK Absam – SK Wörgl', '· FIDE World Championship 2026 · Game 3']);
    expect(el().querySelectorAll('.match-strip .match-head').length).toBe(2);     // Stand je Begegnung bleibt als Kopfzeile
  });

  function eightBoards() {
    const games = Array.from({ length: 8 }, (_, i) => game('g' + i, 'Weiß ' + i, 'Schwarz ' + i));
    return {
      ...SNAPSHOT, games,
      layout: { team: false, matches: [], unassigned: [],
        boards: games.map((g, i) => ({ board: i + 1, white: g.white.name, black: g.black.name, result: null, gameId: g.id, homeIsWhite: null })) },
    };
  }

  async function columnsAt(width: number): Promise<{ firstRow: number; overflow: boolean; cardHeight: number }> {
    await setup();
    const host = el();
    host.style.display = 'block';
    host.style.width = width + 'px';
    document.body.appendChild(host);
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', eightBoards());
    await fixture.whenStable();
    const cards = [...host.querySelectorAll('app-live-board-card')] as HTMLElement[];
    expect(cards.length).toBe(8);
    const top = cards[0].getBoundingClientRect().top;
    const firstRow = cards.filter(c => Math.abs(c.getBoundingClientRect().top - top) < 1).length;
    const overflow = host.scrollWidth > host.clientWidth;
    const cardHeight = cards[0].getBoundingClientRect().height;
    host.remove();
    return { firstRow, overflow, cardHeight };
  }

  it('Bretterwand: bei 1920 px mindestens vier (sieben) Karten je Reihe, acht Bretter in höchstens zwei Reihen', async () => {
    const r = await columnsAt(1920);
    expect(r.firstRow).toBeGreaterThanOrEqual(4);
    expect(r.firstRow).toBe(7);
    expect(r.overflow).toBeFalse();
    // zwei Reihen (8 Bretter) passen unter Navbar, Kopf und Werkzeugzeile in ein 1080er-Fenster (~950 px Inhalt)
    expect(2 * r.cardHeight + 12).toBeLessThan(760);
  });

  it('Bretterwand: 1280 px vier bis fünf Spalten (mit/ohne Scrollleiste), Handy 390 px eine Spalte ohne Seiten-Scroll', async () => {
    const laptop = (await columnsAt(1280)).firstRow;
    expect(laptop).toBeGreaterThanOrEqual(4);
    expect(laptop).toBeLessThanOrEqual(5);
    TestBed.resetTestingModule();
    expect((await columnsAt(1265)).firstRow).toBe(4);                          // 1280 mit 15-px-Scrollleiste
    TestBed.resetTestingModule();
    const phone = await columnsAt(390);
    expect(phone.firstRow).toBe(1);
    expect(phone.overflow).toBeFalse();
  });

  it('samePlayer: Reihenfolge, Komma und Akzente egal', () => {
    expect(samePlayer('Mittal, Aditya', 'Aditya Mittal')).toBeTrue();
    expect(samePlayer('Österreicher, Jörg', 'Jorg Osterreicher')).toBeTrue();
    expect(samePlayer('A, B', 'A C')).toBeFalse();
  });
});
