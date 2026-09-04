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

  it('clearOnLogout räumt auch die lokalen Nutzer-SPUREN ab (nicht nur die Caches)', () => {
    // `logout()` verspricht, dass nichts für den NÄCHSTEN Nutzer desselben Geräts übrig bleibt.
    // Die Endless-Schlüssel standen nicht auf der Liste — und der Endless-Modus überträgt lokale
    // Läufe beim ersten Öffnen INS KONTO: Nutzer B erbte Laufhistorie und Highscore von A, sichtbar
    // bis in die Bestenliste. Ebenso Kalkulations-Notizen, lokaler Kursfortschritt, Menü-Snapshot.
    localStorage.setItem('rookhub_puzzle_offline_pool', '[]');
    localStorage.setItem('rookhub_endless_history', '[{"totalSolved":9}]');
    localStorage.setItem('rookhub_endless_highscore', '42');
    localStorage.setItem('rookhub_calc_local_7', '{"tree":""}');
    localStorage.setItem('rookhub_course_local_solved_3', '[1,2]');
    localStorage.setItem('rookhub_menu_keys', '["dashboard"]');
    localStorage.setItem('rookhub_solve_modes', '{"puzzles":"easy"}');
    // Fremder Schlüssel: bleibt (die App räumt nur ihre eigenen Spuren ab).
    localStorage.setItem('irgendwas_anderes', 'x');

    new OfflineService().clearOnLogout();

    for (const k of ['rookhub_puzzle_offline_pool', 'rookhub_endless_history', 'rookhub_endless_highscore',
                     'rookhub_calc_local_7', 'rookhub_course_local_solved_3', 'rookhub_menu_keys',
                     'rookhub_solve_modes']) {
      expect(localStorage.getItem(k)).toBeNull();
    }
    expect(localStorage.getItem('irgendwas_anderes')).toBe('x');
  });

  it('clearAll (Profil-Knopf „Cache leeren") lässt den laufenden Endless-Lauf stehen', () => {
    // Bewusster Unterschied: „Cache leeren" soll Platz freigeben, nicht die Arbeit des ANGEMELDETEN
    // Nutzers wegwerfen.
    localStorage.setItem('rookhub_puzzle_offline_pool', '[]');
    localStorage.setItem('rookhub_endless_active_game', '{"id":1}');

    new OfflineService().clearAll();

    expect(localStorage.getItem('rookhub_puzzle_offline_pool')).toBeNull();
    expect(localStorage.getItem('rookhub_endless_active_game')).toBe('{"id":1}');
  });
});
