import { Component, OnInit, inject, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { FavoritesService, FavoritePuzzle } from '../../core/favorites.service';
import { SnackbarService } from '../../core/snackbar.service';

@Component({
  selector: 'app-favorites',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressSpinnerModule, TranslateModule,
  ],
  template: `
    <div class="fav-page">
      <div class="head">
        <h1><mat-icon class="title-heart">favorite</mat-icon> {{ 'favorites.title' | translate }}</h1>
        <p class="hint">{{ 'favorites.hint' | translate }}</p>
      </div>

      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (favorites.length === 0) {
        <mat-card class="empty">
          <mat-icon>favorite_border</mat-icon>
          <p>{{ 'favorites.empty' | translate }}</p>
        </mat-card>
      } @else {
        <div class="list">
          @for (f of favorites; track f.id) {
            <mat-card class="fav">
              <div class="info">
                <mat-icon class="src" [matTooltip]="(f.source === 'Book' ? 'favorites.sourceBook' : 'favorites.sourceStandard') | translate">
                  {{ f.source === 'Book' ? 'menu_book' : 'extension' }}
                </mat-icon>
                <div class="meta">
                  <span class="line1">
                    @if (f.title) { <strong class="title">{{ f.title }}</strong> }
                    <span class="rating">{{ 'favorites.rating' | translate }} {{ f.rating }}</span>
                  </span>
                  @if (f.themes) {
                    <span class="themes">
                      @for (t of themeList(f.themes); track t) { <span class="chip">{{ t }}</span> }
                    </span>
                  }
                  <span class="date">{{ f.createdAt | date:'mediumDate' }}</span>
                </div>
              </div>
              <div class="actions">
                <button mat-icon-button (click)="replay(f)" [matTooltip]="'favorites.replay' | translate" [attr.aria-label]="'favorites.replay' | translate">
                  <mat-icon>play_arrow</mat-icon>
                </button>
                <button mat-icon-button (click)="analyze(f)" [matTooltip]="'favorites.analyze' | translate" [attr.aria-label]="'favorites.analyze' | translate">
                  <mat-icon>biotech</mat-icon>
                </button>
                <button mat-icon-button color="warn" (click)="remove(f)" [matTooltip]="'favorites.removeTooltip' | translate" [attr.aria-label]="'favorites.removeTooltip' | translate">
                  <mat-icon>heart_broken</mat-icon>
                </button>
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .fav-page { max-width: 900px; margin: 0 auto; padding: 16px; }
    .head h1 { margin: 0 0 4px; display: flex; align-items: center; gap: 8px; }
    .title-heart { color: #e91e63; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 16px; font-size: 0.9rem; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .list { display: flex; flex-direction: column; gap: 8px; }
    .fav { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 12px; }
    .info { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .src { flex-shrink: 0; opacity: 0.7; }
    .meta { display: flex; flex-direction: column; min-width: 0; gap: 2px; }
    .line1 { display: flex; gap: 8px; align-items: baseline; flex-wrap: wrap; }
    .title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 40ch; }
    .rating { font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .themes { display: flex; gap: 4px; flex-wrap: wrap; }
    .chip { font-size: 0.72rem; padding: 1px 7px; border-radius: 10px; text-transform: capitalize; background: color-mix(in srgb, currentColor 10%, transparent); }
    .date { font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    .actions { display: flex; flex-shrink: 0; }
    @media (max-width: 600px) {
      .fav { flex-direction: column; align-items: stretch; }
      .actions { justify-content: flex-end; }
    }
  `]
})
export class FavoritesComponent implements OnInit {
  favorites: FavoritePuzzle[] = [];
  loading = true;
  private destroyRef = inject(DestroyRef);

  constructor(
    private service: FavoritesService,
    private router: Router,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.service.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.favorites = list; this.loading = false; },
      error: () => { this.loading = false; },
    });
  }

  themeList(themes: string): string[] {
    return themes.split(/[\s,]+/).filter(t => t).slice(0, 6);
  }

  /** Puzzle erneut lösen (quellen-passender Deep-Link zum Solver). */
  replay(f: FavoritePuzzle): void {
    if (f.source === 'Book') {
      this.router.navigate(['/puzzles/book', f.puzzleId], { queryParams: { single: 1 } });
    } else {
      this.router.navigate(['/puzzles', f.puzzleId]);
    }
  }

  /** Stellung + Zugfolge direkt im Analysemodus öffnen. */
  analyze(f: FavoritePuzzle): void {
    const moves = (f.moves || '').split(' ').filter(m => m).join(',');
    this.router.navigate(['/analysis'], {
      queryParams: { fen: f.fen, moves, orientation: this.orientationFor(f), from: '/favorites' },
    });
  }

  remove(f: FavoritePuzzle): void {
    const source = f.source === 'Book' ? 'book' : 'standard';
    this.service.remove(source, f.puzzleId).subscribe({
      next: () => { this.favorites = this.favorites.filter(x => x.id !== f.id); },
      error: () => this.snackbar.warn(this.translate.instant('favorites.removeError')),
    });
  }

  /** Brett-Ausrichtung aus FEN + Quelle: Standard-/Lichess-Puzzles starten mit dem Gegnerzug
   *  (Spieler ist die andere Seite); Buch-Stellungen sind meist schon die Trainingsstellung. */
  private orientationFor(f: FavoritePuzzle): 'white' | 'black' {
    const turn = f.fen.split(' ')[1] === 'b' ? 'black' : 'white';
    if (f.source === 'Book') return turn;
    return turn === 'white' ? 'black' : 'white';
  }
}
