import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import {
  TrainingGoalService, TrainingGoal, TrainingGoalInput, TodayProgress, GoalStatus, TrackerDay,
} from './training-goals.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';

export interface GoalCell { date: string; status: GoalStatus; level: number; } // level -1 = Zukunft (leer)

/** Sekunden → gerundete Minuten (Anzeige in der Tageshistory). */
export function toMinutes(seconds: number): number {
  return Math.round(seconds / 60);
}

/** Tageshistory-Reihenfolge: neueste zuerst (die Tracker-Tage kommen aufsteigend sortiert). */
export function orderHistory(days: TrackerDay[]): TrackerDay[] {
  return [...days].reverse();
}

/** Heatmap-Level je Tagesstatus: voll = 4 (Stern/Gold), teilweise = 2 (Amber), sonst 0 (leer). */
export function statusLevel(status: GoalStatus): number {
  if (status === 'full') return 4;
  if (status === 'partial') return 2;
  return 0;
}

/** Baut ein Wochen-Raster (Spalten = Wochen Mo–So) für die Ziele-Heatmap (rein, testbar). */
export function buildGoalTracker(days: { date: string; status: GoalStatus }[], today: Date, weeks = 27): GoalCell[][] {
  const byDate = new Map(days.map(d => [d.date, d.status]));
  const p = (n: number) => String(n).padStart(2, '0');
  const key = (d: Date) => `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
  const end = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const dow = (end.getDay() + 6) % 7;                       // 0 = Montag
  const firstMonday = new Date(end);
  firstMonday.setDate(end.getDate() - dow - (weeks - 1) * 7);
  const cols: GoalCell[][] = [];
  for (let w = 0; w < weeks; w++) {
    const col: GoalCell[] = [];
    for (let d = 0; d < 7; d++) {
      const day = new Date(firstMonday);
      day.setDate(firstMonday.getDate() + w * 7 + d);
      const future = day > end;
      const status: GoalStatus = future ? 'none' : (byDate.get(key(day)) ?? 'none');
      col.push({ date: key(day), status, level: future ? -1 : statusLevel(status) });
    }
    cols.push(col);
  }
  return cols;
}

@Component({
  selector: 'app-training-goals',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatIconModule, MatButtonModule,
    MatFormFieldModule, MatInputModule, MatProgressBarModule, MatTooltipModule,
    TranslateModule, LoadingSpinnerComponent,
  ],
  template: `
    <div class="tg-container">
      <h1>{{ 'trainingGoals.title' | translate }}</h1>
      <p class="intro">{{ 'trainingGoals.intro' | translate }}</p>

      @if (loading) {
        <app-loading-spinner />
      } @else {
        <!-- Heute -->
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'trainingGoals.today' | translate }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            @if (hasGoal) {
              <div class="today-head">
                <mat-icon class="day-star" [class]="'st-' + (today?.status ?? 'none')">{{ dayIcon(today?.status) }}</mat-icon>
                <div class="today-summary">
                  <div class="today-label">{{ ('trainingGoals.status.' + (today?.status ?? 'none')) | translate }}</div>
                  @if ((today?.weeklyDaysTarget ?? 0) > 0) {
                    <div class="week-line">{{ 'trainingGoals.weekProgress' | translate:{ done: today?.weekDaysMet ?? 0, target: today?.weeklyDaysTarget ?? 0 } }}</div>
                  }
                </div>
              </div>
              <div class="cats">
                @for (c of categories(); track c.key) {
                  <div class="cat">
                    <div class="cat-row">
                      <mat-icon [class.met]="c.met">{{ c.met ? 'check_circle' : c.icon }}</mat-icon>
                      <span class="cat-name">{{ ('trainingGoals.cat.' + c.key) | translate }}</span>
                      <span class="cat-val">{{ minutes(c.doneSeconds) }} / {{ c.targetMinutes }} {{ 'trainingGoals.min' | translate }}</span>
                    </div>
                    <mat-progress-bar mode="determinate" [value]="pct(c.doneSeconds, c.targetMinutes)"></mat-progress-bar>
                  </div>
                }
                <!-- Spielen ist ein Wochenziel (Anzahl Rapid-/Classical-Partien dieser Woche). -->
                @if ((today?.play?.targetGames ?? 0) > 0) {
                  <div class="cat">
                    <div class="cat-row">
                      <mat-icon [class.met]="today?.play?.met">{{ today?.play?.met ? 'check_circle' : 'sports_esports' }}</mat-icon>
                      <span class="cat-name">{{ 'trainingGoals.cat.play' | translate }} <span class="weekly-tag">{{ 'trainingGoals.thisWeek' | translate }}</span></span>
                      <span class="cat-val">{{ today?.play?.doneGames ?? 0 }} / {{ today?.play?.targetGames ?? 0 }} {{ 'trainingGoals.games' | translate }}</span>
                    </div>
                    <mat-progress-bar mode="determinate" [value]="pctCount(today?.play?.doneGames ?? 0, today?.play?.targetGames ?? 0)"></mat-progress-bar>
                  </div>
                }
              </div>
              @if ((today?.play?.targetGames ?? 0) > 0) {
                <button mat-stroked-button class="sync-btn" (click)="syncPlayTime()" [disabled]="syncingPlay">
                  <mat-icon>sync</mat-icon> {{ 'trainingGoals.syncPlay' | translate }}
                </button>
              }
            } @else {
              <p class="muted">{{ 'trainingGoals.noGoalHint' | translate }}</p>
            }
          </mat-card-content>
        </mat-card>

        <!-- Ziele festlegen -->
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'trainingGoals.setTitle' | translate }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            @if (goal?.source === 'group') {
              <p class="source-hint">
                <mat-icon class="inline">groups</mat-icon>
                {{ 'trainingGoals.fromGroup' | translate:{ group: goal?.groupName ?? '' } }}
              </p>
            } @else if (goal?.source === 'personal') {
              <p class="source-hint"><mat-icon class="inline">person</mat-icon>{{ 'trainingGoals.personal' | translate }}</p>
            }
            <div class="goal-fields">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ 'trainingGoals.cat.puzzles' | translate }} ({{ 'trainingGoals.min' | translate }})</mat-label>
                <input matInput type="number" min="0" max="600" [(ngModel)]="edit.puzzleMinutes" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ 'trainingGoals.cat.book' | translate }} ({{ 'trainingGoals.min' | translate }})</mat-label>
                <input matInput type="number" min="0" max="600" [(ngModel)]="edit.bookMinutes" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ 'trainingGoals.cat.chessable' | translate }} ({{ 'trainingGoals.min' | translate }})</mat-label>
                <input matInput type="number" min="0" max="600" [(ngModel)]="edit.chessableMinutes" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ 'trainingGoals.cat.play' | translate }} ({{ 'trainingGoals.gamesPerWeek' | translate }})</mat-label>
                <input matInput type="number" min="0" max="200" [(ngModel)]="edit.playGames" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ 'trainingGoals.weeklyDays' | translate }}</mat-label>
                <input matInput type="number" min="0" max="7" [(ngModel)]="edit.weeklyDaysTarget" />
              </mat-form-field>
            </div>
            <p class="muted small">{{ 'trainingGoals.playHint' | translate }}</p>
            <div class="actions">
              <button mat-raised-button color="primary" (click)="save()" [disabled]="saving">{{ 'common.save' | translate }}</button>
              @if (goal?.source === 'personal') {
                <button mat-button (click)="resetOverride()" [disabled]="saving">{{ 'trainingGoals.resetOverride' | translate }}</button>
              }
            </div>
          </mat-card-content>
        </mat-card>

        <!-- Tracker -->
        @if (tracker.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'trainingGoals.tracker' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="heatmap">
                @for (week of tracker; track $index) {
                  <div class="hm-col">
                    @for (cell of week; track cell.date) {
                      <div class="hm-cell" [class]="'gl' + cell.level"
                           [matTooltip]="cell.level >= 0 ? (cell.date + ' · ' + (('trainingGoals.status.' + cell.status) | translate)) : ''"></div>
                    }
                  </div>
                }
              </div>
              <div class="legend">
                <span class="legend-item"><span class="sw gl4"></span>{{ 'trainingGoals.status.full' | translate }}</span>
                <span class="legend-item"><span class="sw gl2"></span>{{ 'trainingGoals.status.partial' | translate }}</span>
                <span class="legend-item"><span class="sw gl0"></span>{{ 'trainingGoals.status.none' | translate }}</span>
              </div>
            </mat-card-content>
          </mat-card>
        }

        <!-- Tageshistory: pro Tag je Kategorie die Zahl -->
        @if (historyDays.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'trainingGoals.history' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="history-wrap">
                <table class="history">
                  <thead>
                    <tr>
                      <th class="th-date">{{ 'trainingGoals.dateCol' | translate }}</th>
                      <th>{{ 'trainingGoals.cat.puzzles' | translate }}</th>
                      <th>{{ 'trainingGoals.cat.book' | translate }}</th>
                      <th>{{ 'trainingGoals.cat.chessable' | translate }}</th>
                      <th>{{ 'trainingGoals.cat.play' | translate }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (d of historyDays; track d.date) {
                      <tr>
                        <td class="td-date">{{ d.date }}</td>
                        <td>{{ mins(d.puzzleSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></td>
                        <td>{{ mins(d.bookSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></td>
                        <td>{{ mins(d.chessableSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></td>
                        <td>{{ d.playGames }} <span class="unit">{{ 'trainingGoals.games' | translate }}</span></td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </mat-card-content>
          </mat-card>
        }
      }
    </div>
  `,
  styles: [`
    .tg-container { max-width: 1000px; margin: 16px auto; padding: 0 12px; }
    .intro { color: color-mix(in srgb, currentColor 60%, transparent); margin-top: -8px; }
    mat-card { margin-bottom: 16px; }
    .muted { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; }
    .small { font-size: .8rem; }
    .today-head { display: flex; align-items: center; gap: 14px; margin-bottom: 12px; }
    .day-star { font-size: 44px; width: 44px; height: 44px; }
    .day-star.st-full { color: #f5b301; }
    .day-star.st-partial { color: #fb8c00; }
    .day-star.st-none { color: color-mix(in srgb, currentColor 25%, transparent); }
    .today-label { font-size: 1.1rem; font-weight: 600; }
    .week-line { color: color-mix(in srgb, currentColor 60%, transparent); font-size: .9rem; }
    .cats { display: flex; flex-direction: column; gap: 12px; }
    .cat-row { display: flex; align-items: center; gap: 8px; font-size: .9rem; margin-bottom: 4px; }
    .cat-row mat-icon { color: color-mix(in srgb, currentColor 25%, transparent); }
    .cat-row mat-icon.met { color: #2e7d32; }
    .cat-name { flex: 1; }
    .weekly-tag { color: color-mix(in srgb, currentColor 47%, transparent); font-size: .75rem; font-style: italic; }
    .cat-val { color: color-mix(in srgb, currentColor 65%, transparent); font-variant-numeric: tabular-nums; }
    .sync-btn { margin-top: 12px; }
    .source-hint { display: flex; align-items: center; gap: 6px; color: color-mix(in srgb, currentColor 65%, transparent); }
    .inline { font-size: 18px; width: 18px; height: 18px; }
    .goal-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; margin: 8px 0; }
    .actions { display: flex; gap: 8px; margin-top: 8px; }
    .heatmap { display: flex; gap: 3px; overflow-x: auto; padding-bottom: 4px; }
    .hm-col { display: flex; flex-direction: column; gap: 3px; }
    .hm-cell { width: 12px; height: 12px; border-radius: 2px; background: color-mix(in srgb, currentColor 10%, transparent); }
    .hm-cell.gl-1 { background: transparent; }
    .hm-cell.gl0 { background: color-mix(in srgb, currentColor 10%, transparent); }
    .hm-cell.gl2 { background: #fdd835; }
    .hm-cell.gl4 { background: #f5b301; }
    .legend { display: flex; gap: 16px; margin-top: 8px; }
    .legend-item { display: inline-flex; align-items: center; gap: 5px; font-size: .8rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .sw { width: 12px; height: 12px; border-radius: 2px; display: inline-block; }
    .sw.gl0 { background: color-mix(in srgb, currentColor 10%, transparent); } .sw.gl2 { background: #fdd835; } .sw.gl4 { background: #f5b301; }
    .history-wrap { overflow-x: auto; }
    table.history { width: 100%; border-collapse: collapse; font-size: .9rem; }
    table.history th, table.history td { text-align: right; padding: 6px 10px; white-space: nowrap; }
    table.history th.th-date, table.history td.td-date { text-align: left; font-variant-numeric: tabular-nums; }
    table.history thead th { font-weight: 600; border-bottom: 1px solid color-mix(in srgb, currentColor 18%, transparent); }
    table.history tbody tr { border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    table.history td { font-variant-numeric: tabular-nums; }
    table.history .unit { color: color-mix(in srgb, currentColor 50%, transparent); font-size: .8em; }
  `]
})
export class TrainingGoalsComponent implements OnInit {
  loading = true;
  saving = false;
  syncingPlay = false;
  goal: TrainingGoal | null = null;
  today: TodayProgress | null = null;
  tracker: GoalCell[][] = [];
  /** Tage mit Aktivität, neueste zuerst — für die Tageshistory-Tabelle. */
  historyDays: TrackerDay[] = [];
  edit: TrainingGoalInput = { puzzleMinutes: 0, bookMinutes: 0, chessableMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };

  constructor(
    private service: TrainingGoalService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  get hasGoal(): boolean {
    const g = this.goal;
    return !!g && (g.puzzleMinutes > 0 || g.bookMinutes > 0 || g.chessableMinutes > 0 || g.playGames > 0);
  }

  /** Sekunden → gerundete Minuten (Anzeige in der Tageshistory). */
  mins(seconds: number): number {
    return toMinutes(seconds);
  }

  ngOnInit(): void { this.reload(); }

  private reload(): void {
    this.loading = true;
    forkJoin({
      goal: this.service.getGoal(),
      today: this.service.getToday(),
      tracker: this.service.getTracker(),
    }).subscribe({
      next: ({ goal, today, tracker }) => {
        this.applyGoal(goal);
        this.today = today;
        this.tracker = tracker.days.length ? buildGoalTracker(tracker.days, new Date()) : [];
        this.historyDays = orderHistory(tracker.days); // neueste zuerst
        this.loading = false;
      },
      error: () => { this.loading = false; },
    });
  }

  private applyGoal(goal: TrainingGoal): void {
    this.goal = goal;
    this.edit = {
      puzzleMinutes: goal.puzzleMinutes,
      bookMinutes: goal.bookMinutes,
      chessableMinutes: goal.chessableMinutes,
      playGames: goal.playGames,
      weeklyDaysTarget: goal.weeklyDaysTarget,
    };
  }

  save(): void {
    this.saving = true;
    const input: TrainingGoalInput = {
      puzzleMinutes: this.clamp(this.edit.puzzleMinutes, 600),
      bookMinutes: this.clamp(this.edit.bookMinutes, 600),
      chessableMinutes: this.clamp(this.edit.chessableMinutes, 600),
      playGames: this.clamp(this.edit.playGames, 200),
      weeklyDaysTarget: this.clamp(this.edit.weeklyDaysTarget, 7),
    };
    this.service.saveGoal(input).subscribe({
      next: () => { this.saving = false; this.snackbar.success(this.translate.instant('trainingGoals.saved')); this.reload(); },
      error: () => { this.saving = false; this.snackbar.warn(this.translate.instant('trainingGoals.error')); },
    });
  }

  resetOverride(): void {
    this.saving = true;
    this.service.deleteOverride().subscribe({
      next: () => { this.saving = false; this.snackbar.success(this.translate.instant('trainingGoals.resetDone')); this.reload(); },
      error: () => { this.saving = false; this.snackbar.warn(this.translate.instant('trainingGoals.error')); },
    });
  }

  syncPlayTime(): void {
    this.syncingPlay = true;
    this.service.syncPlay().subscribe({
      next: () => { this.syncingPlay = false; this.snackbar.success(this.translate.instant('trainingGoals.syncDone')); this.reload(); },
      error: () => { this.syncingPlay = false; this.snackbar.warn(this.translate.instant('trainingGoals.error')); },
    });
  }

  /** Tägliche, zeitbasierte Kategorien (Puzzles/Buch). Spielen ist ein Wochenziel und wird separat dargestellt. */
  categories(): { key: string; icon: string; targetMinutes: number; doneSeconds: number; met: boolean }[] {
    if (!this.today) return [];
    const out = [
      { key: 'puzzles', icon: 'extension', ...this.today.puzzles },
      { key: 'book', icon: 'menu_book', ...this.today.book },
      { key: 'chessable', icon: 'school', ...this.today.chessable },
    ];
    return out.filter(c => c.targetMinutes > 0);
  }

  dayIcon(status: GoalStatus | undefined): string {
    if (status === 'full') return 'star';
    if (status === 'partial') return 'star_half';
    return 'star_border';
  }

  minutes(seconds: number): number { return Math.round(seconds / 60); }
  pct(doneSeconds: number, targetMinutes: number): number {
    if (targetMinutes <= 0) return 0;
    return Math.min(100, Math.round((100 * doneSeconds) / (targetMinutes * 60)));
  }
  /** Fortschritt in % für ein Zähl-Ziel (Spielen-Partien). */
  pctCount(done: number, target: number): number {
    if (target <= 0) return 0;
    return Math.min(100, Math.round((100 * done) / target));
  }
  private clamp(v: number, max: number): number { return Math.max(0, Math.min(max, Math.round(v || 0))); }
}
