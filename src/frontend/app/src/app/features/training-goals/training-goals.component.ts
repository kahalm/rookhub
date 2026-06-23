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
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import {
  TrainingGoalService, TrainingGoal, TrainingGoalInput, TodayProgress, GoalStatus, TrackerDay,
  ManualActivity, ManualActivityInput, ManualActivityKind,
  SourceBreakdown, ThemeBreakdown, SOURCE_KEYS, THEME_KEYS,
  ChessableCourseSummary, ChessableTheme,
} from './training-goals.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';

// level -1 = Zukunft (leer); manual = enthält selbst gemeldete Aktivität
export interface GoalCell { date: string; status: GoalStatus; level: number; manual: boolean; }

/** Eine Zeile einer Aufschlüsselung (Quelle/Thema): i18n-Label + Sekunden + Balkenanteil. */
export interface BreakRow { label: string; seconds: number; pct: number; }

/** Alle manuellen Aktivitätsarten + ob sie in Minuten (sonst Partienzahl) gemessen werden. */
export const MANUAL_KINDS: { kind: ManualActivityKind; minutes: boolean }[] = [
  { kind: 'OtbGame', minutes: false },
  { kind: 'OfflinePuzzle', minutes: true },
  { kind: 'OfflineStudy', minutes: true },
  { kind: 'Coaching', minutes: true },
];

/** Wird die Art in Minuten gemessen (sonst Anzahl Partien)? */
export function isMinutesKind(kind: ManualActivityKind): boolean {
  return kind !== 'OtbGame';
}

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

