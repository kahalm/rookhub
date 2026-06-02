import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { PuzzleService, PuzzleStatsDto, PuzzleAttemptDto, EloHistoryPoint } from '../puzzles/puzzle.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

export interface Curve { poly: string; minElo: number; maxElo: number; w: number; h: number; first: string; last: string; }

/** Baut aus den Elo-Punkten eine SVG-Polyline + Achsenwerte (rein, testbar). null bei < 2 Punkten. */
export function buildEloCurve(points: EloHistoryPoint[], w = 600, h = 180, pad = 6): Curve | null {
  if (points.length < 2) return null;
  const elos = points.map(p => p.elo);
  let minElo = Math.min(...elos), maxElo = Math.max(...elos);
  if (minElo === maxElo) { minElo -= 10; maxElo += 10; }
  const n = points.length;
  const poly = points.map((p, i) => {
    const x = pad + (i / (n - 1)) * (w - 2 * pad);
    const y = h - pad - ((p.elo - minElo) / (maxElo - minElo)) * (h - 2 * pad);
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(' ');
  const fmt = (s: string) => { const d = new Date(s); return isNaN(d.getTime()) ? '' : d.toLocaleDateString(); };
  return { poly, minElo, maxElo, w, h, first: fmt(points[0].attemptedAt), last: fmt(points[n - 1].attemptedAt) };
}

@Component({
  selector: 'app-stats',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, MatCardModule, MatIconModule,
    MatFormFieldModule, MatSelectModule, MatTableModule, TranslateModule, LoadingSpinnerComponent
  ],
  template: `
    <div class="stats-container">
      <h1>{{ 'stats.title' | translate }}</h1>

      @if (loading) {
        <app-loading-spinner />
      } @else {
        <div class="cards">
          <mat-card class="stat"><div class="val">{{ stats?.puzzleElo ?? '–' }}</div><div class="lbl">{{ 'stats.currentElo' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ stats?.solved ?? 0 }}</div><div class="lbl">{{ 'stats.totalSolved' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ stats?.totalAttempts ?? 0 }}</div><div class="lbl">{{ 'stats.attempts' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ (stats?.accuracy ?? 0) }}%</div><div class="lbl">{{ 'stats.accuracy' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ stats?.currentStreak ?? 0 }}</div><div class="lbl">{{ 'stats.currentStreak' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ stats?.bestStreak ?? 0 }}</div><div class="lbl">{{ 'stats.bestStreak' | translate }}</div></mat-card>
        </div>

        <mat-card class="chart-card">
          <mat-card-header>
            <mat-card-title>{{ 'stats.eloProgress' | translate }}</mat-card-title>
            <mat-form-field appearance="outline" class="level-field" subscriptSizing="dynamic">
              <mat-label>{{ 'stats.level' | translate }}</mat-label>
              <mat-select [(ngModel)]="level" (selectionChange)="rebuildCurve()">
                <mat-option [value]="-1">{{ 'stats.allLevels' | translate }}</mat-option>
                @for (lv of [0,1,2,3,4]; track lv) { <mat-option [value]="lv">{{ lv }}</mat-option> }
              </mat-select>
            </mat-form-field>
          </mat-card-header>
          <mat-card-content>
            @if (curve) {
              <div class="chart">
                <div class="y-axis"><span>{{ curve.maxElo }}</span><span>{{ curve.minElo }}</span></div>
                <svg [attr.viewBox]="'0 0 ' + curve.w + ' ' + curve.h" preserveAspectRatio="none" class="svg">
                  <polyline [attr.points]="curve.poly" fill="none" stroke="#1976d2" stroke-width="2" vector-effect="non-scaling-stroke" />
                </svg>
              </div>
              <div class="x-axis"><span>{{ curve.first }}</span><span>{{ curve.last }}</span></div>
            } @else {
              <p class="muted">{{ 'stats.noData' | translate }}</p>
            }
          </mat-card-content>
        </mat-card>

        @if (perLevel.length) {
          <mat-card class="perlevel-card">
            <mat-card-header><mat-card-title>{{ 'stats.perLevel' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="perlevel">
                @for (e of perLevel; track e.level) {
                  <div class="pl"><span class="pl-lvl">{{ 'stats.vizLevel' | translate }} {{ e.level }}</span><span class="pl-elo">{{ e.elo }}</span></div>
                }
              </div>
            </mat-card-content>
          </mat-card>
        }

        <mat-card class="recent-card">
          <mat-card-header><mat-card-title>{{ 'stats.recentPuzzles' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            @if (recent.length === 0) {
              <p class="muted">{{ 'stats.noData' | translate }}</p>
            } @else {
              <table mat-table [dataSource]="recent" class="full-width">
                <ng-container matColumnDef="date">
                  <th mat-header-cell *matHeaderCellDef>{{ 'stats.date' | translate }}</th>
                  <td mat-cell *matCellDef="let a">{{ a.attemptedAt | date:'dd.MM. HH:mm' }}</td>
                </ng-container>
                <ng-container matColumnDef="rating">
                  <th mat-header-cell *matHeaderCellDef>{{ 'stats.rating' | translate }}</th>
                  <td mat-cell *matCellDef="let a">{{ a.puzzleRating }}</td>
                </ng-container>
                <ng-container matColumnDef="result">
                  <th mat-header-cell *matHeaderCellDef>{{ 'stats.result' | translate }}</th>
                  <td mat-cell *matCellDef="let a">
                    <mat-icon [class]="a.solved ? 'res-ok' : 'res-fail'">{{ a.solved ? 'check_circle' : 'cancel' }}</mat-icon>
                  </td>
                </ng-container>
                <ng-container matColumnDef="elo">
                  <th mat-header-cell *matHeaderCellDef>Δ Elo</th>
                  <td mat-cell *matCellDef="let a" [class.pos]="(a.eloChange ?? 0) >= 0" [class.neg]="(a.eloChange ?? 0) < 0">
                    {{ a.eloChange != null ? ((a.eloChange >= 0 ? '+' : '') + a.eloChange) : '–' }}
                  </td>
                </ng-container>
                <ng-container matColumnDef="time">
                  <th mat-header-cell *matHeaderCellDef>{{ 'stats.time' | translate }}</th>
                  <td mat-cell *matCellDef="let a">{{ a.timeSpentSeconds }}s</td>
                </ng-container>
                <ng-container matColumnDef="open">
                  <th mat-header-cell *matHeaderCellDef></th>
                  <td mat-cell *matCellDef="let a"><a mat-icon-button [routerLink]="['/puzzles', a.puzzleId]"><mat-icon>open_in_new</mat-icon></a></td>
                </ng-container>
                <tr mat-header-row *matHeaderRowDef="cols"></tr>
                <tr mat-row *matRowDef="let row; columns: cols;"></tr>
              </table>
            }
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .stats-container { max-width: 1000px; margin: 16px auto; padding: 0 12px; }
    .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 12px; margin-bottom: 16px; }
    .stat { text-align: center; padding: 8px; }
    .stat .val { font-size: 1.6rem; font-weight: 700; color: #1976d2; }
    .stat .lbl { font-size: .8rem; color: #666; }
    .chart-card mat-card-header { display: flex; align-items: center; justify-content: space-between; }
    .level-field { width: 130px; }
    .chart { display: flex; gap: 6px; height: 180px; }
    .y-axis { display: flex; flex-direction: column; justify-content: space-between; font-size: .7rem; color: #888; }
    .svg { flex: 1; height: 180px; background: linear-gradient(#fafafa,#f0f0f0); border-radius: 4px; }
    .x-axis { display: flex; justify-content: space-between; font-size: .7rem; color: #888; margin-top: 2px; }
    .muted { color: #888; font-style: italic; }
    .perlevel { display: flex; flex-wrap: wrap; gap: 12px; }
    .pl { display: flex; flex-direction: column; align-items: center; padding: 6px 12px; background: #f5f5f5; border-radius: 6px; }
    .pl-lvl { font-size: .75rem; color: #666; }
    .pl-elo { font-size: 1.1rem; font-weight: 600; }
    .full-width { width: 100%; }
    .res-ok { color: #2e7d32; } .res-fail { color: #c62828; }
    .pos { color: #2e7d32; } .neg { color: #c62828; }
    mat-card { margin-bottom: 16px; }
  `]
})
export class StatsComponent implements OnInit {
  loading = true;
  stats: PuzzleStatsDto | null = null;
  perLevel: { level: number; elo: number }[] = [];
  recent: PuzzleAttemptDto[] = [];
  cols = ['date', 'rating', 'result', 'elo', 'time', 'open'];

  level = 0;
  private eloPoints: EloHistoryPoint[] = [];
  curve: Curve | null = null;

  constructor(private puzzles: PuzzleService) {}

  ngOnInit(): void {
    forkJoin({
      stats: this.puzzles.getStats(),
      history: this.puzzles.getHistory(1, 30),
      elo: this.puzzles.getEloHistory(1000),
    }).subscribe({
      next: ({ stats, history, elo }) => {
        this.stats = stats;
        this.recent = history;
        this.eloPoints = elo;
        this.perLevel = stats.puzzleEloPerLevel
          ? Object.entries(stats.puzzleEloPerLevel).map(([k, v]) => ({ level: +k, elo: v as number })).sort((a, b) => a.level - b.level)
          : [];
        this.rebuildCurve();
        this.loading = false;
      },
      error: () => { this.loading = false; },
    });
  }

  rebuildCurve(): void {
    const pts = this.eloPoints.filter(p => this.level === -1 || p.vizLevel === this.level);
    this.curve = buildEloCurve(pts);
  }
}
