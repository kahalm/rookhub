import { Observable, catchError, map, of, switchMap, take, takeWhile, timer } from 'rxjs';
import { CrawlJob } from '@rh/core/models';

/**
 * Stand eines verfolgten Crawl-Auftrags: `running` (weiter warten), `completed`/`failed` (fertig),
 * `lost` (Nachfragen gescheitert) oder `timeout` (Deckel erreicht — der Auftrag läuft serverseitig weiter,
 * hier wird nur nicht länger gewartet).
 */
export type CrawlWatch =
  | { state: 'running' | 'completed' | 'failed'; job: CrawlJob }
  | { state: 'lost' | 'timeout' };

export interface CrawlWatchOptions {
  /** Höchstens so lange (ab Start) nachfragen; die letzte Abfrage liegt spätestens hier. */
  maxMs: number;
  /** Erste Abfrage nach … ms (Vorgabe 2 s). */
  firstMs?: number;
  /** Danach alle … ms (Vorgabe 2 s). */
  everyMs?: number;
}

/**
 * Einen Crawl-Auftrag verfolgen — gemeinsam für Turnierseite („Aktualisieren") und Verzeichnis-Detail („Holen").
 * Vorher pollte die Turnierseite per setInterval alle 2 s ohne Obergrenze (Codereview F6-012). Endet nach dem
 * ersten Endstand (`completed`/`failed`/`lost`) oder am Deckel mit `timeout`.
 */
export function watchCrawlJob(
  fetch: (jobId: number) => Observable<CrawlJob>,
  jobId: number,
  { maxMs, firstMs = 2000, everyMs = 2000 }: CrawlWatchOptions,
): Observable<CrawlWatch> {
  const polls = Math.max(1, Math.floor((maxMs - firstMs) / everyMs) + 1);
  return timer(firstMs, everyMs).pipe(
    take(polls),
    switchMap(i => fetch(jobId).pipe(
      map((job): CrawlWatch => job.status === 'Completed' ? { state: 'completed', job }
        : job.status === 'Failed' ? { state: 'failed', job }
        : i >= polls - 1 ? { state: 'timeout' } : { state: 'running', job }),
      catchError(() => of<CrawlWatch>({ state: 'lost' })),
    )),
    takeWhile(w => w.state === 'running', true),
  );
}
