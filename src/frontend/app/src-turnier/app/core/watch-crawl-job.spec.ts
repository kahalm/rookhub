import { fakeAsync, tick } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { CrawlJob } from '@rh/core/models';
import { CrawlWatch, watchCrawlJob } from './watch-crawl-job';

/** Codereview F6-012: Crawl-Aufträge werden mit Deckel verfolgt, nicht endlos alle 2 s. */
describe('watchCrawlJob', () => {
  function run(responses: (CrawlJob['status'] | 'error')[], opts: { maxMs: number; firstMs?: number; everyMs?: number }) {
    let calls = 0;
    const fetch = (id: number): Observable<CrawlJob> => {
      const r = responses[Math.min(calls++, responses.length - 1)];
      return r === 'error' ? throwError(() => new Error('x')) : of({ id, status: r });
    };
    const seen: CrawlWatch[] = [];
    let done = false;
    watchCrawlJob(fetch, 7, opts).subscribe({ next: w => seen.push(w), complete: () => { done = true; } });
    return { seen, isDone: () => done, calls: () => calls };
  }

  it('fragt nach firstMs, dann alle everyMs, bis der Auftrag fertig ist', fakeAsync(() => {
    const r = run(['Running', 'Running', 'Completed'], { maxMs: 60_000, firstMs: 2000, everyMs: 2000 });
    tick(1999);
    expect(r.calls()).toBe(0);
    tick(1);
    expect(r.seen.map(w => w.state)).toEqual(['running']);
    tick(4000);
    expect(r.seen.map(w => w.state)).toEqual(['running', 'running', 'completed']);
    expect(r.isDone()).toBeTrue();
  }));

  it('meldet einen gescheiterten Auftrag mit dem Auftrag', fakeAsync(() => {
    const r = run(['Failed'], { maxMs: 60_000 });
    tick(2000);
    expect(r.seen).toEqual([{ state: 'failed', job: { id: 7, status: 'Failed' } }]);
    expect(r.isDone()).toBeTrue();
  }));

  it('ein Fehler beim Nachfragen beendet das Verfolgen als „lost"', fakeAsync(() => {
    const r = run(['Running', 'error'], { maxMs: 60_000 });
    tick(4000);
    expect(r.seen.map(w => w.state)).toEqual(['running', 'lost']);
    expect(r.isDone()).toBeTrue();
  }));

  it('hört am Deckel auf und meldet „timeout"', fakeAsync(() => {
    const r = run(['Running'], { maxMs: 10_000, firstMs: 2000, everyMs: 2000 });
    tick(60_000);
    // 2, 4, 6, 8, 10 s → fünf Abfragen, die letzte meldet den Deckel.
    expect(r.calls()).toBe(5);
    expect(r.seen.map(w => w.state)).toEqual(['running', 'running', 'running', 'running', 'timeout']);
    expect(r.isDone()).toBeTrue();
  }));
});
