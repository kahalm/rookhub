import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import {
  TrainingGoalService, TrainingGoal, TrainingGoalInput, TodayProgress, GoalStatus, TrackerDay,
  ManualActivity, ManualActivityInput, ManualActivityKind,
  SourceBreakdown, ThemeBreakdown, SOURCE_KEYS, THEME_KEYS,
  ActivityPreset, ActivityPresetInput, TIMER_KINDS,
  ActivityTheme, ACTIVITY_THEMES,
} from './training-goals.service';
import { ManualActivitiesCardComponent } from './manual-activities-card.component';
import { ActivityPresetsCardComponent } from './activity-presets-card.component';
import { ChessableThemesCardComponent } from './chessable-themes-card.component';
import { PeriodBreakdownCardComponent } from './period-breakdown-card.component';
import { formatDuration } from './duration.util';
import { clampGoal } from './goal.util';
import { BreakRow, breakdownRows } from './breakdown.util';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';

// level -1 = Zukunft (leer); manual = enthält selbst gemeldete Aktivität
export interface GoalCell { date: string; status: GoalStatus; level: number; manual: boolean; }

// Perioden-/Aufschlüsselungs-Mathematik + BreakRow/BreakdownPeriod liegen in breakdown.util
// (Zyklus-Vermeidung mit PeriodBreakdownCardComponent); hier re-exportiert für bestehende Importe/Specs.
export type { BreakRow, BreakdownPeriod } from './breakdown.util';
export {
  BREAKDOWN_PERIODS, ymd, parseYmd,
  periodBounds, shiftAnchor, sumBreakdown, breakdownRows,
} from './breakdown.util';

// MANUAL_KINDS + isMinutesKind liegen in manual-activity.util (geteilt mit ManualActivitiesCardComponent);
// hier rückwärtskompatibel re-exportiert (bestehende Importe/Specs).
export { MANUAL_KINDS, isMinutesKind } from './manual-activity.util';

/** Sekunden → gerundete Minuten (Anzeige in der Tageshistory). */
export function toMinutes(seconds: number): number {
  return Math.round(seconds / 60);
}

