import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
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

  it('marks the API unreachable on a status-0 error for /api requests', () => {
    run(new HttpRequest('GET', '/api/menu'), failNext);
    expect(connectivity.problem()).toBe('unreachable');
  });

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
  });
});
