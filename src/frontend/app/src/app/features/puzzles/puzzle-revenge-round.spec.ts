import { of } from 'rxjs';
import { PuzzleRevengeRound } from './puzzle-revenge-round';

describe('PuzzleRevengeRound', () => {
  it('meldet Challenge und Revanche je genau einmal; nur gelöste zählen', () => {
    const r = new PuzzleRevengeRound({} as any);
    expect(r.claimChallenge()).toBeNull();
    r.challengeId = 5;
    expect(r.claimChallenge()).toBe(5);
    expect(r.claimChallenge()).toBeNull();

    r.userId = 9;
    expect(r.claimRevenge(true, false)).toBeNull();   // ohne Puzzle nichts
    expect(r.claimRevenge(true, true)).toBe(9);
    expect(r.claimRevenge(true, true)).toBeNull();
    expect(r.solvedCount).toBe(1);
    r.notified = false;                                // nächstes Puzzle
    expect(r.claimRevenge(false, true)).toBe(9);
    expect(r.solvedCount).toBe(1);
  });

  it('lädt die offenen Puzzles ohne die aktuellen und schließt die Runde mit leerer Schlange', () => {
    const http: any = {
      get: jasmine.createSpy('get').and.returnValue(of({
        displayName: null, username: 'anna',
        puzzles: [{ puzzleId: 1, solvedByViewer: false }, { puzzleId: 2, solvedByViewer: true }, { puzzleId: 3, solvedByViewer: false }],
      })),
    };
    const r = new PuzzleRevengeRound(http);
    r.loadQueue(9, () => [1, undefined]);
    expect(http.get).toHaveBeenCalledWith('/api/friends/9/revenge');
    expect(r.friendName).toBe('anna');
    expect(r.totalCount).toBe(2);
    expect(r.takeNext()).toBe(3);
    expect(r.complete).toBeFalse();
    expect(r.takeNext()).toBeUndefined();
    expect(r.complete).toBeTrue();
  });
});
