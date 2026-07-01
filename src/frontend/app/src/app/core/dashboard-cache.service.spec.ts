import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { DashboardCacheService } from './dashboard-cache.service';

/** Fake AuthService reicht, wir brauchen nur currentUser?.userId. */
function makeAuth(userId?: number): Partial<AuthService> {
  return { currentUser: userId != null ? ({ userId } as any) : null };
}

describe('DashboardCacheService', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('returns null when not logged in', () => {
    TestBed.configureTestingModule({
      providers: [DashboardCacheService, { provide: AuthService, useValue: makeAuth(undefined) }],
    });
    expect(TestBed.inject(DashboardCacheService).load()).toBeNull();
  });

  it('returns null when nothing cached yet', () => {
    TestBed.configureTestingModule({
      providers: [DashboardCacheService, { provide: AuthService, useValue: makeAuth(42) }],
    });
    expect(TestBed.inject(DashboardCacheService).load()).toBeNull();
  });

  it('save + load round-trips the snapshot for the current user', () => {
    TestBed.configureTestingModule({
      providers: [DashboardCacheService, { provide: AuthService, useValue: makeAuth(7) }],
    });
    const svc = TestBed.inject(DashboardCacheService);
    svc.save({
      repertoireCount: 3, courseCount: 5, pinnedCourses: [],
      subscriptionCount: 1, subscriptions: [], friendCount: 4,
      favoriteCount: 2, puzzleSolved: 1200, puzzleAccuracy: 87, puzzleElo: 2300,
    });
    const loaded = svc.load();
    expect(loaded).not.toBeNull();
    expect(loaded!.puzzleElo).toBe(2300);
    expect(loaded!.puzzleSolved).toBe(1200);
    expect(loaded!.courseCount).toBe(5);
    expect(loaded!.repertoireCount).toBe(3);
  });

  it('does not read another user\'s cache', () => {
    // Speichern als User 7.
    TestBed.configureTestingModule({
      providers: [DashboardCacheService, { provide: AuthService, useValue: makeAuth(7) }],
    });
    TestBed.inject(DashboardCacheService).save({
      repertoireCount: 99, courseCount: 99, pinnedCourses: [],
      subscriptionCount: 0, subscriptions: [], friendCount: 0,
      favoriteCount: 0, puzzleSolved: 0, puzzleAccuracy: 0, puzzleElo: 0,
    });
    // Als User 8 wieder aufrufen → nichts sehen.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [DashboardCacheService, { provide: AuthService, useValue: makeAuth(8) }],
    });
    expect(TestBed.inject(DashboardCacheService).load()).toBeNull();
  });

  it('load returns null on corrupted JSON', () => {
    TestBed.configureTestingModule({
      providers: [DashboardCacheService, { provide: AuthService, useValue: makeAuth(9) }],
    });
    localStorage.setItem('rookhub_dashboard_cache_v1_u9', '{not json');
    expect(TestBed.inject(DashboardCacheService).load()).toBeNull();
  });
});
