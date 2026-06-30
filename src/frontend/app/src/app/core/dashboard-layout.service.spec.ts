import { DashboardLayoutService } from './dashboard-layout.service';

describe('DashboardLayoutService', () => {
  let service: DashboardLayoutService;

  beforeEach(() => {
    localStorage.removeItem('rookhub_dashboard_layout_v2');
    service = new DashboardLayoutService();
  });

  afterEach(() => localStorage.removeItem('rookhub_dashboard_layout_v2'));

  it('returns empty defaults when nothing is stored', () => {
    expect(service.load()).toEqual({ order: [], hidden: [] });
  });

  it('round-trips a saved layout', () => {
    service.save({ order: ['puzzles', 'friends'], hidden: ['stats'] });
    expect(service.load()).toEqual({ order: ['puzzles', 'friends'], hidden: ['stats'] });
  });

  it('ignores non-string and malformed entries', () => {
    localStorage.setItem('rookhub_dashboard_layout_v2', JSON.stringify({ order: ['a', 5, null], hidden: 'nope' }));
    expect(service.load()).toEqual({ order: ['a'], hidden: [] });
  });

  it('falls back to defaults on invalid JSON', () => {
    localStorage.setItem('rookhub_dashboard_layout_v2', '{not json');
    expect(service.load()).toEqual({ order: [], hidden: [] });
  });

  it('reset clears the stored layout', () => {
    service.save({ order: ['puzzles'], hidden: [] });
    service.reset();
    expect(service.load()).toEqual({ order: [], hidden: [] });
  });
});
