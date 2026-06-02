import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { of, catchError } from 'rxjs';

/**
 * Meldet client-seitige Diagnose-Events (v. a. Browser-Engine-Crashes/Hänger) an
 * `POST /api/client-log` → die API loggt sie strukturiert nach Elasticsearch/Kibana.
 * Fire-and-forget; pro Event-Art gedrosselt, damit Crash-Stürme das Log nicht fluten.
 */
@Injectable({ providedIn: 'root' })
export class ClientLogService {
  private static readonly THROTTLE_MS = 10000;
  private lastSent = new Map<string, number>();

  constructor(private http: HttpClient) {}

  report(kind: string, detail?: string): void {
    if (!kind) return;
    const now = Date.now();
    const last = this.lastSent.get(kind) ?? 0;
    if (now - last < ClientLogService.THROTTLE_MS) return;   // Drosselung pro Art
    this.lastSent.set(kind, now);

    const url = typeof location !== 'undefined' ? location.pathname : undefined;
    this.http.post('/api/client-log', { kind, detail: detail ?? null, url })
      .pipe(catchError(() => of(null)))
      .subscribe();
  }
}
