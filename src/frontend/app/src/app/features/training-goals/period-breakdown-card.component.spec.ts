import { PeriodBreakdownCardComponent } from './period-breakdown-card.component';

function make() {
  const translate = {
    instant: (k: string) => k,
    currentLang: 'en',
    getDefaultLang: () => 'en',
  } as any;
  return new PeriodBreakdownCardComponent(translate);
}

/** Minimaler TrackerDay mit Zeit in einer Quelle + einem Thema. */
function day(date: string): any {
  return {
    date,
    status: 'full',
    bySource: { randomPuzzleSeconds: 600, courseBookSeconds: 0, chessableSeconds: 0 },
    byTheme: { openingSeconds: 600, middlegameSeconds: 0, endgameSeconds: 0, tacticsSeconds: 0, otherSeconds: 0 },
  };
}

describe('PeriodBreakdownCardComponent', () => {
  it('defaults to the "all" period', () => {
    const c = make();
    expect(c.period).toBe('all');
    expect(c.periodLabel).toBe('');
  });

  it('ngOnChanges computes the breakdown rows over the whole series', () => {
    const c = make();
    c.series = [day('2000-01-01'), day('2000-01-02')];
    c.ngOnChanges();
    expect(c.periodSourceRows.length).toBe(1);
    expect(c.periodSourceRows[0].label).toBe('randomPuzzle');
    expect(c.periodSourceRows[0].seconds).toBe(1200);
    expect(c.periodThemeRows[0].label).toBe('opening');
    // 'all' has no paging.
    expect(c.canPrev).toBeFalse();
    expect(c.canNext).toBeFalse();
    expect(c.periodLabel).toBe('trainingGoals.period.all');
  });

  it('setPeriod switches the period and recomputes (day → prev enabled, past history)', () => {
    const c = make();
    c.series = [day('2000-01-01')];
    c.setPeriod('day');
    expect(c.period).toBe('day');
    // Anchor reset to today → the old day is empty, but paging back is possible.
    expect(c.canPrev).toBeTrue();
  });

  it('navPeriod moves the anchor and stays consistent', () => {
    const c = make();
    c.series = [day('2000-01-01')];
    c.setPeriod('day');
    const before = c.anchor;
    c.navPeriod(-1);
    expect(c.anchor).not.toBe(before);
  });
});
