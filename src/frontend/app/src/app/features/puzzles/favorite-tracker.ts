import { FavoritesService, FavoriteSource } from '../../core/favorites.service';

/**
 * Kapselt den „geliebtes Puzzle"-Zustand (Herz) für einen Solver. Hält zwei Flags:
 * {@link currentIsFavorite} (das gerade gelöste/gescheiterte Puzzle) und {@link lastIsFavorite}
 * (das zuletzt abgeschlossene Puzzle hinter dem „<3 last puzzle"-Knopf). Beide bleiben synchron,
 * solange aktuelles und letztes Puzzle dasselbe sind. Add/Remove laufen optimistisch (sofortiges
 * UI-Feedback) und werden bei Serverfehler zurückgerollt; alle Backend-Calls sind idempotent.
 */
export class FavoriteTracker {
  currentIsFavorite = false;
  lastIsFavorite = false;

  constructor(
    private favorites: FavoritesService,
    private source: FavoriteSource,
    private currentId: () => number | null | undefined,
    private lastId: () => number | null | undefined,
    private loggedIn: () => boolean,
  ) {}

  /** Nach dem Lösen/Scheitern: echten Favoriten-Status des aktuellen Puzzles vom Server holen. */
  refresh(): void {
    this.currentIsFavorite = false;
    const id = this.currentId();
    if (!this.loggedIn() || id == null) return;
    this.favorites.contains(this.source, id).subscribe({
      next: f => this.setState(id, f),
      error: () => {},
    });
  }

  toggleCurrent(): void { this.toggle(this.currentId()); }
  toggleLast(): void { this.toggle(this.lastId()); }

  private toggle(id: number | null | undefined): void {
    if (!this.loggedIn() || id == null) return;
    const target = !this.stateOf(id);
    this.setState(id, target); // optimistisch
    const op = target ? this.favorites.add(this.source, id) : this.favorites.remove(this.source, id);
    op.subscribe({
      next: f => this.setState(id, f),
      error: () => this.setState(id, !target), // Rollback
    });
  }

  private stateOf(id: number): boolean {
    return this.currentId() === id ? this.currentIsFavorite : this.lastIsFavorite;
  }

  private setState(id: number, value: boolean): void {
    if (this.currentId() === id) this.currentIsFavorite = value;
    if (this.lastId() === id) this.lastIsFavorite = value;
  }
}
