import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { HandoffService } from '@rh/core/handoff.service';
import { LineupsApiService, RoundLineups, formatMoves, parseMoves, points } from '../core/lineups';
import { RoundLineupsComponent } from './round-lineups.component';
import { By } from '@angular/platform-browser';
import { AuthService } from '@rh/core/auth.service';
import { PLAYER_CARD_API } from '@rh/shared/player-card/player-card-api';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';

// Erfundene Vereine und Spieler.
const DATA: RoundLineups = {
  tnr: 4711, round: 2, date: '2026-10-11', canEdit: true,
  matches: [
    { matchNo: 1, home: 'Bergheim', away: 'Testdorf 1', homePts: 1.5, awayPts: 2.5, own: true, boards: [
      { board: 1, homePlayer: 'Ackermann, Anna', homeTitle: 'FM', homeElo: 2201, awayPlayer: 'Brunner, Bert', awayTitle: null, awayElo: 2105,
        homeFide: '1610001', awayFide: null, homeColor: 'w', result: '0 - 1', forfeit: 0, moves: 'e4 c5 Nf3', canEditMoves: true },
      { board: 2, homePlayer: 'Clauss, Carl', homeTitle: null, homeElo: null, awayPlayer: 'Dorn, Dora', awayTitle: null, awayElo: 1990,
        homeFide: null, awayFide: '1610002', homeColor: 's', result: '½ - ½', forfeit: 0, moves: null, canEditMoves: true },
    ] },
    { matchNo: 2, home: 'Talhausen', away: 'Seewinkel', homePts: 2, awayPts: 2, own: false, boards: [
      { board: 1, homePlayer: 'Fink, Franz', homeTitle: null, homeElo: 1800, awayPlayer: 'Gruber, Gerd', awayTitle: null, awayElo: 1750,
        homeColor: 'w', result: '1 - 0', forfeit: 0, moves: null, canEditMoves: false },
    ] },
    { matchNo: 3, home: 'Oberdorf', away: 'Unterdorf', homePts: null, awayPts: null, own: false, boards: [] },
  ],
};

