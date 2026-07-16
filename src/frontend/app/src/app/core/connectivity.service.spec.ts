import { TestBed } from '@angular/core/testing';
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

  afterEach(() => httpMock.verify());

  it('starts without a problem', () => {
    expect(service.problem()).toBeNull();
  });

  it('reports unreachable after an API failure and clears it on success', () => {
    service.reportApiFailure();
    expect(service.problem()).toBe('unreachable');
    service.reportApiSuccess();
    expect(service.problem()).toBeNull();
  });

  it('reports the outage duration via the recovery hook', () => {
    const events: string[] = [];
    service.reportRecovery = (kind, detail) => events.push(`${kind}:${detail}`);
    service.reportApiFailure();
    service.reportApiSuccess();
    expect(events.length).toBe(1);
    expect(events[0]).toMatch(/^connectivity_restored:api unreachable for \d+s$/);
  });

  it('does not report recovery when there was no failure', () => {
    const events: string[] = [];
    service.reportRecovery = kind => events.push(kind);
    service.reportApiSuccess();
    expect(events.length).toBe(0);
  });

  it('checkNow pings /api/menu', () => {
    service.checkNow();
    const req = httpMock.expectOne('/api/menu');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('checkNow swallows ping errors (state stays unreachable)', () => {
    service.reportApiFailure();
    service.checkNow();
    httpMock.expectOne('/api/menu').error(new ProgressEvent('error'));
    expect(service.problem()).toBe('unreachable');
  });
});