// Gestufte Dauer-Formatierung liegt in duration.util (Zyklus-Vermeidung mit den Kind-Karten);
// hier re-exportiert für bestehende Importe/Specs.
export { formatDuration };

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
export function buildGoalTracker(days: { date: string; status: GoalStatus; hasManual?: boolean }[], today: Date, weeks = 27): GoalCell[][] {
  const byDate = new Map(days.map(d => [d.date, d.status]));
  const manualDates = new Set(days.filter(d => d.hasManual).map(d => d.date));
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
      const k = key(day);
      const status: GoalStatus = future ? 'none' : (byDate.get(k) ?? 'none');
      col.push({ date: k, status, level: future ? -1 : statusLevel(status), manual: !future && manualDates.has(k) });
    }
    cols.push(col);
  }
  return cols;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-training-goals',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatIconModule, MatButtonModule,
    MatFormFieldModule, MatInputModule, MatProgressBarModule, MatTooltipModule,
    TranslatePipe, LoadingSpinnerComponent,
    ManualActivitiesCardComponent, ActivityPresetsCardComponent, ChessableThemesCardComponent,
    PeriodBreakdownCardComponent,
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
                <!-- Ein gemeinsames Tageszeit-Ziel (alle Quellen füttern es). -->
                @if ((today?.daily?.targetMinutes ?? 0) > 0) {
                  <div class="cat">
                    <div class="cat-row">
                      <mat-icon [class.met]="today?.daily?.met">{{ today?.daily?.met ? 'check_circle' : 'schedule' }}</mat-icon>
                      <span class="cat-name">{{ 'trainingGoals.dailyGoal' | translate }}</span>
                      <span class="cat-val">{{ minutes(today?.daily?.doneSeconds ?? 0) }} / {{ today?.daily?.targetMinutes ?? 0 }} {{ 'trainingGoals.min' | translate }}</span>
                    </div>
                    <mat-progress-bar mode="determinate" [value]="pct(today?.daily?.doneSeconds ?? 0, today?.daily?.targetMinutes ?? 0)"></mat-progress-bar>
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

              <!-- Aufzeichnung von heute: nach Quelle + nach Thema -->
              @if (todaySourceRows.length) {
                <div class="breakdowns">
                  <div class="bd">
                    <div class="bd-title">{{ 'trainingGoals.breakdownBySource' | translate }}</div>
                    @for (r of todaySourceRows; track r.label) {
                      <div class="bd-row">
                        <span class="bd-label">{{ ('trainingGoals.source.' + r.label) | translate }}</span>
                        <span class="bd-bar"><span class="bd-fill src" [style.width.%]="r.pct"></span></span>
                        <span class="bd-val">{{ durValue(r.seconds) }} {{ durUnit(r.seconds) | translate }}</span>
                      </div>
                    }
                  </div>
                  <div class="bd">
                    <div class="bd-title">{{ 'trainingGoals.breakdownByTheme' | translate }}</div>
                    @for (r of todayThemeRows; track r.label) {
                      <div class="bd-row">
                        <span class="bd-label">{{ ('trainingGoals.theme.' + r.label) | translate }}</span>
                        <span class="bd-bar"><span class="bd-fill thm" [style.width.%]="r.pct"></span></span>
                        <span class="bd-val">{{ durValue(r.seconds) }} {{ durUnit(r.seconds) | translate }}</span>
                      </div>
                    }
                  </div>
                </div>
              }

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
                <mat-label>{{ 'trainingGoals.dailyGoal' | translate }} ({{ 'trainingGoals.min' | translate }})</mat-label>
                <input matInput type="number" min="0" max="600" [(ngModel)]="edit.dailyMinutes" />
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
            <p class="muted small">{{ 'trainingGoals.dailyHint' | translate }}</p>
            <p class="muted small">{{ 'trainingGoals.playHint' | translate }}</p>
            <div class="actions">
              <button mat-raised-button color="primary" (click)="save()" [disabled]="saving">{{ 'common.save' | translate }}</button>
              @if (goal?.source === 'personal') {
                <button mat-button (click)="resetOverride()" [disabled]="saving">{{ 'trainingGoals.resetOverride' | translate }}</button>
              }
            </div>
          </mat-card-content>
        </mat-card>

        <app-manual-activities-card [manualList]="manualList" (changed)="reload()" />

        <app-activity-presets-card />

        <!-- Tracker -->
        @if (tracker.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'trainingGoals.tracker' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="heatmap">
                @for (week of tracker; track $index) {
                  <div class="hm-col">
                    @for (cell of week; track cell.date) {
                      <div class="hm-cell" [class]="'gl' + cell.level" [class.manual]="cell.manual"
                           [matTooltip]="cell.level >= 0 ? (cell.date + ' · ' + (('trainingGoals.status.' + cell.status) | translate) + (cell.manual ? ' · ' + ('trainingGoals.manual.marker' | translate) : '')) : ''"></div>
                    }
                  </div>
                }
              </div>
              <div class="legend">
                <span class="legend-item"><span class="sw gl4"></span>{{ 'trainingGoals.status.full' | translate }}</span>
                <span class="legend-item"><span class="sw gl2"></span>{{ 'trainingGoals.status.partial' | translate }}</span>
                <span class="legend-item"><span class="sw gl0"></span>{{ 'trainingGoals.status.none' | translate }}</span>
                <span class="legend-item"><span class="sw gl0 manual"></span>{{ 'trainingGoals.manual.marker' | translate }}</span>
              </div>

              <!-- Umschaltbare Perioden-Aufschlüsselung: eigene, selbst rechnende Karte -->
              <app-period-breakdown-card [series]="series" />
            </mat-card-content>
          </mat-card>
        }

        <!-- Tageshistory: pro Tag Gesamtzeit + je Quelle + Partien -->
        @if (historyDays.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'trainingGoals.history' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="history-wrap">
                <table class="history">
                  <thead>
                    <tr>
                      <th class="th-date">{{ 'trainingGoals.dateCol' | translate }}</th>
                      <th>{{ 'trainingGoals.total' | translate }}</th>
                      <th>{{ 'trainingGoals.source.randomPuzzle' | translate }}</th>
                      <th>{{ 'trainingGoals.source.courseBook' | translate }}</th>
                      <th>{{ 'trainingGoals.source.chessable' | translate }}</th>
                      <th>{{ 'trainingGoals.cat.play' | translate }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (d of historyDays; track d.date) {
                      <tr>
                        <td class="td-date">{{ d.date }}</td>
                        <td class="strong">{{ durValue(d.totalSeconds) }} <span class="unit">{{ durUnit(d.totalSeconds) | translate }}</span></td>
                        <td>{{ durValue(d.bySource.randomPuzzleSeconds) }} <span class="unit">{{ durUnit(d.bySource.randomPuzzleSeconds) | translate }}</span></td>
                        <td>{{ durValue(d.bySource.courseBookSeconds) }} <span class="unit">{{ durUnit(d.bySource.courseBookSeconds) | translate }}</span></td>
                        <td>{{ durValue(d.bySource.chessableSeconds) }} <span class="unit">{{ durUnit(d.bySource.chessableSeconds) | translate }}</span></td>
                        <td>{{ d.playGames }} <span class="unit">{{ 'trainingGoals.games' | translate }}</span></td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </mat-card-content>
          </mat-card>
        }

        <!-- Chessable-Kurse: History + manuelle Themen-Zuordnung (eigene, self-loading Karte) -->
        <app-chessable-themes-card (changed)="reload()" />
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
    .breakdowns { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px 28px; margin-top: 16px; }
    .breakdowns.period { margin-top: 16px; }
    .period-breakdown { margin-top: 16px; border-top: 1px solid color-mix(in srgb, currentColor 10%, transparent); padding-top: 14px; }
    .pb-controls { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px 16px; }
    .pb-periods { font-size: .8rem; }
    .pb-nav { display: flex; align-items: center; gap: 4px; }
    .pb-label { font-size: .9rem; font-weight: 600; min-width: 120px; text-align: center; font-variant-numeric: tabular-nums; }
    .pb-empty { margin-top: 14px; }
    .bd-title { font-size: .8rem; font-weight: 600; color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 6px; text-transform: uppercase; letter-spacing: .03em; }
    .bd-row { display: flex; align-items: center; gap: 8px; font-size: .85rem; margin-bottom: 5px; }
    .bd-label { flex: 0 0 32%; }
    .bd-bar { flex: 1; height: 8px; border-radius: 4px; background: color-mix(in srgb, currentColor 10%, transparent); overflow: hidden; }
    .bd-fill { display: block; height: 100%; border-radius: 4px; }
    .bd-fill.src { background: #1976d2; }
    .bd-fill.thm { background: #6a1b9a; }
    .bd-val { flex: 0 0 auto; color: color-mix(in srgb, currentColor 65%, transparent); font-variant-numeric: tabular-nums; min-width: 56px; text-align: right; }
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
    /* Manuell (selbst) gemeldete Tage: dezenter Punkt/Rahmen, unabhängig vom Status */
    .hm-cell.manual { box-shadow: inset 0 0 0 1.5px #1976d2; }
    .sw.manual { box-shadow: inset 0 0 0 1.5px #1976d2; }
    .legend { display: flex; gap: 16px; margin-top: 8px; flex-wrap: wrap; }
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
    table.history td.strong { font-weight: 600; }
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
  edit: TrainingGoalInput = { dailyMinutes: 0, playGames: 0, weeklyDaysTarget: 0 };

  // Aufschlüsselung von heute (Heute-Karte), vorgerechnet fürs Template.
  todaySourceRows: BreakRow[] = [];
  todayThemeRows: BreakRow[] = [];

  /** Vollständige Tagesreihe (ganze Historie) — an die PeriodBreakdownCardComponent durchgereicht. */
  series: TrackerDay[] = [];

  // ----- Manuelle Offline-Aktivitäten (Formular/Liste in ManualActivitiesCardComponent) -----
  manualList: ManualActivity[] = [];

  constructor(
    private service: TrainingGoalService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  get hasGoal(): boolean {
    const g = this.goal;
    return !!g && (g.dailyMinutes > 0 || g.playGames > 0);
  }

  /** Gestufte Dauer-Anzeige: Zahlteil (Min < 2 h, Std < 48 h, sonst Tage). */
  durValue(seconds: number): string {
    return formatDuration(seconds, this.translate.currentLang()).value;
  }
  /** i18n-Einheitenschlüssel passend zu {@link durValue} (min/hours/days). */
  durUnit(seconds: number): string {
    return formatDuration(seconds, this.translate.currentLang()).unitKey;
  }

  ngOnInit(): void { this.reload(); }

  reload(): void {
    this.loading = true;
    forkJoin({
      goal: this.service.getGoal(),
      today: this.service.getToday(),
      tracker: this.service.getTracker(),
      series: this.service.getDailySeries(),
      manual: this.service.listManual(),
    }).subscribe({
      next: ({ goal, today, tracker, series, manual }) => {
        this.applyGoal(goal);
        this.today = today;
        this.todaySourceRows = this.sourceRows(today.bySource);
        this.todayThemeRows = this.themeRows(today.byTheme);
        this.series = series.days;
        this.tracker = tracker.days.length ? buildGoalTracker(tracker.days, new Date()) : [];
        this.historyDays = orderHistory(tracker.days); // neueste zuerst
        this.manualList = manual;
        this.loading = false;
      },
      error: () => { this.loading = false; },
    });
  }

  private sourceRows(b: SourceBreakdown): BreakRow[] {
    return breakdownRows(b as unknown as Record<string, number>, SOURCE_KEYS);
  }
  private themeRows(b: ThemeBreakdown): BreakRow[] {
    return breakdownRows(b as unknown as Record<string, number>, THEME_KEYS);
  }

  private applyGoal(goal: TrainingGoal): void {
    this.goal = goal;
    this.edit = {
      dailyMinutes: goal.dailyMinutes,
      playGames: goal.playGames,
      weeklyDaysTarget: goal.weeklyDaysTarget,
    };
  }

  save(): void {
    this.saving = true;
    const input: TrainingGoalInput = {
      dailyMinutes: clampGoal(this.edit.dailyMinutes, 600),
      playGames: clampGoal(this.edit.playGames, 200),
      weeklyDaysTarget: clampGoal(this.edit.weeklyDaysTarget, 7),
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
}
