import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PublicTournamentComponent } from './public-tournament.component';

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
    expect(items.length).toBe(2);
    expect(items[0]).toContain('tournaments.actions.share');
    expect(items[1]).toContain('tournamentDirectory.openChessResults');
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

  // UI-Sweep 2026-10-11 (r2-t-public-actions): dieselbe Knopfreihe wie /tournaments/:id — Merken primaer, „Volle Ansicht",
  // Teilen umrandet, Chess-Results als Textlink zuletzt; abgemeldet fuehrt Merken zur Anmeldung.
  it('Knopfreihe: Merken, Volle Ansicht, Teilen, Chess-Results — in dieser Reihenfolge und diesen Stilen', async () => {
    const { fixture, http } = await render();
    http.expectOne(`/api/tournaments/${id}/players`).flush(players);
    http.expectOne(`/api/tournaments/${id}/teams`).flush(teams);
    fixture.detectChanges();
    const bar = (fixture.nativeElement as HTMLElement).querySelector('.action-bar')!;
    const items = Array.from(bar.children) as HTMLElement[];
    expect(items.map(i => i.textContent!.trim())).toEqual([
      'bookmark_add tournamentDirectory.bookmark', 'open_in_full tournaments.actions.openFull',
      'share tournaments.actions.share', 'open_in_new tournaments.actions.chessResults']);
    expect(items[0].hasAttribute('mat-flat-button')).toBeTrue();
    expect(items[1].getAttribute('href')).toBe(`/tournaments/${id}`);
    expect(items[2].hasAttribute('mat-stroked-button')).toBeTrue();
    expect(items[3].hasAttribute('mat-button')).toBeTrue();
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