describe('RoundLineupsComponent', () => {
  let fixture: ComponentFixture<RoundLineupsComponent>;
  let api: jasmine.SpyObj<LineupsApiService>;

  async function render(): Promise<HTMLElement> {
    fixture.componentRef.setInput('tnr', 4711);
    fixture.componentRef.setInput('round', 2);
    fixture.componentRef.setInput('teamPrefix', 'Testdorf');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<LineupsApiService>('LineupsApiService', ['lineups', 'saveMoves', 'deleteMoves', 'clubGame', 'fixturePgn']);
    api.lineups.and.resolveTo(DATA);
    TestBed.configureTestingModule({ imports: [RoundLineupsComponent], providers: [{ provide: LineupsApiService, useValue: api },
      { provide: HandoffService, useValue: jasmine.createSpyObj('HandoffService', ['jumpToRookHub']) },
      { provide: PLAYER_CARD_API, useValue: jasmine.createSpyObj('PlayerCardApi', ['card']) },
      { provide: AuthService, useValue: { has: () => false, isLoggedIn: true } },
      provideRouter([]), provideTranslateService({ fallbackLang: 'de' })] });
    fixture = TestBed.createComponent(RoundLineupsComponent);
  });

  it('zeigt alle Begegnungen mit Brettern, Titel, Elo, Ergebnis und hebt die eigene hervor', async () => {
    const el = await render();
    expect(api.lineups).toHaveBeenCalledWith(4711, 2);
    const matches = el.querySelectorAll('.lu-match');
    expect(matches.length).toBe(3);
    expect(matches[0].classList).toContain('own');
    expect(matches[1].classList).not.toContain('own');
    expect(matches[0].querySelector('.lu-score')?.textContent).toContain('1½ : 2½');
    const first = matches[0].querySelector('.lu-board')!;
    expect(first.querySelector('.lu-home')?.textContent).toContain('FM Ackermann, Anna');
    expect(first.querySelector('.lu-home')?.textContent).toContain('2201');
    expect(first.querySelector('.lu-res')?.textContent).toContain('0 - 1');
    expect(first.querySelector('.lu-away')?.textContent).toContain('Brunner, Bert');
    expect(first.querySelector('.sq.w')).toBeTruthy();
  });

  it('zeigt Züge deutsch mit Nummern, „Züge eingeben" nur wo erlaubt, und „noch keine Aufstellung"', async () => {
    const el = await render();
    const boards = el.querySelectorAll('.lu-board');
    expect(boards[0].querySelector('.bm-moves')?.textContent).toContain('1.e4 c5 2.Sf3');
    expect(boards[0].querySelector('.bm-edit')?.textContent).toContain('Züge ändern');
    expect(boards[1].querySelector('.bm-edit')?.textContent).toContain('Züge eingeben');
    expect(boards[2].querySelector('.bm-edit')).toBeNull();   // fremde Begegnung
    expect(el.querySelectorAll('.lu-match')[2].textContent).toContain('Noch keine Aufstellung');
  });

  it('dreht das Brett aus Sicht des eigenen Spielers (Gast, Heim hat Weiß → wir Schwarz)', async () => {
    await render();
    const c = fixture.componentInstance;
    expect(c.ownIsBlack(DATA.matches[0], DATA.matches[0].boards[0])).toBeTrue();
    expect(c.ownIsBlack(DATA.matches[0], DATA.matches[0].boards[1])).toBeFalse();
    expect(c.ownIsBlack(DATA.matches[1], DATA.matches[1].boards[0])).toBeFalse();
  });

  it('meldet einen Ladefehler mit „Erneut versuchen"', async () => {
    api.lineups.and.rejectWith(new Error('weg'));
    const el = await render();
    expect(el.querySelector('.err')?.textContent).toContain('Aufstellungen nicht geladen');
  });

  // 0.727.2: „namen sollten klickbar sein (selbe info wie bei der prognose)"
  it('Namen mit FIDE-ID öffnen die Spielerkarte mit der Farbe an diesem Brett, ohne FIDE-ID bloßer Text', async () => {
    const el = await render();
    const card = fixture.debugElement.query(By.directive(PlayerCardComponent)).componentInstance as PlayerCardComponent;
    const open = spyOn(card, 'open').and.resolveTo();
    const boards = el.querySelectorAll('.lu-board');
    const home1 = boards[0].querySelector('.lu-home button.pl') as HTMLButtonElement;
    expect(home1.textContent?.trim()).toBe('FM Ackermann, Anna');
    expect(boards[0].querySelector('.lu-away button.pl')).toBeNull();          // Brunner ohne FIDE-ID
    expect(boards[0].querySelector('.lu-away')?.textContent).toContain('Brunner, Bert');
    expect(boards[1].querySelector('.lu-home button.pl')).toBeNull();
    home1.click();
    expect(open).toHaveBeenCalledWith('1610001', 'w', 1, null);
    (boards[1].querySelector('.lu-away button.pl') as HTMLButtonElement).click();   // Heim hat Schwarz → Gast Weiß
    expect(open).toHaveBeenCalledWith('1610002', 'w', 2, null);
    expect(el.querySelectorAll('button.pl').length).toBe(2);
  });

  // 0.724.0: „wenn ich die Partie hab, soll er nicht Züge eingeben lassen, sondern die Partie ausweisen"
  const PGN = '[White "Ackermann, Anna"]\n[Black "Brunner, Bert"]\n\n1. e4 c5 2. Nf3 d6 1-0';
  function withGames(): RoundLineups {
    const d: RoundLineups = JSON.parse(JSON.stringify(DATA));
    Object.assign(d.matches[0].boards[0], { canEditMoves: false, canDeleteMoves: true, game: { source: 'club', clubGameId: 168, plies: 81,
      result: '1-0', white: 'Ackermann, Anna', black: 'Brunner, Bert', firstMoves: ['e4', 'c5', 'Nf3', 'd6', 'd4', 'cxd4', 'Nxd4', 'Nf6', 'Nc3', 'a6'],
      canEdit: true } });
    Object.assign(d.matches[1].boards[0], { game: { source: 'profile', clubGameId: null, plies: 4, result: '1-0', white: 'Fink, Franz',
      black: 'Gruber, Gerd', firstMoves: ['d4', 'd5', 'c4', 'e6'], canEdit: false } });
    return d;
  }

  it('Brett mit Partie: Partie-Zeile statt „Züge eingeben", alter Handeintrag nur noch „ersetzt"', async () => {
    api.lineups.and.resolveTo(withGames());
    const el = await render();
    const boards = el.querySelectorAll('.lu-board');
    const what = boards[0].querySelector('.bg-what')?.textContent ?? '';
    expect(what).toContain('Partie vorhanden · 81 Halbzüge');
    expect(what).toContain('1.e4 c5 2.Sf3 d6 3.d4 cxd4 4.Sxd4 Sf6 5.Sc3 a6 …');
    expect(boards[0].querySelector('.bm-edit')).toBeNull();
    expect(boards[0].querySelector('.bm-replaced')?.textContent).toContain('ersetzt durch die Partie');
    expect(boards[0].querySelector('.bm-del')).not.toBeNull();
    expect(boards[0].querySelector('.bg-edit')?.getAttribute('href')).toBe('/verein?bearbeiten=168');
    expect(boards[0].querySelector('.bg-fix')?.getAttribute('href')).toBe('/verein/partie/168/korrigieren');
    expect(boards[1].querySelector('.bm-edit')?.textContent).toContain('Züge eingeben');   // Brett ohne Partie bleibt
    expect(boards[2].querySelector('.bg-what')?.textContent).toContain('Partie vorhanden · 4 Halbzüge · 1.d4 d5 2.c4 e6');
    expect(boards[2].querySelector('.bg-edit')).toBeNull();
  });

  it('Nachspielen holt das PGN: Vereinspartie über den Club-Endpunkt, Spielerkarte über …/games?team=', async () => {
    api.lineups.and.resolveTo(withGames());
    api.clubGame.and.resolveTo({ pgn: PGN, analysis: { status: 'done' } });
    api.fixturePgn.and.resolveTo(PGN);
    const el = await render();
    const boards = el.querySelectorAll('.lu-board');
    (boards[0].querySelector('.bg-replay') as HTMLButtonElement).click();
    (boards[2].querySelector('.bg-replay') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.clubGame).toHaveBeenCalledWith(168);
    expect(api.fixturePgn).toHaveBeenCalledWith(4711, 2, 'Talhausen', 1);
    expect(el.querySelectorAll('lh-game-replay').length).toBe(2);
  });

  it('Löschen eines ersetzten Handeintrags', async () => {
    api.lineups.and.resolveTo(withGames());
    api.deleteMoves.and.resolveTo();
    const el = await render();
    (el.querySelector('.bm-del') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.deleteMoves).toHaveBeenCalledWith({ tnr: 4711, round: 2, matchNo: 1, board: 1 });
    expect(el.querySelector('.bm-replaced')).toBeNull();
  });
});

