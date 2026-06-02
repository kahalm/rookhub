import { Injectable } from '@angular/core';
import { forkJoin, of, catchError } from 'rxjs';
import { PuzzleService, PuzzleDto } from '../features/puzzles/puzzle.service';
import { PreferencesService } from './preferences.service';
import { AuthService } from './auth.service';
import { OfflineService, PUZZLE_POOL_KEY } from './offline.service';
import { EndlessStorageService, EndlessConfig } from '../features/puzzles/endless-storage.service';
import { puzzleWindow } from '../features/puzzles/puzzle-window.util';
import { buildEndlessRunWindows } from '../features/puzzles/endless-prefetch.util';

const ENDLESS_DEFAULT_CONFIG: EndlessConfig = { startElo: 700, themes: '', stockfishDepth: 16 };

/**
 * Füllt die Offline-Pools (Standard-Puzzle + Endless) bereits beim App-Start, sobald online —
 * nicht erst beim ersten Öffnen des jeweiligen Modus. Greift nur, wenn noch nicht genug gecacht
 * ist; nutzt dieselbe Fenster-Logik wie die Live-Modi (puzzle-window.util / endless-prefetch.util).
 */
@Injectable({ providedIn: 'root' })
export class OfflinePrefetchService {
  constructor(
    private puzzleService: PuzzleService,
    private prefs: PreferencesService,
    private auth: AuthService,
    private offline: OfflineService,
    private endlessStorage: EndlessStorageService,
  ) {}

  /**
   * Beide Pools vorab laden (nur online). Idempotent über die Pool-Längen-Guards —
   * darf bei Start UND bei jedem 'online'-Event aufgerufen werden (füllt nur, was fehlt).
   */
  prefetchAll(): void {
    if (typeof navigator !== 'undefined' && !navigator.onLine) return;
    this.prefetchStandardPool();
    this.prefetchEndlessPool();
  }

  private prefetchStandardPool(): void {
    const n = this.offline.puzzleCount;
    if (n <= 0 || this.poolLength(PUZZLE_POOL_KEY) >= n) return;
    const stats$ = (this.auth.isLoggedIn
      ? this.puzzleService.getStats(this.prefs.visualization)
      : this.puzzleService.getAnonymousStats()).pipe(catchError(() => of(null)));
    const bounds$ = this.puzzleService.getRatingRange().pipe(catchError(() => of(null)));
    forkJoin([stats$, bounds$]).subscribe(([stats, bounds]) => {
      const w = puzzleWindow(stats?.puzzleElo ?? 1500, this.prefs.puzzleDifficulty || 'normal', bounds);
      const windows = Array.from({ length: n }, () => ({ minRating: w.min, maxRating: w.max }));
      this.puzzleService.getRandomBatch(windows, undefined, false).subscribe({
        next: pool => this.savePool(PUZZLE_POOL_KEY, pool || []),
        error: () => { /* offline/Fehler: ignorieren */ },
      });
    });
  }

  private prefetchEndlessPool(): void {
    const runs = Math.max(1, this.offline.endlessRuns);
    if (this.endlessStorage.loadOfflinePool().length > 0) return;   // schon ein Run gecacht
    const config = this.endlessStorage.loadConfig({ ...ENDLESS_DEFAULT_CONFIG });
    const history = this.endlessStorage.loadSessionHistory();
    this.puzzleService.getRatingRange().pipe(catchError(() => of(null))).subscribe(bounds => {
      const windows = buildEndlessRunWindows(config, history, bounds?.max ?? 3000, runs);
      if (!windows.length) return;
      const themes = config.themes.trim() || undefined;
      this.puzzleService.getRandomBatch(windows, themes).subscribe({
        next: pool => this.endlessStorage.saveOfflinePool(pool || []),
        error: () => { /* offline/Fehler: ignorieren */ },
      });
    });
  }

  private poolLength(key: string): number {
    try {
      const arr = JSON.parse(localStorage.getItem(key) || '[]');
      return Array.isArray(arr) ? arr.length : 0;
    } catch { return 0; }
  }

  private savePool(key: string, pool: PuzzleDto[]): void {
    try { localStorage.setItem(key, JSON.stringify(pool)); } catch { /* Quota */ }
  }
}
