import { HttpInterceptorFn } from '@angular/common/http';
import { catchError, switchMap, throwError, timer } from 'rxjs';

/** Maximale Anzahl automatischer Wiederholungen (zusätzlich zum Erstversuch). */
const MAX_RETRIES = 3;
/** Basis-Verzögerung; effektiv exponentiell: 500 ms, 1 s, 2 s. */
const BASE_DELAY_MS = 500;

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError(err => {
      // 504 gehört dazu: mit aktivem Service Worker kommt ein Netzfehler NIE als Status 0 an —
      // der ngsw wandelt gescheiterte Passthrough-Fetches in synthetische 504-Antworten um (der
      // connectivityInterceptor behandelt sie deshalb schon als Netzfehler). Ohne 504 griff der
      // Retry genau in der installierten App nicht, für die er gedacht ist.
      const retryable = err.status === 502 || err.status === 503 || err.status === 504 || err.status === 0;
      // Nur idempotente Methoden erneut versuchen — ein Retry von POST/PUT/DELETE
      // kann doppelte Seiteneffekte ausloesen (z.B. doppelte Puzzle-Attempts).
      const idempotent = req.method === 'GET' || req.method === 'HEAD';
      // Bisherige Versuchszahl steckt im X-Retry-Header (0 = Erstversuch).
      const attempt = Number(req.headers.get('X-Retry') ?? '0');

      if (retryable && idempotent && attempt < MAX_RETRIES) {
        // Exponential-Backoff: 500 ms · 2^attempt → 500 ms, 1 s, 2 s.
        const delay = BASE_DELAY_MS * Math.pow(2, attempt);
        const retryReq = req.clone({ setHeaders: { 'X-Retry': String(attempt + 1) } });
        return timer(delay).pipe(switchMap(() => retryInterceptor(retryReq, next)));
      }
      return throwError(() => err);
    })
  );
};