describe('lineups (Züge lesen/schreiben)', () => {
  it('liest Nummern, deutsche Buchstaben, Rochade mit Null und Kommentare', () => {
    expect(parseMoves('1.e4 e5 2.Sf3 Sc6 3.Lb5 a6 4.0-0 {gut} (4.La4) 1-0').sans).toEqual(['e4', 'e5', 'Nf3', 'Nc6', 'Bb5', 'a6', 'O-O']);
    expect(parseMoves('e4 c5 Nf3').error).toBeNull();
  });

  it('nennt den ersten falschen Zug und behält die Züge davor', () => {
    const p = parseMoves('1.e4 e5 2.Ke3');
    expect(p.sans).toEqual(['e4', 'e5']);
    expect(p.error).toEqual({ kind: 'illegal', ply: 3, move: 'Ke3' });
  });

  it('höchstens 60 Halbzüge', () => {
    const sixty = Array(15).fill('Nf3 Nf6 Ng1 Ng8').join(' ');
    expect(parseMoves(sixty).error).toBeNull();
    expect(parseMoves(sixty + ' e4').error).toEqual({ kind: 'tooLong' });
  });

  it('schreibt deutsch mit Zugnummern, Punkte mit ½', () => {
    expect(formatMoves('e4 c5 Nf3 d6 Bb5+')).toBe('1.e4 c5 2.Sf3 d6 3.Lb5+');
    expect(points(2.5)).toBe('2½');
    expect(points(0.5)).toBe('½');
    expect(points(3)).toBe('3');
  });
});
