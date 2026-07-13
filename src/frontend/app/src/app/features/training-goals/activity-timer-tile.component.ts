import { Component, DestroyRef, EventEmitter, OnDestroy, OnInit, Output, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { of, timer } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import {
  TrainingGoalService, ActivityPreset, ActivityTimer, ManualActivityKind,
} from './training-goals.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ActivityTimerStopDialogComponent, StopDialogData, StopDialogResult } from './activity-timer-stop-dialog.component';

/** Material-Icon je Timer-Aktivitätsart. */
export function activityKindIcon(kind: ManualActivityKind): string {
  switch (kind) {
    case 'OfflinePuzzle': return 'extension';
    case 'OfflineStudy': return 'menu_book';
    case 'Coaching': return 'school';
    default: return 'schedule';
  }
}

/**
 * Dashboard-Kachel für den Offline-Trainings-Timer. Zwei Zustände:
 *  • Kein Timer läuft → Liste der Preset-Chips (Schnellstart); Klick startet den Timer.
 *    Wenn keine Vorlagen existieren → Hinweis + Deep-Link auf /training-goals.
 *  • Timer läuft → Label + Live-Dauer (client-seitig 1-s-Ticker) + Stop-Button (Backdate-Dialog)
 *    + Verwerfen-Button (bricht ohne Eintrag ab, mit Bestätigung).
 * Pollt initial und dann alle 30 s vom Server, damit andere Geräte/Browser-Tabs schnell mitziehen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-activity-timer-tile',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatDialogModule, TranslatePipe,
  ],
  template: `
    @if (running) {
      <div class="running">
        <div class="run-head">
          <mat-icon class="run-kind-icon">{{ icon(running.kind) }}</mat-icon>
          <span class="run-label" [title]="running.label">{{ running.label }}</span>
        </div>
        <div class="run-duration" aria-live="polite">{{ formatDuration(elapsed) }}</div>
        <div class="run-actions">
          <button mat-flat-button color="primary" (click)="openStopDialog()" [disabled]="busy">
            <mat-icon>stop_circle</mat-icon>
            {{ 'trainingGoals.timer.stop' | translate }}
          </button>
          <button mat-stroked-button (click)="discard()" [disabled]="busy"
                  [matTooltip]="'trainingGoals.timer.discardTooltip' | translate">
            <mat-icon>delete_outline</mat-icon>
            {{ 'trainingGoals.timer.discard' | translate }}
          </button>
        </div>
      </div>
    } @else if (loading) {
      <p class="hint">{{ 'common.loading' | translate }}</p>
    } @else if (presets.length === 0) {
      <p class="hint">{{ 'trainingGoals.timer.noPresets' | translate }}</p>
      <div class="run-actions">
        <button mat-stroked-button routerLink="/training-goals" fragment="presets">
          <mat-icon>add</mat-icon>
          {{ 'trainingGoals.timer.managePresets' | translate }}
        </button>
      </div>
    } @else {
      <p class="hint">{{ 'trainingGoals.timer.pickPreset' | translate }}</p>
      <div class="presets">
        @for (p of presets; track p.id) {
          <button mat-stroked-button class="preset-chip" (click)="start(p)" [disabled]="busy">
            <mat-icon>{{ icon(p.kind) }}</mat-icon>
            {{ p.label }}
          </button>
        }
      </div>
      <div class="run-actions">
        <button mat-button routerLink="/training-goals" fragment="presets">
          <mat-icon>edit</mat-icon>
          {{ 'trainingGoals.timer.managePresets' | translate }}
        </button>
      </div>
    }
  `,
  styles: [`
    :host { display: block; padding: 0.25rem 0; }
    .hint { color: color-mix(in srgb, currentColor 65%, transparent); font-size: 0.88rem; margin: 0 0 0.5rem; }
    .presets { display: flex; flex-wrap: wrap; gap: 0.4rem; margin-bottom: 0.5rem; }
    .preset-chip { min-width: 0; padding: 0 12px; }
    .preset-chip mat-icon { font-size: 18px; width: 18px; height: 18px; margin-right: 4px; vertical-align: middle; }
    .run-actions { display: flex; flex-wrap: wrap; gap: 0.4rem; align-items: center; }
    .running { display: flex; flex-direction: column; gap: 0.6rem; }
    .run-head { display: flex; align-items: center; gap: 0.4rem; }
    .run-kind-icon { color: color-mix(in srgb, currentColor 65%, transparent); }
    .run-label { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .run-duration { font-size: 1.7rem; font-variant-numeric: tabular-nums; font-weight: 600;
      letter-spacing: 0.02em; color: var(--mat-sys-primary, #3f51b5); }
  `]
})
export class ActivityTimerTileComponent implements OnInit, OnDestroy {
  private destroyRef = inject(DestroyRef);

  presets: ActivityPreset[] = [];
  running: ActivityTimer | null = null;
  loading = true;
  /** Sperrt Bedienelemente während eines Start/Stop/Discard-Requests. */
  busy = false;
  /** Live-Sekunden für die Anzeige (client-seitiger Ticker + Server-Elapsed als Ausgangswert). */
  elapsed = 0;

  /** Ausgabe an das Dashboard: sobald ein Timer gestoppt/verworfen wird → z. B. `today`-Card neu laden. */
  @Output() saved = new EventEmitter<void>();

  private tickHandle: ReturnType<typeof setInterval> | null = null;

  constructor(
    private goals: TrainingGoalService,
    private dialog: MatDialog,
    private translate: TranslateService,
    private snackbar: SnackbarService,
  ) {}

  ngOnInit(): void {
    // Sofort + alle 30 s vom Server aktualisieren (andere Geräte / verpasstes Auto-Stop).
    timer(0, 30_000).pipe(
      switchMap(() => this.goals.getTimer().pipe(catchError(() => of(null)))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(t => {
      this.running = t;
      if (t) this.elapsed = t.elapsedSeconds;
      this.loading = false;
      this.ensureTicker();
    });

    this.reloadPresets();
  }

  ngOnDestroy(): void {
    this.stopTicker();
  }

  reloadPresets(): void {
    this.goals.listPresets().pipe(
      catchError(() => of([] as ActivityPreset[])),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(list => this.presets = list);
  }

  start(preset: ActivityPreset): void {
    if (this.busy) return;
    if (this.running && !confirm(this.translate.instant('trainingGoals.timer.replaceConfirm'))) return;
    this.busy = true;
    this.goals.startTimer({ presetId: preset.id }).subscribe({
      next: t => {
        this.running = t;
        this.elapsed = t.elapsedSeconds;
        this.ensureTicker();
        this.busy = false;
        this.snackbar.info(this.translate.instant('trainingGoals.timer.started', { label: preset.label }),
          { action: 'common.ok', duration: 2000 });
      },
      error: () => {
        this.busy = false;
        this.snackbar.info(this.translate.instant('trainingGoals.timer.startFailed'),
          { action: 'common.ok', duration: 3000 });
      },
    });
  }

  openStopDialog(): void {
    if (!this.running || this.busy) return;
    const data: StopDialogData = {
      label: this.running.label,
      startedAtIso: this.running.startedAt,
      theme: this.running.theme ?? null,
    };
    const ref = this.dialog.open<ActivityTimerStopDialogComponent, StopDialogData, StopDialogResult>(
      ActivityTimerStopDialogComponent, { data, width: '460px', maxWidth: '95vw' });
    ref.afterClosed().subscribe(result => {
      if (!result) return;
      this.doStop(result);
    });
  }

  private doStop(result: StopDialogResult): void {
    this.busy = true;
    this.goals.stopTimer({
      startedAt: result.startedAtIso,
      endedAt: result.endedAtIso ?? undefined,
      note: result.note || undefined,
      theme: result.theme,
    }).subscribe({
        next: saved => {
          this.running = null;
          this.elapsed = 0;
          this.stopTicker();
          this.busy = false;
          this.saved.emit();
          this.snackbar.info(this.translate.instant('trainingGoals.timer.savedEntry', { minutes: saved.amount }),
            { action: 'common.ok', duration: 3000 });
        },
        error: () => {
          this.busy = false;
          this.snackbar.info(this.translate.instant('trainingGoals.timer.stopFailed'),
            { action: 'common.ok', duration: 3000 });
        },
      });
  }

  discard(): void {
    if (!this.running || this.busy) return;
    if (!confirm(this.translate.instant('trainingGoals.timer.discardConfirm'))) return;
    this.busy = true;
    this.goals.discardTimer().subscribe({
      next: () => {
        this.running = null;
        this.elapsed = 0;
        this.stopTicker();
        this.busy = false;
      },
      error: () => {
        this.busy = false;
        this.snackbar.info(this.translate.instant('trainingGoals.timer.discardFailed'),
          { action: 'common.ok', duration: 3000 });
      },
    });
  }

  icon(kind: ManualActivityKind): string { return activityKindIcon(kind); }

  formatDuration(totalSeconds: number): string {
    const s = Math.max(0, Math.floor(totalSeconds));
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    const pad = (n: number) => n.toString().padStart(2, '0');
    return h > 0 ? `${h}:${pad(m)}:${pad(sec)}` : `${m}:${pad(sec)}`;
  }

  private ensureTicker(): void {
    if (this.running && !this.tickHandle) {
      this.tickHandle = setInterval(() => this.elapsed++, 1000);
    } else if (!this.running && this.tickHandle) {
      this.stopTicker();
    }
  }

  private stopTicker(): void {
    if (this.tickHandle) { clearInterval(this.tickHandle); this.tickHandle = null; }
  }
}
