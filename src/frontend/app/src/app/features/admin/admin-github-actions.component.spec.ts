import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminGithubActionsComponent, CiRun, CiRepo } from './admin-github-actions.component';

function makeRun(partial: Partial<CiRun>): CiRun {
  return {
    id: 1, name: 'CI', title: 't', branch: '', event: 'push',
    status: 'completed', conclusion: 'success', runNumber: 1,
    createdAt: '2026-07-01T00:00:00Z', updatedAt: '2026-07-01T00:01:00Z',
    htmlUrl: 'http://x', actor: null, headSha: null, ref: null, isTag: false,
    ...partial,
  };
}

/** rookhub-Repo: Markierung kommt aus dem client-seitigen buildSha/buildRef. */
const rookhub: CiRepo = { repo: 'rookhub', error: null, runs: [] };
/** Fremd-Stack: Markierung kommt aus repo.runningSha/runningRef (vom Server). */
function stack(runningSha?: string | null, runningRef?: string | null): CiRepo {
  return { repo: 'chessresults_crawler', error: null, runs: [], runningSha, runningRef };
}

describe('AdminGithubActionsComponent.isRunningBuild', () => {
  let comp: AdminGithubActionsComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminGithubActionsComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
    });
    comp = TestBed.createComponent(AdminGithubActionsComponent).componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function initWith(body: { sha?: string; ref?: string } | null) {
    comp.ngOnInit();
    httpMock.expectOne('/build-info.json').flush(body);
    // Der Runs-Poll (timer(0,5000)) feuert asynchron; falls er im Test doch schon lief, abfangen.
    httpMock.match('/api/admin/ci/runs').forEach(r => r.flush({ configured: true, repos: [], fetchedAt: '' }));
  }

  it('markiert nur den passenden Ref-Run, wenn der Build seinen Ref meldet (:dev = master)', () => {
    initWith({ sha: 'abc1234def', ref: 'master' });
    expect(comp.isRunningBuild(makeRun({ headSha: 'abc1234def', ref: 'master' }), rookhub)).toBe(true);
    // Gleiche SHA, aber Tag-Run → NICHT markiert (das ist der :prod-Run).
    expect(comp.isRunningBuild(makeRun({ headSha: 'abc1234def', ref: 'v0.234.0', isTag: true }), rookhub)).toBe(false);
  });

  it('markiert bei :prod nur den Tag-Run', () => {
    initWith({ sha: 'abc1234def', ref: 'v0.234.0' });
    expect(comp.isRunningBuild(makeRun({ headSha: 'abc1234def', ref: 'v0.234.0', isTag: true }), rookhub)).toBe(true);
    expect(comp.isRunningBuild(makeRun({ headSha: 'abc1234def', ref: 'master' }), rookhub)).toBe(false);
  });

  it('fällt ohne gemeldeten Ref (altes Image) auf reines SHA-Matching zurück', () => {
    initWith({ sha: 'abc1234def' });
    expect(comp.buildRef).toBeNull();
    expect(comp.isRunningBuild(makeRun({ headSha: 'abc1234def', ref: 'master' }), rookhub)).toBe(true);
    expect(comp.isRunningBuild(makeRun({ headSha: 'abc1234def', ref: 'v0.234.0' }), rookhub)).toBe(true);
  });

  it('markiert nichts ohne build-info oder ohne SHA-Übereinstimmung', () => {
    initWith(null);
    expect(comp.buildSha).toBeNull();
    expect(comp.isRunningBuild(makeRun({ headSha: 'abc1234def', ref: 'master' }), rookhub)).toBe(false);
  });

  it('markiert Fremd-Stacks aus repo.runningSha/runningRef (nicht dem rookhub-Build)', () => {
    initWith({ sha: 'rookhubsha', ref: 'master' });   // rookhub-eigener Build; für Fremd-Stack irrelevant
    // Crawler läuft auf einem eigenen Commit/Ref → markiert bei Übereinstimmung.
    expect(comp.isRunningBuild(makeRun({ headSha: 'crawlersha', ref: 'master' }), stack('crawlersha', 'master'))).toBe(true);
    // Gleiche SHA, anderer Ref (Tag-Run) → nicht markiert.
    expect(comp.isRunningBuild(makeRun({ headSha: 'crawlersha', ref: 'v1.2.3', isTag: true }), stack('crawlersha', 'master'))).toBe(false);
    // Kein runningSha (Stack nicht erreichbar/altes Image) → nichts markiert.
    expect(comp.isRunningBuild(makeRun({ headSha: 'crawlersha', ref: 'master' }), stack(null, null))).toBe(false);
  });
});

