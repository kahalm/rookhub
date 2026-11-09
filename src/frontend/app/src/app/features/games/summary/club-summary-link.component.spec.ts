import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth.service';
import { ClubSummaryLinkComponent, formatElapsed } from './club-summary-link.component';

describe('ClubSummaryLinkComponent (0.913.0)', () => {
  function setup(isAdmin: boolean, allowAgain = false) {
    TestBed.configureTestingModule({
      imports: [ClubSummaryLinkComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin } }],
    });
    const f = TestBed.createComponent(ClubSummaryLinkComponent);
    f.componentRef.setInput('gameId', 236);
    if (allowAgain) f.componentRef.setInput('allowAgain', true);
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

  it('0.916.0: vorhandener Text — „neu anfordern" nur auf der Zusammenfassungsseite, nicht in der Partie-Kopfzeile', () => {
    const header = setup(true);
    header.http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'done', markdown: '# x' });
    header.f.detectChanges();
    expect(header.el.querySelector('a.summary-link')).not.toBeNull();
    expect(header.el.querySelector('.summary-request')).toBeNull();
    TestBed.resetTestingModule();
    const page = setup(true, true);
    page.http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'done', markdown: '# x' });
    page.f.detectChanges();
    expect(page.el.querySelector('.summary-request')?.textContent).toContain('games.summary.requestAgain');
  });

  it('Teilen-Link: liest über den Link, zeigt den Text-Link, nie einen Anforder-Knopf — auch für Admins', () => {
    TestBed.configureTestingModule({
      imports: [ClubSummaryLinkComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }), { provide: AuthService, useValue: { isAdmin: true } }],
    });
    const f = TestBed.createComponent(ClubSummaryLinkComponent);
    f.componentRef.setInput('gameId', 236);
    f.componentRef.setInput('shareToken', 'abc');
    f.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/league/s/abc/club/games/236/summary').flush({ ...none, status: 'none' });
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.summary-request')).toBeNull();
    expect(el.querySelector('a.summary-link')).toBeNull();
    f.componentInstance.state.set({ ...none, status: 'done', markdown: '# x' } as never);
    f.detectChanges();
    expect(el.querySelector('a.summary-link')?.getAttribute('href')).toBe('/s/abc/club-games/236/summary');
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
    // Kurz in der Kopfzeile, der Fortschritt steckt im (i) (Wunsch 2026-11-09).
    expect(el.querySelector('.summary-wait')?.textContent).toContain('games.summary.requested');
    expect(el.querySelector('.summary-wait')?.textContent).not.toContain('waitingForAnalysis');
    const detail = el.querySelector('.wait-info')?.getAttribute('aria-label') ?? '';
    expect(detail).toContain('games.summary.waitingForAnalysis');
    expect(detail).toContain('games.summary.progressRefine');
    expect(detail).toContain('games.summary.progressComparisonsPending');
    expect(f.componentInstance.progressText({ ready: false, hasAnalysis: true, analysisDone: true, refining: false,
      refinedPositions: 62, totalPositions: 62, comparisonsPlanned: true, comparisonsDone: 3, comparisonsTotal: 11 }))
      .toBe('games.summary.progressComparisons');
    // 0.919.3: schneller Durchgang läuft noch → Zahlen statt nur „Analyse läuft noch“
    expect(f.componentInstance.progressText({ ready: false, hasAnalysis: true, analysisDone: false, refining: false,
      refinedPositions: 0, totalPositions: 62, analyzedPositions: 40, comparisonsPlanned: false, comparisonsDone: 0, comparisonsTotal: 0 }))
      .toBe('games.summary.progressFast · games.summary.progressRefineNext');
  });

  function setupOwn() {
    TestBed.configureTestingModule({
      imports: [ClubSummaryLinkComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isAdmin: false } }],
    });
    const f = TestBed.createComponent(ClubSummaryLinkComponent);
    f.componentRef.setInput('gameId', 142);
    f.componentRef.setInput('kind', 'own');
    f.detectChanges();
    return { f, http: TestBed.inject(HttpTestingController), el: f.nativeElement as HTMLElement };
  }

  it('0.923.0: eigene Partie — der Besitzer ohne Adminrechte fordert über die Partie an', () => {
    const { f, http, el } = setupOwn();
    http.expectOne('/api/games/142/summary').flush({ ...none, quotaLeft: 2, quotaLimit: 2 });
    f.detectChanges();
    (el.querySelector('.summary-request') as HTMLButtonElement).click();
    const req = http.expectOne('/api/games/142/summary');
    expect(req.request.method).toBe('POST');
    req.flush({ ...none, status: 'requested', quotaLeft: 1, quotaLimit: 2 });
    f.detectChanges();
    expect(el.querySelector('.summary-wait')).not.toBeNull();
  });

  it('0.923.0: Kontingent verbraucht — Hinweis statt Knopf, Link führt zur eigenen Seite', () => {
    const { f, http, el } = setupOwn();
    http.expectOne('/api/games/142/summary').flush({ ...none, quotaLeft: 0, quotaLimit: 2 });
    f.detectChanges();
    expect(el.querySelector('.summary-request')).toBeNull();
    expect(el.querySelector('.summary-wait')?.textContent).toContain('games.summary.quotaUsed');
    TestBed.resetTestingModule();
    const done = setupOwn();
    done.http.expectOne('/api/games/142/summary').flush({ ...none, status: 'done', markdown: '# x', quotaLeft: 0, quotaLimit: 2 });
    done.f.detectChanges();
    expect(done.el.querySelector('a.summary-link')?.getAttribute('href')).toBe('/games/142/summary');
  });

  it('zeigt beim Schreiben, wie lange es schon läuft', () => {
    const { f, http, el } = setup(true);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'running', runningSeconds: 125 });
    f.detectChanges();
    expect(el.querySelector('.summary-timer')?.textContent).toBe('2:05');
  });

  it('angefordert: Zähler auch dann, Neu-Anfordern mit vorhandenem Text eingeschlossen', () => {
    const { f, http, el } = setup(true, true);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'requested', markdown: '# x', runningSeconds: 65 });
    f.detectChanges();
    expect(el.querySelector('.summary-wait')?.textContent).toContain('games.summary.requested');
    expect(el.querySelector('.summary-timer')?.textContent).toBe('1:05');
  });

  it('angefordert, Hintergrund rechnet noch: Zähler neben „requested“, vor dem (i)', () => {
    const { f, http, el } = setup(true);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'requested', runningSeconds: 7, readiness: {
      ready: false, hasAnalysis: true, analysisDone: false, refining: false, refinedPositions: 0, totalPositions: 62,
      comparisonsPlanned: false, comparisonsDone: 0, comparisonsTotal: 0 } });
    f.detectChanges();
    const timer = el.querySelector('.summary-timer');
    expect(timer?.textContent).toBe('0:07');
    expect(timer!.compareDocumentPosition(el.querySelector('.wait-info')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  describe('Uhr', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    it('angefordert: zählt je Sekunde und sieht alle 10 s nach', () => {
      const { f, http, el } = setup(true);
      http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'requested', runningSeconds: 5 });
      jasmine.clock().tick(9000);
      f.detectChanges();
      expect(el.querySelector('.summary-timer')?.textContent).toBe('0:14');
      http.expectNone('/api/league/club/games/236/summary');
      jasmine.clock().tick(1000);
      http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'running', runningSeconds: 1 });
      f.detectChanges();
      expect(el.querySelector('.summary-timer')?.textContent).toBe('0:01'); // Wechsel aufs Schreiben: gewollt von vorn
    });
  });

  it('ohne Zeitangabe des Servers oder fertig: kein Zähler', () => {
    const { f, http, el } = setup(true);
    http.expectOne('/api/league/club/games/236/summary').flush({ ...none, status: 'requested' });
    f.detectChanges();
    expect(el.querySelector('.summary-timer')).toBeNull();
    f.componentInstance.state.set({ ...none, status: 'done', markdown: '# x', runningSeconds: 40 } as never);
    f.detectChanges();
    expect(el.querySelector('.summary-timer')).toBeNull();
  });

  it('formatElapsed: Minuten:Sekunden, ab einer Stunde mit Stunden', () => {
    expect(formatElapsed(0)).toBe('0:00');
    expect(formatElapsed(59)).toBe('0:59');
    expect(formatElapsed(3725)).toBe('1:02:05');
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
