import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { AnalysisHistoryEntry, AnalysisHistoryService } from './analysis-history.service';

const START_BOARD = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR';

/**
 * Analyse-Verlauf (0.603.0): die letzten 20 Analysen des Analysebretts zur Auswahl. Schließt mit dem gewählten Eintrag;
 * das Brett lädt ihn samt Stand und Sternen.
 */
@Component({
  selector: 'app-analysis-history-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'analysis.history.title' | translate }}</h2>
    <mat-dialog-content>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      } @else if (entries().length === 0) {
        <p class="muted">{{ 'analysis.history.empty' | translate }}</p>
      } @else {
        <div class="list">
          @for (e of entries(); track e.id) {
            <div class="row">
              <button type="button" class="pick" (click)="choose(e)">
                <span class="head">
                  <span class="name">{{ e.title || (fromStart(e) ? ('analysis.history.fromStart' | translate) : ('analysis.history.fromPosition' | translate)) }}</span>
                  @if (e.starred.length > 0) { <span class="stars">★ {{ e.starred.length }}</span> }
                  <span class="muted small">{{ e.updatedAt | date:'short' }}</span>
                </span>
                <span class="preview">{{ e.preview || ('analysis.history.noMoves' | translate) }}</span>
                <span class="muted small">{{ 'analysis.history.moves' | translate:{ count: e.moveCount } }}</span>
              </button>
              <button mat-icon-button type="button" (click)="remove(e)" [matTooltip]="'common.delete' | translate">
                <mat-icon>delete</mat-icon>
              </button>
            </div>
          }
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .78rem; }
    .list { display: flex; flex-direction: column; gap: 2px; }
    .row { display: flex; align-items: center; gap: 4px; }
    .pick { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 2px; text-align: left; padding: 6px 8px;
      border: 0; border-radius: 6px; background: transparent; color: inherit; font: inherit; cursor: pointer; }
    .pick:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .head { display: flex; align-items: baseline; gap: 8px; flex-wrap: wrap; }
    .name { font-weight: 500; }
    .stars { color: #f9a825; font-size: .8rem; font-weight: 600; }
    .preview { font-family: 'Roboto Mono', monospace; font-size: .82rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  `],
})
export class AnalysisHistoryDialogComponent implements OnInit {
  private readonly api = inject(AnalysisHistoryService);
  private readonly ref = inject(MatDialogRef<AnalysisHistoryDialogComponent>);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  readonly entries = signal<AnalysisHistoryEntry[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.api.list().subscribe({
      next: e => { this.entries.set(e); this.loading.set(false); },
      error: () => { this.loading.set(false); this.snackbar.warn(this.translate.instant('analysis.history.loadFailed')); },
    });
  }

  fromStart(e: AnalysisHistoryEntry): boolean { return e.startFen.split(' ')[0] === START_BOARD; }

  choose(e: AnalysisHistoryEntry): void { this.ref.close(e); }

  remove(e: AnalysisHistoryEntry): void {
    this.api.delete(e.id).subscribe({
      next: () => this.entries.set(this.entries().filter(x => x.id !== e.id)),
      error: () => this.snackbar.warn(this.translate.instant('analysis.history.deleteFailed')),
    });
  }
}
