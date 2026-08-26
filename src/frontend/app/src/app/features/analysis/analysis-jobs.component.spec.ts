import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AnalysisJobsComponent } from './analysis-jobs.component';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

describe('AnalysisJobsComponent', () => {
  async function make() {
    await TestBed.configureTestingModule({
      imports: [AnalysisJobsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(AnalysisJobsComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }

  const job = (id: number, extra: object = {}) => ({
    id, fen: START, title: null, engineId: 'eei_bg', targetDepth: 30, multiPv: 2, status: 'paused', reachedDepth: 18,
    resultJson: '{"time":5,"depth":18,"nodes":100,"pvs":[{"depth":18,"cp":25,"moves":["e2e4","e7e5"]},{"depth":18,"cp":10,"moves":["d2d4"]}]}',
    secondsSpent: 125, lastError: null, createdAt: '2026-08-26T10:00:00Z', updatedAt: '2026-08-26T10:02:00Z', lastRunAt: null, finishedAt: null, ...extra,
  });

  it('lists jobs with status, depth and the main-line evaluation', async () => {
    const { fixture, c, http } = await make();
    http.expectOne('/api/analysis-jobs').flush([job(1), job(2, { status: 'done', resultJson: null })]);
    fixture.detectChanges();

    expect(c.jobs.length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('analysisJobs.status.paused');
    expect(text).toContain('+0.25');          // Bewertung der Hauptvariante aus der gespeicherten Zeile
    expect(text).toContain('2:05');           // 125 s Rechenzeit
    expect(c.evalOf(c.jobs[1])).toBeNull();   // ohne Ergebnis keine Bewertung
  });

  it('expanding shows the stored lines as SAN and saving sends the new target', async () => {
    const { fixture, c, http } = await make();
    http.expectOne('/api/analysis-jobs').flush([job(1)]);
    fixture.detectChanges();

    c.toggle(c.jobs[0]);
    fixture.detectChanges();
    expect(c.expandedId).toBe(1);
    expect(c.linesOf(c.jobs[0]).map(l => l.san)).toEqual(['1. e4 e5', '1. d4']);
    expect(fixture.nativeElement.textContent).toContain('1. e4 e5');

    c.editDepth = 40;
    c.save(c.jobs[0]);
    const put = http.expectOne('/api/analysis-jobs/1');
    expect(put.request.body).toEqual({ targetDepth: 40, multiPv: 2 });
    put.flush(job(1, { targetDepth: 40, status: 'queued' }));
    expect(c.jobs[0].targetDepth).toBe(40);
  });
});
