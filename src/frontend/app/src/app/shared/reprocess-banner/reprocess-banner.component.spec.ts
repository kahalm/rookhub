import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ReprocessBannerComponent } from './reprocess-banner.component';
import { SnackbarService } from '../../core/snackbar.service';

/**
 * Sichert die Banner-Sichtbarkeit/Zählung ab: der Knopf darf nur Datensätze zählen, die er
 * tatsächlich aktualisieren kann (lokal aufbereitbar + von Chessable nachladbar); rein manuell
 * zu re-importierende Kurse werden getrennt ausgewiesen, nicht mitgezählt (Regression v0.157.2 —
 * „Aktualisieren (12)" blieb hängen, weil manuell hochgeladene Kurse mitgezählt, aber übersprungen wurden).
 */
describe('ReprocessBannerComponent', () => {
  let snackbar: jasmine.SpyObj<SnackbarService>;

  function createComponent(): ReprocessBannerComponent {
    const fixture = TestBed.createComponent(ReprocessBannerComponent);
    fixture.componentInstance.section = 'courses';
    return fixture.componentInstance;
  }

  beforeEach(() => {
    snackbar = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['info']);
    TestBed.configureTestingModule({
      imports: [ReprocessBannerComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), 
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SnackbarService, useValue: snackbar },
      ],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('allCount = reprocessableLocally + refetchable (manuelle Re-Imports zählen NICHT mit)', () => {
    const c = createComponent();
    c.status = { currentVersion: 2, total: 50, stale: 48, reprocessableLocally: 5, refetchable: 31, needsReimport: 12 };
    expect(c.allCount).toBe(36);
    expect(c.actionableCount).toBe(36);
  });

  it('Zählungen = 0 ohne Status', () => {
    const c = createComponent();
    expect(c.allCount).toBe(0);
  });

  it('Re-Import-Hinweis: wegklickbar + bleibt verborgen, bis die Zahl steigt', () => {
    localStorage.removeItem('rookhub_reprocess_reimport_dismissed_courses');
    const c = createComponent();
    c.status = { currentVersion: 2, total: 50, stale: 12, reprocessableLocally: 0, refetchable: 0, needsReimport: 12 };
    expect(c.showReimportNote).toBeTrue();          // anfangs sichtbar

    c.dismissReimport();
    expect(c.showReimportNote).toBeFalse();          // weggeklickt → weg
    expect(localStorage.getItem('rookhub_reprocess_reimport_dismissed_courses')).toBe('12');

    c.status = { ...c.status, needsReimport: 12 };    // unverändert → bleibt verborgen
    expect(c.showReimportNote).toBeFalse();
    c.status = { ...c.status, needsReimport: 8 };     // weniger → bleibt verborgen
    expect(c.showReimportNote).toBeFalse();
    c.status = { ...c.status, needsReimport: 15 };    // NEUE manuelle Kurse → wieder sichtbar
    expect(c.showReimportNote).toBeTrue();
    localStorage.removeItem('rookhub_reprocess_reimport_dismissed_courses');
  });

  it('run() postet ohne localOnly und lädt den Status verzögert neu (Hintergrundlauf)', fakeAsync(() => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    c.run();
    const req = http.expectOne('/api/courses/reprocess');
    expect(req.request.method).toBe('POST');
    req.flush({ started: true }, { status: 202, statusText: 'Accepted' });   // Server startet im Hintergrund
    expect(c.working).toBeFalse();
    expect(snackbar.info).toHaveBeenCalled();
    // Der Status wird erst nach kurzer Verzögerung neu geholt (der Lauf ist asynchron).
    tick(2500);
    http.expectOne('/api/courses/reprocess/status').flush(
      { currentVersion: 2, total: 50, stale: 12, reprocessableLocally: 0, refetchable: 0, needsReimport: 12 });
    expect(c.allCount).toBe(0);   // nur noch manuell zu re-importierende → Knopf verschwindet
    expect(c.status!.needsReimport).toBe(12);
  }));

  it('blockt den zweiten Klick, solange ein Lauf aktiv ist', () => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    c.run();
    http.expectOne('/api/courses/reprocess');   // erster Lauf offen
    c.run();                                      // zweiter Klick wird ignoriert (working === true)
    // Kein zweiter Request: verify() in afterEach würde sonst fehlschlagen.
    expect(c.working).toBeTrue();
  });
});
