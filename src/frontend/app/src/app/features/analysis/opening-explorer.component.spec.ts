import { TestBed, fakeAsync, tick, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject, of } from 'rxjs';
import { OpeningExplorerComponent } from './opening-explorer.component';
import { ExplorerGames, ExplorerPosition, ExplorerSources, RepertoireExplorerService } from '../repertoire/repertoire-explorer.service';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const AFTER_E4 = 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1';
const AFTER_D4 = 'rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR b KQkq - 0 1';

const ONLINE_ONLY: ExplorerSources = { online: true, local: false, localRatings: [], localSpeeds: [] };

function position(extra: Partial<ExplorerPosition> = {}): ExplorerPosition {
  return {
    status: 'ok', retryAfterSeconds: null, source: 'online', database: 'lichess',
    total: 1000, white: 400, draws: 200, black: 400, opening: null, eco: null,
    moves: [
      { uci: 'e2e4', san: 'e4', games: 600, white: 300, draws: 60, black: 240, averageRating: 1900, opening: "King's Pawn", eco: 'B00' },
      { uci: 'd2d4', san: 'd4', games: 400, white: 160, draws: 80, black: 160, averageRating: 1910, opening: null, eco: null },
    ],
    ...extra,
  };
}

describe('OpeningExplorerComponent', () => {
  let fixture: ComponentFixture<OpeningExplorerComponent>;
  let positionSpy: jasmine.Spy;
  let gamesSpy: jasmine.Spy;

  afterEach(() => {
    localStorage.removeItem('rookhub_analysis_explorer_open');
    localStorage.removeItem('rookhub_explorer_settings');
  });

  const GAMES: ExplorerGames = {
    status: 'ok', retryAfterSeconds: null,
    games: [
      { id: 'g1', white: 'Caruana, Fabiano', whiteRating: 2818, black: 'Carlsen, Magnus', blackRating: 2882, winner: 'white', date: '2019-08', speed: null, url: 'https://lichess.org/g1' },
      { id: 'g2', white: 'Ding, Liren', whiteRating: null, black: 'Nepo', blackRating: 2790, winner: null, date: '2021', speed: null, url: null },
    ],
  };

  function setup(answer: (fen: string) => any = () => of(position()), games: () => any = () => of(GAMES)): void {
    positionSpy = jasmine.createSpy('position').and.callFake((fen: string) => answer(fen));
    gamesSpy = jasmine.createSpy('games').and.callFake(games);
    TestBed.configureTestingModule({
      imports: [OpeningExplorerComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: RepertoireExplorerService, useValue: { sources: () => of(ONLINE_ONLY), position: positionSpy, games: gamesSpy } },
      ],
    });
    fixture = TestBed.createComponent(OpeningExplorerComponent);
  }

  function setFen(fen: string): void {
    fixture.componentRef.setInput('fen', fen);
    fixture.detectChanges();
  }

  it('asks once for the position after the pause and shows moves with share and results', fakeAsync(() => {
    setup();
    setFen(START);
    tick(300);
    fixture.detectChanges();

    expect(positionSpy).toHaveBeenCalledTimes(1);
    expect(positionSpy.calls.mostRecent().args[0]).toBe(START);
    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('e4');
    expect(rows[0].textContent).toContain('60');   // 600 von 1000 Partien
    const c = fixture.componentInstance;
    expect(c.rows()[0].w).toBeCloseTo(0.5, 6);   // 300 von 600
  }));

  it('walking through positions quickly asks only for the last one', fakeAsync(() => {
    setup();
    setFen(START);
    tick(100);
    setFen(AFTER_E4);
    tick(100);
    setFen(AFTER_D4);
    tick(300);

    expect(positionSpy).toHaveBeenCalledTimes(1);
    expect(positionSpy.calls.mostRecent().args[0]).toBe(AFTER_D4);
  }));

  it('a position seen before comes from memory', fakeAsync(() => {
    setup();
    setFen(START); tick(300);
    setFen(AFTER_E4); tick(300);
    setFen(START); tick(300);

    expect(positionSpy).toHaveBeenCalledTimes(2);
  }));

  it('a late answer for a position already left is not shown', fakeAsync(() => {
    const pending = new Subject<ExplorerPosition>();
    setup(fen => fen === START ? pending : of(position({ total: 7 })));
    setFen(START); tick(300);
    setFen(AFTER_E4);
    pending.next(position({ total: 999 }));
    pending.complete();
    tick(300);

    expect(fixture.componentInstance.result()!.total).toBe(7);
  }));

  it('clicking a move plays it', fakeAsync(() => {
    setup();
    const played: string[] = [];
    fixture.componentInstance.playMove.subscribe(s => played.push(s));
    setFen(START); tick(300); fixture.detectChanges();

    fixture.nativeElement.querySelectorAll('.san-btn')[1].click();

    expect(played).toEqual(['d4']);
  }));

  it('folded away it does not ask at all', fakeAsync(() => {
    localStorage.setItem('rookhub_analysis_explorer_open', '0');
    setup();
    setFen(START); tick(300);
    expect(positionSpy).not.toHaveBeenCalled();

    fixture.componentInstance.toggleOpen();
    tick(300);
    expect(positionSpy).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem('rookhub_analysis_explorer_open')).toBe('1');
  }));

  it('a missing token is explained, and a rate limit is waited out by itself', fakeAsync(() => {
    let calls = 0;
    setup(() => of(++calls === 1 ? position({ status: 'rateLimited', retryAfterSeconds: 2, moves: [] }) : position()));
    setFen(START); tick(300); fixture.detectChanges();
    expect(fixture.componentInstance.result()!.status).toBe('rateLimited');

    tick(3000 + 300);
    expect(calls).toBe(2);
    expect(fixture.componentInstance.result()!.status).toBe('ok');
  }));

  it('by default it asks for master games — online here, because this server has no local explorer', fakeAsync(() => {
    setup();
    setFen(START); tick(300);
    const s = positionSpy.calls.mostRecent().args[1];
    expect(s.database).toBe('masters');
    expect(s.source).toBe('online');
    // Der Rückfall wird nicht gespeichert.
    expect(localStorage.getItem('rookhub_explorer_settings')).toBeNull();
  }));

  it('switching to Lichess asks again with rating and speed', fakeAsync(() => {
    setup();
    setFen(START); tick(300);
    fixture.componentInstance.setDatabase('lichess');
    tick(300);

    expect(positionSpy).toHaveBeenCalledTimes(2);
    const s = positionSpy.calls.mostRecent().args[1];
    expect(s.database).toBe('lichess');
    expect(s.ratings.length).toBeGreaterThan(0);
  }));

  it('(i) shows the games that played the move — asked for the position AFTER it', fakeAsync(() => {
    setup();
    setFen(START); tick(300); fixture.detectChanges();

    fixture.nativeElement.querySelectorAll('.info-btn')[0].click();   // e4
    fixture.detectChanges();

    expect(gamesSpy.calls.mostRecent().args[0]).toBe('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1');
    const items = fixture.nativeElement.querySelectorAll('ul.games li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('1-0');
    expect(items[0].textContent).toContain('Caruana, Fabiano (2818) – Carlsen, Magnus (2882)');
    expect(items[0].querySelector('a').getAttribute('href')).toBe('https://lichess.org/g1');
    expect(items[1].textContent).toContain('½-½');
    expect(items[1].textContent).toContain('Ding, Liren – Nepo (2790)');
    expect(items[1].querySelector('a')).toBeNull();   // lokale Meisterpartie: kein Link
  }));

  it('(i) does not play the move, toggles closed, and a second look comes from memory', fakeAsync(() => {
    setup();
    const played: string[] = [];
    fixture.componentInstance.playMove.subscribe(s => played.push(s));
    setFen(START); tick(300); fixture.detectChanges();
    const c = fixture.componentInstance;
    const e4 = c.rows()[0].move;

    c.toggleGames(e4);
    expect(c.expanded()).toBe('e2e4');
    c.toggleGames(e4);
    expect(c.expanded()).toBeNull();
    c.toggleGames(e4);

    expect(gamesSpy).toHaveBeenCalledTimes(1);
    expect(played).toEqual([]);
  }));

  it('another position folds the games away', fakeAsync(() => {
    setup();
    setFen(START); tick(300); fixture.detectChanges();
    fixture.componentInstance.toggleGames(fixture.componentInstance.rows()[1].move);
    expect(fixture.componentInstance.expanded()).toBe('d2d4');

    setFen(AFTER_E4); tick(300);

    expect(fixture.componentInstance.expanded()).toBeNull();
  }));
});
