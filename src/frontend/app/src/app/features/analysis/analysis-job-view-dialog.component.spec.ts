import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { AnalysisJobViewDialogComponent } from './analysis-job-view-dialog.component';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('AnalysisJobViewDialogComponent', () => {
  function make() {
    TestBed.configureTestingModule({
      imports: [AnalysisJobViewDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { jobId: 5, fen: START, title: 'Kritisch' } }],
    });
    const fixture = TestBed.createComponent(AnalysisJobViewDialogComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  const job = (extra: object = {}) => ({
    id: 5, fen: START, title: null, engineId: 'eei_bg', targetDepth: 30, multiPv: 2, status: 'running', reachedDepth: 18,
    resultJson: '{"time":5,"depth":18,"nodes":100,"pvs":[{"depth":18,"cp":25,"moves":["e2e4","e7e5"]},{"depth":18,"cp":10,"moves":["d2d4"]}]}',
    secondsSpent: 125, lastError: null, createdAt: '2026-09-29T10:00:00Z', updatedAt: '2026-09-29T10:02:00Z',
    lastRunAt: null, finishedAt: null, ...extra,
  });

  it('shows board arrows, lines and the live state of a running job — and never touches an engine', fakeAsync(() => {
    const { fixture, c, http } = make();
    http.expectOne('/api/analysis-jobs/5').flush(job());
    fixture.detectChanges();

    expect(c.arrows()).toEqual([{ from: 'e2', to: 'e4', brush: 'green' }, { from: 'd2', to: 'd4', brush: 'paleBlue' }]);
    let text = fixture.nativeElement.textContent as string;
    expect(text).toContain('analysisJobs.status.running');
    expect(text).toContain('+0.25');
    expect(text).toContain('Kritisch');
    expect(text).toContain('analysisJobs.view.keepsRunning');

    // Sekundentakt: nur der laufende Stand
    tick(1_000);
    http.expectOne('/api/analysis-jobs/live').flush([{ id: 9, depth: 3, nps: 1, seconds: 1 }, { id: 5, depth: 22, nps: 1_500_000, seconds: 130 }]);
    fixture.detectChanges();
    text = fixture.nativeElement.textContent as string;
    expect(c.depthNow()).toBe(22);
    expect(text).toContain('analysisJobs.runningAt');
    expect(text).toContain('2:10');
    expect(c.speed()).toBe('1.500 kN/s');

    // Die Uhr läuft zwischen zwei Antworten lokal weiter; alle 5 s kommt der ganze Auftrag
    for (let i = 0; i < 4; i++) {
      tick(1_000);
      http.match('/api/analysis-jobs/live').forEach(r => r.flush([]));
    }
    http.expectOne('/api/analysis-jobs/5').flush(job({ status: 'done', reachedDepth: 30 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('analysisJobs.view.done');

    // Fertig: der Takt ruht — keine weiteren Anfragen, und nie eine an die Engine
    tick(10_000);
    http.expectNone(r => r.url.startsWith('/api/engine'));
    http.verify();
    discardPeriodicTasks();
  }));

  it('a finished job shows its final lines without polling', fakeAsync(() => {
    const { fixture, c, http } = make();
    http.expectOne('/api/analysis-jobs/5').flush(job({ status: 'done', reachedDepth: 30, secondsSpent: 61 }));
    fixture.detectChanges();

    expect(c.lines().length).toBe(2);
    expect(c.elapsed()).toBe('1:01');
    // 100 Knoten in 5 ms → 20 kN/s aus dem gespeicherten Ergebnis
    expect(c.speed()).toBe('20 kN/s');
    expect(fixture.nativeElement.textContent).not.toContain('analysisJobs.view.keepsRunning');
    tick(12_000);
    http.verify();
    discardPeriodicTasks();
  }));

  it('says so when the job is gone', fakeAsync(() => {
    const { fixture, c, http } = make();
    http.expectOne('/api/analysis-jobs/5').flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(c.notFound()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('analysisJobs.view.notFound');
    tick(10_000);
    http.verify();
    discardPeriodicTasks();
  }));
});
