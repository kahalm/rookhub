import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth.service';
import { ClubSummaryLinkComponent } from './club-summary-link.component';

describe('ClubSummaryLinkComponent (0.913.0)', () => {
  function setup(isAdmin: boolean) {
    TestBed.configureTestingModule({
      imports: [ClubSummaryLinkComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin } }],
    });
    const f = TestBed.createComponent(ClubSummaryLinkComponent);
    f.componentRef.setInput('gameId', 236);
    f.detectChanges();
    return { f, http: TestBed.inject(HttpTestingController), el: f.nativeElement as HTMLElement };
  }
  const none = { status: 'none', markdown: null, doneAt: null, requestedAt: null, stale: false, model: null, error: null };

  it('zeigt den Link, sobald es einen Text gibt — ohne Anforder-Knopf für Nicht-Admins', () => {
    const { f, http, el } = setup(false);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'done', markdown: '# x' });
    f.detectChanges();
    expect(el.querySelector('a.summary-link')?.getAttribute('href')).toBe('/club-games/236/summary');
    expect(el.querySelector('.summary-request')).toBeNull();
  });

  it('Admin: anfordern schickt POST und zeigt danach den Wartestand', () => {
    const { f, http, el } = setup(true);
    http.expectOne('/api/league/club/games/236/summary').flush(none);
    f.detectChanges();
    expect(el.querySelector('a.summary-link')).toBeNull();
    (el.querySelector('.summary-request') as HTMLButtonElement).click();
    const req = http.expectOne('/api/league/club/games/236/summary');
    expect(req.request.method).toBe('POST');
    req.flush({ ...none, status: 'requested' });
    f.detectChanges();
    expect(el.querySelector('.summary-wait')).not.toBeNull();
    expect(el.querySelector('.summary-request')).toBeNull();
  });

  it('0.914.0: angefordert, Hintergrund noch nicht durch — Hinweis mit Fortschritt statt nur „angefordert"', () => {
    const { f, http, el } = setup(true);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'requested', readiness: {
      ready: false, hasAnalysis: true, analysisDone: true, refining: true, refinedPositions: 56, totalPositions: 62,
      comparisonsPlanned: false, comparisonsDone: 0, comparisonsTotal: 0 } });
    f.detectChanges();
    const text = el.querySelector('.summary-wait')?.textContent ?? '';
    expect(text).toContain('games.summary.waitingForAnalysis');
    expect(text).toContain('games.summary.progressRefine');
    expect(text).toContain('games.summary.progressComparisonsPending');
    expect(f.componentInstance.progressText({ ready: false, hasAnalysis: true, analysisDone: true, refining: false,
      refinedPositions: 62, totalPositions: 62, comparisonsPlanned: true, comparisonsDone: 3, comparisonsTotal: 11 }))
      .toBe('games.summary.progressComparisons');
  });

  it('0.914.0: bereit — wieder der normale Wartevermerk', () => {
    const { f, http, el } = setup(true);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'running', readiness: {
      ready: true, hasAnalysis: true, analysisDone: true, refining: false, refinedPositions: 62, totalPositions: 62,
      comparisonsPlanned: true, comparisonsDone: 11, comparisonsTotal: 11 } });
    f.detectChanges();
    expect(el.querySelector('.summary-wait')?.textContent).toContain('games.summary.running');
  });
});