/**
 * Die Restzeit-Schätzung. Gemeldet am 2026-09-20: sie fiel zu kurz aus — in derselben Liste stehen
 * der kurze Test-Lauf und der lange Image-Bau, und ein Mittel über beide schätzt für den Bau zu
 * knapp.
 */
describe('AdminGithubActionsComponent ETA', () => {
  let comp: AdminGithubActionsComponent;
  let httpMock: HttpTestingController;
  const t0 = Date.parse('2026-09-20T10:00:00Z');

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminGithubActionsComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideHttpClient(), provideHttpClientTesting()],
    });
    comp = TestBed.createComponent(AdminGithubActionsComponent).componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function done(name: string, seconds: number, over: Partial<CiRun> = {}): CiRun {
    return makeRun({
      name, status: 'completed', conclusion: 'success',
      createdAt: new Date(t0).toISOString(),
      updatedAt: new Date(t0 + seconds * 1000).toISOString(),
      ...over,
    });
  }

  /** Läuft seit `elapsed` Sekunden. */
  function running(name: string, elapsed: number): CiRun {
    return makeRun({ name, status: 'in_progress', conclusion: null,
      createdAt: new Date(t0 - elapsed * 1000).toISOString() });
  }

  function compute(repo: CiRepo): void {
    comp.overview = { configured: true, repos: [repo], fetchedAt: '' };
    (comp as unknown as { nowMs: number }).nowMs = t0;
    (comp as unknown as { recomputeEta(): void }).recomputeEta();
  }

  it('rechnet mit dem WORKFLOW des laufenden Laufs, nicht über alle Läufe gemittelt', () => {
    const repo: CiRepo = { repo: 'rookhub', error: null, runs: [
      running('Build & Push Docker Images', 60),
      done('Build & Push Docker Images', 600),
      done('tests', 120),
      done('tests', 100),
    ] };

    compute(repo);

    // Das alte Mittel über alle vier (~273 s) hätte 213 s Restzeit gemeldet; richtig sind ~540 s.
    expect(comp.running[0].remaining).toBe(540);
    expect(comp.running[0].overdue).toBeFalse();
  });

  it('nimmt die Zahl des Servers zum Workflow, wenn sie mitkommt', () => {
    const repo: CiRepo = {
      repo: 'rookhub', error: null,
      typicalSeconds: { 'Build & Push Docker Images': 900 },
      runs: [running('Build & Push Docker Images', 300), done('Build & Push Docker Images', 400)],
    };

    compute(repo);

    expect(comp.running[0].remaining).toBe(600);
  });

  it('lässt gescheiterte Läufe aus der Schätzung heraus', () => {
    // Ein abgebrochener Lauf endet oft nach Sekunden und zöge die Schätzung nach unten.
    const repo: CiRepo = { repo: 'rookhub', error: null, runs: [
      running('tests', 30),
      done('tests', 5, { conclusion: 'cancelled' }),
      done('tests', 240),
    ] };

    compute(repo);

    expect(comp.running[0].remaining).toBe(210);
  });

  it('sagt „dauert länger als üblich", statt ewig „gleich fertig" zu zeigen', () => {
    const repo: CiRepo = { repo: 'rookhub', error: null, runs: [
      running('tests', 400),
      done('tests', 200),
    ] };

    compute(repo);

    expect(comp.running[0].overdue).toBeTrue();
  });

  it('ohne vergleichbaren Lauf gibt es keine Schätzung', () => {
    const repo: CiRepo = { repo: 'rookhub', error: null, runs: [running('tests', 30), done('andere', 300)] };

    compute(repo);

    expect(comp.running[0].remaining).toBeNull();
  });
});
