import { buildCompareRows, buildThemeRows, compareValues } from './friend-stats.component';
import { PuzzleStatsDto, ThemeStat } from '../puzzles/puzzle.service';

describe('friend-stats comparison logic', () => {
  const stats = (over: Partial<PuzzleStatsDto>): PuzzleStatsDto => ({
    totalAttempts: 0, solved: 0, accuracy: 0, currentStreak: 0, bestStreak: 0, puzzleElo: 1500, ...over
  });

  describe('compareValues', () => {
    it('marks the higher value as the winner', () => {
      expect(compareValues(10, 5)).toBe('mine');
      expect(compareValues(5, 10)).toBe('theirs');
      expect(compareValues(7, 7)).toBe('tie');
    });
  });

  describe('buildCompareRows', () => {
    it('produces one row per metric with the correct winner', () => {
      const mine = stats({ puzzleElo: 1700, solved: 100, totalAttempts: 120, accuracy: 83, currentStreak: 5, bestStreak: 12 });
      const theirs = stats({ puzzleElo: 1600, solved: 100, totalAttempts: 150, accuracy: 90, currentStreak: 2, bestStreak: 20 });

      const rows = buildCompareRows(mine, theirs);

      expect(rows.length).toBe(6);
      expect(rows.find(r => r.label === 'stats.currentElo')!.winner).toBe('mine');     // 1700 > 1600
      expect(rows.find(r => r.label === 'stats.totalSolved')!.winner).toBe('tie');      // 100 == 100
      expect(rows.find(r => r.label === 'stats.attempts')!.winner).toBe('theirs');      // 150 > 120
      expect(rows.find(r => r.label === 'stats.accuracy')!.winner).toBe('theirs');      // 90 > 83
      expect(rows.find(r => r.label === 'stats.accuracy')!.suffix).toBe('%');
      expect(rows.find(r => r.label === 'stats.bestStreak')!.winner).toBe('theirs');    // 20 > 12
    });
  });

  describe('buildThemeRows', () => {
    const t = (theme: string, attempts: number, solved: number): ThemeStat => ({ theme, attempts, solved });

    it('unions both theme sets and computes per-side accuracy', () => {
      const mine = [t('fork', 10, 8)];          // 80%
      const theirs = [t('fork', 10, 5), t('pin', 4, 4)]; // fork 50%, pin 100%

      const rows = buildThemeRows(mine, theirs);

      const fork = rows.find(r => r.theme === 'fork')!;
      expect(fork.mine).toEqual({ acc: 80, attempts: 10 });
      expect(fork.theirs).toEqual({ acc: 50, attempts: 10 });
      expect(fork.winner).toBe('mine');

      const pin = rows.find(r => r.theme === 'pin')!;
      expect(pin.mine).toBeNull();              // only friend has data
      expect(pin.theirs).toEqual({ acc: 100, attempts: 4 });
      expect(pin.winner).toBe('tie');           // no head-to-head
    });

    it('ignores themes with zero attempts and respects the limit', () => {
      const mine: ThemeStat[] = [t('a', 0, 0), t('b', 5, 1), t('c', 9, 9), t('d', 3, 0)];
      const rows = buildThemeRows(mine, [], 2);

      expect(rows.length).toBe(2);
      expect(rows.some(r => r.theme === 'a')).toBeFalse();   // zero attempts dropped
      expect(rows[0].theme).toBe('c');                        // highest attempts first
    });
  });
});

/** UI-Sweep z1 (z1-rk-friendstats-titel): beide Karten mit gleichem, bündigem Kopf. */
describe('FriendStatsComponent Kartenköpfe', () => {
  it('gibt „Übersicht“ und „Themen-Vergleich“ denselben schlichten Kopf statt mat-card-header', async () => {
    const { TestBed } = await import('@angular/core/testing');
    const { provideRouter, ActivatedRoute, convertToParamMap } = await import('@angular/router');
    const { provideTranslateService } = await import('@ngx-translate/core');
    const { FriendStatsComponent } = await import('./friend-stats.component');
    const { FriendsService } = await import('../../core/friends.service');
    const { PuzzleService } = await import('../puzzles/puzzle.service');
    const { SnackbarService } = await import('../../core/snackbar.service');
    TestBed.configureTestingModule({
      imports: [FriendStatsComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: FriendsService, useValue: {} },
        { provide: PuzzleService, useValue: {} },
        { provide: SnackbarService, useValue: { info: () => {} } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({}) } } },
      ],
    });
    const fixture = TestBed.createComponent(FriendStatsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.friend = { userId: 7, username: 'bob', displayName: null, stats: {} as PuzzleStatsDto, themes: [] } as never;
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const titles = Array.from(el.querySelectorAll('mat-card h2.card-title')).map(h => h.textContent!.trim());
    expect(titles).toEqual(['friends.stats.overview', 'friends.stats.themes']);
    expect(el.querySelector('mat-card-header')).toBeNull();
  });
});
