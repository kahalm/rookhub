import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TournamentDetailComponent } from './tournament-detail.component';
import { Subscription, TournamentGroup } from '@rh/core/models';
import { OpenTournamentService } from '../../core/open-tournament.service';

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
  async function render(monitor: { active: boolean; activeUntil: string | null }, groups?: TournamentGroup[],
                        opts: { chessResultsId?: string; subscriptions?: Subscription[] } = {}): Promise<ComponentFixture<TournamentDetailComponent>> {
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
      id: 1, name: 'Schach Tirol Open', chessResultsId: opts.chessResultsId ?? '4711', location: null, date: null, totalRounds: 0, knownRounds: 0, createdAt: '', updatedAt: '',
      groups,
    });
    http.expectOne('/api/subscriptions').flush(opts.subscriptions ?? []);
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

  // ----- Gruppen derselben Veranstaltung -----------------------------------

  const rally: TournamentGroup[] = [
    { chessResultsId: '1503214', label: 'Gruppe A', current: false },
    { chessResultsId: '1503215', label: 'Gruppe B', current: false },
    { chessResultsId: '1503219', label: 'Mädchen', current: false },
    { chessResultsId: '4711', label: 'Schnellschach', current: true },
  ];

  /** Vier Gruppen am selben Rallye-Tag: eine Leiste, die eigene ist gewaehlt. */
  it('zeigt die Gruppen der Veranstaltung als Umschalter, die eigene gewaehlt', async () => {
    const fixture = await render({ active: false, activeUntil: null }, rally);
    const toggles = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.group-switch mat-button-toggle'));

    expect(toggles.map(t => t.textContent?.trim())).toEqual(['Gruppe A', 'Gruppe B', 'Mädchen', 'Schnellschach']);
    expect(toggles[3].classList.contains('mat-button-toggle-checked')).toBeTrue();
  });

  /** Ein Klick oeffnet die andere Gruppe — auf demselben Reiter —, die Leiste bleibt bis dahin bei der eigenen. */
  it('oeffnet eine andere Gruppe auf demselben Reiter', async () => {
    const fixture = await render({ active: false, activeUntil: null }, rally);
    const open = spyOn(TestBed.inject(OpenTournamentService), 'open');
    fixture.componentInstance.selectedTabIndex = 2;

    const button = (fixture.nativeElement as HTMLElement).querySelectorAll('.group-switch mat-button-toggle button')[1] as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(open).toHaveBeenCalledWith('1503215', 'pairings');
    const checked = (fixture.nativeElement as HTMLElement).querySelector('.group-switch .mat-button-toggle-checked');
    expect(checked?.textContent?.trim()).toBe('Schnellschach');
  });

  it('zeigt ohne Gruppen keine Leiste', async () => {
    const fixture = await render({ active: false, activeUntil: null });
    expect((fixture.nativeElement as HTMLElement).querySelector('.group-switch')).toBeNull();
  });

  // ----- Abo: eine Kennung je Turnier (A5-001) ------------------------------

  const sub = (id: number, crawlerTournamentId: string): Subscription => ({
    id, crawlerTournamentId, tournamentName: 'Schach Tirol Open', subscribedAt: '', tournamentDbId: null, eventDate: null,
  });

  /**
   * Die Route traegt die Crawler-DB-Id (4711), der Server speichert das Abo aber unter der
   * chess-results-Nummer — vorher erkannte die Seite es dann nicht und zeigte „nicht gemerkt".
   */
  it('erkennt ein Abo unter der chess-results-Nummer, obwohl die Route die DB-Id traegt', async () => {
    const fixture = await render({ active: false, activeUntil: null }, undefined,
      { chessResultsId: '1234567', subscriptions: [sub(9, '1234567')] });
    expect(fixture.componentInstance.subscription?.id).toBe(9);
  });

  /** Ein Alt-Abo steht noch unter der DB-Id aus der Route — das gilt weiter. */
  it('erkennt ein Alt-Abo unter der DB-Id der Route', async () => {
    const fixture = await render({ active: false, activeUntil: null }, undefined,
      { chessResultsId: '1234567', subscriptions: [sub(3, '4711')] });
    expect(fixture.componentInstance.subscription?.id).toBe(3);
  });

  it('nimmt kein fremdes Abo', async () => {
    const fixture = await render({ active: false, activeUntil: null }, undefined,
      { chessResultsId: '1234567', subscriptions: [sub(5, '7654321')] });
    expect(fixture.componentInstance.subscription).toBeNull();
  });
});
