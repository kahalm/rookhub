import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { ClubOverview, LineupsApiService } from '../../core/lineups';
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

describe('ClubOverviewPageComponent', () => {
  let api: jasmine.SpyObj<LineupsApiService>;
  let fixture: ComponentFixture<ClubOverviewPageComponent>;

  async function create(allowed = true): Promise<HTMLElement> {
    api = jasmine.createSpyObj<LineupsApiService>('LineupsApiService', ['overview', 'clubGame', 'clubGameMarks', 'fixturePgn']);
    api.overview.and.resolveTo(DATA);
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
    expect(boards[1].querySelector('.bg-what')?.textContent).toContain('Partie vorhanden');
    expect(el.querySelector('.ov-filter')?.textContent).toContain('(1)');
  });

  it('„Nur mit interessanten Stellungen" blendet Bretter und Begegnungen ohne Marker aus', async () => {
    const el = await create();
    (el.querySelector('.ov-filter input') as HTMLInputElement).click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.lu-match').length).toBe(1);
    expect(el.querySelectorAll('.lu-board').length).toBe(1);
    expect(Array.from(el.querySelectorAll('.ov-round')).map(h => h.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Runde 3 · 24.10.2026']);
  });

  it('ohne league.view die Sperrkarte und keine Abfrage', async () => {
    const el = await create(false);
    expect(el.querySelector('lh-access-gate')).not.toBeNull();
    expect(api.overview).not.toHaveBeenCalled();
  });
});
