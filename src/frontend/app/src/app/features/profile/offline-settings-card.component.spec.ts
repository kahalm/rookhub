import { OfflineSettingsCardComponent } from './offline-settings-card.component';

function make(offlineOverride?: any) {
  const offline = offlineOverride ?? {
    puzzleCount: 10, endlessRuns: 2,
    setPuzzleCount: jasmine.createSpy('setPuzzleCount'),
    setEndlessRuns: jasmine.createSpy('setEndlessRuns'),
    formatSize: () => '0 B', cacheSizeBytes: () => 0, cachedBookCount: () => 0, cachedRepertoireCount: () => 0,
    clearAll: jasmine.createSpy('clearAll'),
  };
  const offlineQueue = { pendingCount: () => 0 };
  const snackbar = { success: jasmine.createSpy('success') };
  const translate = { instant: (k: string) => k };
  const c = new OfflineSettingsCardComponent(offline as any, offlineQueue as any, snackbar as any, translate as any);
  return { c, offline, snackbar };
}

describe('OfflineSettingsCardComponent', () => {
  it('ngOnInit seeds the counts and cache size from the offline service', () => {
    const { c } = make();
    c.ngOnInit();
    expect(c.offlinePuzzleCount).toBe(10);
    expect(c.offlineEndlessRuns).toBe(2);
    expect(c.offlineSize).toBe('0 B');
  });

  it('saveOffline pushes the counts into the offline service and reflects clamped values', () => {
    const offline = {
      puzzleCount: 20, endlessRuns: 5,
      setPuzzleCount: jasmine.createSpy('setPuzzleCount'),
      setEndlessRuns: jasmine.createSpy('setEndlessRuns'),
      formatSize: () => '0 B', cacheSizeBytes: () => 0, cachedBookCount: () => 0, cachedRepertoireCount: () => 0,
      clearAll: jasmine.createSpy('clearAll'),
    };
    const { c } = make(offline);
    c.offlinePuzzleCount = 999;
    c.offlineEndlessRuns = 999;
    c.saveOffline();
    expect(offline.setPuzzleCount).toHaveBeenCalledWith(999);
    expect(offline.setEndlessRuns).toHaveBeenCalledWith(999);
    // geklemmte Werte aus dem Service zurückgespiegelt
    expect(c.offlinePuzzleCount).toBe(20);
    expect(c.offlineEndlessRuns).toBe(5);
  });

  it('clearOfflineCache clears the cache, refreshes size and shows success', () => {
    const { c, offline, snackbar } = make();
    c.clearOfflineCache();
    expect(offline.clearAll).toHaveBeenCalled();
    expect(snackbar.success).toHaveBeenCalledWith('profile.offline.cleared');
  });
});

describe('OfflineSettingsCardComponent cacheDetail', () => {
  it('lists books and repertoires only when present', () => {
    const offline = {
      puzzleCount: 10, endlessRuns: 2,
      setPuzzleCount: () => {}, setEndlessRuns: () => {},
      formatSize: () => '1.0 KB', cacheSizeBytes: () => 1024,
      cachedBookCount: () => 2, cachedRepertoireCount: () => 1,
      clearAll: () => {},
    };
    const { c } = make(offline);
    c.ngOnInit();
    expect(c.cacheDetail).toBe(' (profile.offline.books, profile.offline.repertoires)');
    c.offlineBooks = 0;
    c.offlineRepertoires = 0;
    expect(c.cacheDetail).toBe('');
  });
});
