import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpRequest } from '@angular/common/http';
import { fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { retryInterceptor } from './retry.interceptor';

describe('retryInterceptor', () => {
  // next gibt fuer jeden Aufruf den naechsten Status zurueck (200 = Erfolg).
  function makeNext(statuses: number[]) {
    let calls = 0;
    const next: HttpHandlerFn = () => {
      const status = statuses[Math.min(calls, statuses.length - 1)];
      calls++;
      return status === 200
        ? of({} as HttpEvent<unknown>)
        : throwError(() => new HttpErrorResponse({ status }));
    };
    return { next, getCalls: () => calls };
  }

  it('retries an idempotent GET on 503 and then succeeds', fakeAsync(() => {
    const { next, getCalls } = makeNext([503, 200]);
    const req = new HttpRequest('GET', '/api/x');
    let succeeded = false;
    retryInterceptor(req as any, next).subscribe({ next: () => (succeeded = true), error: () => {} });
    tick(500); // erster Backoff = 500 ms
    expect(getCalls()).toBe(2);
    expect(succeeded).toBeTrue();
  }));

  it('retries up to 3 times with exponential backoff, then gives up', fakeAsync(() => {
    const { next, getCalls } = makeNext([503]); // immer 503
    const req = new HttpRequest('GET', '/api/x');
    let errored = false;
    retryInterceptor(req as any, next).subscribe({ next: () => {}, error: () => (errored = true) });

    // Erstversuch sofort.
    expect(getCalls()).toBe(1);
    tick(500);  // 1. Retry
    expect(getCalls()).toBe(2);
    tick(1000); // 2. Retry
    expect(getCalls()).toBe(3);
    tick(2000); // 3. Retry
    expect(getCalls()).toBe(4);
    tick(4000); // kein weiterer Retry mehr
    expect(getCalls()).toBe(4);
    expect(errored).toBeTrue();
  }));

  it('recovers on the third attempt', fakeAsync(() => {
    const { next, getCalls } = makeNext([503, 503, 200]);
    const req = new HttpRequest('GET', '/api/x');
    let succeeded = false;
    retryInterceptor(req as any, next).subscribe({ next: () => (succeeded = true), error: () => {} });
    tick(500);
    tick(1000);
    expect(getCalls()).toBe(3);
    expect(succeeded).toBeTrue();
  }));

  it('does NOT retry a non-idempotent POST on 503 (no duplicate side effect)', fakeAsync(() => {
    const { next, getCalls } = makeNext([503, 200]);
    const req = new HttpRequest('POST', '/api/x', {});
    let errored = false;
    retryInterceptor(req as any, next).subscribe({ next: () => {}, error: () => (errored = true) });
    tick(1000);
    expect(getCalls()).toBe(1);
    expect(errored).toBeTrue();
  }));
});
