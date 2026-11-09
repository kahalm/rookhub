import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { PreferencesService } from '../../../core/preferences.service';
import { GamesService } from '../games.service';
import { ClubGameSummaryComponent } from './club-game-summary.component';
import { ClubGameSummaryService } from './club-game-summary.service';
import { SummaryGameBoardComponent } from './summary-game-board.component';

const PGN = '1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *';
const prefs = { boardTheme: 'brown', pieceSet: 'cburnett' };

describe('SummaryGameBoardComponent', () => {
  let fixture: ComponentFixture<SummaryGameBoardComponent>;
  let c: SummaryGameBoardComponent;
  const el = () => fixture.nativeElement as HTMLElement;
  const click = (sel: string) => { el().querySelector<HTMLButtonElement>(sel)!.click(); fixture.detectChanges(); };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SummaryGameBoardComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), { provide: PreferencesService, useValue: prefs }],
    });
    fixture = TestBed.createComponent(SummaryGameBoardComponent);
    fixture.componentRef.setInput('pgn', PGN);
    c = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('blättert mit den Knöpfen und bleibt an den Enden stehen', () => {
    expect(c.ply()).toBe(0);
    expect(el().querySelector<HTMLButtonElement>('.nav-back')!.disabled).toBeTrue();
    click('.nav-forward');
    click('.nav-forward');
    expect(c.ply()).toBe(2);
    click('.nav-back');
    expect(c.ply()).toBe(1);
    click('.nav-end');
    expect(c.ply()).toBe(6);
    expect(el().querySelector<HTMLButtonElement>('.nav-forward')!.disabled).toBeTrue();
    click('.nav-start');
    expect(c.ply()).toBe(0);
  });

  it('Klick auf einen Zug stellt die Stellung NACH diesem Zug aufs Brett', () => {
    click('.move[data-index="2"]');
    expect(c.ply()).toBe(3);
    expect(el().querySelector('.move.active')!.textContent).toContain('f3');
    expect(c.positions().fens[c.ply()]).toContain('RNBQKB1R');
  });

  it('Zugliste nummeriert beide Farben in einer Zeile', () => {
    expect(el().querySelectorAll('.move-row').length).toBe(3);
  });

  it('Pfeiltasten blättern, im Eingabefeld nicht', () => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true }));
    expect(c.ply()).toBe(1);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', cancelable: true }));
    expect(c.ply()).toBe(0);

    const input = document.createElement('input');
    document.body.appendChild(input);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    input.remove();
    expect(c.ply()).toBe(0);
  });

  it('showPly springt auf den Halbzug, begrenzt ihn und übernimmt die Blickrichtung des Diagramms', () => {
    c.showPly(4, true);
    expect(c.ply()).toBe(4);
    expect(c.isFlipped()).toBeTrue();
    c.showPly(99);
    expect(c.ply()).toBe(6);
  });

  const mv = (san: string, fen: string) => ({ san, uci: 'x', fen, from: 'a1', to: 'a2' });
  const FEN_A = 'r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3';

  it('showLine: solange die Linie der Partie folgt, ist es ein Partiezug', () => {
    const line = ['e4', 'e5', 'Nf3'].map(s => mv(s, 'f'));
    c.showLine(line, 2);
    expect(c.ply()).toBe(2);
    expect(c.variation()).toBeNull();
  });

  it('showLine: Abzweig wird Variante, ←/→ laufen durch, ← am Anfang führt zur Partie', () => {
    const line = [mv('e4', 'a'), mv('c5', FEN_A), mv('Nf3', FEN_A)];
    c.showLine(line, 3);
    fixture.detectChanges();
    expect(c.variation()!.base).toBe(1);
    expect(c.variation()!.at).toBe(2);
    expect(c.ply()).toBe(1);
    expect(el().querySelector('.var-move.active')!.textContent).toContain('Nf3');
    c.back();
    expect(c.variation()!.at).toBe(1);
    c.back();
    expect(c.variation()!.at).toBe(0);
    expect(c.currentFen()).toBe(c.positions().fens[1]);
    c.back();
    expect(c.variation()).toBeNull();
    expect(c.ply()).toBe(1);
  });

  it('eigener Zug: Partiezug läuft weiter, anderer Zug ist eine Variante, Blättern verwirft sie', () => {
    c.onUserMove({ from: 'e2', to: 'e4', san: 'e4', fen: c.positions().fens[1] });
    expect(c.ply()).toBe(1);
    expect(c.variation()).toBeNull();
    c.onUserMove({ from: 'c7', to: 'c5', san: 'c5', fen: FEN_A });
    expect(c.variation()!.line.map(m => m.san)).toEqual(['c5']);
    expect(c.currentFen()).toBe(FEN_A);
    c.onUserMove({ from: 'g1', to: 'f3', san: 'Nf3', fen: FEN_A });
    c.goToVariation(1);
    c.onUserMove({ from: 'b1', to: 'c3', san: 'Nc3', fen: FEN_A });
    expect(c.variation()!.line.map(m => m.san)).toEqual(['c5', 'Nc3']);
    c.goTo(4);
    expect(c.variation()).toBeNull();
    expect(c.ply()).toBe(4);
  });

  it('Knopf „zurück zur Partie“ beendet die Variante', () => {
    c.onUserMove({ from: 'd2', to: 'd4', san: 'd4', fen: FEN_A });
    fixture.detectChanges();
    click('.var-exit');
    expect(c.variation()).toBeNull();
    expect(c.ply()).toBe(0);
  });

  it('Blickrichtung ohne Sprung kommt vom Aufrufer', () => {
    expect(c.isFlipped()).toBeFalse();
    fixture.componentRef.setInput('flipped', true);
    expect(c.isFlipped()).toBeTrue();
  });
});