/** Wandelt eine Aufschlüsselung in Anzeige-Zeilen (nur Töpfe mit Zeit, Anteil am Topf-Total). */
export function breakdownRows(buckets: Record<string, number>, keys: { key: string; label: string }[]): BreakRow[] {
  const total = keys.reduce((sum, k) => sum + (buckets[k.key] ?? 0), 0);
  return keys
    .map(k => ({ label: k.label, seconds: buckets[k.key] ?? 0, pct: total > 0 ? Math.round((100 * (buckets[k.key] ?? 0)) / total) : 0 }))
    .filter(r => r.seconds > 0);
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
  selector: 'app-training-goals',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatIconModule, MatButtonModule,
    MatFormFieldModule, MatInputModule, MatProgressBarModule, MatTooltipModule,
    MatSelectModule, TranslateModule, LoadingSpinnerComponent,
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
                        <span class="bd-val">{{ minutes(r.seconds) }} {{ 'trainingGoals.min' | translate }}</span>
                      </div>
                    }
                  </div>
                  <div class="bd">
                    <div class="bd-title">{{ 'trainingGoals.breakdownByTheme' | translate }}</div>
                    @for (r of todayThemeRows; track r.label) {
                      <div class="bd-row">
                        <span class="bd-label">{{ ('trainingGoals.theme.' + r.label) | translate }}</span>
                        <span class="bd-bar"><span class="bd-fill thm" [style.width.%]="r.pct"></span></span>
                        <span class="bd-val">{{ minutes(r.seconds) }} {{ 'trainingGoals.min' | translate }}</span>
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

        <!-- Manuelle Offline-Aktivität eintragen -->
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'trainingGoals.manual.title' | translate }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <p class="muted small">{{ 'trainingGoals.manual.intro' | translate }}</p>
            <div class="manual-fields">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ 'trainingGoals.manual.kind' | translate }}</mat-label>
                <mat-select [(ngModel)]="manualEdit.kind">
                  @for (k of manualKinds; track k.kind) {
                    <mat-option [value]="k.kind">{{ ('trainingGoals.manual.kinds.' + k.kind) | translate }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ 'trainingGoals.dateCol' | translate }}</mat-label>
                <input matInput type="date" [max]="todayDate" [(ngModel)]="manualEdit.date" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>{{ (manualMinutes ? 'trainingGoals.min' : 'trainingGoals.games') | translate }}</mat-label>
                <input matInput type="number" min="1" [max]="manualMinutes ? 600 : 50" [(ngModel)]="manualEdit.amount" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="note-field">
                <mat-label>{{ 'trainingGoals.manual.note' | translate }}</mat-label>
                <input matInput maxlength="200" [(ngModel)]="manualEdit.note" />
              </mat-form-field>
            </div>
            <div class="actions">
              <button mat-raised-button color="primary" (click)="saveManual()" [disabled]="savingManual">
                {{ (editingManualId ? 'common.save' : 'trainingGoals.manual.add') | translate }}
              </button>
              @if (editingManualId) {
                <button mat-button (click)="cancelManualEdit()" [disabled]="savingManual">{{ 'common.cancel' | translate }}</button>
              }
            </div>

            @if (manualList.length) {
              <ul class="manual-list">
                @for (m of manualList; track m.id) {
                  <li>
                    <span class="m-date">{{ m.date }}</span>
                    <span class="m-kind">{{ ('trainingGoals.manual.kinds.' + m.kind) | translate }}</span>
                    <span class="m-amount">{{ m.amount }} <span class="unit">{{ (isMinutes(m.kind) ? 'trainingGoals.min' : 'trainingGoals.games') | translate }}</span></span>
                    <span class="m-note">{{ m.note }}</span>
                    <span class="m-actions">
                      <button mat-icon-button (click)="editManual(m)" [attr.aria-label]="'common.edit' | translate"><mat-icon>edit</mat-icon></button>
                      <button mat-icon-button (click)="deleteManual(m)" [attr.aria-label]="'common.delete' | translate"><mat-icon>delete</mat-icon></button>
                    </span>
                  </li>
                }
              </ul>
            }
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

              <!-- Perioden-Aufschlüsselung über das ganze Tracker-Fenster -->
              @if (totalSourceRows.length) {
                <div class="breakdowns period">
                  <div class="bd">
                    <div class="bd-title">{{ 'trainingGoals.breakdownBySource' | translate }}</div>
                    @for (r of totalSourceRows; track r.label) {
                      <div class="bd-row">
                        <span class="bd-label">{{ ('trainingGoals.source.' + r.label) | translate }}</span>
                        <span class="bd-bar"><span class="bd-fill src" [style.width.%]="r.pct"></span></span>
                        <span class="bd-val">{{ minutes(r.seconds) }} {{ 'trainingGoals.min' | translate }}</span>
                      </div>
                    }
                  </div>
                  <div class="bd">
                    <div class="bd-title">{{ 'trainingGoals.breakdownByTheme' | translate }}</div>
                    @for (r of totalThemeRows; track r.label) {
                      <div class="bd-row">
                        <span class="bd-label">{{ ('trainingGoals.theme.' + r.label) | translate }}</span>
                        <span class="bd-bar"><span class="bd-fill thm" [style.width.%]="r.pct"></span></span>
                        <span class="bd-val">{{ minutes(r.seconds) }} {{ 'trainingGoals.min' | translate }}</span>
                      </div>
                    }
                  </div>
                </div>
              }
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
                        <td class="strong">{{ mins(d.totalSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></td>
                        <td>{{ mins(d.bySource.randomPuzzleSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></td>
                        <td>{{ mins(d.bySource.courseBookSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></td>
                        <td>{{ mins(d.bySource.chessableSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></td>
                        <td>{{ d.playGames }} <span class="unit">{{ 'trainingGoals.games' | translate }}</span></td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </mat-card-content>
          </mat-card>
        }

        <!-- Chessable-Kurse: History + manuelle Themen-Zuordnung -->
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'trainingGoals.chessable.title' | translate }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <p class="intro small">{{ 'trainingGoals.chessable.intro' | translate }}</p>
            <label class="filter-toggle">
              <input type="checkbox" [ngModel]="chessableUnassignedOnly"
                     (ngModelChange)="toggleUnassignedFilter($event)" />
              {{ 'trainingGoals.chessable.unassignedOnly' | translate }}
            </label>

            @if (loadingCourses) {
              <app-loading-spinner />
            } @else if (!chessableCourses.length) {
              <p class="muted">{{ (chessableUnassignedOnly ? 'trainingGoals.chessable.noneUnassigned' : 'trainingGoals.chessable.none') | translate }}</p>
            } @else {
              <ul class="course-list">
                @for (c of chessableCourses; track c.courseId) {
                  <li>
                    <div class="c-main">
                      <span class="c-name">{{ c.courseName || ('trainingGoals.chessable.course' | translate) }}</span>
                      <span class="c-id">#{{ c.courseId }}</span>
                    </div>
                    <span class="c-time">{{ mins(c.totalSeconds) }} <span class="unit">{{ 'trainingGoals.min' | translate }}</span></span>
                    @if (!c.assignedTheme && c.autoTheme) {
                      <span class="c-auto" [matTooltip]="'trainingGoals.chessable.autoHint' | translate">
                        {{ 'trainingGoals.chessable.auto' | translate }}: {{ ('trainingGoals.theme.' + c.autoTheme) | translate }}
                      </span>
                    } @else if (!c.isAssigned) {
                      <span class="c-unassigned">{{ 'trainingGoals.chessable.unassigned' | translate }}</span>
                    }
                    <mat-form-field appearance="outline" class="c-theme" subscriptSizing="dynamic">
                      <mat-label>{{ 'trainingGoals.chessable.themeLabel' | translate }}</mat-label>
                      <mat-select [ngModel]="selectedTheme(c)" [disabled]="savingCourseId === c.courseId"
                                  (ngModelChange)="assignTheme(c, $event)">
                        <mat-option [value]="null">{{ 'trainingGoals.chessable.clear' | translate }}</mat-option>
                        @for (t of chessableThemes; track t) {
                          <mat-option [value]="t">{{ ('trainingGoals.theme.' + t.toLowerCase()) | translate }}</mat-option>
                        }
                      </mat-select>
                    </mat-form-field>
                  </li>
                }
              </ul>
            }
          </mat-card-content>
        </mat-card>
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
    .breakdowns.period { margin-top: 16px; border-top: 1px solid color-mix(in srgb, currentColor 10%, transparent); padding-top: 14px; }
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
    .manual-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin: 8px 0; }
    .manual-fields .note-field { grid-column: 1 / -1; }
    .manual-list { list-style: none; padding: 0; margin: 12px 0 0; }
    .manual-list li { display: flex; align-items: center; gap: 10px; padding: 6px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); font-size: .9rem; }
    .manual-list .m-date { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 65%, transparent); }
    .manual-list .m-kind { font-weight: 600; }
    .manual-list .m-amount { white-space: nowrap; font-variant-numeric: tabular-nums; }
    .manual-list .m-amount .unit { color: color-mix(in srgb, currentColor 50%, transparent); font-size: .8em; }
    .manual-list .m-note { flex: 1; color: color-mix(in srgb, currentColor 60%, transparent); overflow-wrap: anywhere; }
    .manual-list .m-actions { display: flex; gap: 2px; margin-left: auto; }
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
    .filter-toggle { display: inline-flex; align-items: center; gap: 6px; font-size: .85rem; margin: 4px 0 10px; cursor: pointer; color: color-mix(in srgb, currentColor 75%, transparent); }
    .course-list { list-style: none; padding: 0; margin: 0; }
    .course-list li { display: flex; align-items: center; gap: 12px; padding: 8px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); flex-wrap: wrap; }
    .course-list .c-main { display: flex; flex-direction: column; min-width: 0; flex: 1 1 200px; }
    .course-list .c-name { font-weight: 600; overflow-wrap: anywhere; }
    .course-list .c-id { font-size: .75rem; color: color-mix(in srgb, currentColor 50%, transparent); font-variant-numeric: tabular-nums; }
    .course-list .c-time { font-variant-numeric: tabular-nums; white-space: nowrap; color: color-mix(in srgb, currentColor 70%, transparent); }
    .course-list .c-time .unit { color: color-mix(in srgb, currentColor 50%, transparent); font-size: .8em; }
    .course-list .c-auto { font-size: .78rem; font-style: italic; color: color-mix(in srgb, currentColor 60%, transparent); white-space: nowrap; }
    .course-list .c-unassigned { font-size: .78rem; color: #c2772e; white-space: nowrap; }
    .course-list .c-theme { width: 170px; flex: 0 0 auto; }
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

  // Aufschlüsselungen (heute + Periode), vorgerechnet für die Templates.
  todaySourceRows: BreakRow[] = [];
  todayThemeRows: BreakRow[] = [];
  totalSourceRows: BreakRow[] = [];
  totalThemeRows: BreakRow[] = [];

  // ----- Manuelle Offline-Aktivitäten -----
  readonly manualKinds = MANUAL_KINDS;
  manualList: ManualActivity[] = [];
  savingManual = false;
  editingManualId: number | null = null;
  manualEdit: ManualActivityInput = this.emptyManual();

  // ----- Chessable-Kurs-History + manuelle Themen-Zuordnung -----
  readonly chessableThemes: ChessableTheme[] = ['Opening', 'Middlegame', 'Endgame', 'Tactics'];
  chessableCourses: ChessableCourseSummary[] = [];
  chessableUnassignedOnly = false;
  loadingCourses = false;
  savingCourseId: string | null = null;

  constructor(
    private service: TrainingGoalService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  get hasGoal(): boolean {
    const g = this.goal;
    return !!g && (g.dailyMinutes > 0 || g.playGames > 0);
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
      manual: this.service.listManual(),
    }).subscribe({
      next: ({ goal, today, tracker, manual }) => {
        this.applyGoal(goal);
        this.today = today;
        this.todaySourceRows = this.sourceRows(today.bySource);
        this.todayThemeRows = this.themeRows(today.byTheme);
        this.totalSourceRows = this.sourceRows(tracker.breakdownBySource);
        this.totalThemeRows = this.themeRows(tracker.breakdownByTheme);
        this.tracker = tracker.days.length ? buildGoalTracker(tracker.days, new Date()) : [];
        this.historyDays = orderHistory(tracker.days); // neueste zuerst
        this.manualList = manual;
        this.loading = false;
        this.loadChessableCourses();
      },
      error: () => { this.loading = false; },
    });
  }

  // ----- Chessable-Kurs-History + manuelle Themen-Zuordnung -----

  /** Lädt die Chessable-Kurs-History (respektiert den „nur unzugeordnet"-Filter). */
  loadChessableCourses(): void {
    this.loadingCourses = true;
    this.service.listChessableCourses(this.chessableUnassignedOnly).subscribe({
      next: list => { this.chessableCourses = list; this.loadingCourses = false; },
      error: () => { this.loadingCourses = false; },
    });
  }

  toggleUnassignedFilter(on: boolean): void {
    this.chessableUnassignedOnly = on;
    this.loadChessableCourses();
  }

  /** mat-select-Wert je Kurs: das manuell zugeordnete Thema (Großschreibung) oder null. */
  selectedTheme(c: ChessableCourseSummary): ChessableTheme | null {
    if (!c.assignedTheme) return null;
    return (c.assignedTheme.charAt(0).toUpperCase() + c.assignedTheme.slice(1)) as ChessableTheme;
  }

  /** Thema eines Kurses setzen (null = manuelle Zuordnung entfernen). Aktualisiert danach alles. */
  assignTheme(c: ChessableCourseSummary, theme: ChessableTheme | null): void {
    this.savingCourseId = c.courseId;
    const req = theme
      ? this.service.setChessableCourseTheme(c.courseId, theme)
      : this.service.clearChessableCourseTheme(c.courseId);
    req.subscribe({
      next: () => {
        this.savingCourseId = null;
        this.snackbar.success(this.translate.instant('trainingGoals.chessable.saved'));
        this.reload(); // Themen-Aufschlüsselung + History neu laden
      },
      error: () => { this.savingCourseId = null; this.snackbar.warn(this.translate.instant('trainingGoals.error')); },
    });
  }

  private sourceRows(b: SourceBreakdown): BreakRow[] {
    return breakdownRows(b as unknown as Record<string, number>, SOURCE_KEYS);
  }
  private themeRows(b: ThemeBreakdown): BreakRow[] {
    return breakdownRows(b as unknown as Record<string, number>, THEME_KEYS);
  }

  // ----- Manuelle Offline-Aktivitäten -----

  /** Lokales Datum als yyyy-MM-dd (für date-Input + Default). */
  get todayDate(): string {
    const d = new Date();
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
  }

  /** Wird die aktuell gewählte Art in Minuten gemessen (sonst Partienzahl)? */
  get manualMinutes(): boolean { return isMinutesKind(this.manualEdit.kind); }
  isMinutes(kind: ManualActivityKind): boolean { return isMinutesKind(kind); }

  private emptyManual(): ManualActivityInput {
    return { kind: 'OtbGame', date: this.todayDate, amount: 1, note: '' };
  }

  saveManual(): void {
    const input: ManualActivityInput = {
      kind: this.manualEdit.kind,
      date: this.manualEdit.date || this.todayDate,
      amount: this.clamp(this.manualEdit.amount, this.manualMinutes ? 600 : 50) || 1,
      note: this.manualEdit.note?.trim() || null,
    };
    this.savingManual = true;
    const req = this.editingManualId
      ? this.service.updateManual(this.editingManualId, input)
      : this.service.addManual(input);
    req.subscribe({
      next: () => {
        this.savingManual = false;
        this.snackbar.success(this.translate.instant('trainingGoals.manual.saved'));
        this.cancelManualEdit();
        this.reload();
      },
      error: () => { this.savingManual = false; this.snackbar.warn(this.translate.instant('trainingGoals.error')); },
    });
  }

  editManual(m: ManualActivity): void {
    this.editingManualId = m.id;
    this.manualEdit = { kind: m.kind, date: m.date, amount: m.amount, note: m.note ?? '' };
  }

  cancelManualEdit(): void {
    this.editingManualId = null;
    this.manualEdit = this.emptyManual();
  }

  deleteManual(m: ManualActivity): void {
    this.service.deleteManual(m.id).subscribe({
      next: () => {
        if (this.editingManualId === m.id) this.cancelManualEdit();
        this.snackbar.success(this.translate.instant('trainingGoals.manual.deleted'));
        this.reload();
      },
      error: () => this.snackbar.warn(this.translate.instant('trainingGoals.error')),
    });
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
      dailyMinutes: this.clamp(this.edit.dailyMinutes, 600),
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
