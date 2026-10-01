import { EnvironmentProviders } from '@angular/core';
import { HttpInterceptorFn, provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './auth.interceptor';
import { connectivityInterceptor } from './connectivity.interceptor';
import { renderAfterHttpInterceptor } from './render-after-http.interceptor';
import { retryInterceptor } from './retry.interceptor';

/**
 * Die HTTP-Kette ALLER Oberflaechen in einer Reihenfolge (Codereview F8-014/F1-020):
 * connectivity zuerst (aeusserster — sieht Erfolge/finale Fehler NACH den Retries), dann retry,
 * dann die oberflaecheneigenen (`extra`, RookHub: visitorInterceptor), dann auth,
 * renderAfterHttp zuletzt.
 */
export function rhInterceptors(extra: readonly HttpInterceptorFn[] = []): HttpInterceptorFn[] {
  return [connectivityInterceptor, retryInterceptor, ...extra, authInterceptor, renderAfterHttpInterceptor];
}

/** `provideHttpClient` mit der Kette aus {@link rhInterceptors}. */
export function provideRhHttpClient(extra: readonly HttpInterceptorFn[] = []): EnvironmentProviders {
  return provideHttpClient(withInterceptors(rhInterceptors(extra)));
}
