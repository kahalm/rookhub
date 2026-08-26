import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { AnalysisJob, AnalysisJobsService } from './analysis-jobs.service';

export interface AnalysisJobDialogData {
  fen: string;
  /** Vorbelegung aus den aktuellen Analyse-Einstellungen. */
  depth: number;
  lines: number;
  /** false = im Profil ist keine Hintergrund-Engine festgelegt → nur Hinweis, kein Anlegen. */
  hasBackgroundEngine: boolean;
}

/** Tiefen zur Auswahl — die tiefen Stufen nur für Aufträge sinnvoll (Live-Picker endet bei 50). */
export const JOB_DEPTH_OPTIONS = [20, 24, 28, 30, 32, 35, 40, 45, 50, 55, 60];
export const JOB_LINE_OPTIONS = [1, 2, 3, 4, 5, 6, 8, 10];

/**
 * „Im Hintergrund analysieren": Tiefe + Linien (vorbelegt mit den Live-Einstellungen) und optionaler
 * Titel; legt den Auftrag direkt an (`POST /api/analysis-jobs`) und gibt ihn zurück. Rechnet später auf
 * der Hintergrund-Engine, sobald sie frei ist — pausiert, solange live extern gerechnet wird.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-analysis-job-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatDialogModule, MatButtonModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'analysisJobs.dialog.title' | translate }}</h2>
    <mat-dialog-content>
      @if (!data.hasBackgroundEngine) {
        <p class="warn"><mat-icon>info_outline</mat-icon> {{ 'analysisJobs.dialog.noEngine' | translate }}</p>
        <a mat-stroked-button routerLink="/profile" (click)="ref.close(null)">{{ 'analysisJobs.dialog.toProfile' | translate }}</a>
      } @else {
        <p class="hint">{{ 'analysisJobs.dialog.hint' | translate }}</p>
        <div class="row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ 'analysisJobs.dialog.depth' | translate }}</mat-label>
            <mat-select [(ngModel)]="depth">
              @for (d of depthOptions; track d) { <mat-option [value]="d">{{ d }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ 'analysisJobs.dialog.lines' | translate }}</mat-label>
            <mat-select [(ngModel)]="lines">
              @for (n of lineOptions; track n) { <mat-option [value]="n">{{ n }}</mat-option> }
            </mat-select>
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
          <mat-label>{{ 'analysisJobs.dialog.titleLabel' | translate }}</mat-label>
          <input matInput [(ngModel)]="title" maxlength="200" (keyup.enter)="submit()" />
        </mat-form-field>
        <p class="hint small">{{ 'analysisJobs.dialog.costHint' | translate }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(null)">{{ 'common.cancel' | translate }}</button>
      @if (data.hasBackgroundEngine) {
        <button mat-flat-button color="primary" [disabled]="busy" (click)="submit()">
          {{ 'analysisJobs.dialog.submit' | translate }}
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .row { display: flex; gap: 12px; flex-wrap: wrap; }
    .row mat-form-field { flex: 1 1 120px; }
    .full { width: 100%; margin-top: 8px; }
    .hint { color: color-mix(in srgb, currentColor 65%, transparent); font-size: .9rem; margin: 0 0 12px; }
    .hint.small { font-size: .8rem; margin: 4px 0 0; }
    .warn { display: flex; align-items: center; gap: 6px; margin: 0 0 12px; }
  `],
})
export class AnalysisJobDialogComponent {
  readonly depthOptions = JOB_DEPTH_OPTIONS;
  readonly lineOptions = JOB_LINE_OPTIONS;
  depth: number;
  lines: number;
  title = '';
  busy = false;

  constructor(
    public ref: MatDialogRef<AnalysisJobDialogComponent, AnalysisJob | null>,
    @Inject(MAT_DIALOG_DATA) public data: AnalysisJobDialogData,
    private jobs: AnalysisJobsService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {
    // Vorbelegung auf die nächste angebotene Stufe heben (Live-Tiefe 22 → Auftrag 24).
    this.depth = JOB_DEPTH_OPTIONS.find(d => d >= data.depth) ?? JOB_DEPTH_OPTIONS[JOB_DEPTH_OPTIONS.length - 1];
    this.lines = JOB_LINE_OPTIONS.includes(data.lines) ? data.lines : 3;
  }

  submit(): void {
    if (this.busy || !this.data.hasBackgroundEngine) return;
    this.busy = true;
    this.jobs.create({ fen: this.data.fen, targetDepth: this.depth, multiPv: this.lines, title: this.title.trim() || null })
      .subscribe({
        next: job => { this.busy = false; this.ref.close(job); },
        error: err => {
          this.busy = false;
          const msg = err?.error?.message as string | undefined;
          this.snackbar.warn(this.translate.instant('analysisJobs.createFailed') + (msg ? ` (${msg})` : ''));
        },
      });
  }
}
