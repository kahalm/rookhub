import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { GameTrainingController, GameTrainingHost } from './game-training-controller';
import { GamesService } from './games.service';
import { MistakeJudgeService } from './mistake-judge.service';
import { Mistake } from './mistakes.util';

const START = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

function mistake(ply: number, white = true): Mistake {
  return {
    ply, white, cls: 'mistake', fenBefore: START, playedSan: 'a3', playedUci: 'a2a3', bestUci: 'e2e4', bestSan: 'e4',
    acceptUci: ['e2e4'], acceptSan: ['e4'], checkUnlisted: false, evalBefore: { cp: 30 }, evalAfter: { cp: -60 },
    lostPercent: 9, candidates: [{ uci: 'e2e4', score: { cp: 30 } }],
  } as Mistake;
}

describe('GameTrainingController', () => {
  let games: jasmine.SpyObj<GamesService>;
  let calls: string[];
  let own: boolean;
  let gameId: number | null;

  function create(): GameTrainingController {
    const host: GameTrainingHost = {
      stopLive: () => calls.push('stopLive'),
      stopTrainingAnalysis: () => calls.push('stopAnalysis'),
      goToMove: i => calls.push('goTo ' + i),
      own: () => own,
      gameId: () => gameId,
    };
    return TestBed.runInInjectionContext(() =>
      new GameTrainingController(host, games, jasmine.createSpyObj<MistakeJudgeService>('judge', ['judge', 'evaluate'])));
  }

  beforeEach(() => {
    calls = [];
    own = true;
    gameId = 7;
    games = jasmine.createSpyObj<GamesService>('GamesService', ['recordMistakes', 'dismissMistake']);
    games.recordMistakes.and.returnValue(of({}) as never);
    games.dismissMistake.and.returnValue(of({}) as never);
  });

  it('zaehlt die Seite des Besitzers, ohne Besitzer die mit mehr Fehlern; der Link schlaegt beide', () => {
    const t = create();
    t.mistakes.set({ white: [mistake(0)], black: [mistake(1, false), mistake(3, false)] });
    expect(t.mistakeSide()).toBe('black');
    t.ownerSide.set('white');
    expect(t.mistakeSide()).toBe('white');
    expect(t.mistakeTotal()).toBe(1);
    t.trainSide.set('black');
    expect(t.mistakeSide()).toBe('black');
    expect(t.mistakeCount('black')).toBe(2);
  });

  it('ausgeblendete Fehler zaehlen nicht mit', () => {
    const t = create();
    t.mistakes.set({ white: [mistake(0), mistake(2)], black: [] });
    t.dismissedPlies.set([2]);
    expect(t.mistakeTotal()).toBe(1);
  });

  it('startet das Training, stoppt die Live-Engine und springt vor den Fehler', () => {
    const t = create();
    t.mistakes.set({ white: [mistake(4)], black: [] });
    t.trainMistakes();
    TestBed.tick();
    expect(calls).toEqual(['stopLive', 'goTo 3']);
    expect(t.training()).not.toBeNull();
  });

  it('?train= startet einmal von selbst, sobald es Aufgaben gibt', () => {
    const t = create();
    t.startFromLink('white');
    TestBed.tick();
    expect(t.training()).toBeNull();
    t.mistakes.set({ white: [mistake(0)], black: [] });
    TestBed.tick();
    expect(t.training()).not.toBeNull();
    t.end();
    t.mistakes.set({ white: [mistake(0)], black: [] });
    TestBed.tick();
    expect(t.training()).withContext('nur einmal').toBeNull();
  });

  it('end meldet den Stand der eigenen Partie, beendet die Analyse und raeumt auf', () => {
    const t = create();
    t.mistakes.set({ white: [mistake(0), mistake(2)], black: [] });
    t.trainMistakes();
    t.end();
    expect(games.recordMistakes).toHaveBeenCalledOnceWith(7, 2, []);
    expect(calls).toContain('stopAnalysis');
    expect(t.training()).toBeNull();
  });

  it('meldet nichts ohne eigene Partie', () => {
    own = false;
    const t = create();
    t.mistakes.set({ white: [mistake(0)], black: [] });
    t.trainMistakes();
    t.end();
    expect(games.recordMistakes).not.toHaveBeenCalled();
  });

  it('restoreDismissed blendet alles wieder ein; ein Fehlschlag stellt den alten Stand her', () => {
    const t = create();
    t.dismissedPlies.set([2, 4]);
    games.dismissMistake.and.returnValue(throwError(() => new Error('x')));
    t.restoreDismissed();
    expect(games.dismissMistake).toHaveBeenCalledWith(7, -1, false, 0);
    expect(t.dismissedPlies()).toEqual([2, 4]);

    games.dismissMistake.and.returnValue(of({}) as never);
    t.restoreDismissed();
    expect(t.dismissedPlies()).toEqual([]);
  });
});
