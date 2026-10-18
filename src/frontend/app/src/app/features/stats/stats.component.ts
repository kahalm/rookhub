import { Component, OnInit, ChangeDetectionStrategy, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { PuzzleService, PuzzleStatsDto, EloHistoryPoint, ThemeStat, RatingBand, ActivityDay, PuzzleBreakdown, CourseStatsDto, AttemptStatsDto } from '../puzzles/puzzle.service';
import { PreferencesService } from '../../core/preferences.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LatestRequest } from '../../shared/latest-request.util';
import { PctPipe } from '../../shared/format/pct';
import { PuzzleThemePipe } from '../puzzles/puzzle-theme.pipe';

export interface Curve { poly: string; path: string; minElo: number; maxElo: number; w: number; h: number; first: string; last: string; }
export interface HeatCell { date: string; count: number; level: number; }   // level -1 = Zukunft (leer)
/** Normalisierte Zeile der „letzte Puzzles"-Tabelle (Standard + Kurs vereinheitlicht). */
export interface RecentRow { rating: number; solved: boolean; time: number; attemptedAt: string; eloChange: number | null; link: (string | number)[];
  /** Kurs-Versuch: Titel der Aufgabe (z. B. „Exercise 245“), sonst null (b1-rk-stats-kurse-wertung-leer). */
  label?: string | null; }

/**
 * Baut aus Punkten einen geglätteten SVG-Pfad (Catmull-Rom → kubische Béziers, läuft durch
 * alle Punkte). Glättet die zackige Polyline ohne die Datenpunkte zu verfälschen. '' bei < 2.
 */
export function smoothPath(pts: { x: number; y: number }[]): string {
  if (pts.length < 2) return '';
  const f = (n: number) => n.toFixed(1);
  if (pts.length === 2) return `M${f(pts[0].x)},${f(pts[0].y)} L${f(pts[1].x)},${f(pts[1].y)}`;
  let d = `M${f(pts[0].x)},${f(pts[0].y)}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] ?? pts[i];
    const p1 = pts[i];
    const p2 = pts[i + 1];
    const p3 = pts[i + 2] ?? p2;
    const c1x = p1.x + (p2.x - p0.x) / 6, c1y = p1.y + (p2.y - p0.y) / 6;
    const c2x = p2.x - (p3.x - p1.x) / 6, c2y = p2.y - (p3.y - p1.y) / 6;
    d += ` C${f(c1x)},${f(c1y)} ${f(c2x)},${f(c2y)} ${f(p2.x)},${f(p2.y)}`;
  }
  return d;
}

/**
 * Gleitender Mittelwert (zentriert, ungerades Fenster `win`) — dämpft Zacken/Ausreißer der
 * Elo-Reihe, bevor daraus die Kurve gebaut wird. Längen-erhaltend; Reihen < 3 bleiben unverändert.
 */
export function smoothSeries(vals: number[], win = 5): number[] {
  if (win < 3 || vals.length < 3) return vals.slice();
  const half = Math.floor(win / 2);
  return vals.map((_, i) => {
    let sum = 0, n = 0;
    for (let j = Math.max(0, i - half); j <= Math.min(vals.length - 1, i + half); j++) { sum += vals[j]; n++; }
    return sum / n;
  });
}

export function heatLevel(count: number): number {
  if (count <= 0) return 0;
  if (count <= 2) return 1;
  if (count <= 5) return 2;
  if (count <= 9) return 3;
  return 4;
}

/** UI-Sweep 2026-10-11 (d2-rk-stats-heatmap): ab Laptop-Breite ein ganzes Jahr (53 Wochen, ~800 px; der Server
 *  liefert 365 Tage), darunter ein halbes Jahr — am Handy scrollt das Raster ohnehin nach rechts. */
export function heatmapWeeksFor(viewportWidth: number): number {
  if (viewportWidth >= 1024) return 53;
  // d3-rk-stats-aktivitaet-raster-halbleer: am Tablet (600–1023 px) so viele Wochen, wie in die Karte passen
  // (12-px-Zelle + 3 px Lücke = 15 px je Woche; 72 px = Seiten- und Kartenpolster) — 820 px → 49 statt fest 27.
  if (viewportWidth >= 600) return Math.min(53, Math.floor((viewportWidth - 72) / 15));
  return 27;
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
  let minElo = Math.min(...elos), maxElo = Math.max(...elos);   // Achsen aus den Rohwerten
  if (minElo === maxElo) { minElo -= 10; maxElo += 10; }
  const n = points.length;
  const sm = smoothSeries(elos);                                // Reihe vorglätten (gleitender Mittel)
  const x = (i: number) => pad + (i / (n - 1)) * (w - 2 * pad);
  const y = (elo: number) => h - pad - ((elo - minElo) / (maxElo - minElo)) * (h - 2 * pad);
  const poly = points.map((p, i) => `${x(i).toFixed(1)},${y(p.elo).toFixed(1)}`).join(' ');
  const path = smoothPath(sm.map((e, i) => ({ x: x(i), y: y(e) })));
  const fmt = (s: string) => { const d = new Date(s); return isNaN(d.getTime()) ? '' : d.toLocaleDateString(); };
  return { poly, path, minElo, maxElo, w, h, first: fmt(points[0].attemptedAt), last: fmt(points[n - 1].attemptedAt) };
}

/**
 * UI-Sweep 2026-10-11 (r2-rp-elo-level): hat `level` keine Versuche, andere Level aber schon, nennt der Hinweis
 * das Level mit den meisten (bei Gleichstand das niedrigste). Sonst null.
 */
export function emptyLevelHint(points: EloHistoryPoint[], level: number | null): { level: number; other: number; count: number } | null {
  if (level == null || level < 0 || points.some(p => p.vizLevel === level)) return null;
  const counts = new Map<number, number>();
  for (const p of points) counts.set(p.vizLevel, (counts.get(p.vizLevel) ?? 0) + 1);
  let best: { other: number; count: number } | null = null;
  for (const [other, count] of counts) {
    if (!best || count > best.count || (count === best.count && other < best.other)) best = { other, count };
  }
  return best ? { level, ...best } : null;
}

export interface OverlayLine { level: number; poly: string; path: string; color: string; }
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

  const lines: OverlayLine[] = drawn.map(([level, ps]) => {
    const sm = smoothSeries(ps.map(p => p.elo));               // je Level vorglätten
    return {
      level,
      color: LEVEL_COLORS[level % LEVEL_COLORS.length],
      poly: ps.map((p, i) => `${xOf(p, i, ps.length).toFixed(1)},${yOf(p.elo).toFixed(1)}`).join(' '),
      path: smoothPath(ps.map((p, i) => ({ x: xOf(p, i, ps.length), y: yOf(sm[i]) }))),
    };
  });

  const byTime = [...all].sort((a, b) => Date.parse(a.attemptedAt) - Date.parse(b.attemptedAt));
  const fmt = (s: string) => { const d = new Date(s); return isNaN(d.getTime()) ? '' : d.toLocaleDateString(); };
  return { lines, minElo, maxElo, w, h, first: fmt(byTime[0].attemptedAt), last: fmt(byTime[byTime.length - 1].attemptedAt) };
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-stats',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, MatCardModule, MatIconModule,
    MatFormFieldModule, MatSelectModule, MatButtonModule, MatButtonToggleModule, MatTableModule, MatTooltipModule, TranslatePipe, LoadingSpinnerComponent, LoadErrorComponent, PuzzleThemePipe, PctPipe
  ],
  template: `
    <div class="stats-container">
      <h1>{{ 'stats.title' | translate }}</h1>

      <mat-button-toggle-group class="mode-toggle" [(ngModel)]="mode" (change)="onModeChange()" aria-label="Modus">
        <mat-button-toggle value="standard">{{ 'stats.modeStandard' | translate }}</mat-button-toggle>
        <mat-button-toggle value="course">{{ 'stats.modeCourse' | translate }}</mat-button-toggle>
      </mat-button-toggle-group>

      @if (loading) {
        <app-loading-spinner />
      } @else if (loadError) {
        <app-load-error (retry)="load()" />
      } @else {
        <div class="cards">
          @if (mode === 'standard') {
            <mat-card class="stat"><div class="val">{{ stats?.puzzleElo ?? '–' }}</div><div class="lbl">{{ 'stats.currentElo' | translate }}</div></mat-card>
          }
          <mat-card class="stat"><div class="val">{{ current?.solved ?? 0 }}</div><div class="lbl">{{ 'stats.totalSolved' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ current?.totalAttempts ?? 0 }}</div><div class="lbl">{{ 'stats.attempts' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ (current?.accuracy ?? 0) | pct:1 }}</div><div class="lbl">{{ 'stats.accuracy' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ current?.currentStreak ?? 0 }}</div><div class="lbl">{{ 'stats.currentStreak' | translate }}</div></mat-card>
          <mat-card class="stat"><div class="val">{{ current?.bestStreak ?? 0 }}</div><div class="lbl">{{ 'stats.bestStreak' | translate }}</div></mat-card>
        </div>

        <!-- UI-Sweep a1 (a1-rk-statistik-leer): ohne einen einzigen Versuch statt drei Leerkarten (Verlauf, Level-Elo
             mit Startwerten, Letzte Puzzles) EINE Leerkarte mit Weg weiter (DESIGN 5.10). b1 (b1-rk-stats-kurse-leer): im
             Kurs-Modus genauso — vorher eine kopflose Karte plus „Letzte Puzzles – Noch keine Daten.“, ohne Weg weiter. -->
        @if (empty) {
          <mat-card class="stats-empty">
            <mat-card-content>
              @if (mode === 'course') {
                <p class="se-text">{{ 'stats.courseEmpty' | translate }}</p>
                <a mat-flat-button color="primary" routerLink="/courses">{{ 'stats.courseEmptyAction' | translate }}</a>
              } @else {
                <p class="se-text">{{ 'stats.standardEmpty' | translate }}</p>
                <a mat-flat-button color="primary" routerLink="/puzzles">{{ 'stats.standardEmptyAction' | translate }}</a>
              }
            </mat-card-content>
          </mat-card>
        }

        @if (mode === 'standard' && !empty) {
        <mat-card class="chart-card">
          <mat-card-header>
            <mat-card-title>{{ 'stats.eloProgress' | translate }}</mat-card-title>
            <mat-form-field appearance="outline" class="level-field" subscriptSizing="dynamic">
              <mat-label>{{ 'stats.level' | translate }}</mat-label>
              <mat-select [(ngModel)]="level" (selectionChange)="onLevelChange()">
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
                      <path [attr.d]="l.path" fill="none" [attr.stroke]="l.color" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke" />
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
                  <path class="elo-line" [attr.d]="curve.path" fill="none" stroke="#1976d2" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke" />
                </svg>
              </div>
              <div class="x-axis"><span>{{ curve.first }}</span><span>{{ curve.last }}</span></div>
            } @else {
              <p class="muted">{{ 'stats.noData' | translate }}</p>
            }
            <!-- UI-Sweep 2026-10-11 (r2-rp-elo-level): das bevorzugte Level ist leer — sagen, wo die Versuche liegen. -->
            @if (levelHint; as h) {
              <p class="level-hint">
                <!-- a1-rk-statistik-level-hinweis: selbst auf „Alle“ umgeschaltet → sagen, warum. -->
                {{ (level === -1 ? 'stats.levelEmptySwitched' : 'stats.levelEmpty') | translate:{ level: h.level } }} –
                <button type="button" class="level-link" (click)="showLevel(h.other)">
                  {{ (h.count === 1 ? 'stats.levelOtherOne' : 'stats.levelOther') | translate:{ level: h.other, count: h.count } }}
                </button>
              </p>
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
        }

        <!-- UI-Sweep z2 (z2-rk-stats-gross-einspaltig): ab 1280 px stehen „Nach Thema" und „Schwächste Themen" nebeneinander. -->
        <div class="theme-pair" [class.pair]="worstThemes.length && themes.length > 5">
        @if (themes.length) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'stats.byTheme' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="theme-list">
                @for (t of themes; track t.theme) {
                  <div class="theme-row">
                    <span class="theme-name" [title]="t.theme | puzzleTheme">{{ t.theme | puzzleTheme }}</span>
                    <div class="theme-bar"><div class="theme-fill" [style.width.%]="acc(t.solved, t.attempts)"></div></div>
                    <span class="theme-val">{{ acc(t.solved, t.attempts) | pct }} · {{ t.solved }}/{{ t.attempts }}</span>
                  </div>
                }
              </div>
            </mat-card-content>
          </mat-card>
        }

        @if (worstThemes.length && themes.length > 5) {
          <mat-card>
            <mat-card-header><mat-card-title>{{ 'stats.worstThemes' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="theme-list">
                @for (t of worstThemes; track t.theme) {
                  <div class="theme-row">
                    <span class="theme-name" [title]="t.theme | puzzleTheme">{{ t.theme | puzzleTheme }}</span>
                    <div class="theme-bar"><div class="theme-fill warn" [style.width.%]="acc(t.solved, t.attempts)"></div></div>
                    <span class="theme-val">{{ acc(t.solved, t.attempts) | pct }} · {{ t.solved }}/{{ t.attempts }}</span>
                  </div>
                }
              </div>
            </mat-card-content>
          </mat-card>
        }
        </div>

        @if (ratingBands.length) {
          <mat-card class="bands-card">
            <mat-card-header><mat-card-title>{{ 'stats.byRating' | translate }}</mat-card-title></mat-card-header>
            <mat-card-content>
              <div class="bands">
                @for (b of ratingBands; track b.from) {
                  <div class="band" [matTooltip]="b.solved + '/' + b.attempts + ' · ' + (acc(b.solved, b.attempts) | pct)">
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
              <div class="heatmap" #heatmapEl>
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

        @if (!empty) {
        <mat-card class="recent-card">
          <mat-card-header><mat-card-title>{{ 'stats.recentPuzzles' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            @if (recentRows.length === 0) {
              <p class="muted">{{ 'stats.noData' | translate }}</p>
            } @else {
              <div class="recent-scroll">
              <table mat-table [dataSource]="recentRows" class="full-width recent-table">
                <ng-container matColumnDef="date">
                  <th mat-header-cell *matHeaderCellDef>{{ 'stats.date' | translate }}</th>
                  <td mat-cell *matCellDef="let a">{{ a.attemptedAt | date:'short' }}</td>
                </ng-container>
                <!-- b1-rk-stats-kurse-wertung-leer: Kurs-Versuche haben meist keine Wertung — die Aufgabe heißt nach ihrem Titel. -->
                <ng-container matColumnDef="task">
                  <th mat-header-cell *matHeaderCellDef>{{ 'stats.task' | translate }}</th>
                  <td mat-cell *matCellDef="let a" class="task-cell" [title]="a.label || ('stats.courseTask' | translate)">{{ a.label || ('stats.courseTask' | translate) }}</td>
                </ng-container>
                <ng-container matColumnDef="rating">
                  <th mat-header-cell *matHeaderCellDef>{{ 'stats.rating' | translate }}</th>
                  <td mat-cell *matCellDef="let a">{{ a.rating || '–' }}</td>
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
                  <td mat-cell *matCellDef="let a">{{ a.time }}s</td>
                </ng-container>
                <ng-container matColumnDef="open">
                  <th mat-header-cell *matHeaderCellDef></th>
                  <td mat-cell *matCellDef="let a"><a mat-icon-button [routerLink]="a.link"><mat-icon>open_in_new</mat-icon></a></td>
                </ng-container>
                <tr mat-header-row *matHeaderRowDef="cols"></tr>
                <tr mat-row *matRowDef="let row; columns: cols;"></tr>
              </table>
              </div>
              <!-- UI-Sweep 2026-10-11 (r2-rp-stats-mobile): am Handy statt der 6-spaltigen Tabelle eine
                   zweizeilige Liste, die ganze Zeile führt zum Puzzle. -->
              <div class="recent-list">
                @for (a of recentRows; track $index) {
                  <a class="recent-item" [routerLink]="a.link">
                    <span class="ri-main">
                      <!-- UI-Sweep z2 (z2-rk-stats-letzte-liste): Zahl beschriftet, Pfeil zeigt die antippbare Zeile. -->
                      <!-- b1-rk-stats-kurse-wertung-leer: Kurs-Zeilen nennen die Aufgabe statt „Puzzle –“. -->
                      <span class="ri-title">{{ a.attemptedAt | date:'short' }} · {{ mode === 'course'
                        ? (a.label || (a.rating ? ('stats.puzzleRating' | translate: { rating: a.rating }) : ('stats.courseTask' | translate)))
                        : ('stats.puzzleRating' | translate: { rating: a.rating || '–' }) }}</span>
                      <span class="ri-sub">{{ a.time }} s</span>
                    </span>
                    <span class="ri-res" [class.res-ok]="a.solved" [class.res-fail]="!a.solved">
                      <mat-icon [attr.aria-label]="'stats.result' | translate">{{ a.solved ? 'check_circle' : 'cancel' }}</mat-icon>
                      @if (a.eloChange != null) { <span>{{ (a.eloChange >= 0 ? '+' : '−') + (a.eloChange >= 0 ? a.eloChange : -a.eloChange) }}</span> }
                      <mat-icon class="ri-go" aria-hidden="true">chevron_right</mat-icon>
                    </span>
                  </a>
                }
              </div>
            }
          </mat-card-content>
        </mat-card>
        }
      }
    </div>
  `,
  styles: [`
    /* UI-Sweep z3 (z3-rk-stats-seitenbreite): Seitenbreite wie /courses (DESIGN 4.1) statt 1000 px bzw. 1400 px ab 1280 px —
       am Laptop lief der Inhalt sonst bis auf 12 px an den Rand, und die Breite sprang beim Seitenwechsel. */
    /* b1-rk-statistik-rand-12: Seitenpolster 16 px wie die übrigen Seiten (DESIGN 3) — vorher 12 px, die Kante sprang. */
    .stats-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 16px; }
    .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 12px; margin-bottom: 16px; }
    /* UI-Sweep z1 (z1-rk-stats-kacheln): Abstand der Kacheln nur über gap — sonst kamen die 16 px von mat-card dazu. */
    .cards mat-card { margin: 0; }
    /* b1-rk-stats-kurse-leer: Leerkarte zentriert, ein Satz, eine Aktion (DESIGN 5.10) — in beiden Modi. */
    .stats-empty mat-card-content { display: flex; flex-direction: column; align-items: center; gap: 12px;
      text-align: center; padding: 2rem 1rem; }
    .stats-empty .se-text { margin: 0; color: var(--mat-sys-on-surface-variant); }
    @media (max-width: 599.98px) {
      .cards { grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 8px; }
      .stat .val { font-size: 1.35rem; }
    }
    /* UI-Sweep z1 (z1-rk-stats-titelabstand): einheitlicher Abstand zwischen Kartentitel und Inhalt. */
    mat-card:not(.chart-card) > mat-card-header + mat-card-content { padding-top: 8px; }
    .stat { text-align: center; padding: 8px; }
    .stat .val { font-size: 1.6rem; font-weight: 700; color: var(--rh-accent); }
    .stat .lbl { font-size: .8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .chart-card mat-card-header { display: flex; align-items: center; justify-content: space-between; }
    .level-field { width: 130px; }
    .level-hint { font-size: .85rem; color: color-mix(in srgb, currentColor 60%, transparent); margin: 8px 0 0; }
    .level-link { background: none; border: none; padding: 0; font: inherit; cursor: pointer; text-decoration: underline;
      color: var(--mat-sys-primary, #1976d2); }
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
    .recent-scroll { overflow-x: auto; }
    .recent-table { min-width: 480px; }
    .recent-list { display: none; }
    .recent-item { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 4px;
      min-height: 44px; border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); color: inherit; text-decoration: none; }
    .ri-main { display: flex; flex-direction: column; font-variant-numeric: tabular-nums; min-width: 0; }
    .ri-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .task-cell { max-width: 22rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .ri-sub { font-size: .75rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .ri-res { display: inline-flex; align-items: center; gap: 4px; font-variant-numeric: tabular-nums; font-weight: 600; }
    .ri-res mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .ri-res mat-icon.ri-go { font-size: 18px; width: 18px; height: 18px; color: color-mix(in srgb, var(--mat-sys-on-surface, currentColor) 45%, transparent); }
    @media (max-width: 599.98px) {
      .recent-scroll { display: none; }
      .recent-list { display: block; }
    }
    .res-ok { color: var(--rh-success); } .res-fail { color: var(--rh-error); }
    .pos { color: var(--rh-success); } .neg { color: var(--rh-error); }
    .small { font-size: .75rem; }
    .theme-list { display: flex; flex-direction: column; gap: 6px; }
    .theme-row { display: flex; align-items: center; gap: 10px; font-size: .85rem; }
    /* UI-Sweep 2026-10-11 (r2-rp-themes-en): übersetzte Namen statt capitalize-Schlüssel; lange Namen werden
       mit Auslassung gekürzt statt mitten im Wort umzubrechen (voller Name im title). */
    .theme-name { width: 150px; flex: 0 0 auto; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .theme-bar { flex: 1; height: 12px; background: color-mix(in srgb, currentColor 8%, transparent); border-radius: 6px; overflow: hidden; }
    .theme-fill { height: 100%; background: var(--rh-accent); }
    .theme-fill.warn { background: var(--rh-error); }
    .theme-val { width: 120px; flex: 0 0 auto; text-align: right; color: color-mix(in srgb, currentColor 65%, transparent); font-variant-numeric: tabular-nums; }
    /* UI-Sweep z1 (z1-rk-stats-themenbalken): Handy-Breiten NACH den Grundregeln — gleiche Spezifität, die spätere gewinnt
       (vorher standen sie davor und wirkten nie; der Balken blieb ~44 px schmal). */
    @media (max-width: 599.98px) {
      /* UI-Sweep z2 (z2-rk-stats-themenname): Name breiter („Vorgerückter Bauer"), Zahl nur so breit wie nötig. */
      .theme-name { width: 128px; }
      .theme-val { width: auto; min-width: 64px; }
    }
    /* UI-Sweep z2 (z2-rk-stats-gross-einspaltig): großer Bildschirm nutzt die Breite. */
    @media (min-width: 1280px) {
      .theme-pair.pair { display: grid; grid-template-columns: 3fr 2fr; gap: 16px; align-items: start; }
    }
    /* Die Baender sind BREITER als ein Hochkant-Handy: 200er-Schritte ueber die ganze
       Rating-Spanne ergeben schnell 14-18 Saeulen. Ohne eigenes overflow-x wuchs damit die SEITE
       (rund zwei Bildschirme Scroll nach rechts) - und weil CDK-Overlays gegen das verbreiterte
       Dokument rechnen, landeten die Untermenues des Hamburger-Menues unten rechts ausserhalb des
       Sichtfelds. Breite Inhalte scrollen deshalb IN IHREM Container, wie die Heatmap darunter.
       ACHTUNG: keine Backticks in diesen Kommentaren - der styles-Block ist ein Template-Literal. */
    .bands { display: flex; align-items: stretch; gap: 6px; height: 140px; overflow-x: auto; }
    /* flex: 1 0 34px - fuellt die Karte, wenn Platz ist, schrumpft aber NICHT unter die
       Beschriftung (sonst ueberlappen Zahl und Rating-Grenze); dann scrollt der Streifen. */
    .band { display: flex; flex-direction: column; align-items: center; gap: 2px; flex: 1 0 34px; height: 100%; }
    .band-lbl, .band-cnt { white-space: nowrap; }
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
    /* d3-rk-eink-aktivitaet-heatmap-hellgrau (DESIGN 3.6): im E-Ink-Modus keine Stufen in Grüntönen/10-%-Grau — jede Zelle
       schwarz umrandet, leerer Tag weiß, dann eine Helligkeitstreppe bis Schwarz (auch in Graustufen unterscheidbar).
       Elo-Verlauf: weiße Fläche mit Rahmen, Linie schwarz und kräftiger. */
    :host-context(html.eink) .hm-cell { border: 1px solid #000; box-sizing: border-box; }
    :host-context(html.eink) .hm-cell.lvl-1 { border-color: transparent; background: transparent; }
    :host-context(html.eink) .hm-cell.lvl0 { background: #fff; }
    :host-context(html.eink) .hm-cell.lvl1 { background: #a3a3a3; }
    :host-context(html.eink) .hm-cell.lvl2 { background: #6b6b6b; }
    :host-context(html.eink) .hm-cell.lvl3 { background: #333; }
    :host-context(html.eink) .hm-cell.lvl4 { background: #000; }
    :host-context(html.eink) .svg { background: #fff; outline: 2px solid #000; }
    :host-context(html.eink) .elo-line { stroke: #000; stroke-width: 2.5px; }
    /* UI-Sweep z3 (z3-rk-stats-heatmap-gross): am großen Bildschirm wachsen die Zellen mit (53 × 21 px ≈ 1110 px),
       sonst stand das Raster in der linken Hälfte der Karte. */
    @media (min-width: 1280px) {
      .heatmap, .hm-col { gap: 4px; }
      .hm-cell { width: 17px; height: 17px; }
    }
    /* b1-rk-stats-heatmap-ultrawide: in der 1 880-px-Breitbildseite füllte das 17-px-Raster nur ~60 % der Karte —
       ab 2400 px 28-px-Zellen (53 Wochen × 34 px ≈ 1 800 px). */
    @media (min-width: 2400px) {
      .heatmap, .hm-col { gap: 6px; }
      .hm-cell { width: 28px; height: 28px; }
    }
    mat-card { margin-bottom: 16px; }
    .mode-toggle { margin-bottom: 16px; }
    /* UI-Sweep u1 (u1-rk-stats-2spalten): Breitbild-Stufe ab 2400 px (DESIGN 4.1) — Seite breiter und zweispaltig:
       links Elo-Verlauf, Elo pro Stufe und Rating-Histogramm, rechts das Themen-Paar; Kopf, Kennzahlen, Aktivität und
       letzte Puzzles über die volle Breite. Alle Karten sind direkte Kinder des Containers, daher ohne Umbau des
       Templates; „dense“ holt das Themen-Paar neben den Elo-Verlauf hoch. */
    @media (min-width: 2400px) {
      .stats-container { max-width: min(var(--page-max-width-wide, 1880px), 96vw); display: grid;
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); grid-auto-flow: row dense; column-gap: 16px; align-items: start; }
      .stats-container > * { grid-column: 1 / -1; }
      .stats-container > .chart-card, .stats-container > .perlevel-card, .stats-container > .bands-card { grid-column: 1; }
      .stats-container > .theme-pair { grid-column: 2; grid-row: span 3; }
      /* alles-voll-2: der Umschalter zog sich als Rasterkind über die ganze Breite; ohne Themen-Paar (keine Daten) blieb
         die rechte Spalte leer — dann alles über die volle Breite. */
      .stats-container > .mode-toggle { justify-self: start; }
      .stats-container:not(:has(> .theme-pair)) > * { grid-column: 1 / -1; }
    }
  `]
})
export class StatsComponent implements OnInit {
  /** Umschalter: Standard-Puzzle-Statistik vs. Kurs-Puzzle-Statistik (kein Elo bei Kursen). */
  mode: 'standard' | 'course' = 'standard';

  loading = true;
  /** Laden gescheitert — sonst zeigten die Karten Nullen und „–" wie bei einem neuen Konto. */
  loadError = false;
  stats: PuzzleStatsDto | null = null;
  courseStats: CourseStatsDto | null = null;
  perLevel: { level: number; elo: number }[] = [];
  /** Normalisierte Zeilen der „letzte Puzzles"-Tabelle (für beide Modi). */
  recentRows: RecentRow[] = [];
  cols: string[] = ['date', 'rating', 'result', 'elo', 'time', 'open'];

  /** Default = aktuell bei den Puzzles eingestellte Visualisierungsstufe (im Konstruktor gesetzt). */
  level = 0;
  private eloPoints: EloHistoryPoint[] = [];
  curve: Curve | null = null;
  overlay: Overlay | null = null;   // „Alle"-Ansicht: alle Level in einer Grafik, farbkodiert
  /** UI-Sweep 2026-10-11 (r2-rp-elo-level): Hinweis „Level X leer – Level Y hat n Versuche" (null = keiner). */
  levelHint: { level: number; other: number; count: number } | null = null;
  /** Bevorzugtes Level, von dem beim Laden auf „Alle" ausgewichen wurde, weil es keine Versuche hat. */
  private autoSwitchedFrom: number | null = null;

  themes: ThemeStat[] = [];
  worstThemes: ThemeStat[] = [];   // 5 schwächste (Lösungs-% aufsteigend, min. 3 Versuche)
  ratingBands: RatingBand[] = [];
  heatmap: HeatCell[][] = [];
  private maxBandSolved = 0;
  /** Laden des GEWÄHLTEN Modus: ein Wechsel bricht das Laden des vorigen ab — sonst landeten dessen
   *  Zeilen (recentRows/Themen teilen sich beide Modi) unter der neuen Überschrift. */
  private loadRequest = new LatestRequest();

  /** UI-Sweep 2026-10-11 (r2-rp-stats-mobile): die Heatmap beginnt links mit den ältesten Wochen —
   *  am Handy lag „heute" rechts außerhalb des Sichtbereichs. Sobald sie gerendert ist, ganz nach rechts. */
  @ViewChild('heatmapEl') set heatmapEl(ref: ElementRef<HTMLElement> | undefined) {
    const el = ref?.nativeElement;
    if (!el || el === this.scrolledHeatmap) return;
    this.scrolledHeatmap = el;
    requestAnimationFrame(() => { el.scrollLeft = el.scrollWidth; });
  }
  private scrolledHeatmap: HTMLElement | null = null;

  constructor(private puzzles: PuzzleService, private prefs: PreferencesService) {
    this.level = prefs.visualization;
  }

  /**
   * Die Statistik des GERADE gewaehlten Modus. Die fuenf Kacheln zeigen genau die Kennzahlen, die
   * beide Modi gemeinsam haben (`AttemptStatsDto`) — vorher stand die Modus-Weiche fuenfmal
   * nebeneinander, einmal je Kachel, und eine sechste Kennzahl haette eine sechste gebraucht.
   * Das Elo bleibt draussen: es gibt es nur im Standard-Modus, und genau dafuer steht die
   * eigene Kachel hinter `mode === 'standard'`.
   */
  /** a1-rk-statistik-leer / b1-rk-stats-kurse-leer: gewählter Modus ganz ohne Versuch — eine Leerkarte mit Weg weiter
   *  (Standard → Puzzles, Kurse → Kursliste). */
  get empty(): boolean {
    return (this.current?.totalAttempts ?? 0) === 0 && this.recentRows.length === 0;
  }

  get current(): AttemptStatsDto | null {
    return this.mode === 'course' ? this.courseStats : this.stats;
  }

  ngOnInit(): void { this.load(); }

  onModeChange(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.loadError = false;
    this.cols = this.mode === 'course'
      ? ['date', 'task', 'result', 'time', 'open']   // Kurs-Modus: kein Δ-Elo, Aufgabe statt Wertung
      : ['date', 'rating', 'result', 'elo', 'time', 'open'];
    if (this.mode === 'course') this.loadCourse();
    else this.loadStandard();
  }

  private loadStandard(): void {
    this.loadRequest.run(forkJoin({
      stats: this.puzzles.getStats(),
      history: this.puzzles.getHistory(1, 30),
      elo: this.puzzles.getEloHistory(1000),
      breakdown: this.puzzles.getBreakdown(),
    }), {
      next: ({ stats, history, elo, breakdown }) => {
        this.stats = stats;
        this.eloPoints = elo;
        this.perLevel = stats.puzzleEloPerLevel
          ? Object.entries(stats.puzzleEloPerLevel).map(([k, v]) => ({ level: +k, elo: v as number })).sort((a, b) => a.level - b.level)
          : [];
        this.recentRows = history.map(a => ({
          rating: a.puzzleRating, solved: a.solved, time: a.timeSpentSeconds,
          attemptedAt: a.attemptedAt, eloChange: a.eloChange ?? null, link: ['/puzzles', a.puzzleId],
        }));
        this.applyBreakdown(breakdown);
        // UI-Sweep 2026-10-11 (r2-rp-elo-level): hat das bevorzugte Level keine Versuche, andere aber
        // schon, startet der Verlauf auf „Alle" statt leer — der Hinweis sagt, warum.
        if (this.level !== -1 && elo.length > 0 && !elo.some(p => p.vizLevel === this.level)) {
          this.autoSwitchedFrom = this.level;
          this.level = -1;
        }
        this.rebuildCurve();
        this.loading = false;
      },
      error: () => { this.loading = false; this.loadError = true; },
    });
  }

  private loadCourse(): void {
    this.loadRequest.run(forkJoin({
      stats: this.puzzles.getCourseStats(),
      history: this.puzzles.getCourseHistory(1, 30),
      breakdown: this.puzzles.getCourseBreakdown(),
    }), {
      next: ({ stats, history, breakdown }) => {
        this.courseStats = stats;
        this.perLevel = [];
        this.curve = null;
        this.overlay = null;
        this.recentRows = history.map(a => ({
          rating: a.bookRating ?? 0, solved: a.solved, time: a.timeSeconds,
          attemptedAt: a.attemptedAt, eloChange: null, link: ['/puzzles/book', a.bookPuzzleId],
          label: a.title?.trim() || null,
        }));
        // b1-rk-stats-kurse-wertung-leer: Wertungsspalte nur, wenn wenigstens ein Versuch eine hat (DESIGN 5.3).
        this.cols = this.recentRows.some(r => r.rating > 0)
          ? ['date', 'task', 'rating', 'result', 'time', 'open']
          : ['date', 'task', 'result', 'time', 'open'];
        this.applyBreakdown(breakdown);
        this.loading = false;
      },
      error: () => { this.loading = false; this.loadError = true; },
    });
  }

  /** Themen-/Worst-Themen-/Rating-Band-/Heatmap-Aufbereitung — für beide Modi identisch. */
  private applyBreakdown(breakdown: PuzzleBreakdown): void {
    // Nach Lösungs-% absteigend sortieren (bei Gleichstand mehr Versuche zuerst).
    this.themes = [...breakdown.themes].sort((a, b) =>
      this.acc(b.solved, b.attempts) - this.acc(a.solved, a.attempts) || b.attempts - a.attempts);
    // 5 schwächste: nur Themen mit genug Versuchen, nach Lösungs-% aufsteigend (bei Gleichstand mehr Versuche zuerst).
    this.worstThemes = [...breakdown.themes]
      .filter(t => t.attempts >= 3)
      .sort((a, b) => this.acc(a.solved, a.attempts) - this.acc(b.solved, b.attempts) || b.attempts - a.attempts)
      .slice(0, 5);
    this.ratingBands = breakdown.ratingBands;
    this.maxBandSolved = Math.max(1, ...this.ratingBands.map(b => b.solved));
    this.heatmap = breakdown.activity.length
      ? buildHeatmap(breakdown.activity, new Date(), heatmapWeeksFor(window.innerWidth)) : [];
  }

  acc(solved: number, attempts: number): number {
    return attempts > 0 ? Math.round((100 * solved) / attempts) : 0;
  }
  barPct(solved: number): number {
    return Math.round((100 * solved) / this.maxBandSolved);
  }

  /** Auswahl von Hand: ein früheres Ausweichen auf „Alle" gilt nicht mehr. */
  onLevelChange(): void {
    this.autoSwitchedFrom = null;
    this.rebuildCurve();
  }

  /** Knopf im Hinweis: zum Level mit den Versuchen springen. */
  showLevel(level: number): void {
    this.level = level;
    this.onLevelChange();
  }

  rebuildCurve(): void {
    this.levelHint = emptyLevelHint(this.eloPoints, this.level === -1 ? this.autoSwitchedFrom : this.level);
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
