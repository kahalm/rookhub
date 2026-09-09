import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
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

  /**
   * Mobil-Spielerkarte: der Verein oeffnet die Aufstellung, ohne dass die (selbst klickbare) Karte
   * darunter den Favoriten toggelt — vorher war der Name reiner Text und jeder Tipp ein Favoriten-Write.
   */
  it('oeffnet aus der Spielerkarte die Mannschaft, ohne den Favoriten zu toggeln', () => {
    component.displayedPlayers = [player()];
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
    component.displayedPlayers = [player()];
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
    component.displayedPlayers = [player()];
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
});
