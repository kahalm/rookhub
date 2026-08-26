import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
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
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();

    expect(c.jobs.length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('analysisJobs.status.paused');
    expect(text).toContain('+0.25');          // Bewertung der Hauptvariante aus der gespeicherten Zeile
    expect(text).toContain('2:05');           // 125 s Rechenzeit
    expect(c.evalOf(c.jobs[1])).toBeNull();   // ohne Ergebnis keine Bewertung
  });

  it('shows speed in kN/s, offers engine switch + restart, and hands engine/params to the board', async () => {
    const { fixture, c, http } = await make();
    http.expectOne('/api/analysis-jobs').flush([job(1, { status: 'running' })]);
    http.expectOne('/api/engine/external').flush({
      hasCredentials: true, tokenInvalid: false, backgroundEngineId: 'eei_bg',
      engines: [{ id: 'eei_bg', name: 'Hintergrund', maxThreads: 12, maxHash: 8192 },
                { id: 'eei_live', name: 'Live', maxThreads: 12, maxHash: 4096 }],
    });
    fixture.detectChanges();

    // 100 Knoten in 5 ms → 20.000 N/s → 20 kN/s
    expect(c.speedOf(c.jobs[0])).toBe('20 kN/s');
    expect(c.nodesOf(c.jobs[0])).toBe('0 kN');
    expect(fixture.nativeElement.textContent).toContain('20 kN/s');

    c.toggle(c.jobs[0]);
    fixture.detectChanges();
    expect(c.editEngineId).toBe('eei_bg');
    expect(c.dirty(c.jobs[0])).toBeFalse();
    c.editEngineId = 'eei_live';
    expect(c.dirty(c.jobs[0])).toBeTrue();
    c.save(c.jobs[0]);
    const put = http.expectOne('/api/analysis-jobs/1');
    expect(put.request.body).toEqual({ targetDepth: 30, multiPv: 2, engineId: 'eei_live' });
    put.flush(job(1, { engineId: 'eei_live', status: 'queued' }));

    c.restart(c.jobs[0]);
    const rst = http.expectOne('/api/analysis-jobs/1/restart');
    expect(rst.request.method).toBe('POST');
    rst.flush(job(1, { status: 'queued' }));

    const nav = spyOn((c as any).router, 'navigate');
    c.openInBoard(c.jobs[0]);
    expect(nav).toHaveBeenCalledWith(['/analysis'],
      { queryParams: { fen: START, engine: 'eei_bg', depth: 30, lines: 2 } });
  });

  it('while running it shows the live depth and the live speed, not the stored one', async () => {
    const { fixture, c, http } = await make();
    // Fortsetzung: Ergebnis von Tiefe 18, die Engine rechnet gerade erst wieder bei 7 hoch.
    http.expectOne('/api/analysis-jobs').flush([job(1, { status: 'running', currentDepth: 7, currentNps: 5_400_000 })]);
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('analysisJobs.runningAt');       // „rechnet bei 7"
    expect(c.speedOf(c.jobs[0])).toBe('5.400 kN/s');        // laufender Wert, NICHT die 20 kN/s des Ergebnisses

    // Pausiert der Auftrag, zählt wieder das gespeicherte Ergebnis.
    c.jobs = [{ ...c.jobs[0], status: 'paused', currentDepth: 0, currentNps: 0 } as any];
    expect(c.speedOf(c.jobs[0])).toBe('20 kN/s');
  });

  it('polls the live values every second and keeps the clock ticking between two answers', fakeAsync(() => {
    TestBed.configureTestingModule({
      imports: [AnalysisJobsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(AnalysisJobsComponent);
    const c = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/analysis-jobs').flush([job(1, { status: 'running', currentDepth: 7, currentNps: 5_400_000 })]);
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();

    tick(1000);
    http.expectOne('/api/analysis-jobs/live').flush([{ id: 1, depth: 21, nps: 4_300_000, seconds: 200 }]);
    fixture.detectChanges();
    expect(c.depthNowOf(c.jobs[0])).toBe(21);                 // laufender Stand schlägt den der Liste
    expect(c.speedOf(c.jobs[0])).toBe('4.300 kN/s');
    expect(fixture.nativeElement.textContent).toContain('3:20');

    // Nächste Sekunde: die Uhr läuft schon weiter, BEVOR die Antwort da ist
    tick(1000);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('3:21');
    http.expectOne('/api/analysis-jobs/live').flush([]);       // Lauf zu Ende → zurück auf den gespeicherten Stand
    fixture.detectChanges();
    expect(c.elapsedOf(c.jobs[0])).toBe('2:05');
    expect(c.depthNowOf(c.jobs[0])).toBe(7);

    discardPeriodicTasks();
  }));

  it('expanding shows the stored lines as SAN and saving sends the new target', async () => {
    const { fixture, c, http } = await make();
    http.expectOne('/api/analysis-jobs').flush([job(1)]);
    http.expectOne('/api/engine/external').flush({ hasCredentials: false, tokenInvalid: false, engines: [] });
    fixture.detectChanges();

    c.toggle(c.jobs[0]);
    fixture.detectChanges();
    expect(c.expandedId).toBe(1);
    expect(c.linesOf(c.jobs[0]).map(l => l.san)).toEqual(['1. e4 e5', '1. d4']);
    expect(fixture.nativeElement.textContent).toContain('1. e4 e5');

    c.editDepth = 40;
    c.save(c.jobs[0]);
    const put = http.expectOne('/api/analysis-jobs/1');
    expect(put.request.body).toEqual({ targetDepth: 40, multiPv: 2, engineId: 'eei_bg' });
    put.flush(job(1, { targetDepth: 40, status: 'queued' }));
    expect(c.jobs[0].targetDepth).toBe(40);
  });
});
