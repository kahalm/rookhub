import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TournamentDetailComponent } from './tournament-detail.component';

describe('TournamentDetailComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TournamentDetailComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  /** Rendert die Detailseite bis zur Aktionsleiste; alle Start-Requests werden mit Leerdaten beantwortet. */
  async function render(monitor: { active: boolean; activeUntil: string | null }): Promise<ComponentFixture<TournamentDetailComponent>> {
    await TestBed.configureTestingModule({
      imports: [TournamentDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '4711' }), queryParams: {} } } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TournamentDetailComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); // ngOnInit
    http.expectOne('/api/tournament-favorites?tournamentId=4711').flush([]);
    http.expectOne('/api/tournament-favorites/settings/4711').flush({ showFavoritesOnly: false });
    http.expectOne('/api/tournaments/4711').flush({
      id: 1, name: 'Schach Tirol Open', chessResultsId: '4711', location: null, date: null, totalRounds: 0, knownRounds: 0, createdAt: '', updatedAt: '',
    });
    http.expectOne('/api/subscriptions').flush([]);
    // lastKnownRounds 0 => kein Monitor-Poll, der im Test weiterliefe.
    http.expectOne('/api/tournament-monitors/4711').flush({ ...monitor, lastKnownRounds: 0 });
    http.expectOne('/api/tournaments/4711/players').flush([]);
    http.expectOne('/api/tournaments/4711/teams').flush([]);
    fixture.detectChanges();
    return fixture;
  }

  const actionsOf = (fixture: ComponentFixture<TournamentDetailComponent>): HTMLElement[] =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.action-bar a, .action-bar button'));

  /**
   * Mobil sind die Aktionen nackte Icon-Kreise (.btn-label display:none nimmt den Text auch aus dem
   * Accessibility-Tree): jeder Knopf braucht aria-label + Tooltip (Tooltip-Trigger-Klasse = Modul verdrahtet).
   */
  it('beschriftet alle Aktionen per aria-label und Tooltip', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const actions = actionsOf(fixture);
    expect(actions.length).toBe(5);
    for (const a of actions) {
      expect(a.getAttribute('aria-label')).withContext(a.outerHTML).toBeTruthy();
      expect(a.classList.contains('mat-mdc-tooltip-trigger')).withContext(a.outerHTML).toBeTrue();
    }
    // Ohne Theme-Farbe: color="primary"/"warn" war unter M3 wirkungslos und ist weg.
    expect(actions.some(a => a.hasAttribute('color'))).toBeFalse();
  });

  /** Aus: Icon 'visibility', Beschriftung 'Beobachten'. */
  it('zeigt bei inaktiver Beobachtung das Auge und die Start-Beschriftung', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    const monitorBtn = actionsOf(fixture).find(a => a.getAttribute('aria-label') === 'tournaments.actions.monitor');
    expect(monitorBtn).toBeTruthy();
    expect(monitorBtn!.querySelector('mat-icon')!.textContent!.trim()).toBe('visibility');
  });

  /**
   * An: Icon 'visibility_off' (= Aktion 'Beobachtung beenden'), Beschriftung mit Endzeit — vorher
   * sahen an und aus auf dem Handy identisch aus (gleiches Icon, Label weg, color="primary" faerbte nichts).
   */
  it('zeigt bei aktiver Beobachtung das durchgestrichene Auge und die Endzeit-Beschriftung', async () => {
    const fixture = await render({ active: true, activeUntil: '2026-09-09T18:30:00' });
    const monitorBtn = actionsOf(fixture).find(a => a.getAttribute('aria-label') === 'tournaments.actions.monitoringUntil');
    expect(monitorBtn).withContext('Monitor-Knopf mit monitoringUntil-Label').toBeTruthy();
    expect(monitorBtn!.querySelector('mat-icon')!.textContent!.trim()).toBe('visibility_off');
    expect(actionsOf(fixture).some(a => a.getAttribute('aria-label') === 'tournaments.actions.monitor')).toBeFalse();
  });
});
