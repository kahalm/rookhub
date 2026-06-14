import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

/** Ein Puzzle, an dem der Freund gescheitert ist (GET /api/friends/{userId}/revenge). */
export interface RevengePuzzle {
  puzzleId: number;
  lichessId: string;
  rating: number;
  themes: string | null;
  failCount: number;
  lastFailedAt: string;
}

export interface RevengeList {
  userId: number;
  username: string;
  displayName: string | null;
  puzzles: RevengePuzzle[];
}

/** Lichess-Themen (leerzeichengetrennt) als lesbare, auf `max` gekürzte Liste. Rein, testbar. */
export function formatRevengeThemes(themes: string | null, max = 4): string {
  if (!themes) return '';
  return themes.split(' ').filter(t => t).slice(0, max).join(', ');
}

@Component({
  selector: 'app-friend-revenge',
  standalone: true,
  imports: [CommonModule, RouterModule, MatCardModule, MatListModule, MatButtonModule, MatIconModule, TranslateModule, LoadingSpinnerComponent],
  template: `
    <div class="rev-container">
      <a mat-button routerLink="/friends" class="back-link">
        <mat-icon>arrow_back</mat-icon> {{ 'friends.revenge.back' | translate }}
      </a>

      @if (loading) {
        <app-loading-spinner />
      } @else if (data) {
        <h1>{{ 'friends.revenge.title' | translate:{ name: data.displayName || data.username } }}</h1>
        <p class="hint">{{ 'friends.revenge.hint' | translate:{ name: data.displayName || data.username } }}</p>

        <mat-card>
          <mat-list>
            @for (p of data.puzzles; track p.puzzleId) {
              <mat-list-item>
                <span matListItemTitle>{{ 'friends.revenge.ratingLabel' | translate:{ rating: p.rating } }}</span>
                <span matListItemLine class="themes">{{ formatThemes(p.themes) }}</span>
                <span matListItemLine class="fail">{{ 'friends.revenge.failCount' | translate:{ count: p.failCount } }}</span>
                <button mat-raised-button color="primary" matListItemMeta
                        [routerLink]="['/puzzles', p.puzzleId]" [queryParams]="{ revengeUserId: data.userId }">
                  <mat-icon>sports_martial_arts</mat-icon> {{ 'friends.revenge.solve' | translate }}
                </button>
              </mat-list-item>
            } @empty {
              <p class="empty-text">{{ 'friends.revenge.empty' | translate:{ name: data.displayName || data.username } }}</p>
            }
          </mat-list>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .rev-container { padding: 2rem; max-width: 800px; margin: 0 auto; }
    .back-link { margin-bottom: 0.5rem; }
    h1 { font-size: 1.5rem; margin: 0.25rem 0 0.25rem; }
    .hint { margin: 0 0 1rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    mat-card { padding: 0.5rem 1rem; }
    .themes { font-size: 0.8rem; color: color-mix(in srgb, currentColor 50%, transparent); }
    .fail { font-size: 0.75rem; color: color-mix(in srgb, currentColor 45%, transparent); }
    .empty-text { padding: 1rem; color: color-mix(in srgb, currentColor 47%, transparent); }
    @media (max-width: 768px) {
      .rev-container { padding: 0.75rem; }
      h1 { font-size: 1.3rem; }
    }
  `]
})
export class FriendRevengeComponent implements OnInit {
  loading = true;
  data: RevengeList | null = null;

  constructor(
    private http: HttpClient,
    private route: ActivatedRoute,
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  ngOnInit(): void {
    const userId = Number(this.route.snapshot.paramMap.get('userId'));
    if (!userId) { this.loading = false; return; }

    this.http.get<RevengeList>(`/api/friends/${userId}/revenge`).subscribe({
      next: d => { this.data = d; this.loading = false; },
      error: () => {
        this.loading = false;
        this.snackbar.info(this.translate.instant('friends.revenge.loadError'));
      }
    });
  }

  formatThemes(themes: string | null): string {
    return formatRevengeThemes(themes);
  }
}
