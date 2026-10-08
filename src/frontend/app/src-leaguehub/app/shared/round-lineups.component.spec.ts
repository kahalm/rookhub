import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LineupsApiService, RoundLineups, formatMoves, parseMoves, points } from '../core/lineups';
import { RoundLineupsComponent } from './round-lineups.component';

// Erfundene Vereine und Spieler.
const DATA: RoundLineups = {
  tnr: 4711, round: 2, date: '2026-10-11', canEdit: true,
  matches: [
    { matchNo: 1, home: 'Bergheim', away: 'Testdorf 1', homePts: 1.5, awayPts: 2.5, own: true, boards: [
      { board: 1, homePlayer: 'Ackermann, Anna', homeTitle: 'FM', homeElo: 2201, awayPlayer: 'Brunner, Bert', awayTitle: null, awayElo: 2105,
        homeColor: 'w', result: '0 - 1', forfeit: 0, moves: 'e4 c5 Nf3', canEditMoves: true },
      { board: 2, homePlayer: 'Clauss, Carl', homeTitle: null, homeElo: null, awayPlayer: 'Dorn, Dora', awayTitle: null, awayElo: 1990,
        homeColor: 's', result: '½ - ½', forfeit: 0, moves: null, canEditMoves: true },
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
    api = jasmine.createSpyObj<LineupsApiService>('LineupsApiService', ['lineups', 'saveMoves']);
    api.lineups.and.resolveTo(DATA);
    TestBed.configureTestingModule({ imports: [RoundLineupsComponent], providers: [{ provide: LineupsApiService, useValue: api }] });
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
