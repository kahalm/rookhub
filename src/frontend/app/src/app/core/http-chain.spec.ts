import { HttpInterceptorFn } from '@angular/common/http';
import { authInterceptor } from './auth.interceptor';
import { connectivityInterceptor } from './connectivity.interceptor';
import { rhInterceptors } from './http-chain';
import { renderAfterHttpInterceptor } from './render-after-http.interceptor';
import { retryInterceptor } from './retry.interceptor';

/** Die Reihenfolge der HTTP-Kette steht fuer alle Oberflaechen an EINER Stelle (Codereview F8-014/F1-020). */
describe('rhInterceptors', () => {
  it('connectivity aussen, retry, auth, renderAfterHttp zuletzt', () => {
    expect(rhInterceptors()).toEqual([connectivityInterceptor, retryInterceptor, authInterceptor, renderAfterHttpInterceptor]);
  });

  it('setzt oberflaecheneigene Interceptoren zwischen retry und auth', () => {
    const extra: HttpInterceptorFn = (req, next) => next(req);
    expect(rhInterceptors([extra])).toEqual([connectivityInterceptor, retryInterceptor, extra, authInterceptor, renderAfterHttpInterceptor]);
  });
});
