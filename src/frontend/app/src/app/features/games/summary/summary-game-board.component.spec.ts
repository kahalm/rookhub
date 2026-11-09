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

  it('ohne Züge kein Brett und kein Knopf', () => {
    const fixture = setup('', 'Text\n\n```diagram\nply: 0\n```\n');
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('app-summary-game-board')).toBeNull();
    expect(host.querySelector('.to-board')).toBeNull();
    expect(host.querySelector('.summary-page.with-board')).toBeNull();
  });
});
