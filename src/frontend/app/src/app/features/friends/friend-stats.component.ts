import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FriendsService } from '../../core/friends.service';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { PuzzleService, PuzzleStatsDto, ThemeStat } from '../puzzles/puzzle.service';
import { PuzzleThemePipe } from '../puzzles/puzzle-theme.pipe';
import { PctPipe } from '../../shared/format/pct';

/** Antwort von GET /api/friends/{userId}/stats. */
export interface FriendStats {
  userId: number;
  username: string;
  displayName: string | null;
  stats: PuzzleStatsDto;
  themes: ThemeStat[];
}

/** Eine Vergleichszeile (Metrik + beide Werte + wer vorn liegt). */
export interface CompareRow {
  label: string;
  mine: number;
  theirs: number;
  /** Prozentwert — angezeigt über `pct` in der Schreibweise der Sprache (en „78.6%“, de „78,6 %“). */
  percent?: boolean;
  winner: 'mine' | 'theirs' | 'tie';
}

/** Eine Themen-Vergleichszeile (Genauigkeit beider Seiten je Thema). */
export interface ThemeRow {
  theme: string;
  mine: { acc: number; attempts: number } | null;
  theirs: { acc: number; attempts: number } | null;
  winner: 'mine' | 'theirs' | 'tie';
}

/** Höher = besser für alle Vergleichsmetriken (rein, testbar). */
export function compareValues(mine: number, theirs: number): 'mine' | 'theirs' | 'tie' {
  if (mine > theirs) return 'mine';
  if (theirs > mine) return 'theirs';
  return 'tie';
}

/** Baut die Metrik-Vergleichszeilen (Elo/Gelöst/Versuche/Genauigkeit/Serien). Rein, testbar. */
export function buildCompareRows(mine: PuzzleStatsDto, theirs: PuzzleStatsDto): CompareRow[] {
  return [
    { label: 'stats.currentElo', mine: mine.puzzleElo, theirs: theirs.puzzleElo, winner: compareValues(mine.puzzleElo, theirs.puzzleElo) },
    { label: 'stats.totalSolved', mine: mine.solved, theirs: theirs.solved, winner: compareValues(mine.solved, theirs.solved) },
    { label: 'stats.attempts', mine: mine.totalAttempts, theirs: theirs.totalAttempts, winner: compareValues(mine.totalAttempts, theirs.totalAttempts) },
    { label: 'stats.accuracy', mine: mine.accuracy, theirs: theirs.accuracy, percent: true, winner: compareValues(mine.accuracy, theirs.accuracy) },
    { label: 'stats.currentStreak', mine: mine.currentStreak, theirs: theirs.currentStreak, winner: compareValues(mine.currentStreak, theirs.currentStreak) },
    { label: 'stats.bestStreak', mine: mine.bestStreak, theirs: theirs.bestStreak, winner: compareValues(mine.bestStreak, theirs.bestStreak) },
  ];
}

