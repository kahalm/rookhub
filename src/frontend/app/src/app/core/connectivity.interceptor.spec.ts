import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { connectivityInterceptor } from './connectivity.interceptor';
import { ConnectivityService } from './connectivity.service';

describe('connectivityInterceptor', () => {
  let connectivity: ConnectivityService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    connectivity = TestBed.inject(ConnectivityService);
  });

  function run(req: HttpRequest<unknown>, next: HttpHandlerFn): void {
    TestBed.runInInjectionContext(() =>
      connectivityInterceptor(req, next).subscribe({ next: () => {}, error: () => {} }));
  }

  const okNext: HttpHandlerFn = () => of(new HttpResponse({ status: 200 }) as HttpEvent<unknown>);
  const failNext: HttpHandlerFn = () => throwError(() => new HttpErrorResponse({ status: 0 }));

  it('marks the API unreachable on a status-0 error for /api requests (after the debounce)', fakeAsync(() => {
    run(new HttpRequest('GET', '/api/menu'), failNext);
    tick(2500);   // Banner ist entprellt — erst nach der Karenzzeit sichtbar
    expect(connectivity.problem()).toBe('unreachable');
    connectivity.reportApiSuccess();   // Recheck-Intervall stoppen (fakeAsync-Timer-Hygiene)
  }));

  // PWA/TWA-Fall: mit aktivem ngsw kommt Status 0 NIE an — der SW synthetisiert bei
  // gescheiterten Passthrough-Fetches eine 504-Antwort (ngsw-worker.js). Ohne diesen
  // Trigger bliebe das Verbindungs-Banner in der installierten App für immer stumm.
  it('marks the API unreachable on a service-worker-synthesized 504 for /api requests', fakeAsync(() => {
    run(new HttpRequest('GET', '/api/menu'), () => throwError(() => new HttpErrorResponse({ status: 504 })));
    tick(2500);
    expect(connectivity.problem()).toBe('unreachable');
    connectivity.reportApiSuccess();
  }));

  it('clears the unreachable state on the next successful /api response', () => {
    run(new HttpRequest('GET', '/api/menu'), failNext);
    run(new HttpRequest('GET', '/api/menu'), okNext);
    expect(connectivity.problem()).toBeNull();
  });

  it('ignores non-network errors (e.g. 500)', () => {
    run(new HttpRequest('GET', '/api/menu'), () => throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(connectivity.problem()).toBeNull();
  });

  it('ignores status-0 errors on non-/api URLs (assets/i18n)', () => {
    run(new HttpRequest('GET', '/i18n/en.json'), failNext);
    expect(connectivity.problem()).toBeNull();
  });

  it('propagates the error to the caller', () => {
    let errored = false;
    TestBed.runInInjectionContext(() =>
      connectivityInterceptor(new HttpRequest('GET', '/api/x'), failNext)
        .subscribe({ next: () => {}, error: () => (errored = true) }));
    expect(errored).toBeTrue();
    connectivity.reportApiSuccess();   // schwebenden Debounce-Timer abräumen
  });
});
