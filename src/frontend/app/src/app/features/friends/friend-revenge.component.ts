import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FriendsService } from '../../core/friends.service';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { PuzzleThemePipe, puzzleThemeList } from '../puzzles/puzzle-theme.pipe';

/** Ein Puzzle, an dem der Freund gescheitert ist (GET /api/friends/{userId}/revenge). */
export interface RevengePuzzle {
  puzzleId: number;
  lichessId: string;
  rating: number;
  themes: string | null;
  failCount: number;
  lastFailedAt: string;
  solvedByViewer: boolean;
}

export interface RevengeList {
  userId: number;
  username: string;
  displayName: string | null;
  puzzles: RevengePuzzle[];
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-friend-revenge',
  standalone: true,
  imports: [CommonModule, RouterModule, MatCardModule, MatListModule, MatButtonModule, MatIconModule, IconLabelDirective, TranslatePipe, LoadingSpinnerComponent, PuzzleThemePipe],
  template: `
    <div class="rev-container">
      <a mat-button routerLink="/friends" class="back-link">
        <mat-icon>arrow_back</mat-icon> {{ 'friends.revenge.back' | translate }}
      </a>

      @if (loading) {
        <app-loading-spinner />
      } @else if (data) {
        <h1>{{ 'friends.revenge.title' | translate:{ name: data.displayName || data.username } }}</h1>
        <!-- d5-rk-revanche-leerzustand-text: der Auftrag „knack sie“ nur, wenn es offene Puzzles gibt — sonst erklärt
             der Leerzustand allein (DESIGN 5.10). -->
        @if (open.length > 0) {
          <p class="hint">{{ 'friends.revenge.hint' | translate:{ name: data.displayName || data.username } }}</p>
        }

        <mat-card>
          <mat-list>
            @for (p of open; track p.puzzleId) {
              <mat-list-item class="rev-item">
                <span matListItemTitle>{{ 'friends.revenge.ratingLabel' | translate:{ rating: p.rating } }}</span>
                <span matListItemLine class="themes">@for (t of themeList(p.themes); track t) { <span class="theme">{{ t | puzzleTheme }}</span> }</span>
                <span matListItemLine class="fail">{{ 'friends.revenge.failCount' | translate:{ count: p.failCount } }}</span>
                <!-- UI-Sweep 2026-10-11 (d1-rk-revenge-loesen-knopf-kaputt): matListItemMeta auf einem Container, nicht
                     auf dem Knopf — sonst überschreiben Schrift, Farbe und Ausrichtung des Listen-Endbereichs den Knopf. -->
                <div matListItemMeta class="rev-action">
                  <button mat-flat-button color="primary"
                          [routerLink]="['/puzzles', p.puzzleId]" [queryParams]="{ revengeUserId: data.userId }">
                    <mat-icon>sports_martial_arts</mat-icon> {{ 'friends.revenge.solve' | translate }}
                  </button>
                </div>
              </mat-list-item>
            } @empty {
              <p class="empty-state">{{ 'friends.revenge.empty' | translate:{ name: data.displayName || data.username } }}</p>
            }
          </mat-list>
        </mat-card>

        @if (done.length > 0) {
          <h2 class="done-title">{{ 'friends.revenge.doneTitle' | translate:{ count: done.length } }}</h2>
          <mat-card class="done-card">
            <mat-list>
              @for (p of done; track p.puzzleId) {
                <mat-list-item class="rev-item">
                  <mat-icon matListItemIcon class="done-icon">check_circle</mat-icon>
                  <span matListItemTitle>{{ 'friends.revenge.ratingLabel' | translate:{ rating: p.rating } }}</span>
                  <span matListItemLine class="themes">@for (t of themeList(p.themes); track t) { <span class="theme">{{ t | puzzleTheme }}</span> }</span>
                  <div matListItemMeta class="rev-action">
                    <button mat-icon-button [routerLink]="['/puzzles', p.puzzleId]"
                            [appIconLabel]="'friends.revenge.review' | translate">
                      <mat-icon>visibility</mat-icon>
                    </button>
                  </div>
                </mat-list-item>
              }
            </mat-list>
          </mat-card>
        }
      }
    </div>
  `,
  styles: [`
    .rev-container { padding: 2rem; max-width: 800px; margin: 0 auto; }
    .back-link { margin-bottom: 0.5rem; }
    h1 { font-size: 1.5rem; margin: 0.25rem 0 0.25rem; }
    .hint { margin: 0 0 1rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    mat-card { padding: 0.5rem 1rem; }
    /* d7-rk-revenge-leer-titelabstand: ohne Hinweiszeile (Leerzustand) klebte die Karte 4 px unter dem Titel —
       derselbe Abstand wie mit Hinweis (≈ 16 px). */
    h1 + mat-card { margin-top: 0.75rem; }
    /* Themen umbrechen statt am Handy mit „…“ abzuschneiden — die Zeile wächst mit. */
    .rev-item.mat-mdc-list-item { height: auto; padding-top: 8px; padding-bottom: 8px; }
    .themes { font-size: 0.8rem; white-space: normal; color: color-mix(in srgb, currentColor 50%, transparent); }
    /* UI-Sweep z1 (z1-rk-revenge-themen-umbruch): die Zeilenregel der Material-Liste (nowrap + „…“) gezielt aufheben —
       das einfache white-space oben verlor gegen sie. */
    .rev-item .themes.mat-mdc-list-item-line {
      white-space: normal; overflow: visible; text-overflow: clip; height: auto; -webkit-line-clamp: unset; display: block;
    }
    .theme + .theme::before { content: ', '; }
    .rev-action { display: flex; align-items: center; height: 100%; }
    .fail { font-size: 0.75rem; color: color-mix(in srgb, currentColor 45%, transparent); }
    .done-title { font-size: 1.05rem; margin: 1.25rem 0 0.5rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .done-card { opacity: 0.85; }
    .done-icon { color: var(--rh-success); }
    @media (max-width: 768px) {
      .rev-container { padding: 0.75rem; }
      h1 { font-size: 1.3rem; }
    }
    /* Am Handy nur das Listenpolster — sonst blieben den Themen neben dem Knopf ~170 px. */
    @media (max-width: 599.98px) {
      mat-card { padding: 4px 0; }
    }
  `]
})
export class FriendRevengeComponent implements OnInit {
  loading = true;
  data: RevengeList | null = null;
  /** Noch offene Revanchen (vom Rächer noch nicht gelöst). */
  open: RevengePuzzle[] = [];
  /** Bereits erledigte Revanchen (vom Rächer gelöst). */
  done: RevengePuzzle[] = [];

  constructor(
    private friendsService: FriendsService,
    private route: ActivatedRoute,
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  ngOnInit(): void {
    const userId = Number(this.route.snapshot.paramMap.get('userId'));
    if (!userId) { this.loading = false; return; }

    this.friendsService.getRevenge<RevengeList>(userId).subscribe({
      next: d => {
        this.data = d;
        this.open = d.puzzles.filter(p => !p.solvedByViewer);
        this.done = d.puzzles.filter(p => p.solvedByViewer);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.snackbar.info(this.translate.instant('friends.revenge.loadError'));
      }
    });
  }

  themeList(themes: string | null): string[] {
    return puzzleThemeList(themes);
  }
}
