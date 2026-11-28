import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatDialog } from '@angular/material/dialog';
import { AuthService } from '@rh/core/auth.service';
import { TournamentHistoryComponent } from './tournament-history.component';
import { PlayerHistory, PlayerHistoryEntry } from './tournament-history.model';
import { EloCalcDialogData, EloCalculatorDialogComponent } from './elo-calculator-dialog.component';

function entry(over: Partial<PlayerHistoryEntry> = {}): PlayerHistoryEntry {
  return {
    chessResultsId: '1107064', name: 'Schach Tirol Open 2025', endDate: '2025-08-30',
    rank: 56, playerCount: 56, rounds: 9, points: 1.5, performanceRating: 1740,
    ratingChange: -51.6, ratingBefore: 1923, hasResult: true, cardFetched: true,
    speed: 'standard', gamesPlayed: null, startNumber: 44, ...over,
  };
}

function history(over: Partial<PlayerHistory> = {}): PlayerHistory {
  return { userId: 1, displayName: 'Ich', status: 'ok', entries: [entry()], pending: 0, fideId: '1693034', ...over };
}

/**
 * Die Links nach draussen im Turnierverlauf: FIDE-Profil (nur mit Kennung) und je Zeile die
 * chess-results-Spielerkarte (mit Startnummer) bzw. die Turnierseite (ohne). Eigene Datei, weil
 * die Haupt-Spec schon ueber 1 000 Zeilen hat.
 */
describe('TournamentHistoryComponent — Links', () => {
  let fixture: ComponentFixture<TournamentHistoryComponent>;
  let http: HttpTestingController;

  beforeEach(() => localStorage.removeItem(TournamentHistoryComponent.ViewKey));
  afterEach(() => {
    localStorage.removeItem(TournamentHistoryComponent.ViewKey);
    TestBed.resetTestingModule();
  });

  async function show(own: PlayerHistory) {
    await TestBed.configureTestingModule({
      imports: [TournamentHistoryComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    spyOnProperty(TestBed.inject(AuthService), 'currentUser', 'get').and.returnValue({
      token: 't', username: 'ich', userId: 1, isAdmin: false,
    });
    fixture = TestBed.createComponent(TournamentHistoryComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    const req = http.expectOne(r => r.url === '/api/tournament-history');
    http.expectOne('/api/tournament-history/friends').flush([]);
    http.expectOne('/api/tournament-history/tracked').flush([]);
    req.flush([own]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement;

  it('zeigt das FIDE-Profil mit Kennung, in neuem Tab', async () => {
    await show(history());
    const link = el().querySelector<HTMLAnchorElement>('a.fide-link');
    expect(link?.getAttribute('href')).toBe('https://ratings.fide.com/profile/1693034');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toContain('noopener');
  });

  it('ohne FIDE-Kennung kein FIDE-Link', async () => {
    await show(history({ fideId: null }));
    expect(el().querySelector('a.fide-link')).toBeNull();
  });

  it('Zeile mit Startnummer verlinkt die Spielerkarte (Elo-Berechnung)', async () => {
    await show(history());
    const link = el().querySelector<HTMLAnchorElement>('.row-line a.row-link');
    expect(link?.getAttribute('href')).toBe('https://chess-results.com/tnr1107064.aspx?lan=0&art=9&snr=44');
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toContain('noopener');
    // Nie ein Link im Zeilen-Knopf.
    expect(el().querySelector('button.row a')).toBeNull();
  });

  it('Zeile ohne Startnummer verlinkt die Turnierseite', async () => {
    await show(history({ entries: [entry({ startNumber: null })] }));
    const link = el().querySelector<HTMLAnchorElement>('.row-line a.row-link');
    expect(link?.getAttribute('href')).toBe('https://chess-results.com/tnr1107064.aspx?lan=0');
  });

  it('„Elo-Rechner" oeffnet den Dialog mit mir als Vorauswahl', async () => {
    await show(history());
    // Die Komponente bringt MatDialogModule mit — ihr eigener Injektor liefert den Dienst.
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const open = spyOn(dialog, 'open').and.callThrough();
    el().querySelector<HTMLButtonElement>('button.elo-calc-open')!.click();
    expect(open).toHaveBeenCalled();
    const [component, config] = open.calls.mostRecent().args;
    expect(component).toBe(EloCalculatorDialogComponent);
    const data = config?.data as EloCalcDialogData;
    expect(data.meKey).toBe('u:1');
    expect(data.loaded['u:1']?.fideId).toBe('1693034');
  });
});
