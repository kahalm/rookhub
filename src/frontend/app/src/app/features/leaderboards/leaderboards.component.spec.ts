import { of, throwError } from 'rxjs';
import { LeaderboardsComponent } from './leaderboards.component';
import { Leaderboards } from './leaderboard.service';

function data(period: string): Leaderboards {
  return {
    period,
    puzzles: [{ name: 'anna', count: 3, rank: 1, isMe: false }, { name: 'ben', count: 1, rank: 2, isMe: true }],
    endlessRuns: [{ name: 'anna', count: 2, rank: 1, isMe: false }],
    courseLines: [],
    dailyPuzzles: [{ name: 'anna', count: 4, rank: 1, isMe: false }],
  };
}

describe('LeaderboardsComponent', () => {
  it('loads the default period (weekly) on init', () => {
    const svc: any = { get: jasmine.createSpy('get').and.callFake((p: string) => of(data(p))) };
    const c = new LeaderboardsComponent(svc);
    c.ngOnInit();

    expect(svc.get).toHaveBeenCalledWith('weekly');
    expect(c.loading).toBeFalse();
    expect(c.rows('puzzles').length).toBe(2);
    expect(c.rows('puzzles')[0].name).toBe('anna');
    expect(c.rows('courseLines').length).toBe(0);
  });

  it('switching period refetches; same period does not', () => {
    const svc: any = { get: jasmine.createSpy('get').and.callFake((p: string) => of(data(p))) };
    const c = new LeaderboardsComponent(svc);
    c.ngOnInit();
    expect(svc.get).toHaveBeenCalledTimes(1);

    c.onPeriod('monthly');
    expect(c.period).toBe('monthly');
    expect(svc.get).toHaveBeenCalledTimes(2);
    expect(svc.get).toHaveBeenCalledWith('monthly');

    c.onPeriod('monthly');               // identische Periode → kein erneuter Call
    expect(svc.get).toHaveBeenCalledTimes(2);
  });

  it('sets error flag when the request fails', () => {
    const svc: any = { get: () => throwError(() => new Error('boom')) };
    const c = new LeaderboardsComponent(svc);
    c.ngOnInit();

    expect(c.error).toBeTrue();
    expect(c.loading).toBeFalse();
    expect(c.rows('puzzles').length).toBe(0);
  });
});
