import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ClubOverview, LineupsApiService, brilliantMoves } from '../../core/lineups';
import { GameEvals } from '@rh/features/games/game-review.util';
import { ClubOverviewPageComponent } from './club-overview-page.component';

// Erfundene Vereine und Spieler.
const DATA: ClubOverview = {
  season: '2026/27',
  leagues: [{ tnr: 4711, name: 'Landesliga', rounds: [
    { number: 3, date: '2026-10-24', matches: [
      { matchNo: 1, home: 'Testdorf', away: 'Bergheim', homePts: 2.5, awayPts: 1.5, own: true, boards: [
        { board: 1, homePlayer: 'Ackermann, Anna', homeTitle: null, homeElo: 2001, awayPlayer: 'Brunner, Bert', awayTitle: null, awayElo: 1950,
          homeColor: 'w', result: '1 - 0', forfeit: 0, moves: null, canEditMoves: false,
          game: { source: 'club', clubGameId: 77, plies: 40, result: '1-0', white: 'Ackermann, Anna', black: 'Brunner, Bert', firstMoves: ['e4'], canEdit: false } },
        { board: 2, homePlayer: 'Clauss, Carl', homeTitle: null, homeElo: null, awayPlayer: 'Dorn, Dora', awayTitle: null, awayElo: null,
          homeColor: 's', result: '½ - ½', forfeit: 0, moves: null, canEditMoves: false,
          game: { source: 'club', clubGameId: 78, plies: 30, result: '1/2-1/2', white: 'Dorn, Dora', black: 'Clauss, Carl', firstMoves: ['d4'], canEdit: false } },
      ] },
    ] },
    { number: 4, date: null, matches: [
      { matchNo: 2, home: 'Seewinkel', away: 'Testdorf', homePts: null, awayPts: null, own: true, boards: [
        { board: 1, homePlayer: 'Fink, Franz', homeTitle: null, homeElo: null, awayPlayer: 'Gruber, Gerd', awayTitle: null, awayElo: null,
          homeColor: 'w', result: '', forfeit: 0, moves: null, canEditMoves: false, game: null },
      ] },
    ] },
  ] }],
  marks: { '77': 3 },
};

// Griechisches Geschenk: 7.Lxh7+ opfert den Läufer, Bestzug, Zweitbester +0,50 → brillant (wie game-review.util.spec)
const GREEK_PGN = '[SetUp "1"]\n[FEN "rnbq1rk1/pppnbppp/4p3/3pP3/3P4/2NB1N2/PPP2PPP/R1BQK2R w KQ - 5 7"]\n\n7. Bxh7+ *\n';
const GREEK_EVALS: GameEvals = { status: 'done', analyzed: 1, total: 1, targetDepth: 20, final: { cp: 150 },
  plies: [{ ply: 0, cp: 150, depth: 20, bestUci: 'd3h7', playedUci: 'd3h7', playedCp: 150, secondCp: 50 }] };

describe('ClubOverviewPageComponent', () => {
  let api: jasmine.SpyObj<LineupsApiService>;
  let fixture: ComponentFixture<ClubOverviewPageComponent>;

  async function create(allowed = true): Promise<HTMLElement> {
    api = jasmine.createSpyObj<LineupsApiService>('LineupsApiService', ['overview', 'clubGame', 'clubGameMarks', 'clubGameReviews', 'fixturePgn']);
    api.overview.and.resolveTo(DATA);
    api.clubGameReviews.and.resolveTo({ '78': { pgn: GREEK_PGN, evals: GREEK_EVALS } });
    TestBed.configureTestingModule({ imports: [ClubOverviewPageComponent], providers: [
      { provide: LineupsApiService, useValue: api },
      { provide: AuthService, useValue: { has: () => allowed, isLoggedIn: true } },
      { provide: HandoffService, useValue: jasmine.createSpyObj('HandoffService', ['jumpToRookHub']) },
      provideRouter([]), provideTranslateService({ fallbackLang: 'de' })] });
    fixture = TestBed.createComponent(ClubOverviewPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // 0.848.0, Wunsch 2026-10-26: „Übersichtsseite für den Verein — alle Begegnungen + Partien + Marker neben Partien mit
  // interessanten Stellungen"
  it('zeigt Ligen, Runden, Begegnungen mit Brettern und Partien, Marker nur an Partien mit markierten Stellungen', async () => {
    const el = await create();
    expect(el.querySelector('h1')?.textContent).toContain('2026/27');
    expect(el.querySelector('.ov-league h2')?.textContent).toContain('Landesliga');
    expect(Array.from(el.querySelectorAll('.ov-round')).map(h => h.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Runde 3 · 24.10.2026', 'Runde 4']);
    expect(el.querySelectorAll('.lu-match').length).toBe(2);
    expect(el.querySelector('.lu-score')?.textContent).toContain('2½ : 1½');
    const boards = el.querySelectorAll('.lu-match')[0].querySelectorAll('.lu-board');
    expect(boards.length).toBe(2);
    expect(boards[0].querySelector('.bg-marks')?.textContent).toContain('◆ 3');
    expect(boards[1].querySelector('.bg-marks')).toBeNull();
    // 0.851.1, Wunsch 2026-10-27: „da nur schreiben Partie vorhanden“ — ohne Halbzüge und erste Züge
    expect(boards[1].querySelector('.bg-what')?.textContent?.trim()).toBe('Partie vorhanden');
    expect(el.querySelector('.ov-filter')?.textContent).toContain('(2)');
  });

  // 0.849.0, Wunsch 2026-10-27: „in der Übersicht … zeige auch !! an"
  it('lädt PGN + Bewertungen in einem Aufruf und zeigt „!! n" an Partien mit brillanten Zügen', async () => {
    const el = await create();
    expect(api.clubGameReviews).toHaveBeenCalledOnceWith([77, 78]);
    const boards = el.querySelectorAll('.lu-board');
    expect(boards[0].querySelector('.bg-brilliant')).toBeNull();
    expect(boards[1].querySelector('.bg-brilliant')?.textContent).toContain('!! 1');
    expect(boards[1].querySelector('.bg-brilliant')?.getAttribute('title')).toBe('1 brillanter Zug');
    expect(brilliantMoves(GREEK_PGN, { ...GREEK_EVALS, plies: [{ ...GREEK_EVALS.plies[0], secondCp: 800 }] })).toBe(0);   // ohnehin gewonnen
    expect(brilliantMoves('kein PGN', GREEK_EVALS)).toBe(0);
  });

  it('„Nur mit interessanten Stellungen oder !!" blendet Bretter und Begegnungen ohne Marker aus', async () => {
    const el = await create();
    (el.querySelector('.ov-filter input') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.lu-match').length).toBe(1);
    expect(el.querySelectorAll('.lu-board').length).toBe(2);                 // Brett 1 mit ◆, Brett 2 mit !!
    expect(Array.from(el.querySelectorAll('.ov-round')).map(h => h.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Runde 3 · 24.10.2026']);
  });

  it('ohne league.view die Sperrkarte und keine Abfrage', async () => {
    const el = await create(false);
    expect(el.querySelector('lh-access-gate')).not.toBeNull();
    expect(api.overview).not.toHaveBeenCalled();
  });
});
