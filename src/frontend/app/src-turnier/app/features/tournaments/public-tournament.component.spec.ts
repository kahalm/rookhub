import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PublicTournamentComponent } from './public-tournament.component';
import { AuthService } from '@rh/core/auth.service';
import { NotificationService } from '@rh/core/notification.service';

describe('PublicTournamentComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [PublicTournamentComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PublicTournamentComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // ----- Favoriten-Ansicht ueber tournament-favorites.util, gecacht (W5 F6-024) -----

  const id = '990024';
  const keys = [`public_fav_players_${id}`, `public_fav_teams_${id}`, `public_fav_filter_${id}`];
  const players = [
    { snr: 1, name: 'Alice', teamName: 'Red' },
    { snr: 2, name: 'Bob', teamName: 'Red' },
    { snr: 3, name: 'Carol', teamName: 'Blue' },
  ];
  const teams = [{ snr: 10, name: 'Red' }, { snr: 11, name: 'Blue' }];

  afterEach(() => keys.forEach(k => localStorage.removeItem(k)));

  /** Oeffentliche Seite mit Alice als lokalem Favoriten und eingeschaltetem Filter; Spieler/Teams bleiben offen. */
  async function render() {
    localStorage.setItem(keys[0], JSON.stringify([1]));
    localStorage.setItem(keys[2], JSON.stringify(true));
    await TestBed.configureTestingModule({
      imports: [PublicTournamentComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PublicTournamentComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges(); // ngOnInit
    http.expectOne(`/api/tournaments/${id}`).flush({ id: 1, name: 'Open', chessResultsId: id, totalRounds: 0 });
    return { fixture, http, c: fixture.componentInstance };
  }

  it('reicht mat-table bei aktivem Filter je Aenderungslauf DASSELBE Array (kein Getter, der neu filtert)', async () => {
    const { fixture, http, c } = await render();
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    fixture.detectChanges();

    expect(c.displayedPlayers.map(p => p.name)).toEqual(['Alice', 'Bob']);
    expect(c.displayedPlayers).toBe(c.displayedPlayers);
    expect(c.displayedTeams).toBe(c.displayedTeams);
    expect(c.displayedPairings).toBe(c.displayedPairings);
  });

  it('hat neben dem Titel ein ⋮-Menue mit Teilen und chess-results (t-head-buttons, am Handy statt der Symbolkreise)', async () => {
    const { fixture, http } = await render();
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    fixture.detectChanges();
    const more = (fixture.nativeElement as HTMLElement).querySelector('.title-row .more-actions') as HTMLButtonElement;
    expect(more.getAttribute('aria-label')).toBe('common.moreActions');
    more.click();
    fixture.detectChanges();
    const items = Array.from(document.querySelectorAll('.mat-mdc-menu-panel [mat-menu-item]')).map(i => i.textContent!);
    expect(items.length).toBe(3);
    expect(items[0]).toContain('tournaments.actions.share');
    expect(items[1]).toContain('tournamentDirectory.openChessResults');
    // d2-t-detail-vorlauf: „Volle Ansicht" steht am Handy im ⋮-Menue statt als eigene Knopfzeile.
    expect(items[2]).toContain('tournaments.actions.openFull');
  });

  it('nimmt „Volle Ansicht" am Handy aus der Knopfreihe (d2-t-detail-vorlauf)', async () => {
    const frame = window.frameElement as HTMLElement | null;
    if (!frame) pending('Karma laeuft nicht im iframe — der Viewport laesst sich nicht verstellen');
    const prev = frame!.style.width;
    frame!.style.width = '360px';
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    try {
      const { fixture, http } = await render();
      http.expectOne(`/api/tournaments/${id}/players`).flush(players);
      http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
      fixture.detectChanges();
      const full = (fixture.nativeElement as HTMLElement).querySelector('.action-bar .full-view') as HTMLElement;
      expect(getComputedStyle(full).display).toBe('none');
    } finally {
      frame!.style.width = prev;
    }
  });

  it('Teams vor Spielern geladen: das Team des favorisierten Spielers steht trotzdem in der Teamliste', async () => {
    const { http, c } = await render();
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    expect(c.displayedTeams).toEqual([]);
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    expect(c.displayedTeams.map(t => t.name)).toEqual(['Red']);
  });

  it('Sortieren, Filter und Sterne berechnen die angezeigten Zeilen neu', async () => {
    const { http, c } = await render();
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);

    c.onPlayerSort({ active: 'name', direction: 'desc' });
    expect(c.displayedPlayers.map(p => p.name)).toEqual(['Bob', 'Alice']);

    c.toggleTeamFavorite(teams[1] as any);
    expect(c.displayedPlayers.map(p => p.name)).toEqual(['Carol', 'Bob', 'Alice']);
    expect(c.displayedTeams.map(t => t.name)).toEqual(['Red', 'Blue']);

    c.onFavoritesToggle(false);
    c.onPlayerSort({ active: '', direction: '' });
    expect(c.displayedPlayers.length).toBe(3);
    expect(localStorage.getItem(keys[1])).toBe('[11]');
  });

  // UI-Sweep 2026-10-11 (r2-t-public-actions): dieselbe Knopfreihe wie /tournaments/:id — Merken primaer, Beobachten,
  // „Volle Ansicht", Teilen umrandet, Chess-Results als Textlink zuletzt; abgemeldet fuehren Merken und Beobachten zur Anmeldung.
  it('Knopfreihe: Merken, Beobachten, Volle Ansicht, Teilen, Chess-Results — in dieser Reihenfolge und diesen Stilen', async () => {
    const { fixture, http } = await render();
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    fixture.detectChanges();
    const bar = (fixture.nativeElement as HTMLElement).querySelector('.action-bar')!;
    const items = Array.from(bar.children) as HTMLElement[];
    expect(items.map(i => i.textContent!.trim())).toEqual([
      'bookmark_add tournamentDirectory.bookmark', 'notifications_active tournaments.actions.monitor',
      'open_in_full tournaments.actions.openFull',
      'share tournaments.actions.share', 'open_in_new tournaments.actions.chessResults']);
    expect(items[0].hasAttribute('mat-flat-button')).toBeTrue();
    expect(items[1].hasAttribute('mat-stroked-button')).toBeTrue();
    expect(items[1].getAttribute('aria-label')).toBe('tournaments.actions.monitor');
    expect(items[2].getAttribute('href')).toBe(`/tournaments/${id}`);
    expect(items[3].hasAttribute('mat-stroked-button')).toBeTrue();
    expect(items[4].hasAttribute('mat-button')).toBeTrue();
  });

  it('Beobachten ohne Anmeldung fuehrt zur Anmeldung (mit Ruecksprung), ohne einen Monitor anzulegen', async () => {
    localStorage.removeItem('rookhub_user');
    const { fixture, http, c } = await render();
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    const nav = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    c.monitor.toggle();
    expect(String(nav.calls.mostRecent().args[0])).toContain('/login?returnUrl=');
    http.expectNone(`/api/tournament-monitors/${id}`);
    fixture.destroy();
  });

  it('angemeldet: laedt den Stand der Beobachtung und schaltet sie mit demselben Endpunkt wie die Detailansicht ein', async () => {
    await TestBed.configureTestingModule({
      imports: [PublicTournamentComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
      ],
    }).compileComponents();
    spyOnProperty(TestBed.inject(AuthService), 'isLoggedIn', 'get').and.returnValue(true);
    spyOn(TestBed.inject(NotificationService), 'requestPermission').and.resolveTo('denied');
    const fixture = TestBed.createComponent(PublicTournamentComponent);
    const http = TestBed.inject(HttpTestingController);
    const c = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit
    http.expectOne('/api/subscriptions').flush([]);
    http.expectOne(`/api/tournament-monitors/${id}`).flush({ active: false, activeUntil: null, lastKnownRounds: 0 });
    http.expectOne(`/api/tournaments/${id}`).flush({ id: 1, name: 'Open', chessResultsId: id, totalRounds: 0 });
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    expect(c.monitor.monitoring()).toBeFalse();

    c.monitor.toggle();
    const start = http.expectOne(r => r.method === 'POST' && r.url === `/api/tournament-monitors/${id}`);
    start.flush({ active: true, activeUntil: '2026-10-11T20:00:00Z', lastKnownRounds: 3 });
    fixture.detectChanges();
    expect(c.monitor.monitoring()).toBeTrue();
    const monitorBtn = (fixture.nativeElement as HTMLElement).querySelector('.action-bar .monitor')!;
    expect(monitorBtn.getAttribute('aria-label')).toBe('tournaments.actions.monitoringUntil');
    expect(monitorBtn.querySelector('mat-icon')!.textContent!.trim()).toBe('notifications_off');
    fixture.destroy(); // beendet auch die 30-s-Abfrage (DestroyRef)
  });

  it('Merken ohne Anmeldung fuehrt zur Anmeldung, ohne etwas zu speichern', async () => {
    localStorage.removeItem('rookhub_user');
    const { fixture, http, c } = await render();
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    const router = TestBed.inject(Router);
    const nav = spyOn(router, 'navigateByUrl').and.resolveTo(true);
    c.subscribe();
    expect(nav).toHaveBeenCalled();
    expect(String(nav.calls.mostRecent().args[0])).toContain('/login');
    http.expectNone('/api/subscriptions');
    fixture.destroy();
  });
});
