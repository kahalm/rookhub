import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { TournamentPlayer, TournamentTeam } from '@rh/core/models';
import { PLAYER_COLUMNS, TEAM_COLUMNS } from './tournament-table.util';
import { TournamentTablesComponent } from './tournament-tables.component';

describe('TournamentTablesComponent', () => {
  let fixture: ComponentFixture<TournamentTablesComponent>;
  let component: TournamentTablesComponent;

  const player = (over: Partial<TournamentPlayer> = {}): TournamentPlayer => ({
    id: 1, snr: 1, title: null, name: 'Anna Muster', fideId: null, elo: 2105, country: 'AUT', teamName: 'SK Dornbirn', boardNumber: 3, ...over,
  });
  const team = (over: Partial<TournamentTeam> = {}): TournamentTeam => ({ id: 1, snr: 1, name: 'SK Dornbirn', players: [], ...over });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentTablesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TournamentTablesComponent);
    component = fixture.componentInstance;
    component.playerColumns = PLAYER_COLUMNS;
    component.teamColumns = TEAM_COLUMNS;
  });

  it('creates (template AOT-compiles + DI resolves)', () => {
    expect(component).toBeTruthy();
  });

  // ----- Codereview UX-080: 178 Spieler einer Landesliga ohne Suche, am Handy ohne Sortierung -----

  const field = () => fixture.nativeElement.querySelector('.player-search input') as HTMLInputElement | null;
  const cardNames = () => [...fixture.nativeElement.querySelectorAll('.player-card .player-name')]
    .map((n: Element) => n.textContent?.trim());

  function liga(): void {
    component.players = component.displayedPlayers = [
      player({ id: 1, snr: 1, name: 'Oberschmid, Patrik', teamName: 'SK Schwaz', elo: 1900 }),
      player({ id: 2, snr: 2, name: 'Muster, Anna', teamName: 'SK Dornbirn', elo: 2105 }),
      player({ id: 3, snr: 3, name: 'Martinović, Saša', teamName: null, club: 'ŠK Zagreb', elo: 2300 }),
      player({ id: 4, snr: 4, name: 'Huber, Franz', teamName: 'SK Schwaz', elo: 1700 }),
    ];
  }

  async function type(text: string): Promise<void> {
    const input = field()!;
    input.value = text;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('findet Spieler über Namen oder Verein, ohne Groß-/Kleinschreibung und Akzente', async () => {
    liga();
    fixture.detectChanges();
    expect(field()).withContext('Suchfeld über der Spielerliste').not.toBeNull();

    await type('schwaz');
    expect(cardNames()).toEqual(['Oberschmid, Patrik', 'Huber, Franz']);

    await type('sasa zagreb');
    expect(cardNames()).toEqual(['Martinović, Saša']);

    // Die Desktop-Tabelle zeigt dieselbe Auswahl.
    expect(fixture.nativeElement.querySelectorAll('tr.mat-mdc-row').length).toBe(1);
  });

  it('sagt, wenn kein Spieler zur Suche passt', async () => {
    liga();
    fixture.detectChanges();

    await type('Carlsen');

    expect(cardNames()).toEqual([]);
    expect(fixture.nativeElement.querySelector('.search-none')).not.toBeNull();
  });

  it('bietet am Handy eine Sortierung an, Elo mit den Stärksten zuerst', () => {
    liga();
    const sorts: unknown[] = [];
    component.playerSort.subscribe(s => sorts.push(s));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.player-sort mat-select'))
      .withContext('Sortier-Menue fuer die Kartenliste').not.toBeNull();
    component.pickMobileSort('elo');
    component.pickMobileSort('team');

    expect(sorts).toEqual([{ active: 'elo', direction: 'desc' }, { active: 'team', direction: 'asc' }]);
    expect(component.mobileSortLabel('team')).toBe('tournaments.players.club');
  });

  it('zeigt „Nur Favoriten" auch ohne Stern — gesperrt und mit Hinweis', () => {
    liga();
    component.hasFavorites = false;
    fixture.detectChanges();

    const toggle = () => fixture.nativeElement.querySelector('.player-tools .favorites-only button') as HTMLButtonElement | null;
    expect(toggle()).withContext('Schalter fehlt ohne Favoriten').not.toBeNull();
    expect(toggle()!.disabled).toBeTrue();
    // Der Hinweis steht hinter „?" (Tooltip + aria-label), nicht mehr als Satz daneben (t-fav-hint).
    const hint = () => fixture.nativeElement.querySelector('.player-tools .filter-hint-btn') as HTMLButtonElement | null;
    expect(hint()).not.toBeNull();
    expect(hint()!.getAttribute('aria-label')).toBe('tournaments.favoritesOnlyHint');
    expect(fixture.nativeElement.querySelector('.player-tools .filter-hint')).toBeNull();

    component.hasFavorites = true;
    fixture.detectChanges();
    expect(toggle()!.disabled).toBeFalse();
    expect(hint()).toBeNull();
  });

  it('zeigt in der Titelspalte nur echte Titel — „Z56" aus der Startliste nicht (t-title-col)', () => {
    component.players = component.displayedPlayers = [
      player({ id: 1, snr: 1, title: 'GM', name: 'Navara David' }),
      player({ id: 2, snr: 2, title: 'Z56', name: 'Saric Ivan' }),
    ];
    fixture.detectChanges();
    const cells = [...fixture.nativeElement.querySelectorAll('td.mat-column-title')].map((c: Element) => c.textContent!.trim());
    expect(cells).toEqual(['GM', '—']);
    const cardTitles = [...fixture.nativeElement.querySelectorAll('.player-card .player-title')].map((c: Element) => c.textContent!.trim());
    expect(cardTitles).toEqual(['GM']);
  });

  /**
   * Startliste ohne Vereinsspalte: der uebernommene Verein steht kursiv da (Herkunft im Tooltip),
   * und fuer die, bei denen gar keiner steht, gibt es den Knopf „Vereine nachtragen".
   */
  it('zeigt uebernommene Vereine kursiv und bietet das Nachtragen fuer die uebrigen an', () => {
    component.players = component.displayedPlayers = [
      player({ id: 1, snr: 1, name: 'Martinovic, Sasa', fideId: '14509792', teamName: null, club: 'ŠK Zagreb', clubSource: 'Zagreb Open 2025' }),
      player({ id: 2, snr: 2, name: 'Ohne, Otto', fideId: '555', teamName: null }),
      player({ id: 3, snr: 3, name: 'Ohne FIDE, Fritz', fideId: null, teamName: null }),
    ];
    component.hasTeamPairings = false;
    const fill = jasmine.createSpy('fillClubs');
    component.fillClubs.subscribe(fill);
    fixture.detectChanges();

    const derived = fixture.nativeElement.querySelector('td .club-derived') as HTMLElement;
    expect(derived.textContent?.trim()).toBe('ŠK Zagreb');
    // Nur Otto: Martinovic hat einen (uebernommenen), Fritz laesst sich ohne FIDE-ID nicht sicher suchen.
    expect(component.playersWithoutClub).toBe(1);
    (fixture.nativeElement.querySelector('.fill-clubs') as HTMLButtonElement).click();
    expect(fill).toHaveBeenCalled();
  });

  /** In Mannschaftsturnieren ist die Spalte die MANNSCHAFT — dort gibt es nichts nachzutragen. */
  it('bietet in Mannschaftsturnieren kein Nachtragen an', () => {
    component.players = component.displayedPlayers = [player({ teamName: null, fideId: '555' })];
    component.hasTeamPairings = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.fill-clubs')).toBeNull();
  });

  /**
   * Ohne Teilnehmer steht ein Grund da, keine leere Tabelle — gemeldet an der Schachrallye Pradl,
   * deren Startliste auf chess-results erst am Turniermorgen eingetragen wird.
   */
  it('zeigt ohne Teilnehmer einen Hinweis statt einer leeren Tabelle', () => {
    component.players = component.displayedPlayers = [];
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.empty-hint')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });

  /**
   * Mobil-Spielerkarte: der Verein oeffnet die Aufstellung, ohne dass die (selbst klickbare) Karte
   * darunter den Favoriten toggelt — vorher war der Name reiner Text und jeder Tipp ein Favoriten-Write.
   */
  it('oeffnet aus der Spielerkarte die Mannschaft, ohne den Favoriten zu toggeln', () => {
    component.players = component.displayedPlayers = [player()];
    component.hasTeamPairings = true;
    const teamSpy = jasmine.createSpy('showTeamPlayers');
    const favSpy = jasmine.createSpy('toggleFavorite');
    component.showTeamPlayers.subscribe(teamSpy);
    component.toggleFavorite.subscribe(favSpy);
    fixture.detectChanges();

    const link = fixture.nativeElement.querySelector('.player-card .team-link') as HTMLElement | null;
    expect(link).withContext('Team-Link in der Mobil-Karte').toBeTruthy();
    link!.click();
    expect(teamSpy).toHaveBeenCalledWith('SK Dornbirn');
    expect(favSpy).not.toHaveBeenCalled();
  });

  /** Ohne Mannschaftspaarungen (Einzelturnier mit Vereinsspalte) gibt es nichts zu oeffnen: Text bleibt Text. */
  it('zeigt den Verein ohne Mannschaftspaarungen als reinen Text', () => {
    component.players = component.displayedPlayers = [player()];
    component.hasTeamPairings = false;
    fixture.detectChanges();

    const card = fixture.nativeElement.querySelector('.player-card') as HTMLElement;
    expect(card.querySelector('.team-link')).toBeNull();
    expect(card.querySelector('.player-details')!.textContent).toContain('SK Dornbirn');
  });

  /**
   * Der Trennpunkt zwischen Elo, Land, Verein und Brett ist ein CSS-Escape; mit doppeltem Backslash
   * stand auf jedem Handy der Text "\00b7" in der Karte. Letztes Detail traegt keinen Punkt.
   */
  it('trennt die Kartendetails mit einem Mittelpunkt statt dem Text \\00b7', () => {
    component.players = component.displayedPlayers = [player()];
    component.hasTeamPairings = true;
    fixture.detectChanges();

    // .mobile-only ist oberhalb 768px display:none — fuer den Pseudo-Element-Stil sichtbar schalten.
    const cards = fixture.nativeElement.querySelector('.player-cards') as HTMLElement;
    cards.style.display = 'block';
    const details = Array.from(cards.querySelectorAll('.player-details > span')) as HTMLElement[];
    expect(details.length).toBe(4);

    const content = (el: HTMLElement) => getComputedStyle(el, '::after').content.replace(/["']/g, '');
    expect(content(details[0])).toBe('·');
    expect(content(details[2])).withContext('Wrapper des Team-Links traegt den Punkt').toBe('·');
    expect(content(details[3])).toBe('none');
  });

  /**
   * Teams-Tab hat keine Mobil-Karten: der Favoriten-Klick sitzt auf der ganzen Zelle, nicht nur auf
   * dem 24-px-Stern — und ein Klick auf den Stern selbst feuert genau einmal (Bubbling, kein zweiter Handler).
   */
  it('toggelt den Team-Favoriten ueber die ganze Zelle, einmal je Klick', () => {
    component.displayedTeams = [team()];
    component.selectedTabIndex = 1;
    const spy = jasmine.createSpy('toggleTeamFavorite');
    component.toggleTeamFavorite.subscribe(spy);
    fixture.detectChanges();
    fixture.detectChanges();

    const cell = fixture.nativeElement.querySelector('td.fav-cell') as HTMLElement | null;
    expect(cell).withContext('Favoriten-Zelle der Team-Tabelle').toBeTruthy();
    cell!.click();
    expect(spy).toHaveBeenCalledTimes(1);
    expect((spy.calls.mostRecent().args[0] as TournamentTeam).snr).toBe(1);

    (cell!.querySelector('.fav-icon') as HTMLElement).click();
    expect(spy).toHaveBeenCalledTimes(2);
  });

  /**
   * Codereview UX-043: jeder Stern hiess „Favorit umschalten" — auf einer Turnierseite mit 178
   * Spielern 178-mal derselbe Name in der Schaltflaechenliste, ohne Hinweis, wem er gilt. Der
   * Zustand steht weiter in aria-pressed.
   */
  function useGermanStarLabel() {
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { tournaments: { favorites: { toggleNameAria: '{{name}} als Favorit' } } });
    translate.use('de');
  }

  it('nennt am Stern den Spieler', () => {
    useGermanStarLabel();
    component.players = component.displayedPlayers = [player({ snr: 1, name: 'Anna Muster' }), player({ id: 2, snr: 2, name: 'Bert Beispiel' })];
    component.favoriteSnrs = new Set([2]);
    fixture.detectChanges();

    const stars = [...fixture.nativeElement.querySelectorAll('td .fav-icon')] as HTMLElement[];
    expect(stars.map(s => s.getAttribute('aria-label'))).toEqual(['Anna Muster als Favorit', 'Bert Beispiel als Favorit']);
    expect(stars.map(s => s.getAttribute('aria-pressed'))).toEqual(['false', 'true']);
    const cards = [...fixture.nativeElement.querySelectorAll('.player-card')] as HTMLElement[];
    expect(cards.map(c => c.getAttribute('aria-label'))).toEqual(['Anna Muster als Favorit', 'Bert Beispiel als Favorit']);
  });

  it('nennt am Stern die Mannschaft', () => {
    useGermanStarLabel();
    component.teams = component.displayedTeams = [team({ name: 'SK Dornbirn' })];
    component.favoriteTeamSnrs = new Set([1]);
    component.selectedTabIndex = 1;
    fixture.detectChanges();
    fixture.detectChanges();

    const star = fixture.nativeElement.querySelector('td.fav-cell .fav-icon') as HTMLElement;
    expect(star.getAttribute('aria-label')).toBe('SK Dornbirn als Favorit');
    expect(star.getAttribute('aria-pressed')).toBe('true');
  });
});
