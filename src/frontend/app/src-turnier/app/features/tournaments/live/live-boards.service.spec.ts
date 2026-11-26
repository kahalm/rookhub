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
    expect(es.url).toBe('/api/live-boards/1404438/rounds/3/stream?eval=true');
    es.emit('snapshot', { state: 'live', round: 3, layout: { team: false, matches: [], boards: [], unassigned: [] }, games: [] });
    es.emit('game', { id: 'g1' });
    es.emit('state', { state: 'finished' });
    expect(got.map(e => e.type)).toEqual(['snapshot', 'game', 'state']);
    expect((got[0] as Extract<LiveStreamEvent, { type: 'snapshot' }>).state).toBe('live');
    sub.unsubscribe();
    expect(es.closed).toBeTrue();
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
