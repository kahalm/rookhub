import { OfflineService, ENDLESS_POOL_KEY, PUZZLE_POOL_KEY, BOOK_OFFLINE_PREFIX, REPERTOIRE_OFFLINE_PREFIX, COURSES_CACHE_KEY } from './offline.service';

function clearAllStorage() {
  localStorage.clear();
}

describe('OfflineService', () => {
  beforeEach(() => clearAllStorage());
  afterEach(() => clearAllStorage());

  it('defaults to 30 puzzles / 2 endless runs', () => {
    const s = new OfflineService();
    expect(s.puzzleCount).toBe(30);
    expect(s.endlessRuns).toBe(2);
  });

  it('persists + clamps settings', () => {
    const s = new OfflineService();
    s.setPuzzleCount(25);
    s.setEndlessRuns(3);
    expect(s.puzzleCount).toBe(25);
    expect(s.endlessRuns).toBe(3);
    // neue Instanz lädt aus localStorage
    expect(new OfflineService().puzzleCount).toBe(25);
    // clamp
    s.setPuzzleCount(9999);
    expect(s.puzzleCount).toBe(200);
    s.setEndlessRuns(-5);
    expect(s.endlessRuns).toBe(0);
  });

  it('sums cache size across endless/puzzle/book keys + counts books', () => {
    const s = new OfflineService();
    localStorage.setItem(ENDLESS_POOL_KEY, 'x'.repeat(100));
    localStorage.setItem(PUZZLE_POOL_KEY, 'y'.repeat(50));
    localStorage.setItem(BOOK_OFFLINE_PREFIX + '7', 'z'.repeat(30));
    localStorage.setItem(BOOK_OFFLINE_PREFIX + '9', 'z'.repeat(30));
    localStorage.setItem('unrelated_key', 'should-not-count');
    // size > 0 und zählt nur Offline-Keys
    expect(s.cacheSizeBytes()).toBeGreaterThan((100 + 50 + 30 + 30) * 2);   // inkl. Key-Längen *2
    expect(s.cachedBookCount()).toBe(2);
  });

  it('counts downloaded repertoires + includes repertoire/course caches in the size', () => {
    const s = new OfflineService();
    localStorage.setItem(REPERTOIRE_OFFLINE_PREFIX + '3', 'r'.repeat(40));
    localStorage.setItem(REPERTOIRE_OFFLINE_PREFIX + '5', 'r'.repeat(40));
    localStorage.setItem(COURSES_CACHE_KEY, 'c'.repeat(20));
    expect(s.cachedRepertoireCount()).toBe(2);
    expect(s.cacheSizeBytes()).toBeGreaterThan((40 + 40 + 20) * 2);
  });

  it('clearAll removes offline caches but keeps settings + unrelated keys', () => {
    const s = new OfflineService();
    s.setPuzzleCount(15);
    localStorage.setItem(ENDLESS_POOL_KEY, 'pool');
    localStorage.setItem(BOOK_OFFLINE_PREFIX + '1', 'book');
    localStorage.setItem(REPERTOIRE_OFFLINE_PREFIX + '1', 'rep');
    localStorage.setItem(COURSES_CACHE_KEY, 'courses');
    localStorage.setItem('rookhub_user', 'token');
    s.clearAll();
    expect(localStorage.getItem(ENDLESS_POOL_KEY)).toBeNull();
    expect(localStorage.getItem(BOOK_OFFLINE_PREFIX + '1')).toBeNull();
    expect(localStorage.getItem(REPERTOIRE_OFFLINE_PREFIX + '1')).toBeNull();
    expect(localStorage.getItem(COURSES_CACHE_KEY)).toBeNull();
    expect(localStorage.getItem('rookhub_user')).toBe('token');   // fremd bleibt
    expect(s.puzzleCount).toBe(15);                                // Einstellung bleibt
  });

  it('formatSize is human readable', () => {
    const s = new OfflineService();
    expect(s.formatSize(500)).toBe('500 B');
    expect(s.formatSize(2048)).toBe('2.0 KB');
    expect(s.formatSize(3 * 1024 * 1024)).toBe('3.0 MB');
  });
});
