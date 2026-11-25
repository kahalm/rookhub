import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { LiveBoardsComponent, samePlayer } from './live-boards.component';
import { EVENT_SOURCE_FACTORY } from './live-boards.service';
import { FakeEventSource } from './fake-event-source';
import { LiveGame, LiveStatus } from './live-boards.models';
import { LIVE_EVAL_KEY } from './live-boards.util';

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

  async function setup(params: Record<string, string> = {}): Promise<void> {
    FakeEventSource.reset();
    query = new BehaviorSubject(convertToParamMap(params));
    await TestBed.configureTestingModule({
      imports: [LiveBoardsComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: EVENT_SOURCE_FACTORY, useValue: FakeEventSource.factory },
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: '1404438' })), queryParamMap: query } },
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
    try { localStorage.removeItem(LIVE_EVAL_KEY); } catch { /* egal */ }
  });

  it('Gast, verknüpft: verbindet den Strom der laufenden Runde und zeigt die Bretter der Begegnung mit Stand', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const es = FakeEventSource.last();
    expect(es.url).toBe('/api/live-boards/1404438/rounds/1/stream');
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

  it('Engine-Balken: Schalter merkt sich die Wahl und verbindet mit eval=1 neu', async () => {
    await setup();
    http.expectOne('/api/live-boards/1404438').flush(STATUS);
    await fixture.whenStable();
    const first = FakeEventSource.last();
    fixture.componentInstance.setEval(true);
    expect(first.closed).toBeTrue();
    expect(FakeEventSource.last().url).toBe('/api/live-boards/1404438/rounds/1/stream?eval=1');
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

  it('samePlayer: Reihenfolge, Komma und Akzente egal', () => {
    expect(samePlayer('Mittal, Aditya', 'Aditya Mittal')).toBeTrue();
    expect(samePlayer('Österreicher, Jörg', 'Jorg Osterreicher')).toBeTrue();
    expect(samePlayer('A, B', 'A C')).toBeFalse();
  });
});
