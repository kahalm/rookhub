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

  it('actionableCount = reprocessableLocally + refetchable (manuelle Re-Imports zählen NICHT mit)', () => {
    const c = createComponent();
    c.status = { currentVersion: 2, total: 50, stale: 48, reprocessableLocally: 5, refetchable: 31, needsReimport: 12 };
    expect(c.actionableCount).toBe(36);
  });

  it('actionableCount = 0 ohne Status', () => {
    expect(createComponent().actionableCount).toBe(0);
  });

  it('run() bereitet nur auf, wenn aktualisierbare Datensätze existieren', () => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    // Initialer Status-Load aus ngOnInit-losem Aufruf vermeiden: run() direkt.
    c.run();
    const req = http.expectOne('/api/courses/reprocess');
    expect(req.request.method).toBe('POST');
    req.flush({ reprocessed: 0, updatedLines: 0, enqueued: 36, skipped: 12 });
    // Nach Reprocess wird der Status neu geladen.
    http.expectOne('/api/courses/reprocess/status').flush(
      { currentVersion: 2, total: 50, stale: 12, reprocessableLocally: 0, refetchable: 0, needsReimport: 12 });
    expect(c.actionableCount).toBe(0);   // nur noch manuell zu re-importierende → Knopf verschwindet
    expect(c.status!.needsReimport).toBe(12);
  });
});
