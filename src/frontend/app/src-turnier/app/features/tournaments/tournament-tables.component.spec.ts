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

    expect(fixture.nativeElement.querySelector('.player-tools .player-menu-btn'))
      .withContext('Sortier-/Filterknopf fuer die Kartenliste').not.toBeNull();
    // z1-t-detail-sortier-knopf: sichtbar beschriftet, nicht nur ⇅ ▾
    expect(fixture.nativeElement.querySelector('.player-menu-btn .btn-label').textContent).toContain('tournaments.players.sortShort');
    expect(fixture.nativeElement.querySelector('.player-menu-btn .drop')).toBeNull();
    component.pickMobileSort('elo');
    component.pickMobileSort('team');

    expect(sorts).toEqual([{ active: 'elo', direction: 'desc' }, { active: 'team', direction: 'asc' }]);
    expect(component.mobileSortLabel('team')).toBe('tournaments.players.club');
  });

  // ----- d2-t-detail-vorlauf: Sortierung und Favoriten am Handy hinter EINEM Knopf -----

  it('legt Sortierung und „Nur Favoriten" ins Menue neben dem Suchfeld; eine Abweichung steht als Chip darunter', () => {
    liga();
    component.hasFavorites = true;
    const sorts: unknown[] = [];
    const favs: boolean[] = [];
    component.playerSort.subscribe(s => sorts.push(s));
    component.favoritesToggle.subscribe(f => favs.push(f));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.active-chips')).withContext('Standard: kein Chip').toBeNull();

    (root.querySelector('.player-menu-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const items = Array.from(document.querySelectorAll('.mat-mdc-menu-panel [mat-menu-item]')) as HTMLButtonElement[];
    expect(items.map(i => i.getAttribute('role'))).toEqual(['menuitemradio', 'menuitemradio', 'menuitemradio', 'menuitemradio', 'menuitemcheckbox']);
    items[2].click();   // Elo
    fixture.detectChanges();
    expect(sorts).toEqual([{ active: 'elo', direction: 'desc' }]);
    const chip = root.querySelector('.active-chips .sort-chip') as HTMLButtonElement;
    expect(chip.textContent).toContain('tournaments.players.elo');

    chip.click();
    fixture.detectChanges();
    expect(component.mobileSort).toBe('snr');
    expect(root.querySelector('.active-chips')).toBeNull();

    component.showFavoritesOnly = true;
    fixture.detectChanges();
    (root.querySelector('.active-chips .fav-chip') as HTMLButtonElement).click();
    expect(favs).toEqual([false]);
  });

  // ----- d2-t-players-paging: lange Startlisten in Portionen zu 50 -----

  function grosseListe(n: number): void {
    component.players = component.displayedPlayers =
      Array.from({ length: n }, (_, i) => player({ id: i + 1, snr: i + 1, name: `Spieler ${i + 1}`, teamName: i % 2 ? 'SK Schwaz' : 'SK Wörgl' }));
  }

  it('zeigt erst 50 Spieler, „Weitere 50 anzeigen" haengt die naechsten an', () => {
    grosseListe(122);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('.player-card').length).toBe(50);
    expect(root.querySelectorAll('tr.mat-mdc-row').length).toBe(50);
    expect(root.querySelector('.more-count')!.textContent).toContain('tournaments.players.shownOf');
    expect(component.nextPlayerPortion).toBe(50);

    (root.querySelector('.more-players') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.player-card').length).toBe(100);
    expect(component.nextPlayerPortion).toBe(22);

    (root.querySelector('.more-players') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.player-card').length).toBe(122);
    expect(root.querySelector('.more-players')).toBeNull();
  });

  it('begrenzt bei Suche und „Nur Favoriten" nicht und faengt nach neuer Suche/Sortierung wieder bei 50 an', async () => {
    grosseListe(122);
    fixture.detectChanges();
    component.showMorePlayers();
    fixture.detectChanges();
    expect(component.visiblePlayers.length).toBe(100);

    await type('schwaz');
    expect(cardNames().length).withContext('Suche: alle 61 Treffer').toBe(61);
    expect(fixture.nativeElement.querySelector('.more-players')).toBeNull();

    await type('');
    expect(component.visiblePlayers.length).withContext('nach der Suche wieder erste Portion').toBe(50);

    component.showMorePlayers();
    component.pickMobileSort('elo');
    expect(component.visiblePlayers.length).withContext('neue Sortierung: erste Portion').toBe(50);

    component.showFavoritesOnly = true;
    expect(component.visiblePlayers.length).toBe(122);
  });

  // ----- d2-t-players-sticky-head -----

  // UI-Sweep 2026-10-12 (z3-t-zahlenkopf-rechts): Köpfe der Zahlenspalten fluchten rechts mit den Ziffern.
  it('stellt die Köpfe „Nr.", „Elo", „Br." rechtsbündig über die Ziffern (Pfeil links davor)', async () => {
    grosseListe(10);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    fixture.detectChanges();
    for (const col of ['snr', 'elo', 'board']) {
      const th = fixture.nativeElement.querySelector(`.table-scroll th.mat-column-${col}`) as HTMLElement | null;
      if (!th) continue;
      const box = getComputedStyle(th.querySelector('.mat-sort-header-container')!);
      expect(box.flexDirection).withContext(col).toBe('row-reverse');
      expect(box.justifyContent).withContext(`${col}: in row-reverse heißt flex-start rechts`).toBe('flex-start');
      const label = th.querySelector('.mat-sort-header-content') as HTMLElement | null;
      const td = fixture.nativeElement.querySelector(`.table-scroll td.mat-column-${col}`) as HTMLElement | null;
      if (label && td && th.getBoundingClientRect().width > 0) {
        const pad = (e: HTMLElement) => parseFloat(getComputedStyle(e).paddingRight);
        expect(Math.abs((label.getBoundingClientRect().right) - (td.getBoundingClientRect().right - pad(td))))
          .withContext(`${col}: Kopf über den Ziffern`).toBeLessThan(3);
      }
    }
  });

  it('haelt den Kopf der Tabellen beim Scrollen fest, auf Flaechenfarbe', async () => {
    grosseListe(10);
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    fixture.detectChanges();
    const th = fixture.nativeElement.querySelector('.table-scroll th.mat-mdc-header-cell') as HTMLElement;
    expect(th.classList).toContain('mat-mdc-table-sticky');
    expect(getComputedStyle(th).position).toBe('sticky');
    if (window.innerWidth > 768) {
      expect(getComputedStyle(fixture.nativeElement.querySelector('.table-scroll')).overflowX)
        .withContext('Wrapper ist kein Scroll-Container').toBe('visible');
    }
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
    // z1-t-detail-fav-hilfe: kein Minus-Rand, das ? klebt nicht an der Beschriftung
    expect(getComputedStyle(hint()!).marginLeft).toBe('0px');

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

    expect(fixture.nativeElement.querySelector('.empty-state')).toBeTruthy();
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
    expect(card.querySelector('.player-club')!.textContent).toContain('SK Dornbirn');
  });

  /**
   * Der Trennpunkt zwischen Elo, Land und Brett ist ein CSS-Escape; mit doppeltem Backslash stand auf jedem Handy der
   * Text "\00b7" in der Karte. Seit UI-Sweep 2026-10-11 (r2-t-meta-dot) steht er VOR dem Folgeelement (::before) —
   * so bleibt er beim Umbruch nie allein am Zeilenende —, und der Verein hat eine eigene Zeile mit „…".
   */
  it('trennt die Kartendetails mit einem Mittelpunkt davor, der Verein in eigener Zeile', () => {
    component.players = component.displayedPlayers = [player({ teamName: 'ŠK Prakovce - www.sachovetreningy.sk' })];
    component.hasTeamPairings = true;
    fixture.detectChanges();

    // .mobile-only ist oberhalb 768px display:none — fuer den Pseudo-Element-Stil sichtbar schalten.
    const cards = fixture.nativeElement.querySelector('.player-cards') as HTMLElement;
    cards.style.display = 'block';
    const details = Array.from(cards.querySelectorAll('.player-details > span')) as HTMLElement[];
    expect(details.map(d => d.textContent!.trim())).toEqual(['2105', 'AUT', 'tournaments.players.boardShort 3']);

    const before = (el: HTMLElement) => getComputedStyle(el, '::before').content.replace(/["']/g, '');
    const after = (el: HTMLElement) => getComputedStyle(el, '::after').content.replace(/["']/g, '');
    expect(before(details[0])).toBe('none');
    expect(before(details[1])).toBe('·');
    expect(before(details[2])).toBe('·');
    expect(after(details[2])).withContext('kein Punkt am Zeilenende').toBe('none');

    const club = cards.querySelector('.player-club') as HTMLElement;
    expect(club.textContent).toContain('ŠK Prakovce');
    expect(getComputedStyle(club).textOverflow).toBe('ellipsis');
    expect(getComputedStyle(club).whiteSpace).toBe('nowrap');
  });

  // UI-Sweep 2026-10-11 (r2-t-title-empty): eine durchgehend leere Titelspalte kostet nur Breite.
  it('blendet die Spalte „Titel" aus, wenn kein Spieler einen echten Titel traegt', () => {
    component.players = component.displayedPlayers = [player({ title: 'Z56' }), player({ id: 2, snr: 2, title: null })];
    fixture.detectChanges();
    expect(component.shownPlayerColumns).not.toContain('title');
    expect(fixture.nativeElement.querySelector('th.mat-column-title')).toBeNull();
    expect(component.shownPlayerColumns).toBe(component.shownPlayerColumns);

    component.players = component.displayedPlayers = [player({ title: 'GM' }), player({ id: 2, snr: 2 })];
    fixture.detectChanges();
    expect(component.shownPlayerColumns).toContain('title');
    expect(fixture.nativeElement.querySelector('th.mat-column-title')).not.toBeNull();
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

  // ----- UI-Sweep u1 (u1-t-turnier-paarungen): ab 2400 px Paarungen neben Spielern/Teams -----

  const tabLabels = () => [...fixture.nativeElement.querySelectorAll('.mdc-tab__text-label')]
    .map((t: Element) => t.textContent?.trim());

  it('breit (split): Spieler und Teams als Reiter links, die Paarungen daneben in eigener Spalte', () => {
    liga();
    component.pairingColumns = ['board', 'white', 'result', 'black'];
    component.displayedPairings = [{ board: 1, white: 'Oberschmid, Patrik', black: 'Muster, Anna', result: '1-0' }];
    component.split = true;
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(tabLabels()).toEqual(['tournaments.tabs.players', 'tournaments.tabs.teams']);
    const left = el.querySelector('.split-tables > mat-tab-group.split-left')!;
    expect(left.querySelector('.player-search')).withContext('Spielerliste links').not.toBeNull();
    const right = el.querySelector('.split-tables > section.split-pairings')!;
    expect(right.querySelector('h2')!.textContent).toContain('tournaments.tabs.pairings');
    expect(right.querySelector('.round-selector')).not.toBeNull();
    expect(right.querySelector('td.result-cell')!.textContent).toContain('1-0');
  });

  it('breit ohne Paarungen: ein Satz mit Grund statt eines leeren Tabellenkopfs (alles-voll-2)', () => {
    liga();
    component.displayedPairings = [];
    component.split = true;
    fixture.detectChanges();
    const right = (fixture.nativeElement as HTMLElement).querySelector('.split-tables > section.split-pairings')!;
    expect(right.querySelector('.empty-state')!.textContent).toContain('tournaments.pairings.none');
    expect(right.querySelector('table')).toBeNull();
  });

  it('breit: ein auf „Paarungen" stehender Reiter zeigt links die Spieler (die Paarungen stehen ja rechts)', () => {
    liga();
    component.split = true;
    component.selectedTabIndex = 2;
    fixture.detectChanges();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.split-left .player-search')).not.toBeNull();
  });

  it('schmal (Vorgabe): drei Reiter wie bisher, keine geteilte Ansicht', () => {
    liga();
    fixture.detectChanges();
    expect(tabLabels()).toEqual(['tournaments.tabs.players', 'tournaments.tabs.teams', 'tournaments.tabs.pairings']);
    expect((fixture.nativeElement as HTMLElement).querySelector('.split-tables')).toBeNull();
  });

  it('CSS: die Zweispalten-Regeln stehen nur in @media (min-width: 2400px)', () => {
    const css = ((TournamentTablesComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n')
      .replace(/%NS%/g, '').replace(/\s+/g, '');
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    expect(css.slice(at)).toMatch(/\.split-tables[^{]*\{display:grid;/);
    expect(css.slice(at)).toMatch(/\.split-pairings[^{]*\{position:sticky;top:1rem;max-height:calc\(100vh-2rem\);overflow:auto/);
    expect(css.slice(0, at)).not.toContain('split-');
  });
});
