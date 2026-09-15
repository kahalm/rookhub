import { TestBed, fakeAsync, flush, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ConnectivityService, MIN_VISIBLE_MS, SHOW_DELAY_MS } from './connectivity.service';

describe('ConnectivityService', () => {
  let service: ConnectivityService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(ConnectivityService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // Debounce-/Recheck-Timer der Tests sind je Test via tick()/flush() abgelaufen oder abgebrochen;
    // offene Requests dürfen keine bleiben.
    window.dispatchEvent(new Event('online'));
    httpMock.verify();
  });

  /** Die Gegenprobe (Ping auf /api/menu) eines reportApiFailure scheitern lassen. */
  function failProbe(): void {
    httpMock.expectOne('/api/menu').error(new ProgressEvent('error'));
  }

  /** Den Zustand bis zum sichtbaren Banner treiben (Frist abgelaufen + Gegenprobe gescheitert). */
  function showUnreachable(): void {
    service.reportApiFailure();
    failProbe();
    tick(SHOW_DELAY_MS);
    expect(service.problem()).toBe('unreachable');
  }

  it('starts without a problem', () => {
    expect(service.problem()).toBeNull();
  });

  it('shows unreachable only after the delay and hides after the minimum visible time', fakeAsync(() => {
    service.reportApiFailure();
    failProbe();
    expect(service.problem()).toBeNull();        // entprellt — noch kein Banner
    tick(SHOW_DELAY_MS - 1);
    expect(service.problem()).toBeNull();
    tick(1);
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
    expect(service.problem()).toBe('unreachable');   // steht mindestens MIN_VISIBLE_MS
    tick(MIN_VISIBLE_MS);
    expect(service.problem()).toBeNull();
    tick(30000);                                  // gestoppter Recheck darf nicht mehr pingen
  }));

  it('a probe slower than the delay does NOT flash the banner when it succeeds', fakeAsync(() => {
    service.reportApiFailure();
    const probe = httpMock.expectOne('/api/menu');   // Gegenprobe noch unterwegs …
    tick(SHOW_DELAY_MS + 3000);
    expect(service.problem()).toBeNull();            // … die Frist allein zeigt nichts
    probe.flush([]);
    service.reportApiSuccess();                      // Interceptor meldet den späten Erfolg
    tick(MIN_VISIBLE_MS);
    expect(service.problem()).toBeNull();
  }));

  it('a probe that fails after the delay shows the banner at that moment', fakeAsync(() => {
    service.reportApiFailure();
    tick(SHOW_DELAY_MS + 2000);
    expect(service.problem()).toBeNull();
    failProbe();
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
    flush();
  }));

  it('a transient blip (success within the delay) never shows the banner nor logs recovery', fakeAsync(() => {
    const events: string[] = [];
    service.reportRecovery = kind => events.push(kind);
    service.reportApiFailure();
    httpMock.expectOne('/api/menu').flush([]);   // Gegenprobe gelingt …
    service.reportApiSuccess();                   // … Interceptor meldet den Erfolg
    tick(SHOW_DELAY_MS);
    expect(service.problem()).toBeNull();
    expect(events.length).toBe(0);
  }));

  it('a new failure while the hide is scheduled keeps the banner up', fakeAsync(() => {
    showUnreachable();
    service.reportApiSuccess();
    tick(MIN_VISIBLE_MS / 2);
    service.reportApiFailure();                   // wieder weg, bevor der Banner verschwand
    tick(MIN_VISIBLE_MS);
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
    flush();
    expect(service.problem()).toBeNull();
  }));

  it('repeated failures while pending do not stack timers or probes', fakeAsync(() => {
    service.reportApiFailure();
    service.reportApiFailure();
    service.reportApiFailure();
    failProbe();                                  // nur EINE Gegenprobe
    httpMock.expectNone('/api/menu');
    tick(SHOW_DELAY_MS);
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
    flush();
  }));

  it('reports the outage duration via the recovery hook once the banner was shown', fakeAsync(() => {
    const events: string[] = [];
    service.reportRecovery = (kind, detail) => events.push(`${kind}:${detail}`);
    showUnreachable();
    service.reportApiSuccess();
    tick(MIN_VISIBLE_MS);
    expect(events.length).toBe(1);
    expect(events[0]).toMatch(/^connectivity_restored:api unreachable for \d+s$/);
  }));

  it('does not report recovery when there was no failure', () => {
    const events: string[] = [];
    service.reportRecovery = kind => events.push(kind);
    service.reportApiSuccess();
    expect(events.length).toBe(0);
  });

  it('the offline banner is debounced too and stays for the minimum visible time', fakeAsync(() => {
    window.dispatchEvent(new Event('offline'));
    tick(SHOW_DELAY_MS - 1);
    expect(service.problem()).toBeNull();        // entprellt
    tick(1);
    expect(service.problem()).toBe('offline');
    window.dispatchEvent(new Event('online'));
    expect(service.problem()).toBe('offline');   // nicht sofort weg
    tick(MIN_VISIBLE_MS);
    expect(service.problem()).toBeNull();
  }));

  it('the offline banner disappears at once after it was visible long enough', fakeAsync(() => {
    window.dispatchEvent(new Event('offline'));
    tick(SHOW_DELAY_MS + MIN_VISIBLE_MS);
    window.dispatchEvent(new Event('online'));
    expect(service.problem()).toBeNull();
  }));

  it('a short offline blip does not show the banner', fakeAsync(() => {
    window.dispatchEvent(new Event('offline'));
    tick(SHOW_DELAY_MS - 1000);
    window.dispatchEvent(new Event('online'));   // Blip vorbei, bevor die Frist abläuft
    tick(SHOW_DELAY_MS);
    expect(service.problem()).toBeNull();
  }));

  it('checkNow pings /api/menu', () => {
    service.checkNow();
    const req = httpMock.expectOne('/api/menu');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('checkNow swallows ping errors (state stays unreachable)', fakeAsync(() => {
    showUnreachable();
    service.checkNow();
    httpMock.expectOne('/api/menu').error(new ProgressEvent('error'));
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
    flush();
  }));
});
