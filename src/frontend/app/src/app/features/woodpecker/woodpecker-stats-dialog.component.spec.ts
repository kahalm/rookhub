import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { WoodpeckerPlan } from './woodpecker.service';
import { WoodpeckerStatsDialogComponent } from './woodpecker-stats-dialog.component';

// Wunsch 2026-11-18: „mach die Statistik als modales Overlay“.
describe('WoodpeckerStatsDialogComponent (Statistik als Fenster)', () => {
  let dialog: MatDialog;
  let http: HttpTestingController;

  const plan = (): WoodpeckerPlan => ({
    id: 3, name: 'Woodpecker Method', createdAt: '', completedAt: null, books: [], setSize: 100, availableCount: 400,
    currentCycle: 2, maxCycles: 7, status: 'solving', nextCycleAt: null,
    cycles: [
      { number: 1, targetDays: 28, startedAt: '', deadline: '', finishedAt: '', attempted: 100, solved: 80, timeSeconds: 9000 },
      { number: 2, targetDays: 14, startedAt: new Date().toISOString(), deadline: new Date(Date.now() + 10 * 86_400_000).toISOString(),
        finishedAt: null, attempted: 10, solved: 8, timeSeconds: 600 },
    ],
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideNoopAnimations(), provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    dialog = TestBed.inject(MatDialog);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    dialog.closeAll();
    TestBed.resetTestingModule();
  });

  const pane = () => document.querySelector('.cdk-overlay-container mat-dialog-container') as HTMLElement | null;

  it('öffnet modal als Inhalts-Dialog (760 px / 94vw) mit Titel, Trainingsname + Zyklus, Statistik-Panel und lädt /stats', () => {
    const ref = WoodpeckerStatsDialogComponent.open(dialog, { plan: plan(), solving: false });
    TestBed.inject(ApplicationRef).tick();   // Dialog-Hülle und Inhalt zeichnen
    const p = pane()!;
    expect(p).not.toBeNull();
    expect(p.getAttribute('role')).toBe('dialog');
    expect(document.querySelector('.cdk-overlay-backdrop')).not.toBeNull();   // modal: Hintergrund gesperrt
    expect(ref.disableClose).toBeFalsy();                       // Esc und Klick daneben schließen
    const overlay = document.querySelector('.cdk-overlay-pane') as HTMLElement;
    expect(overlay.style.width).toBe('760px');
    expect(overlay.style.maxWidth).toBe('94vw');
    expect(p.querySelector('h2[mat-dialog-title]')?.textContent).toContain('woodpecker.statistics');
    expect(p.querySelector('.wpd-name')?.textContent).toContain('Woodpecker Method');
    expect(p.querySelector('.wpd-sub')?.textContent).toContain('woodpecker.cycleOf');
    expect(p.querySelector('mat-dialog-content')?.classList).toContain('rh-scroll-hint');
    expect(p.querySelector('mat-dialog-content app-woodpecker-stats-panel')).not.toBeNull();
    http.expectOne(r => r.url === '/api/woodpecker/3/stats');
    // nicht mitten im Lösen: kein Pausen-Hinweis, der Knopf heißt „Schließen“
    expect(p.querySelector('.wpd-paused')).toBeNull();
    expect(p.querySelector('.wpd-close')?.textContent).toContain('common.close');
  });

  it('mitten im Lösen: sagt, dass die Uhr steht, und der Knopf heißt „Weiter lösen“; er schließt das Fenster', async () => {
    const ref = WoodpeckerStatsDialogComponent.open(dialog, { plan: plan(), solving: true });
    TestBed.inject(ApplicationRef).tick();   // Dialog-Hülle und Inhalt zeichnen
    http.expectOne(r => r.url === '/api/woodpecker/3/stats');
    const p = pane()!;
    expect(p.querySelector('.wpd-paused')?.textContent).toContain('woodpecker.statsPaused');
    expect(p.querySelector('.wpd-paused')?.getAttribute('role')).toBe('status');
    const btn = p.querySelector('.wpd-close') as HTMLButtonElement;
    expect(btn.textContent).toContain('woodpecker.statsResume');
    let closed = 0;
    ref.afterClosed().subscribe(() => closed++);
    btn.click();
    for (let i = 0; i < 40 && pane(); i++) await new Promise(r => setTimeout(r, 25));   // Schließ-Animation abwarten
    expect(closed).toBe(1);
    expect(pane()).toBeNull();
  });
});
