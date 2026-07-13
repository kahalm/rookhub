import { Component, Inject, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { WeeklyService, WeeklyPlayerBreakdown } from './weekly.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

/** Daten des Dialogs: welcher Wochenpost + welcher Spieler. */
export interface WeeklyBreakdownDialogData {
  weeklyId: number;
  userId: number;
  playerName: string;
}

/**
 * Admin-Detailaufschlüsselung eines Spielers bei einem Wochenpost: eine Zeile je gespieltem Puzzle
 * mit Zeit, Tipps, Fehlzügen und Mausrutschern. Geöffnet über das (i) in der Bestenliste.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-weekly-breakdown-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatIconModule, MatButtonModule, TranslatePipe, LoadingSpinnerComponent],
  template: `
    <h2 mat-dialog-title>
      {{ 'weekly.breakdown.title' | translate:{ name: data.playerName } }}
    </h2>
    <mat-dialog-content>
      @if (loading) {
        <app-loading-spinner />
      } @else if (error) {
        <p class="bd-empty">{{ 'weekly.breakdown.loadFailed' | translate }}</p>
      } @else if ((bd?.rows?.length ?? 0) === 0) {
        <p class="bd-empty">{{ 'weekly.breakdown.empty' | translate }}</p>
      } @else {
        <div class="bd-scroll">
          <table class="bd-table">
            <thead>
              <tr>
                <th class="bd-num">#</th>
                <th>{{ 'weekly.breakdown.puzzle' | translate }}</th>
                <th class="bd-res">{{ 'weekly.breakdown.result' | translate }}</th>
                <th class="bd-num">{{ 'weekly.breakdown.time' | translate }}</th>
                <th class="bd-num">{{ 'weekly.breakdown.hints' | translate }}</th>
                <th class="bd-num">{{ 'weekly.breakdown.wrong' | translate }}</th>
                <th class="bd-num">{{ 'weekly.breakdown.mouseslips' | translate }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of bd!.rows; track row.puzzleIndex) {
                <tr>
                  <td class="bd-num">{{ row.puzzleIndex + 1 }}</td>
                  <td class="bd-title">{{ row.title || ('weekly.breakdown.untitled' | translate) }}</td>
                  <td class="bd-res">
                    @if (row.solved) {
                      <mat-icon class="bd-ok" [attr.title]="'weekly.breakdown.solved' | translate">check_circle</mat-icon>
                    } @else {
                      <mat-icon class="bd-fail" [attr.title]="'weekly.breakdown.failed' | translate">cancel</mat-icon>
                    }
                  </td>
                  <td class="bd-num">{{ fmtTime(row.timeSeconds) }}</td>
                  <td class="bd-num" [class.bd-zero]="row.hintsUsed === 0">{{ row.hintsUsed }}</td>
                  <td class="bd-num" [class.bd-zero]="row.wrongAttempts === 0">{{ row.wrongAttempts }}</td>
                  <td class="bd-num" [class.bd-zero]="row.mouseslips === 0">{{ row.mouseslips }}</td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr class="bd-sum">
                <td colspan="3">{{ 'weekly.breakdown.totalRow' | translate:{ played: bd!.rows.length, total: bd!.total } }}</td>
                <td class="bd-num">{{ fmtTime(sum('timeSeconds')) }}</td>
                <td class="bd-num">{{ sum('hintsUsed') }}</td>
                <td class="bd-num">{{ sum('wrongAttempts') }}</td>
                <td class="bd-num">{{ sum('mouseslips') }}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .bd-scroll { overflow-x: auto; }
    .bd-table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; min-width: 420px; }
    .bd-table th, .bd-table td { text-align: left; padding: 4px 8px; border-bottom: 1px solid color-mix(in srgb, currentColor 10%, transparent); white-space: nowrap; }
    .bd-table th { color: color-mix(in srgb, currentColor 50%, transparent); font-weight: 600; font-size: 0.78rem; }
    .bd-num { text-align: right; width: 3.5em; }
    .bd-res { text-align: center; width: 3em; }
    .bd-title { white-space: normal; min-width: 8em; }
    .bd-zero { color: color-mix(in srgb, currentColor 35%, transparent); }
    .bd-ok { color: #2e7d32; font-size: 20px; height: 20px; width: 20px; }
    .bd-fail { color: #c62828; font-size: 20px; height: 20px; width: 20px; }
    .bd-sum td { border-top: 2px solid color-mix(in srgb, currentColor 20%, transparent); font-weight: 600; }
    .bd-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; padding: 8px 0; }
  `]
})
export class WeeklyBreakdownDialogComponent implements OnInit {
  bd: WeeklyPlayerBreakdown | null = null;
  loading = true;
  error = false;

  constructor(
    private weekly: WeeklyService,
    @Inject(MAT_DIALOG_DATA) public data: WeeklyBreakdownDialogData,
  ) {}

  ngOnInit(): void {
    this.weekly.getPlayerBreakdown(this.data.weeklyId, this.data.userId).subscribe({
      next: bd => { this.bd = bd; this.loading = false; },
      error: () => { this.error = true; this.loading = false; },
    });
  }

  /** Summe einer numerischen Spalte über alle Zeilen. */
  sum(key: 'timeSeconds' | 'hintsUsed' | 'wrongAttempts' | 'mouseslips'): number {
    return (this.bd?.rows ?? []).reduce((acc, r) => acc + (r[key] || 0), 0);
  }

  /** Zeit als m:ss bzw. h:mm:ss. */
  fmtTime(seconds: number): string {
    const s = Math.max(0, Math.floor(seconds));
    const sec = s % 60, m = Math.floor(s / 60) % 60, h = Math.floor(s / 3600);
    const p2 = (n: number) => n.toString().padStart(2, '0');
    return h > 0 ? `${h}:${p2(m)}:${p2(sec)}` : `${m}:${p2(sec)}`;
  }
}
