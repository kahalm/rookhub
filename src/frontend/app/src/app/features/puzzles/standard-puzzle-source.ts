import { Observable, of } from 'rxjs';
import { PuzzleService, PuzzleDto, PuzzleRatingRange } from './puzzle.service';
import { OfflineService, PUZZLE_POOL_KEY } from '../../core/offline.service';
import { puzzleWindow } from './puzzle-window.util';
import { takeFromPool, takeNearestFromPool } from './endless-prefetch.util';

/** Aktuelle Filter der Standard-Puzzles, live von der Komponente gelesen. */
export interface StandardPuzzleFilters {
  /** Puzzle-Elo des Users (ohne Statistik 1500). */
  elo(): number;
  difficulty(): string;
  excludeSolved(): boolean;
  /** „5 schwächste Themen trainieren" aktiv? */
  worstTagsEnabled(): boolean;
  isLoggedIn(): boolean;
}

/**
 * Woher das nächste Standard-Puzzle kommt (ohne Angular): Deep-Link-Id, vorab geladenes Puzzle, Offline-Pool
 * (localStorage) oder Zufallspuzzle im Rating-Fenster aus Elo + Schwierigkeit. Hält die Lade-/Filter-Epochen,
 * damit veraltete Antworten verworfen werden, und die „schwächsten Themen" des Users.
 */
export class StandardPuzzleSource {
  // ===== Offline-Puzzle-Pool (Standard-Modus) =====
  offlinePuzzlePool: PuzzleDto[] = [];
  nextPuzzle: PuzzleDto | null = null;
  ratingRangeBounds: PuzzleRatingRange | null = null;
  worstThemes: string[] = [];
  /** Monotone Epoche je Ladevorgang. Eine ältere, langsamer auflösende Puzzle-Anfrage
   *  (schnelle Navigation: Auto-Advance + „Weiter" + Prefetch) darf ein neueres Puzzle
   *  nicht überschreiben → veraltete Antworten werden anhand der Epoche verworfen. */
  loadEpoch = 0;
  /** Epoche der Puzzle-Filter (Schwierigkeit, Gelöste ausschließen, schwächste Themen): steigt bei jeder
   *  Filteränderung → Vorab-/Pool-Antworten, die noch mit den alten Filtern angefragt wurden, werden verworfen. */
  filterEpoch = 0;

  constructor(
    private readonly puzzleService: PuzzleService,
    private readonly offline: OfflineService,
    private readonly filters: StandardPuzzleFilters,
  ) {
    this.offlinePuzzlePool = this.loadOfflinePool();
  }

  private loadOfflinePool(): PuzzleDto[] {
    try { return JSON.parse(localStorage.getItem(PUZZLE_POOL_KEY) || '[]') || []; } catch { return []; }
  }
  saveOfflinePool(): void {
    try { localStorage.setItem(PUZZLE_POOL_KEY, JSON.stringify(this.offlinePuzzlePool)); } catch { /* ignore */ }
  }

  /** Lädt im Hintergrund N Puzzles auf der aktuellen Schwierigkeit für Offline-Spiel. */
  prefetchOfflinePool(): void {
    const n = this.offline.puzzleCount;
    if (n <= 0 || !navigator.onLine || this.offlinePuzzlePool.length >= n) return;   // nur auffüllen
    const r = this.ratingRange();
    const windows = Array.from({ length: n }, () => ({ minRating: r.min, maxRating: r.max }));
    const filterEpoch = this.filterEpoch;   // Filter geändert, während die Anfrage lief → verwerfen
    this.puzzleService.getRandomBatch(windows, undefined, this.filters.excludeSolved(), this.worstThemesParam).subscribe({
      next: pool => {
        if (filterEpoch !== this.filterEpoch) return;
        this.offlinePuzzlePool = pool || [];
        this.saveOfflinePool();
      },
      error: () => { /* offline/Fehler: bestehenden Pool behalten */ }
    });
  }

  /**
   * Quelle des nächsten Puzzles: Deep-Link-Id, sonst vorab geladenes, offline der Pool (nächstes Rating),
   * sonst ein Zufallspuzzle. `null` = offline und der Pool ist leer.
   */
  pick(routePuzzleId: number | null): Observable<PuzzleDto> | null {
    if (routePuzzleId) return this.puzzleService.getById(routePuzzleId);
    if (this.nextPuzzle) {
      const next = this.nextPuzzle;
      this.nextPuzzle = null;
      return of(next);
    }
    const r = this.ratingRange();
    if (!navigator.onLine) {
      // Offline: aus dem vorab geladenen Pool bedienen.
      const pooled = takeFromPool(this.offlinePuzzlePool, r.min, r.max)
        ?? takeNearestFromPool(this.offlinePuzzlePool, (r.min + r.max) / 2);
      if (!pooled) return null;
      this.saveOfflinePool();
      return of(pooled);
    }
    return this.puzzleService.getRandom(r.min, r.max, undefined, this.filters.excludeSolved(), this.worstThemesParam);
  }

  prefetchNext(): void {
    const epoch = this.loadEpoch;
    const filterEpoch = this.filterEpoch;   // Filter geändert, während die Anfrage lief → verwerfen
    const r = this.ratingRange();
    this.puzzleService.getRandom(r.min, r.max, undefined, this.filters.excludeSolved(), this.worstThemesParam)
      .subscribe({ next: p => { if (epoch === this.loadEpoch && filterEpoch === this.filterEpoch) this.nextPuzzle = p; }, error: () => {} });
  }

  /** Rating-Fenster aus aktueller Elo + Schwierigkeits-Offset (±RATING_WINDOW). */
  ratingRange(): { min: number; max: number } {
    return puzzleWindow(this.filters.elo(), this.filters.difficulty(), this.ratingRangeBounds);
  }

  /** Puzzle-Filter geändert: vorab geladenes Puzzle + Offline-Pool galten für die alten Filter → verwerfen
   *  (auch noch laufende Anfragen); den Pool füllt danach {@link prefetchOfflinePool} mit den neuen Filtern. */
  discardForFilterChange(): void {
    ++this.filterEpoch;
    this.nextPuzzle = null;
    this.offlinePuzzlePool = [];
    this.saveOfflinePool();
  }

  /** Aktive Schwächen-Themen für die Sichtbar-Anzeige während des Lösens (leer, wenn Filter aus). */
  get activeWorstThemes(): string[] {
    return this.filters.worstTagsEnabled() ? this.worstThemes : [];
  }

  /** Themen-Param für die Puzzle-Auswahl, wenn „schwächste Themen trainieren" aktiv ist (sonst undefined). */
  get worstThemesParam(): string | undefined {
    return this.filters.worstTagsEnabled() && this.worstThemes.length ? this.worstThemes.join(' ') : undefined;
  }

  /** Lädt die schwächsten Themen (nur eingeloggt + aktiviert); ruft danach cb. */
  ensureWorstThemes(cb: () => void): void {
    if (!this.filters.worstTagsEnabled() || !this.filters.isLoggedIn() || this.worstThemes.length) { cb(); return; }
    this.puzzleService.getWorstThemes().subscribe({
      next: themes => { this.worstThemes = themes; cb(); },
      error: () => cb(),
    });
  }
}
