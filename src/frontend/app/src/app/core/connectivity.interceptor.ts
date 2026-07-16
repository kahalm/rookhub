import { HttpErrorResponse, HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { ConnectivityService } from './connectivity.service';

/**
 * Speist den ConnectivityService: Status 0 auf /api-Requests bei laut Browser
 * vorhandener Netzverbindung = Server auf Netzwerkebene unerreichbar („Failed to
 * fetch" — z. B. DNS-Blockade durch VPN/Threat-Protection, hängender Tunnel).
 * Jeder erfolgreiche /api-Response hebt den Zustand wieder auf.
 *
 * Als ÄUSSERSTER Interceptor registriert, damit er den finalen Fehler NACH den
 * Retries des retryInterceptor sieht (kein Banner-Geflacker bei einem einzelnen
 * transienten Fehlversuch). Nur /api-URLs — Asset-/i18n-Requests bedient der
 * Service Worker auch offline aus dem Cache.
 */
export const connectivityInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api')) return next(req);
  const connectivity = inject(ConnectivityService);
  return next(req).pipe(
    tap(event => { if (event.type === HttpEventType.Response) connectivity.reportApiSuccess(); }),
    catchError(err => {
      const deviceOnline = typeof navigator === 'undefined' || navigator.onLine;
      // Status 0 = „Failed to fetch" (kein Service Worker dazwischen). Mit AKTIVEM ngsw kommt
      // Status 0 nie an: der SW wandelt gescheiterte Passthrough-Fetches in synthetische
      // 504-„Gateway Timeout"-Antworten um (ngsw-worker.js) — in der PWA/TWA ist 504 also
      // das Netzfehler-Signal, sonst bliebe das Banner dort für immer stumm.
      const networkLevelFailure = err instanceof HttpErrorResponse && (err.status === 0 || err.status === 504);
      if (networkLevelFailure && deviceOnline) connectivity.reportApiFailure();
      return throwError(() => err);
    })
  );
};
