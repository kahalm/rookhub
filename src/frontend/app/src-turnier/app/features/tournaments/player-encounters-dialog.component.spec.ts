import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { AuthService } from '@rh/core/auth.service';
import { TournamentPlayer } from '@rh/core/models';
import { PlayerEncountersDialogComponent, PlayerEncountersData } from './player-encounters-dialog.component';

function player(snr: number, name: string, extra: Partial<TournamentPlayer> = {}): TournamentPlayer {
  return { id: snr, snr, name, title: null, fideId: null, elo: 1800 + snr, country: null, teamName: null, boardNumber: null, ...extra };
}

describe('PlayerEncountersDialogComponent', () => {
  let http: HttpTestingController;
  let loggedIn = false;

  function create(data: PlayerEncountersData) {
    TestBed.configureTestingModule({
      imports: [PlayerEncountersDialogComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: MatDialogRef, useValue: { close: () => {} } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: AuthService, useValue: { get isLoggedIn() { return loggedIn; } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PlayerEncountersDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => http.verify());

  it('Einzelturnier: laedt ALLE Runden auf einmal, zeigt Farbe, Gegner, Ergebnis und die naechste Runde', () => {
    const players = [player(1, 'Anna'), player(2, 'Bert'), player(3, 'Cleo')];
    const fixture = create({ tournamentId: '10', chessResultsId: '123', player: players[0], players });
    http.expectOne('/api/tournaments/10/pairings').flush([
      { id: 1, roundNumber: 1, boardNumber: 1, white: 'Anna', black: 'Bert', result: '1-0' },
      { id: 2, roundNumber: 2, boardNumber: 1, white: 'Cleo', black: 'Anna', result: null },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const rows = Array.from(el.querySelectorAll('.enc-row'));
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('.enc-white')).not.toBeNull();
    expect(rows[0].querySelector('.enc-opp')?.textContent?.trim()).toBe('Bert');
    expect(rows[0].querySelector('.enc-result')?.textContent?.trim()).toBe('1');
    expect(rows[1].classList).toContain('enc-next');
    expect(rows[1].querySelector('.enc-black')).not.toBeNull();
    expect(rows[1].querySelector('.enc-chip')).not.toBeNull();
    // Kein Mannschaftsturnier: keine Spielerseite, kein Holen-Knopf.
    expect(el.querySelector('.enc-fetch')).toBeNull();
  });

  it('Klick auf den Gegner wechselt auf dessen Begegnungen, ohne die Paarungen neu zu laden', () => {
    const players = [player(1, 'Anna'), player(2, 'Bert')];
    const fixture = create({ tournamentId: '10', chessResultsId: null, player: players[0], players });
    http.expectOne('/api/tournaments/10/pairings').flush([
      { id: 1, roundNumber: 1, boardNumber: 1, white: 'Anna', black: 'Bert', result: '1-0' },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('button.enc-opp') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('h2')?.textContent).toContain('Bert');
    expect(el.querySelector('.enc-opp')?.textContent?.trim()).toBe('Anna');
    expect(el.querySelector('.enc-result')?.textContent?.trim()).toBe('0');
  });

  it('Mannschaftsturnier ohne Spielerseite: Mannschaftskaempfe + Hinweis + „Einzelpartien holen"', () => {
    const me = player(1, 'Embacher, Gregor', { teamName: 'Absam' });
    const fixture = create({ tournamentId: '12', chessResultsId: '1479344', player: me, players: [me] });
    http.expectOne('/api/tournaments/12/pairings').flush([
      { id: 1, roundNumber: 1, matchNumber: 4, homeTeam: 'Fügen', awayTeam: 'Absam', homeScore: 2.5, awayScore: 3.5 },
      { id: 2, roundNumber: 2, matchNumber: 1, homeTeam: 'Absam', awayTeam: 'Reutte', homeScore: null, awayScore: null },
    ]);
    http.expectOne('/api/tournaments/12/players/1/results').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const rows = Array.from(el.querySelectorAll('.enc-row'));
    expect(rows.map(r => r.querySelector('.enc-team')?.textContent?.trim())).toEqual(['Fügen', 'Reutte']);
    expect(rows[1].classList).toContain('enc-next');
    expect(el.querySelector('.enc-hint')).not.toBeNull();
    expect(el.querySelector('.enc-fetch')).not.toBeNull();
  });

  it('„Einzelpartien holen" beauftragt den Crawler und zeigt die Spielerseite, sobald sie da ist', fakeAsync(() => {
    loggedIn = true;
    const me = player(1, 'Embacher, Gregor', { teamName: 'Absam' });
    const fixture = create({ tournamentId: '12', chessResultsId: '1479344', player: me, players: [me] });
    http.expectOne('/api/tournaments/12/pairings').flush([
      { id: 1, roundNumber: 1, matchNumber: 4, homeTeam: 'Fügen', awayTeam: 'Absam', homeScore: 2.5, awayScore: 3.5 },
    ]);
    http.expectOne('/api/tournaments/12/players/1/results').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('.enc-fetch') as HTMLButtonElement).click();
    const post = http.expectOne('/api/tournaments/crawl/player-details');
    expect(post.request.body).toEqual({ chessResultsId: '1479344', playerSnrs: [1] });
    post.flush({});
    tick(3000);
    http.expectOne('/api/tournaments/12/players/1/results').flush([]);
    tick(3000);
    http.expectOne('/api/tournaments/12/players/1/results').flush([
      { roundNumber: 1, boardNumber: 1, opponentSnr: 130, opponentName: 'Simenon, Jozef', opponentElo: 2261, points: '1', result: '1' },
    ]);
    fixture.detectChanges();
    expect(el.querySelector('.enc-opp')?.textContent?.trim()).toBe('Simenon, Jozef');
    expect(el.querySelector('.enc-result')?.textContent?.trim()).toBe('1');
    tick(60000);
    http.expectNone('/api/tournaments/12/players/1/results');
    loggedIn = false;
  }));
});
