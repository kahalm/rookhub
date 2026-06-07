import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { PuzzleService, PuzzleStatsDto, PuzzleAttemptDto, EloHistoryPoint, ThemeStat, RatingBand, ActivityDay } from '../puzzles/puzzle.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

export interface Curve { poly: string; minElo: number; maxElo: number; w: number; h: number; first: string; last: string; }
export interface HeatCell { date: string; count: number; level: number; }   // level -1 = Zukunft (leer)

export function heatLevel(count: number): number {
  if (count <= 0) return 0;
  if (count <= 2) return 1;
  if (count <= 5) return 2;
  if (count <= 9) return 3;
  return 4;
}

/** Baut ein Wochen-Raster (Spalten = Wochen Mo–So) für die Aktivitäts-Heatmap (rein, testbar). */
export function buildHeatmap(activity: ActivityDay[], today: Date, weeks = 27): HeatCell[][] {
  const counts = new Map(activity.map(a => [a.date, a.count]));
  const p = (n: number) => String(n).padStart(2, '0');
  const key = (d: Date) => `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
  const end = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const dow = (end.getDay() + 6) % 7;                       // 0 = Montag
  const firstMonday = new Date(end);
  firstMonday.setDate(end.getDate() - dow - (weeks - 1) * 7);
  const cols: HeatCell[][] = [];
  for (let w = 0; w < weeks; w++) {
    const col: HeatCell[] = [];
    for (let d = 0; d < 7; d++) {
      const day = new Date(firstMonday);
      day.setDate(firstMonday.getDate() + w * 7 + d);
      const future = day > end;
      const count = future ? 0 : (counts.get(key(day)) ?? 0);
      col.push({ date: key(day), count, level: future ? -1 : heatLevel(count) });
    }
    cols.push(col);
  }
  return cols;
}

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

export interface OverlayLine { level: number; poly: string; color: string; }
export interface Overlay { lines: OverlayLine[]; minElo: number; maxElo: number; w: number; h: number; first: string; last: string; }

/** Farben je Visualisierungs-Level für die „Alle"-Overlay-Ansicht (Index = Level). */
export const LEVEL_COLORS = ['#1976d2', '#e53935', '#43a047', '#fb8c00', '#8e24aa', '#00897b'];

/**
 * Baut EINE Overlay-Grafik aller Level-Kurven auf GEMEINSAMER Skala (Y = globaler Elo-Bereich,
 * X = globaler Zeitbereich), je Level farbkodiert. Levels mit < 2 Punkten entfallen.
 * null, wenn insgesamt < 2 Punkte oder kein Level zeichenbar.
 */
export function buildOverlay(points: EloHistoryPoint[], w = 600, h = 180, pad = 6): Overlay | null {
  if (points.length < 2) return null;
  const byLevel = new Map<number, EloHistoryPoint[]>();
  for (const p of points) {
    const arr = byLevel.get(p.vizLevel);
    if (arr) arr.push(p); else byLevel.set(p.vizLevel, [p]);
  }
  const drawn = [...byLevel.entries()].filter(([, ps]) => ps.length >= 2).sort((a, b) => a[0] - b[0]);
  if (!drawn.length) return null;

  const all = drawn.flatMap(([, ps]) => ps);
  const elos = all.map(p => p.elo);
  let minElo = Math.min(...elos), maxElo = Math.max(...elos);
  if (minElo === maxElo) { minElo -= 10; maxElo += 10; }
  const times = all.map(p => Date.parse(p.attemptedAt)).filter(t => !isNaN(t));
  const tMin = times.length ? Math.min(...times) : 0;
  const tSpan = (times.length ? Math.max(...times) : 0) - tMin;

  const xOf = (p: EloHistoryPoint, i: number, n: number): number => {
    const t = Date.parse(p.attemptedAt);
    if (tSpan > 0 && !isNaN(t)) return pad + ((t - tMin) / tSpan) * (w - 2 * pad);
    return pad + (n > 1 ? i / (n - 1) : 0) * (w - 2 * pad);   // Fallback: gleichmäßig
  };
  const yOf = (elo: number): number => h - pad - ((elo - minElo) / (maxElo - minElo)) * (h - 2 * pad);

  const lines: OverlayLine[] = drawn.map(([level, ps]) => ({
    level,
    color: LEVEL_COLORS[level % LEVEL_COLORS.length],
    poly: ps.map((p, i) => `${xOf(p, i, ps.length).toFixed(1)},${yOf(p.elo).toFixed(1)}`).join(' '),
  }));

  const byTime = [...all].sort((a, b) => Date.parse(a.attemptedAt) - Date.parse(b.attemptedAt));
  const fmt = (s: string) => { const d = new Date(s); return isNaN(d.getTime()) ? '' : d.toLocaleDateString(); };
  return { lines, minElo, maxElo, w, h, first: fmt(byTime[0].attemptedAt), last: fmt(byTime[byTime.length - 1].attemptedAt) };
}

@Component({
  selector: 'app-stats',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, MatCardModule, MatIconModule,
    MatFormFieldModule, MatSelectModule, MatTableModule, MatTooltipModule, TranslateModule, LoadingSpinnerComponent
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
            @if (level === -1) {
              @if (overlay) {
                <div class="chart">
                  <div class="y-axis"><span>{{ overlay.maxElo }}</span><span>{{ overlay.minElo }}</span></div>
                  <svg [attr.viewBox]="'0 0 ' + overlay.w + ' ' + overlay.h" preserveAspectRatio="none" class="svg">
                    @for (l of overlay.lines; track l.level) {
                      <polyline [attr.points]="l.poly" fill="none" [attr.stroke]="l.color" stroke-width="2" vector-effect="non-scaling-stroke" />
                    }
                  </svg>
                </div>
                <div class="x-axis"><span>{{ overlay.first }}</span><span>{{ overlay.last }}</span></div>
                <div class="legend">
                  @for (l of overlay.lines; track l.level) {
                    <span class="legend-item"><span class="legend-swatch" [style.background]="l.color"></span>{{ 'stats.vizLevel' | translate }} {{ l.level }}</span>
                  }
                </div>
              } @else {
                <p class="muted">{{ 'stats.noData' | translate }}</p>
              }
            } @else if (curve) {
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

        @if (themes.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'stats.byTheme' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="theme-list">
                @for (t of themes; track t.theme) {
                  <div class="theme-row">
                    <span class="theme-name">{{ t.theme }}</span>
                    <div class="theme-bar"><div class="theme-fill" [style.width.%]="acc(t.solved, t.attempts)"></div></div>
                    <span class="theme-val">{{ acc(t.solved, t.attempts) }}% · {{ t.solved }}/{{ t.attempts }}</span>
                  </div>
                }
              </div>
            </mat-card-content>
          </mat-card>
        }

        @if (ratingBands.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'stats.byRating' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="bands">
                @for (b of ratingBands; track b.from) {
                  <div class="band" [matTooltip]="b.solved + '/' + b.attempts + ' · ' + acc(b.solved, b.attempts) + '%'">
                    <div class="band-bar"><div class="band-fill" [style.height.%]="barPct(b.solved)"></div></div>
                    <span class="band-lbl">{{ b.from }}</span>
                    <span class="band-cnt">{{ b.solved }}</span>
                  </div>
                }
              </div>
              <p class="muted small">{{ 'stats.byRatingHint' | translate }}</p>
            </mat-card-content>
          </mat-card>
        }

        @if (heatmap.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'stats.activity' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="heatmap">
                @for (week of heatmap; track $index) {
                  <div class="hm-col">
                    @for (cell of week; track cell.date) {
                      <div class="hm-cell" [class]="'lvl' + cell.level"
                           [matTooltip]="cell.level >= 0 ? (cell.date + ': ' + cell.count) : ''"></div>
                    }
                  </div>
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
    .stat .lbl { font-size: .8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .chart-card mat-card-header { display: flex; align-items: center; justify-content: space-between; }
    .level-field { width: 130px; }
    .chart { display: flex; gap: 6px; height: 180px; }
    .y-axis { display: flex; flex-direction: column; justify-content: space-between; font-size: .7rem; color: color-mix(in srgb, currentColor 47%, transparent); }
    .svg { flex: 1; height: 180px; background: color-mix(in srgb, currentColor 6%, transparent); border-radius: 4px; }
    .x-axis { display: flex; justify-content: space-between; font-size: .7rem; color: color-mix(in srgb, currentColor 47%, transparent); margin-top: 2px; }
    .legend { display: flex; flex-wrap: wrap; gap: 10px 16px; margin-top: 8px; }
    .legend-item { display: inline-flex; align-items: center; gap: 5px; font-size: .8rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .legend-swatch { width: 14px; height: 3px; border-radius: 2px; display: inline-block; }
    .muted { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; }
    .perlevel { display: flex; flex-wrap: wrap; gap: 12px; }
    .pl { display: flex; flex-direction: column; align-items: center; padding: 6px 12px; background: color-mix(in srgb, currentColor 6%, transparent); border-radius: 6px; }
    .pl-lvl { font-size: .75rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .pl-elo { font-size: 1.1rem; font-weight: 600; }
    .full-width { width: 100%; }
    .res-ok { color: #2e7d32; } .res-fail { color: #c62828; }
    .pos { color: #2e7d32; } .neg { color: #c62828; }
    .small { font-size: .75rem; }
    .theme-list { display: flex; flex-direction: column; gap: 6px; }
    .theme-row { display: flex; align-items: center; gap: 10px; font-size: .85rem; }
    .theme-name { width: 130px; flex: 0 0 auto; text-transform: capitalize; }
    .theme-bar { flex: 1; height: 12px; background: color-mix(in srgb, currentColor 8%, transparent); border-radius: 6px; overflow: hidden; }
    .theme-fill { height: 100%; background: #1976d2; }
    .theme-val { width: 120px; flex: 0 0 auto; text-align: right; color: color-mix(in srgb, currentColor 65%, transparent); font-variant-numeric: tabular-nums; }
    .bands { display: flex; align-items: flex-end; gap: 6px; height: 140px; padding-top: 8px; }
    .band { display: flex; flex-direction: column; align-items: center; gap: 2px; flex: 1; min-width: 28px; }
    .band-bar { width: 60%; flex: 1; display: flex; align-items: flex-end; background: color-mix(in srgb, currentColor 8%, transparent); border-radius: 3px; }
    .band-fill { width: 100%; background: #43a047; border-radius: 3px; min-height: 2px; }
    .band-lbl { font-size: .65rem; color: color-mix(in srgb, currentColor 47%, transparent); }
    .band-cnt { font-size: .75rem; font-weight: 600; }
    .heatmap { display: flex; gap: 3px; overflow-x: auto; padding-bottom: 4px; }
    .hm-col { display: flex; flex-direction: column; gap: 3px; }
    .hm-cell { width: 12px; height: 12px; border-radius: 2px; background: color-mix(in srgb, currentColor 10%, transparent); }
    .hm-cell.lvl-1 { background: transparent; }
    .hm-cell.lvl0 { background: color-mix(in srgb, currentColor 10%, transparent); }
    .hm-cell.lvl1 { background: #c6e48b; }
    .hm-cell.lvl2 { background: #7bc96f; }
    .hm-cell.lvl3 { background: #239a3b; }
    .hm-cell.lvl4 { background: #196127; }
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
  overlay: Overlay | null = null;   // „Alle"-Ansicht: alle Level in einer Grafik, farbkodiert

  themes: ThemeStat[] = [];
  ratingBands: RatingBand[] = [];
  heatmap: HeatCell[][] = [];
  private maxBandSolved = 0;

  constructor(private puzzles: PuzzleService) {}

  ngOnInit(): void {
    forkJoin({
      stats: this.puzzles.getStats(),
      history: this.puzzles.getHistory(1, 30),
      elo: this.puzzles.getEloHistory(1000),
      breakdown: this.puzzles.getBreakdown(),
    }).subscribe({
      next: ({ stats, history, elo, breakdown }) => {
        this.stats = stats;
        this.recent = history;
        this.eloPoints = elo;
        this.perLevel = stats.puzzleEloPerLevel
          ? Object.entries(stats.puzzleEloPerLevel).map(([k, v]) => ({ level: +k, elo: v as number })).sort((a, b) => a.level - b.level)
          : [];
        this.themes = breakdown.themes;
        this.ratingBands = breakdown.ratingBands;
        this.maxBandSolved = Math.max(1, ...this.ratingBands.map(b => b.solved));
        this.heatmap = breakdown.activity.length ? buildHeatmap(breakdown.activity, new Date()) : [];
        this.rebuildCurve();
        this.loading = false;
      },
      error: () => { this.loading = false; },
    });
  }

  acc(solved: number, attempts: number): number {
    return attempts > 0 ? Math.round((100 * solved) / attempts) : 0;
  }
  barPct(solved: number): number {
    return Math.round((100 * solved) / this.maxBandSolved);
  }

  rebuildCurve(): void {
    if (this.level === -1) {
      // „Alle" → alle Modi farbkodiert in EINER Grafik (gemeinsame Skala) + Legende.
      this.overlay = buildOverlay(this.eloPoints);
      this.curve = null;
    } else {
      this.overlay = null;
      this.curve = buildEloCurve(this.eloPoints.filter(p => p.vizLevel === this.level));
    }
  }
}
