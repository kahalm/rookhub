import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpEventType, provideHttpClient } from '@angular/common/http';
import { ExternalEngineService, EngineAnalyseLine, EngineAnalyseWork } from './external-engine.service';

const WORK: EngineAnalyseWork = {
  sessionId: 's1',
  initialFen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
  moves: [],
  multiPv: 2,
  depth: 20,
};

describe('ExternalEngineService', () => {
  let svc: ExternalEngineService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    svc = TestBed.inject(ExternalEngineService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists engines from the RookHub proxy', () => {
    let result: unknown;
    svc.listEngines().subscribe(r => result = r);
    const req = http.expectOne('/api/engine/external');
    expect(req.request.method).toBe('GET');
    req.flush({ hasCredentials: true, tokenInvalid: false, engines: [{ id: 'eei_a', name: 'SF', maxThreads: 4, maxHash: 256 }] });
    expect(result).toEqual(jasmine.objectContaining({ hasCredentials: true }));
  });

  it('emits each complete ndjson line as it streams in — without re-emitting earlier lines', () => {
    const seen: EngineAnalyseLine[] = [];
    svc.analyse('eei_a', WORK).subscribe(l => seen.push(l));
    const req = http.expectOne('/api/engine/external/eei_a/analyse');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(WORK);

    const l1 = '{"time":10,"depth":8,"nodes":100,"pvs":[{"depth":8,"cp":20,"moves":["e2e4"]}]}';
    const l2 = '{"time":20,"depth":12,"nodes":900,"pvs":[{"depth":12,"cp":31,"moves":["d2d4"]}]}';

    req.event({ type: HttpEventType.DownloadProgress, loaded: 1, partialText: l1 + '\n' } as never);
    expect(seen.length).toBe(1);
    expect(seen[0].depth).toBe(8);

    // Zweiter Chunk enthält den GANZEN Text bisher — die erste Zeile darf nicht erneut kommen.
    req.event({ type: HttpEventType.DownloadProgress, loaded: 2, partialText: l1 + '\n' + l2 + '\n' } as never);
    expect(seen.length).toBe(2);
    expect(seen[1].pvs[0].cp).toBe(31);

    req.flush(l1 + '\n' + l2 + '\n');
    expect(seen.length).toBe(2);
  });

  it('ignores blank keep-alive lines (server heartbeat) between result lines', () => {
    // Der API-Proxy schickt bei Funkstille des Brokers alle 20 s ein nacktes "\n" (NdjsonHeartbeatPump),
    // damit Proxys davor die Verbindung nicht kappen — für den Parser darf das kein Ereignis sein.
    const seen: EngineAnalyseLine[] = [];
    let completed = false;
    svc.analyse('eei_a', WORK).subscribe({ next: l => seen.push(l), complete: () => completed = true });
    const req = http.expectOne('/api/engine/external/eei_a/analyse');
    const l1 = '{"time":10,"depth":27,"nodes":100,"pvs":[{"depth":27,"cp":20,"moves":["e2e4"]}]}';

    req.event({ type: HttpEventType.DownloadProgress, loaded: 1, partialText: l1 + '\n\n\n' } as never);
    expect(seen.length).toBe(1);
    req.event({ type: HttpEventType.DownloadProgress, loaded: 2, partialText: l1 + '\n\n\n\n\n' } as never);
    expect(seen.length).toBe(1);

    req.flush(l1 + '\n\n\n\n\n');
    expect(seen.length).toBe(1);
    expect(completed).toBeTrue();
  });

  it('ignores a half-received line until it is complete', () => {
    const seen: EngineAnalyseLine[] = [];
    svc.analyse('eei_a', WORK).subscribe(l => seen.push(l));
    const req = http.expectOne('/api/engine/external/eei_a/analyse');

    req.event({ type: HttpEventType.DownloadProgress, loaded: 1, partialText: '{"time":10,"depth":8,"nod' } as never);
    expect(seen.length).toBe(0);

    req.event({ type: HttpEventType.DownloadProgress, loaded: 2, partialText: '{"time":10,"depth":8,"nodes":1,"pvs":[]}\n' } as never);
    expect(seen.length).toBe(1);
    req.flush('{"time":10,"depth":8,"nodes":1,"pvs":[]}\n');
  });

  it('completes when the final response arrives (last line without trailing newline)', () => {
    const seen: EngineAnalyseLine[] = [];
    let completed = false;
    svc.analyse('eei_a', WORK).subscribe({ next: l => seen.push(l), complete: () => completed = true });
    const req = http.expectOne('/api/engine/external/eei_a/analyse');

    req.flush('{"time":1,"depth":5,"nodes":10,"pvs":[{"depth":5,"mate":3,"moves":["a1a8"]}]}');
    expect(seen.length).toBe(1);
    expect(seen[0].pvs[0].mate).toBe(3);
    expect(completed).toBeTrue();
  });

  it('encodes the engine id into the URL', () => {
    svc.analyse('eei/../x', WORK).subscribe({ error: () => {} });
    http.expectOne('/api/engine/external/eei%2F..%2Fx/analyse').flush('');
  });
});
