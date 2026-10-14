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
      expect(rows.find(r => r.label === 'stats.accuracy')!.percent).toBeTrue();
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

/** UI-Sweep a1: Spaltenbreiten am Handy und Breitbild-Stufe ab 2400 px. */
describe('FriendStatsComponent a1 (Spalten, Breitbild)', () => {
  async function render(width: string, username = 'bob') {
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
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = width;
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.friend = { userId: 7, username, displayName: null, stats: {} as PuzzleStatsDto, themes: [] } as never;
    c.themeRows = [{ theme: 'discoveredAttack', mine: null, theirs: { acc: 67, attempts: 3 }, winner: 'tie' }] as never;
    fixture.detectChanges();
    return { host, FriendStatsComponent };
  }

  it('a1-rk-freundesvergleich-spalte: am Handy bekommt die Themenspalte den Rest, Zahlenspalten bleiben schmal', async () => {
    const { host } = await render('480px');
    const row = host.querySelector('.themes-card tbody tr') as HTMLElement;
    const [theme, mine, theirs] = Array.from(row.children).map(td => td.getBoundingClientRect().width);
    expect(theme).toBeGreaterThan(mine * 1.5);
    expect(mine).toBeCloseTo(theirs, 0);
    expect(mine).toBeLessThanOrEqual(6 * 16 + 1);   // Karma-Fenster 1400 px → 6rem (am Handy 5.5rem)
    expect(getComputedStyle(host.querySelector('td.theme-cell')!).hyphens).toBe('manual');
  });

  it('a1-rk-freundesvergleich-breit: ab 2400 px beide Karten nebeneinander in der breiten Seitenspalte', async () => {
    const { host, FriendStatsComponent } = await render('1400px');
    expect(host.querySelectorAll('.fs-cards > mat-card').length).toBe(2);
    const css = (((FriendStatsComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? [])
      .join('\n').replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
    const wide = css.match(/@media \(min-width: ?2400px\) ?\{(.*?\}) ?\}/)?.[1] ?? '';
    expect(wide).toContain('var(--page-max-width-wide, 1880px)');
    expect(wide).toMatch(/\.fs-cards ?\{[^}]*grid-template-columns: ?1fr 1fr/);
    // Unter 2400 px (Karma 1400 px) bleiben die Karten untereinander.
    const [a, b] = Array.from(host.querySelectorAll('.fs-cards > mat-card')).map(c => c.getBoundingClientRect());
    expect(b.top).toBeGreaterThanOrEqual(a.bottom);
  });
  it('b1-rk-freundesvergleich-kopf-naht: Spaltenkopf einzeilig mit Ellipse + Tooltip, Trennlinie je Zeile', async () => {
    const { host, FriendStatsComponent } = await render('480px', 'claude-dev-user-mit-langem-namen');
    const heads = Array.from(host.querySelectorAll('.compare-card thead th')) as HTMLElement[];
    const friendHead = host.querySelector('.compare-card th.friend-col') as HTMLElement;
    expect(friendHead).not.toBeNull();
    const st = getComputedStyle(friendHead);
    expect(st.whiteSpace).toBe('nowrap');
    expect(st.textOverflow).toBe('ellipsis');
    const tops = new Set(heads.slice(1).map(h => Math.round(h.getBoundingClientRect().height)));
    expect(tops.size).withContext('„Du“ und Freundesname gleich hoch').toBe(1);
    const css = (((FriendStatsComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? [])
      .join('\n').replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.compare-table tr ?\{ ?border-bottom: 1px solid var\(--mat-sys-outline-variant\)/);
    expect(css).not.toMatch(/\.compare-table th, \.compare-table td ?\{[^}]*border-bottom/);
    const td = host.querySelector('.themes-card tbody td') as HTMLElement | null;
    expect(getComputedStyle(td!).borderBottomWidth).toBe('0px');
  });
});
