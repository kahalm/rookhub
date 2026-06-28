import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateModule } from '@ngx-translate/core';
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
      imports: [ReprocessBannerComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SnackbarService, useValue: snackbar },
      ],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('allCount = reprocessableLocally + refetchable, cachedCount = reprocessableLocally (manuelle Re-Imports zählen NICHT mit)', () => {
    const c = createComponent();
    c.status = { currentVersion: 2, total: 50, stale: 48, reprocessableLocally: 5, refetchable: 31, needsReimport: 12 };
    expect(c.allCount).toBe(36);
    expect(c.cachedCount).toBe(5);
    expect(c.actionableCount).toBe(36);
  });

  it('Zählungen = 0 ohne Status', () => {
    const c = createComponent();
    expect(c.allCount).toBe(0);
    expect(c.cachedCount).toBe(0);
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

  it('„Alle" (run(false)) postet ohne localOnly und lädt danach den Status neu', () => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    c.run(false);
    const req = http.expectOne('/api/courses/reprocess');
    expect(req.request.method).toBe('POST');
    req.flush({ reprocessed: 5, updatedLines: 0, enqueued: 31, skipped: 12 });
    // Nach Reprocess wird der Status neu geladen.
    http.expectOne('/api/courses/reprocess/status').flush(
      { currentVersion: 2, total: 50, stale: 12, reprocessableLocally: 0, refetchable: 0, needsReimport: 12 });
    expect(c.allCount).toBe(0);   // nur noch manuell zu re-importierende → Knöpfe verschwinden
    expect(c.status!.needsReimport).toBe(12);
  });

  it('„Aus Cache" (run(true)) postet mit localOnly=true', () => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    c.run(true);
    const req = http.expectOne('/api/courses/reprocess?localOnly=true');
    expect(req.request.method).toBe('POST');
    req.flush({ reprocessed: 5, updatedLines: 12, enqueued: 0, skipped: 0 });
    // Status neu geladen: nur noch die Chessable-Re-Fetch-baren übrig.
    http.expectOne('/api/courses/reprocess/status').flush(
      { currentVersion: 2, total: 50, stale: 31, reprocessableLocally: 0, refetchable: 31, needsReimport: 0 });
    expect(c.cachedCount).toBe(0);
    expect(c.allCount).toBe(31);
  });

  it('blockt den zweiten Klick, solange ein Lauf aktiv ist', () => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    c.run(false);
    http.expectOne('/api/courses/reprocess');   // erster Lauf offen
    c.run(true);                                  // zweiter Klick wird ignoriert (working != null)
    // Kein zweiter Request: verify() in afterEach würde sonst fehlschlagen.
    expect(c.working).toBe('all');
  });
});
