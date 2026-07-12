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