/** Vereint beide Themenlisten, rechnet Genauigkeiten, sortiert nach Aktivität, kappt bei `limit`. Rein, testbar. */
export function buildThemeRows(mine: ThemeStat[], theirs: ThemeStat[], limit = 15): ThemeRow[] {
  const acc = (t: ThemeStat) => Math.round((t.solved / t.attempts) * 100);
  const mineMap = new Map(mine.filter(t => t.attempts > 0).map(t => [t.theme, t]));
  const theirsMap = new Map(theirs.filter(t => t.attempts > 0).map(t => [t.theme, t]));
  const themes = new Set([...mineMap.keys(), ...theirsMap.keys()]);

  return [...themes]
    .map(theme => {
      const m = mineMap.get(theme);
      const th = theirsMap.get(theme);
      const mineCell = m ? { acc: acc(m), attempts: m.attempts } : null;
      const theirsCell = th ? { acc: acc(th), attempts: th.attempts } : null;
      let winner: 'mine' | 'theirs' | 'tie' = 'tie';
      if (mineCell && theirsCell) winner = compareValues(mineCell.acc, theirsCell.acc);
      return { theme, mine: mineCell, theirs: theirsCell, winner, _weight: (m?.attempts ?? 0) + (th?.attempts ?? 0) };
    })
    .sort((a, b) => b._weight - a._weight)
    .slice(0, limit)
    .map(({ _weight, ...row }) => row);
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-friend-stats',
  standalone: true,
  imports: [CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, TranslatePipe, LoadingSpinnerComponent, PuzzleThemePipe, PctPipe],
  template: `
    <div class="fs-container">
      <a mat-button routerLink="/friends" class="back-link">
        <mat-icon>arrow_back</mat-icon> {{ 'friends.stats.back' | translate }}
      </a>

      @if (loading) {
        <app-loading-spinner />
      } @else if (friend) {
        <h1>{{ 'friends.stats.title' | translate }}</h1>

        <!-- UI-Sweep z1 (z1-rk-friendstats-titel): beide Karten mit demselben schlichten Kopf, bündig mit den Zellen
             (statt mat-card-header mit eigenem Polster in nur einer Karte). -->
        <!-- a1-rk-freundesvergleich-breit: ab 2400 px stehen beide Karten nebeneinander (.fs-cards). -->
        <div class="fs-cards">
        <mat-card class="compare-card">
          <h2 class="card-title">{{ 'friends.stats.overview' | translate }}</h2>
          <table class="compare-table">
            <!-- a1-rk-freundesvergleich-spalte: Zahlenspalten fest schmal, die erste Spalte bekommt den Rest. -->
            <colgroup><col><col class="num"><col class="num"></colgroup>
            <thead>
              <tr>
                <th></th>
                <th class="you-col">{{ 'friends.stats.you' | translate }}</th>
                <th>{{ friend.displayName || friend.username }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows; track row.label) {
                <tr>
                  <td class="metric">{{ row.label | translate }}</td>
                  <td [class.win]="row.winner === 'mine'">{{ row.percent ? (row.mine | pct:1) : row.mine }}</td>
                  <td [class.win]="row.winner === 'theirs'">{{ row.percent ? (row.theirs | pct:1) : row.theirs }}</td>
                </tr>
              }
            </tbody>
          </table>
        </mat-card>

        <mat-card class="themes-card">
          <h2 class="card-title">{{ 'friends.stats.themes' | translate }}</h2>
          @if (themeRows.length > 0) {
            <table class="compare-table">
              <colgroup><col><col class="num"><col class="num"></colgroup>
              <thead>
                <tr>
                  <th>{{ 'friends.stats.theme' | translate }}</th>
                  <th class="you-col">{{ 'friends.stats.you' | translate }}</th>
                  <th>{{ friend.displayName || friend.username }}</th>
                </tr>
              </thead>
              <tbody>
                @for (t of themeRows; track t.theme) {
                  <tr>
                    <td class="metric theme-cell">{{ t.theme | puzzleTheme }}</td>
                    <td [class.win]="t.winner === 'mine'">
                      @if (t.mine) { {{ t.mine.acc | pct }} <span class="sub">({{ t.mine.attempts }})</span> } @else { – }
                    </td>
                    <td [class.win]="t.winner === 'theirs'">
                      @if (t.theirs) { {{ t.theirs.acc | pct }} <span class="sub">({{ t.theirs.attempts }})</span> } @else { – }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <p class="empty-state">{{ 'friends.stats.noThemes' | translate }}</p>
          }
        </mat-card>
        </div>
      }
    </div>
  `,
  styles: [`
    .fs-container { padding: 2rem; max-width: 800px; margin: 0 auto; }
    /* UI-Sweep a1 (a1-rk-freundesvergleich-breit): Breitbild-Stufe ab 2400 px (DESIGN 4.1) — Übersicht und
       Themen-Vergleich nebeneinander statt untereinander in einer 800-px-Mitte. */
    @media (min-width: 2400px) {
      .fs-container { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
      .fs-cards { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; align-items: start; }
      .fs-cards > mat-card { margin-bottom: 0; }
    }
    .back-link { margin-bottom: 0.5rem; }
    h1 { font-size: 1.5rem; margin: 0.25rem 0 1rem; }
    mat-card { margin-bottom: 1rem; padding: 1rem; }
    .card-title { margin: 0 0 8px; padding: 0 0.5rem; font-size: 1rem; font-weight: 600; }
    .compare-table { width: 100%; border-collapse: collapse; table-layout: fixed; }
    .compare-table th, .compare-table td { overflow-wrap: anywhere; }
    /* UI-Sweep 2026-10-11 (r2-rp-themes-en): Themen-Namen nur an Wortgrenzen bzw. mit Silbentrennung umbrechen. */
    /* a1-rk-freundesvergleich-spalte: mit der breiteren ersten Spalte keine harte Trennung mehr („Abzugsan-griff“);
       nur ein einzelnes überlanges Wort darf noch umbrechen. */
    .compare-table td.theme-cell { overflow-wrap: break-word; hyphens: manual; }
    .compare-table col.num { width: 6rem; }
    .compare-table th, .compare-table td { padding: 0.6rem 0.5rem; text-align: right; border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .compare-table th { font-size: 0.8rem; font-weight: 600; color: color-mix(in srgb, currentColor 60%, transparent); }
    .compare-table th:first-child, .compare-table td.metric { text-align: left; color: color-mix(in srgb, currentColor 70%, transparent); }
    .you-col { color: var(--mat-sys-primary) !important; }
    td.win { font-weight: 700; color: var(--mat-sys-primary); }
    td.win::after { content: ' ▲'; font-size: 0.7em; }
    .sub { font-size: 0.75rem; color: color-mix(in srgb, currentColor 45%, transparent); }
    @media (max-width: 768px) {
      .fs-container { padding: 0.75rem; }
      .compare-table th, .compare-table td { padding: 0.5rem 0.35rem; }
      .card-title { padding: 0 0.35rem; }
      .compare-table col.num { width: 5.5rem; }
    }
  `]
})
export class FriendStatsComponent implements OnInit {
  loading = true;
  friend: FriendStats | null = null;
  rows: CompareRow[] = [];
  themeRows: ThemeRow[] = [];

  constructor(
    private friendsService: FriendsService,
    private route: ActivatedRoute,
    private puzzleService: PuzzleService,
    private snackbar: SnackbarService,
    private translate: TranslateService
  ) {}

  ngOnInit(): void {
    const userId = Number(this.route.snapshot.paramMap.get('userId'));
    if (!userId) { this.loading = false; return; }

    forkJoin({
      mine: this.puzzleService.getStats(),
      mineBreakdown: this.puzzleService.getBreakdown(),
      friend: this.friendsService.getStats<FriendStats>(userId)
    }).subscribe({
      next: ({ mine, mineBreakdown, friend }) => {
        this.friend = friend;
        this.rows = buildCompareRows(mine, friend.stats);
        this.themeRows = buildThemeRows(mineBreakdown.themes, friend.themes);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.snackbar.info(this.translate.instant('friends.stats.loadError'));
      }
    });
  }
}
