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
import { LIVE_BEST_KEY, LIVE_EVAL_KEY, LIVE_TURNED_KEY, LIVE_LOST_GRACE_MS, LIVE_VIEW_KEYS, LIVE_VIEW_SAVE_MS, readViewKey, writeViewKey } from './live-boards.util';
import { MatDialog } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { LiveBoardCardComponent } from './live-board-card.component';
import { of } from 'rxjs';
import { LIVE_VIBRATE_KEY, VIBRATE_CONFIRM, VIBRATE_RESULT, VIBRATE_SWING } from './live-vibrate';
import { DOT_FAVICON, LIVE_FLASH_KEY, LIVE_NOTIFY_KEY, TAB_MARK } from './live-attention';

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
    // Standard ist Balken AN (1.039.0); die meisten Fälle prüfen den schlanken Strom → gemerkte Wahl „aus“.
    try { localStorage.setItem(LIVE_EVAL_KEY, '0'); localStorage.removeItem(LIVE_VIEW_KEYS); } catch { /* egal */ }
  });

  // Wunsch 2026-11-28: „standard mit engine + pfeil“ — auch für Gäste ohne gemerkte Wahl und ohne eval in der Adresse.
  it('ohne gemerkte Wahl: Engine-Balken und bester Zug AN, Strom mit eval=true', async () => {
    try { localStorage.removeItem(LIVE_EVAL_KEY); localStorage.removeItem(LIVE_BEST_KEY); } catch { /* egal */ }
    await setup({ round: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(c.evalOn()).toBeTrue();
    expect(c.bestOn()).toBeTrue();
    expect(FakeEventSource.last().url).toBe('/api/live-boards/1404438/rounds/1/stream?eval=true&ngsw-bypass=1');
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

  it('neuer Vollstand mitten im Strom (Runde bei Lichess neu angelegt): entfernte Partien sind vergessen, je Brett eine Karte', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    // Prod 27.11.2026: der Server hielt alte und neue Ids nebeneinander — je Brett zwei Karten.
    const doubled = { ...SNAPSHOT, games: [...SNAPSHOT.games, game('n1', 'Aditya Mittal', 'Hagner, Bennet'), game('n2', 'Schmider, Niklas', 'Vaibhav, Suri')] };
    doubled.layout = { ...SNAPSHOT.layout, matches: [{ ...SNAPSHOT.layout.matches[0], boards: [
      ...SNAPSHOT.layout.matches[0].boards,
      { board: 3, white: 'Aditya Mittal', black: 'Hagner, Bennet', result: '*', gameId: 'n1', homeIsWhite: true },
      { board: 4, white: 'Schmider, Niklas', black: 'Vaibhav, Suri', result: '*', gameId: 'n2', homeIsWhite: false },
    ] }] };
    es.emit('snapshot', doubled);
    await fixture.whenStable();
    expect(el().querySelectorAll('app-live-board-card').length).toBe(4);
    const current = { ...SNAPSHOT, games: [game('n1', 'Aditya Mittal', 'Hagner, Bennet'), game('n2', 'Schmider, Niklas', 'Vaibhav, Suri')] };
    current.layout = { ...SNAPSHOT.layout, matches: [{ ...SNAPSHOT.layout.matches[0], liveHome: 0, boards: [
      { board: 1, white: 'Aditya Mittal', black: 'Hagner, Bennet', result: '*', gameId: 'n1', homeIsWhite: true },
      { board: 2, white: 'Schmider, Niklas', black: 'Vaibhav, Suri', result: '*', gameId: 'n2', homeIsWhite: false },
    ] }] };
    es.emit('snapshot', current);
    await fixture.whenStable();
    expect(el().querySelectorAll('app-live-board-card').length).toBe(2);
    expect(Object.keys(fixture.componentInstance.games()).sort()).toEqual(['n1', 'n2']);
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

  it('Vibration: Schalter bestätigt kurz, Snapshot still, Ergebnis und Bewertungssprung mit eigenem Muster', async () => {
    try { localStorage.removeItem(LIVE_VIBRATE_KEY); } catch { /* egal */ }
    const vib = spyOn(navigator, 'vibrate').and.returnValue(true);
    await setup({ eval: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    expect(el().querySelector('.live-vibrate')).not.toBeNull();
    expect(fixture.componentInstance.vibrateOn()).toBeFalse();
    fixture.componentInstance.setVibrate(true);
    expect(vib).toHaveBeenCalledWith([...VIBRATE_CONFIRM]);
    expect(localStorage.getItem(LIVE_VIBRATE_KEY)).toBe('1');
    vib.calls.reset();
    const es = FakeEventSource.last();
    const withEval = (cp: number, plies: number): LiveGame => ({ ...game('b2', 'Schmider, Niklas', 'Vaibhav, Suri'), plies,
      eval: { cp, mate: null, source: 'engine', depth: 20 } });
    es.emit('snapshot', { ...SNAPSHOT, games: [SNAPSHOT.games[0], withEval(20, 10)] });
    await fixture.whenStable();
    expect(vib).not.toHaveBeenCalled();
    es.emit('game', withEval(160, 11));
    expect(vib).toHaveBeenCalledWith([...VIBRATE_SWING]);
    es.emit('game', { ...withEval(160, 11), eval: null, result: '0-1', finished: true });
    expect(vib.calls.mostRecent().args[0]).toEqual([...VIBRATE_RESULT]);
    // Wiederverbinden: neuer Snapshot mit fertiger Partie vibriert nicht
    vib.calls.reset();
    es.emit('snapshot', SNAPSHOT);
    expect(vib).not.toHaveBeenCalled();
    try { localStorage.removeItem(LIVE_VIBRATE_KEY); } catch { /* egal */ }
  });

  describe('Aufblitzen und Benachrichtigung (1.042.0)', () => {
    let hidden = false;
    let focused = true;
    let savedNotification: unknown;
    const withEval = (cp: number, plies: number): LiveGame => ({ ...game('b2', 'Schmider, Niklas', 'Vaibhav, Suri'), plies,
      eval: { cp, mate: null, source: 'engine', depth: 20 } });

    /** Fake der Notification API: Erlaubnis einstellbar, merkt sich jede Meldung. */
    class FakeNote {
      static permission: NotificationPermission = 'default';
      static answer: NotificationPermission = 'granted';
      static shown: FakeNote[] = [];
      static requestPermission = async () => { FakeNote.permission = FakeNote.answer; return FakeNote.answer; };
      onclick: (() => void) | null = null;
      constructor(public title: string, public options: NotificationOptions) { FakeNote.shown.push(this); }
      close(): void { /* nichts */ }
    }

    async function connected(): Promise<FakeEventSource> {
      await setup({ eval: '1' });
      http.expectOne('/api/live-boards/1404438').flush(STATUS);
      await fixture.whenStable();
      const es = FakeEventSource.last();
      es.emit('snapshot', { ...SNAPSHOT, games: [SNAPSHOT.games[0], withEval(20, 10)] });
      await fixture.whenStable();
      return es;
    }

    beforeEach(() => {
      hidden = false;
      focused = true;
      Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
      spyOn(document, 'hasFocus').and.callFake(() => focused);
      savedNotification = (window as unknown as { Notification?: unknown }).Notification;
      FakeNote.permission = 'default';
      FakeNote.answer = 'granted';
      FakeNote.shown = [];
      (window as unknown as { Notification: unknown }).Notification = FakeNote;
      try { localStorage.removeItem(LIVE_FLASH_KEY); localStorage.removeItem(LIVE_NOTIFY_KEY); localStorage.removeItem(LIVE_VIBRATE_KEY); } catch { /* egal */ }
    });

    afterEach(() => {
      delete (document as unknown as { hidden?: boolean }).hidden;
      (window as unknown as { Notification: unknown }).Notification = savedNotification;
      try { localStorage.removeItem(LIVE_FLASH_KEY); localStorage.removeItem(LIVE_NOTIFY_KEY); } catch { /* egal */ }
    });

    const card = (id: string) => el().querySelector(`app-live-board-card[data-game="${id}"] .lb-card`)!;

    it('sichtbar: die betroffene Karte blitzt auf — Ergebnis und Sprung mit eigener Klasse; Schalter aus = nichts', async () => {
      const es = await connected();
      expect(el().querySelector('.live-flash')).not.toBeNull();
      es.emit('game', withEval(160, 11));
      await fixture.whenStable();
      expect(card('b2').className).not.toContain('lb-flash');
      fixture.componentInstance.setFlash(true);
      expect(localStorage.getItem(LIVE_FLASH_KEY)).toBe('1');
      es.emit('game', withEval(20, 12));
      await fixture.whenStable();
      expect(card('b2').classList).toContain('lb-flash-swing');
      expect(card('b1').className).not.toContain('lb-flash');
      es.emit('game', { ...withEval(20, 12), eval: null, result: '1-0', finished: true });
      await fixture.whenStable();
      expect(card('b2').classList).toContain('lb-flash-result');
      expect(document.title).not.toContain(TAB_MARK);
    });

    it('Hintergrund: Tab-Titel mit „●“ und Favicon mit Punkt, zurück beim Sichtbarwerden und beim Verlassen der Seite', async () => {
      const title = document.title;
      const link = document.createElement('link');
      link.rel = 'icon';
      link.href = 'favicon.ico';
      document.head.appendChild(link);
      try {
        try { localStorage.setItem(LIVE_FLASH_KEY, '1'); } catch { /* egal */ }
        const es = await connected();
        hidden = true;
        es.emit('game', { ...withEval(20, 11), eval: null, result: '0-1', finished: true });
        expect(document.title).toBe(TAB_MARK + '0-1 tournaments.live.boardN');
        expect(link.getAttribute('href')).toBe(DOT_FAVICON);
        expect(card('b2').className).not.toContain('lb-flash');
        hidden = false;
        document.dispatchEvent(new Event('visibilitychange'));
        expect(document.title).toBe(title);
        expect(link.getAttribute('href')).toBe('favicon.ico');
        // nochmal im Hintergrund, dann Seite verlassen
        hidden = true;
        es.emit('snapshot', { ...SNAPSHOT, games: [{ ...SNAPSHOT.games[0], result: '*', finished: false }, withEval(20, 10)] });
        es.emit('game', { ...SNAPSHOT.games[0] });
        expect(document.title).toContain(TAB_MARK);
        fixture.destroy();
        expect(document.title).toBe(title);
        expect(link.getAttribute('href')).toBe('favicon.ico');
      } finally {
        link.remove();
        document.title = title;
      }
    });

    it('Benachrichtigung erlaubt: nur wenn die Seite nicht im Blick ist, tag je Brett, Klick zeigt das Brett', async () => {
      const es = await connected();
      expect(el().querySelector('.live-notify')).not.toBeNull();
      await fixture.componentInstance.setNotify(true);
      expect(fixture.componentInstance.notifyOn()).toBeTrue();
      expect(localStorage.getItem(LIVE_NOTIFY_KEY)).toBe('1');
      es.emit('game', withEval(160, 11));   // sichtbar + Fokus: keine Meldung
      expect(FakeNote.shown.length).toBe(0);
      focused = false;                      // anderes Fenster vorn
      es.emit('game', withEval(20, 12));
      expect(FakeNote.shown.length).toBe(1);
      expect(FakeNote.shown[0].title).toBe('tournaments.live.boardN: +1.6 → +0.2');
      expect(FakeNote.shown[0].options.tag).toBe('rh-live-b2');
      hidden = true;
      es.emit('game', { ...withEval(20, 12), eval: null, result: '1/2-1/2', finished: true });
      expect(FakeNote.shown.length).toBe(2);
      expect(FakeNote.shown[1].options.body).toBe('Schmider, Niklas – Vaibhav, Suri ½-½');
      expect(FakeNote.shown[1].options.tag).toBe('rh-live-b2');
      const focus = spyOn(window, 'focus');
      const scroll = spyOn(Element.prototype, 'scrollIntoView');
      FakeNote.shown[1].onclick!();
      await fixture.whenStable();
      expect(focus).toHaveBeenCalled();
      expect(scroll).toHaveBeenCalled();
      expect(card('b2').classList).toContain('lb-flash-result');
    });

    it('Benachrichtigung abgelehnt: Schalter zurück und Hinweis; ohne Notification API kein Schalter', async () => {
      FakeNote.answer = 'denied';
      await connected();
      const warn = spyOn(TestBed.inject(SnackbarService), 'warn');
      const toggle = { checked: true };
      await fixture.componentInstance.setNotify(true, toggle as never);
      expect(fixture.componentInstance.notifyOn()).toBeFalse();
      expect(toggle.checked).toBeFalse();
      expect(warn).toHaveBeenCalledWith('tournaments.live.notifyDenied');
      expect(localStorage.getItem(LIVE_NOTIFY_KEY)).toBe('0');
      fixture.destroy();
      TestBed.resetTestingModule();
      delete (window as unknown as { Notification?: unknown }).Notification;
      await connected();
      expect(el().querySelector('.live-notify')).toBeNull();
      expect(el().querySelector('.live-flash')).not.toBeNull();
    });
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
    expect(localStorage.getItem(LIVE_EVAL_KEY)).toBe('0');                    // gemerkte Wahl bleibt
  });

  it('Balken umschalten: gemerkt im Browser, nicht in die Adresse; ein ausdrückliches ?eval= fällt weg (ersetzt den Eintrag)', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.componentInstance.setEval(true);
    expect(nav).not.toHaveBeenCalled();
    expect(localStorage.getItem(LIVE_EVAL_KEY)).toBe('1');
  });

  it('Balken umschalten bei ausdrücklichem ?eval=0: Adresse ging vor, der Schalter nimmt eval heraus (ersetzt den Eintrag)', async () => {
    try { localStorage.removeItem(LIVE_EVAL_KEY); } catch { /* egal */ }
    await setup({ eval: '0', round: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    expect(fixture.componentInstance.evalOn()).toBeFalse();                       // Adresse vor Vorliebe
    const nav2 = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.componentInstance.setEval(true);
    expect(nav2).toHaveBeenCalledWith([], jasmine.objectContaining({ queryParams: { eval: null }, queryParamsHandling: 'merge', replaceUrl: true }));
  });

  const TOKEN = 'Ab3dEf7hJk';
  const KEY = 'K'.repeat(24);
  const viewOf = (over: Record<string, unknown> = {}) => ({ round: 1, team: 'SK Schwaz', player: null, add: [], ext: [], ...over });

  it('lange Adresse bleibt gültig: Runde immer ausdrücklich, Mannschaft mit, Balken nicht (jeder selbst)', async () => {
    await setup({ team: 'Schachklub Schwaz' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    expect(c.shareUrl()).toBe(`${location.origin}/tournaments/1404438/live?round=1&team=Schachklub%20Schwaz`);
    c.evalOn.set(true);
    expect(c.shareUrl()).toBe(`${location.origin}/tournaments/1404438/live?round=1&team=Schachklub%20Schwaz`);
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
    expect(es.url).toBe(`/api/live-boards/views/${TOKEN}/stream?eval=false&ngsw-bypass=1`);
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

  it('/live/<token> Zuschauer: eigener Balken nur für ihn (eval im Strom, gemerkt), andere Änderung → eigene lange Ansicht', async () => {
    await openView();
    const c = fixture.componentInstance;
    c.setEval(true);
    expect(FakeEventSource.last().url).toBe(`/api/live-boards/views/${TOKEN}/stream?eval=true&ngsw-bypass=1`);
    expect(localStorage.getItem(LIVE_EVAL_KEY)).toBe('1');
    // Alte gespeicherte Ansicht mit eval: false (vor 1.039.0) — wird ignoriert.
    FakeEventSource.last().emit('view', { token: TOKEN, tournamentId: '1404438', view: { ...viewOf(), eval: false } as never });
    expect(c.evalOn()).toBeTrue();
    const nav = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    c.setFilter('team', 'SK Absam');
    expect(TestBed.inject(Router).serializeUrl(nav.calls.mostRecent().args[0] as never))
      .toBe('/tournaments/1404438/live?round=1&team=SK%20Absam');
    http.verify();                                                               // kein PUT
  });

  // Wunsch 2026-11-28: „nur die bretter bei allen gleich … engine + bester zug soll jeder selbst ein/ausschalten können“
  it('/live/<token> Besitzer: Balken und bester Zug nur für ihn — kein PUT, die Ansicht bleibt', fakeAsync(() => {
    writeViewKey(TOKEN, KEY);
    void openView(viewOf(), true);
    flush();
    const c = fixture.componentInstance;
    c.setEval(true);
    c.setBest(false);
    tick(LIVE_VIEW_SAVE_MS * 2);
    http.verify();                                                               // nichts gespeichert
    expect(FakeEventSource.last().url).toBe(`/api/live-boards/views/${TOKEN}/stream?eval=true&ngsw-bypass=1`);
    expect(Object.keys(c.currentView()).sort()).toEqual(['add', 'ext', 'player', 'round', 'team']);
    c.setAdded(['2.1'], []);                                                     // Bretter dagegen gehen an alle
    tick(LIVE_VIEW_SAVE_MS);
    const req = http.expectOne(`/api/live-boards/views/${TOKEN}`);
    expect(req.request.body).toEqual({ view: viewOf({ add: ['2.1'] }) });
    req.flush({ token: TOKEN, tournamentId: '1404438', view: viewOf({ add: ['2.1'] }), canEdit: true });
    fixture.destroy();
  }));

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

  // 1.040.0: chess.com-Übertragung als fremde Runde — derselbe Strom, Herkunft „chess.com“ und Link zur Event-Seite.
  it('fremdes chess.com-Brett: EIN Strom mit cc-Kennung, Zeile „Turnier · Runde · chess.com“, Analyse-Link', async () => {
    const round = 'cc~2026-xtx-markets-london-chess-classic-lcc-swiss~02';
    const key = `${round}.Verma_Aditya-Zwirs_Nico`;
    await setup({ team: 'SK Schwaz', round: '1', ext: `fnmEB1kH.RD8sbYw1,${key}` });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    expect(es.url).toContain(`&extra=fnmEB1kH.RD8sbYw1,${key}&`);
    es.emit('snapshot', TWO_MATCHES);
    es.emit('ext-snapshot', { round, tourName: 'LCC Swiss', roundName: 'Round 2',
      url: 'https://www.chess.com/events/2026-xtx-markets-london-chess-classic-lcc-swiss/games', state: 'live',
      games: [game('Verma_Aditya-Zwirs_Nico', 'Verma, Aditya', 'Zwirs, Nico')] });
    await fixture.whenStable();
    const links = [...el().querySelectorAll<HTMLAnchorElement>('.sources .ext-link')];
    const cc = links.find(a => a.href.includes('chess.com'))!;
    expect(cc.textContent).toContain('LCC Swiss · Round 2 · chess.com');
    expect(cc.href).toBe('https://www.chess.com/events/2026-xtx-markets-london-chess-classic-lcc-swiss/games');
    expect(links.find(a => a.href.includes('lichess.org'))!.textContent).toContain('lichess.org');   // Lichess-Runde: Herkunft ebenso
    expect(el().textContent).toContain('Verma, Aditya');
    const c = fixture.componentInstance;
    (c as unknown as { rookhub: string }).rookhub = 'https://rookhub.oberschmid.homes';
    expect(c.analysisUrl(round, 'Verma_Aditya-Zwirs_Nico')).toBe(`https://rookhub.oberschmid.homes/analysis?live=${key}`);
    expect(c.analysisUrl(round, 'a.b')).toBeNull();
    expect(c.shareUrl()).toContain(`ext=fnmEB1kH.RD8sbYw1,${key}`);
  });

  it('eigene Übertragung bei chess.com: Quelle „chess.com“ in der Liste, Begegnungen verlinken die Event-Seite', async () => {
    await setup({ round: '1' });
    http.expectOne('/api/live-boards/1404438').flush({ ...STATUS, tourId: 'cc~2026-fide-chess-world-championship',
      url: 'https://www.chess.com/events/2026-fide-chess-world-championship',
      rounds: STATUS.rounds.map(r => ({ ...r, lichessRoundId: `cc~2026-fide-chess-world-championship~0${r.round}` })) });
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', TWO_MATCHES);
    await fixture.whenStable();
    expect(el().querySelector('.sources .live-source')!.textContent).toContain('· chess.com');
    expect(fixture.componentInstance.ownRoundUrl()).toBe('https://www.chess.com/events/2026-fide-chess-world-championship/games');
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
    expect(el().querySelectorAll('.sources .match-head').length).toBe(2);         // Stand je Begegnung bleibt als Zeile
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
    // zwei Reihen (8 Bretter) passen unter Navbar, Kopf und Werkzeugzeile in ein 1080er-Fenster (~950 px Inhalt). Seit 1.035.2
    // steht die Elo in eigener Zeile unter dem Namen (Wunsch 27.11.) — je Karte rund 26 px mehr, Grenze daher 775 statt 760.
    expect(2 * r.cardHeight + 12).toBeLessThan(775);
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

  it('Besten Zug zeigen: je Gerät gemerkt, nicht in Adresse/geteilter Ansicht, kein neuer Strom, ohne Balken gesperrt', async () => {
    try { localStorage.removeItem(LIVE_BEST_KEY); } catch { /* egal */ }
    await setup({ round: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    const toggle = () => el().querySelector('.live-best input, .live-best button') as HTMLButtonElement;
    expect(toggle().disabled).toBeTrue();                                         // Balken aus → nichts zu zeigen
    c.setEval(true);
    await fixture.whenStable();
    expect(toggle().disabled).toBeFalse();
    const streams = FakeEventSource.all.length;
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    c.setBest(true);
    expect(localStorage.getItem(LIVE_BEST_KEY)).toBe('1');
    expect(nav).not.toHaveBeenCalled();
    expect(FakeEventSource.all.length).toBe(streams);
    expect(c.shareUrl()).not.toContain('best');
    expect(Object.keys(c.currentView())).not.toContain('best');
  });

  // Wunsch 2026-11-26: „Brett drehen“ je Karte — nur dieser Zuschauer, nicht im Link
  it('Brett drehen: kehrt die Vorgabe nur für diese Partie um, gemerkt in der Sitzung, nicht in Adresse/geteilter Ansicht', async () => {
    try { sessionStorage.removeItem(LIVE_TURNED_KEY); } catch { /* egal */ }
    await setup({ team: 'SK Schwaz', round: '1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', SNAPSHOT);
    await fixture.whenStable();
    const c = fixture.componentInstance;
    const cards = () => [...el().querySelectorAll('app-live-board-card')] as HTMLElement[];
    const flips = () => fixture.debugElement.queryAll(By.directive(LiveBoardCardComponent)).map(d => (d.componentInstance as LiveBoardCardComponent).flipped());
    // Reihenfolge b2, b1: das fertige Brett 1 steht seit 1.043.0 hinten
    expect(flips()).toEqual([false, true]);                                      // Vorgabe: Schwaz unten
    const nav = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    (cards()[1].querySelector('.lb-turn') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(flips()).toEqual([false, false]);
    expect(cards()[1].querySelector('.lb-turn')!.getAttribute('aria-pressed')).toBe('true');
    expect(JSON.parse(sessionStorage.getItem(LIVE_TURNED_KEY)!)).toEqual(['b1']);
    expect(nav).not.toHaveBeenCalled();
    expect(c.shareUrl()).toBe(`${location.origin}/tournaments/1404438/live?round=1&team=SK%20Schwaz`);
    c.toggleTurn('b1');
    await fixture.whenStable();
    expect(flips()).toEqual([false, true]);
  });

  // Wunsch 2026-11-26: „Sammel alle Links … und schreib die untereinander irgendwo, wo’s passt“
  it('Übertragungen: Turnier, Begegnungen (Link zur Lichess-Runde) und fremde Runden untereinander, Umbruch statt Abschneiden', async () => {
    await setup({ team: 'SK Schwaz', round: '1', ext: 'fnmEB1kH.RD8sbYw1' });
    const host = el();
    host.style.display = 'block';
    host.style.width = '390px';
    document.body.appendChild(host);
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    es.emit('snapshot', TWO_MATCHES);
    es.emit('ext-snapshot', { round: 'fnmEB1kH', tourName: '35. ekipno prvenstvo Hrvatske 2026 – 1. liga, muškarci', roundName: 'Round 6',
      url: 'https://lichess.org/broadcast/-/-/fnmEB1kH', state: 'live', games: [game('RD8sbYw1', 'Gukesh D', 'Carlsen, Magnus')] });
    await fixture.whenStable();
    const rows = [...host.querySelectorAll('.sources li:not(.sources-label)')] as HTMLElement[];
    expect(rows.length).toBe(3);
    expect(rows[0].querySelector('a')!.getAttribute('href')).toBe(STATUS.url!);
    expect(rows[0].textContent).toContain('1. Bundesliga Austria 2026/2027 · lichess.org');
    expect(rows[1].querySelector('a')!.getAttribute('href')).toBe('https://lichess.org/broadcast/-/-/L3MX2oE3');
    expect(rows[1].textContent!.replace(/\s+/g, '')).toBe('Pinggau-FriedbergØ25391:0SKSchwazØ2462');   // Abstände per CSS
    expect(rows[2].querySelector('a')!.getAttribute('href')).toBe('https://lichess.org/broadcast/-/-/fnmEB1kH');
    expect(rows[2].textContent).toContain('35. ekipno prvenstvo Hrvatske 2026 – 1. liga, muškarci · Round 6');
    // untereinander: jede Zeile unter der vorigen; nichts ragt über die Breite, keine Ellipse
    for (let i = 1; i < rows.length; i++) expect(rows[i].getBoundingClientRect().top).toBeGreaterThanOrEqual(rows[i - 1].getBoundingClientRect().bottom - 0.5);
    const list = host.querySelector('.sources') as HTMLElement;
    expect(list.scrollWidth).toBeLessThanOrEqual(list.clientWidth);
    for (const r of rows) expect(getComputedStyle(r).textOverflow).not.toBe('ellipsis');
    expect(host.querySelector('.live-head .live-source')).toBeNull();           // der Turnier-Link steht jetzt in der Liste
    host.remove();
  });

  // Wunsch 2026-11-28: „wenn ein Spiel fertig ist, dann wandert es ans Ende mit dem Ergebnis als großes Overlay“.
  it('beendete Partien ans Ende: Begegnung allein → alle Bretter gemeinsam, Übersicht → je Begegnung, Blöcke bleiben ganz', async () => {
    await setup({ ext: 'fnmEB1kH.RD8sbYw1' });
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    const c = fixture.componentInstance;
    const order = () => c.allBoards().map(b => b.game.id);
    // Eine Begegnung + fremdes Brett: das fertige b1 rückt hinter alle laufenden (auch hinter das fremde)
    es.emit('snapshot', SNAPSHOT);
    es.emit('ext-snapshot', { round: 'fnmEB1kH', tourName: 'WM', roundName: 'Game 3', url: 'https://lichess.org/broadcast/-/-/fnmEB1kH',
      state: 'live', games: [game('RD8sbYw1', 'Gukesh D', 'Carlsen, Magnus')] });
    await fixture.whenStable();
    expect(order()).toEqual(['b2', 'RD8sbYw1', 'b1']);
    // Zwei Begegnungen: b1 nur innerhalb seiner Begegnung nach hinten, die zweite Begegnung bleibt ein Block danach
    es.emit('snapshot', { ...TWO_MATCHES, games: [...TWO_MATCHES.games.slice(0, 2), game('c1', 'Muster, Max', 'Gast, Erster', '0-1'),
      TWO_MATCHES.games[3]] });
    await fixture.whenStable();
    expect(order()).toEqual(['b2', 'b1', 'c2', 'c1', 'RD8sbYw1']);
    // Die gespeicherte Auswahl bleibt unberührt (nur Darstellung)
    expect(c.matches()[0].boards.map(b => b.game.id)).toEqual(['b1', 'b2']);
  });

  it('Ergebnis-Schleier: fertiges Brett abgedunkelt mit großem Ergebnis, laufendes ohne; Klicks gehen durch', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    FakeEventSource.last().emit('snapshot', SNAPSHOT);
    await fixture.whenStable();
    const cards = [...el().querySelectorAll('app-live-board-card')] as HTMLElement[];
    expect(cards[0].querySelector('.lb-final')).toBeNull();                  // b2 läuft (steht vorne)
    const veil = cards[1].querySelector('.lb-final') as HTMLElement;
    expect(veil).not.toBeNull();
    expect(veil.querySelector('.lb-final-text')!.textContent!.trim()).toBe('1–0');
    expect(veil.getAttribute('aria-label')).toBe('tournaments.live.resultOverlay');
    expect(getComputedStyle(veil).pointerEvents).toBe('none');
    expect(cards[1].querySelector('.lb-name')).not.toBeNull();               // Namen stehen außerhalb des Schleiers
    expect(veil.contains(cards[1].querySelector('.lb-name'))).toBeFalse();
  });

  it('Wechsel ans Ende gleitet (FLIP) — nicht beim Erstladen, nicht mit „Bewegung reduzieren“', async () => {
    const anim = spyOn(HTMLElement.prototype, 'animate').and.callThrough();
    await setup();
    const host = el();
    host.style.display = 'block';
    host.style.width = '1280px';
    document.body.appendChild(host);
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    const three = { ...SNAPSHOT, games: [game('b1', 'Aditya Mittal', 'Hagner, Bennet', '1-0'), game('b2', 'Schmider, Niklas', 'Vaibhav, Suri'),
      game('b3', 'Dritter, Weiß', 'Dritter, Schwarz')] };
    three.layout = { ...SNAPSHOT.layout, matches: [{ ...SNAPSHOT.layout.matches[0], boards: [...SNAPSHOT.layout.matches[0].boards,
      { board: 3, white: 'Dritter, Weiß', black: 'Dritter, Schwarz', result: '*', gameId: 'b3', homeIsWhite: true }] }] };
    es.emit('snapshot', three);
    await fixture.whenStable();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.allBoards().map(b => b.game.id)).toEqual(['b2', 'b3', 'b1']);   // schon fertig → einfach hinten
    expect(anim).not.toHaveBeenCalled();
    es.emit('game', { ...three.games[1], result: '1/2-1/2', finished: true });   // b2 wird fertig
    await fixture.whenStable();
    fixture.detectChanges();
    expect(c.allBoards().map(b => b.game.id)).toEqual(['b3', 'b1', 'b2']);
    expect(anim).toHaveBeenCalled();
    const moved = anim.calls.all().map(x => (x.object as HTMLElement).dataset['key']);
    expect(moved.length).toBeGreaterThan(0);
    host.remove();
  });

  it('Wechsel ans Ende ohne Animation, wenn das Gerät „Bewegung reduzieren“ will', async () => {
    const anim = spyOn(HTMLElement.prototype, 'animate').and.callThrough();
    const realMatch = window.matchMedia.bind(window);
    spyOn(window, 'matchMedia').and.callFake((q: string) => q.includes('reduced-motion') ? { matches: true } as MediaQueryList : realMatch(q));
    await setup();
    const host = el();
    host.style.display = 'block';
    host.style.width = '1280px';
    document.body.appendChild(host);
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    es.emit('snapshot', { ...SNAPSHOT, games: [game('b1', 'Aditya Mittal', 'Hagner, Bennet'), SNAPSHOT.games[1]] });
    await fixture.whenStable();
    expect(fixture.componentInstance.allBoards().map(b => b.game.id)).toEqual(['b1', 'b2']);
    es.emit('game', { ...SNAPSHOT.games[0] });                                // b1 wird fertig → rückt ans Ende, springt aber
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.allBoards().map(b => b.game.id)).toEqual(['b2', 'b1']);
    expect(anim).not.toHaveBeenCalled();
    host.remove();
  });

  it('samePlayer: Reihenfolge, Komma und Akzente egal', () => {
    expect(samePlayer('Mittal, Aditya', 'Aditya Mittal')).toBeTrue();
    expect(samePlayer('Österreicher, Jörg', 'Jorg Osterreicher')).toBeTrue();
    expect(samePlayer('A, B', 'A C')).toBeFalse();
  });
});
