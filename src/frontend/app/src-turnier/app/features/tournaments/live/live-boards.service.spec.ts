import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EVENT_SOURCE_FACTORY, LIVE_RETRY_MS, LiveBoardsService } from './live-boards.service';
import { LiveStreamEvent } from './live-boards.models';
import { FakeEventSource } from './fake-event-source';

describe('LiveBoardsService', () => {
  let service: LiveBoardsService;
  let http: HttpTestingController;

  beforeEach(() => {
    FakeEventSource.reset();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: EVENT_SOURCE_FACTORY, useValue: FakeEventSource.factory }],
    });
    service = TestBed.inject(LiveBoardsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('status und link sprechen /api/live-boards an', () => {
    service.status('1404438').subscribe();
    http.expectOne('/api/live-boards/1404438').flush({ linked: false, rounds: [] });
    service.link('1404438', 'https://lichess.org/broadcast/x/IjDgZVUR').subscribe();
    const req = http.expectOne('/api/live-boards/1404438/link');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ url: 'https://lichess.org/broadcast/x/IjDgZVUR' });
    req.flush({ linked: true, rounds: [] });
  });

  it('stream: Adresse mit Runde und eval, reicht snapshot/game/state weiter, schließt beim Abbestellen', () => {
    const got: LiveStreamEvent[] = [];
    const sub = service.stream('1404438', 3, true).subscribe(e => got.push(e));
    const es = FakeEventSource.last();
    expect(es.url).toBe('/api/live-boards/1404438/rounds/3/stream?eval=true&ngsw-bypass=1');
    es.emit('snapshot', { state: 'live', round: 3, layout: { team: false, matches: [], boards: [], unassigned: [] }, games: [] });
    es.emit('game', { id: 'g1' });
    es.emit('state', { state: 'finished' });
    expect(got.map(e => e.type)).toEqual(['snapshot', 'game', 'state']);
    expect((got[0] as Extract<LiveStreamEvent, { type: 'snapshot' }>).state).toBe('live');
    sub.unsubscribe();
    expect(es.closed).toBeTrue();
  });

  // 1.025.0: Strom am Service Worker vorbei (Chrome beendete den untätigen Worker nach ~30 s → Abriss alle 29–60 s).
  it('stream: immer ngsw-bypass, fremde Bretter als extra=, ext-Ereignisse mit Runde', () => {
    const got: LiveStreamEvent[] = [];
    service.stream('20', 2, false).subscribe().unsubscribe();
    expect(FakeEventSource.last().url).toBe('/api/live-boards/20/rounds/2/stream?ngsw-bypass=1');
    const sub = service.stream('20', 2, false, ['fnmEB1kH.RD8sbYw1', 'fnmEB1kH.Other001']).subscribe(e => got.push(e));
    const es = FakeEventSource.last();
    expect(es.url).toBe('/api/live-boards/20/rounds/2/stream?extra=fnmEB1kH.RD8sbYw1,fnmEB1kH.Other001&ngsw-bypass=1');
    es.emit('ext-snapshot', { round: 'fnmEB1kH', tourName: 'WM', roundName: 'Game 3', url: 'u', state: 'live', games: [] });
    es.emit('ext-game', { round: 'fnmEB1kH', game: { id: 'RD8sbYw1' } });
    es.emit('ext-error', { round: 'Unknown1', code: 'live_ext_not_found' });
    es.emit('ext-state', { nope: 1 });                                     // ohne Runde: verworfen
    expect(got.map(e => e.type)).toEqual(['ext-snapshot', 'ext-game', 'ext-error']);
    expect((got[2] as Extract<LiveStreamEvent, { type: 'ext-error' }>).code).toBe('live_ext_not_found');
    sub.unsubscribe();
  });

  it('external: Link als Parameter', () => {
    service.external('https://lichess.org/broadcast/x/game-3/fnmEB1kH/RD8sbYw1').subscribe();
    const req = http.expectOne(r => r.url === '/api/live-boards/external');
    expect(req.request.params.get('url')).toBe('https://lichess.org/broadcast/x/game-3/fnmEB1kH/RD8sbYw1');
    req.flush({});
  });

  it('stream: aufgegebene Verbindung → offline, dann neuer Versuch nach der Wartezeit', fakeAsync(() => {
    const got: LiveStreamEvent[] = [];
    const sub = service.stream('1404438', 1, false).subscribe(e => got.push(e));
    FakeEventSource.last().fail(false);                     // EventSource versucht es selbst
    expect(got.at(-1)).toEqual({ type: 'state', state: 'reconnecting' });
    expect(FakeEventSource.all.length).toBe(1);
    FakeEventSource.last().fail(true);                      // aufgegeben (z. B. 429)
    expect(got.at(-1)).toEqual({ type: 'state', state: 'offline' });
    tick(LIVE_RETRY_MS[0]);
    expect(FakeEventSource.all.length).toBe(2);
    sub.unsubscribe();
    expect(FakeEventSource.last().closed).toBeTrue();
  }));
});
