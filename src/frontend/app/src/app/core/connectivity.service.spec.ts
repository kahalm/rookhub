import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ConnectivityService } from './connectivity.service';

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
    // Debounce-/Recheck-Timer der Tests sind je Test via tick() abgelaufen oder abgebrochen;
    // offene Requests dürfen keine bleiben.
    window.dispatchEvent(new Event('online'));
    httpMock.verify();
  });

  /** Die sofortige Gegenprobe (Ping auf /api/menu) eines reportApiFailure scheitern lassen. */
  function failProbe(): void {
    httpMock.expectOne('/api/menu').error(new ProgressEvent('error'));
  }

  it('starts without a problem', () => {
    expect(service.problem()).toBeNull();
  });

  it('shows unreachable only after the debounce delay, recovery hides immediately', fakeAsync(() => {
    service.reportApiFailure();
    failProbe();
    expect(service.problem()).toBeNull();       // entprellt — noch kein Banner
    tick(2500);
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
    expect(service.problem()).toBeNull();       // Erholung sofort
    tick(30000);                                 // gestoppter Recheck darf nicht mehr pingen
  }));

  it('a transient blip (success within the delay) never shows the banner nor logs recovery', fakeAsync(() => {
    const events: string[] = [];
    service.reportRecovery = kind => events.push(kind);
    service.reportApiFailure();
    httpMock.expectOne('/api/menu').flush([]);   // Gegenprobe gelingt …
    service.reportApiSuccess();                   // … Interceptor meldet den Erfolg
    tick(2500);
    expect(service.problem()).toBeNull();
    expect(events.length).toBe(0);
  }));

  it('repeated failures while pending do not stack timers or probes', fakeAsync(() => {
    service.reportApiFailure();
    service.reportApiFailure();
    service.reportApiFailure();
    failProbe();                                  // nur EINE Gegenprobe
    httpMock.expectNone('/api/menu');
    tick(2500);
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
  }));

  it('reports the outage duration via the recovery hook once the banner was shown', fakeAsync(() => {
    const events: string[] = [];
    service.reportRecovery = (kind, detail) => events.push(`${kind}:${detail}`);
    service.reportApiFailure();
    failProbe();
    tick(2500);
    service.reportApiSuccess();
    expect(events.length).toBe(1);
    expect(events[0]).toMatch(/^connectivity_restored:api unreachable for \d+s$/);
  }));

  it('does not report recovery when there was no failure', () => {
    const events: string[] = [];
    service.reportRecovery = kind => events.push(kind);
    service.reportApiSuccess();
    expect(events.length).toBe(0);
  });

  it('the offline banner is debounced too; going online hides it immediately', fakeAsync(() => {
    window.dispatchEvent(new Event('offline'));
    expect(service.problem()).toBeNull();        // entprellt
    tick(2500);
    expect(service.problem()).toBe('offline');
    window.dispatchEvent(new Event('online'));
    expect(service.problem()).toBeNull();
  }));

  it('a short offline blip does not show the banner', fakeAsync(() => {
    window.dispatchEvent(new Event('offline'));
    tick(1000);
    window.dispatchEvent(new Event('online'));   // Blip vorbei, bevor der Timer feuert
    tick(2500);
    expect(service.problem()).toBeNull();
  }));

  it('checkNow pings /api/menu', () => {
    service.checkNow();
    const req = httpMock.expectOne('/api/menu');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('checkNow swallows ping errors (state stays unreachable)', fakeAsync(() => {
    service.reportApiFailure();
    failProbe();
    tick(2500);
    service.checkNow();
    httpMock.expectOne('/api/menu').error(new ProgressEvent('error'));
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
  }));
});