describe('ClubGameSummaryComponent mit Seitenbrett', () => {
  const route = { snapshot: { paramMap: new Map([['id', '7'], ['token', 'abc']]), data: { mode: 'club-share' } } };

  function setup(pgn: string, markdown: string): ComponentFixture<ClubGameSummaryComponent> {
    TestBed.configureTestingModule({
      imports: [ClubGameSummaryComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: route },
        { provide: PreferencesService, useValue: prefs },
        { provide: GamesService, useValue: { sharedClubGame: () => of({ pgn, white: 'A', black: 'B', result: '*', ownerSide: 'black' }) } },
        { provide: ClubGameSummaryService, useValue: { getShared: () => of({ status: 'done', markdown }) } },
      ],
    });
    const fixture = TestBed.createComponent(ClubGameSummaryComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('Knopf am Diagramm springt das Seitenbrett auf dessen Halbzug', () => {
    const fixture = setup(PGN, 'Text\n\n```diagram\nply: 3\norientation: white\n```\n');
    const host = fixture.nativeElement as HTMLElement;
    host.querySelector<HTMLButtonElement>('.to-board')!.click();
    fixture.detectChanges();
    const board = fixture.componentInstance.sideBoard()!;
    expect(board.ply()).toBe(3);
    expect(board.isFlipped()).toBeFalse();
    expect(host.querySelector('.to-ply')).not.toBeNull();
    expect(host.querySelector('.summary-page.with-board')).not.toBeNull();
  });

  it('Züge im Text sind klickbar: Partiezug springt, Variante zeigt sich, Fettschrift bleibt', () => {
    const fixture = setup(PGN, '**2.Sf3** war gut, besser wäre 2.Sc3 Sf6 3.f4 gewesen.\n\n```diagram\nply: 1\ncaption: Nach 1.e4\n```\n');
    const host = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(host.querySelectorAll<HTMLButtonElement>('button.mv'));
    expect(buttons.map(b => b.textContent)).toEqual(['2.Sf3', '2.Sc3', 'Sf6', '3.f4', '1.e4']);
    expect(buttons[0].classList).toContain('mv-b');
    const board = fixture.componentInstance.sideBoard()!;
    buttons[0].click();
    expect(board.ply()).toBe(3);
    expect(board.variation()).toBeNull();
    buttons[3].click();
    expect(board.ply()).toBe(2);
    expect(board.variation()!.line.map(m => m.san)).toEqual(['Nc3', 'Nf6', 'f4']);
    expect(board.variation()!.at).toBe(3);
    buttons[4].click();
    expect(board.ply()).toBe(1);
    expect(board.variation()).toBeNull();
  });

  it('ohne Züge kein Brett und kein Knopf', () => {
    const fixture = setup('', 'Text\n\n```diagram\nply: 0\n```\n');
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('app-summary-game-board')).toBeNull();
    expect(host.querySelector('.to-board')).toBeNull();
    expect(host.querySelector('.summary-page.with-board')).toBeNull();
  });
});
