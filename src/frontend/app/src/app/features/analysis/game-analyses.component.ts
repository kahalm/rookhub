import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, interval } from 'rxjs';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';
import { GameAnalysis, GameAnalysisService } from './game-analysis.service';
import { JOB_DEPTH_OPTIONS } from './analysis-job-dialog.component';

/**
 * Seite „Partie-Analysen" (`/analysis/games`): eine ganze Partie einwerfen und von der
 * Hintergrund-Engine Stellung für Stellung durchrechnen lassen — statt jede Stellung einzeln
 * einzureihen. Zeigt je Partie den Fortschritt; Details (Brett + Bewertung je Zug) liegen unter
 * `/analysis/games/:id`.
 *
 * <p>Der Poll läuft NUR, solange eine Partie offen ist (wie auf der Auftragsseite) — eine
 * abgeschlossene Liste erzeugt keinen Verkehr.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-game-analyses',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatTooltipModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatProgressBarModule,
    TranslatePipe, LoadingSpinnerComponent],
  template: `
    <div class="ga-container">
      <div class="header">
        <h1>{{ 'gameAnalysis.title' | translate }}</h1>
        <a mat-stroked-button routerLink="/analysis/jobs">
          <mat-icon>list</mat-icon> {{ 'gameAnalysis.toJobs' | translate }}
        </a>
      </div>
      <p class="muted intro">{{ 'gameAnalysis.intro' | translate }}</p>

      <mat-card class="new-card">
        <mat-card-content>
          <mat-form-field appearance="outline" class="full">
            <mat-label>{{ 'gameAnalysis.pgnLabel' | translate }}</mat-label>
            <textarea matInput rows="5" [(ngModel)]="pgn" [disabled]="creating"
                      [placeholder]="'gameAnalysis.pgnPlaceholder' | translate"></textarea>
          </mat-form-field>
          <div class="new-row">
            <mat-form-field appearance="outline" class="depth">
              <mat-label>{{ 'gameAnalysis.depth' | translate }}</mat-label>
              <mat-select [(ngModel)]="depth" [disabled]="creating">
                @for (d of depthOptions; track d) { <mat-option [value]="d">{{ d }}</mat-option> }
              </mat-select>
            </mat-form-field>
            <span class="muted small hint">{{ 'gameAnalysis.depthHint' | translate }}</span>
            <button mat-flat-button color="primary" [disabled]="creating || !pgn.trim()" (click)="create()">
              <mat-icon>play_arrow</mat-icon> {{ 'gameAnalysis.start' | translate }}
            </button>
          </div>
        </mat-card-content>
      </mat-card>

      @if (loading) {
        <app-loading-spinner />
      } @else if (analyses.length === 0) {
        <mat-card><mat-card-content>
          <p>{{ 'gameAnalysis.empty' | translate }}</p>
        </mat-card-content></mat-card>
      } @else {
        @for (a of analyses; track a.id) {
          <mat-card class="ga">
            <mat-card-content>
              <div class="ga-head">
                <a class="ga-title" [routerLink]="['/analysis/games', a.id]">{{ a.title || ('gameAnalysis.untitled' | translate) }}</a>
                <span class="chip" [class]="'st-' + a.status">{{ 'gameAnalysis.status.' + a.status | translate }}</span>
                <span class="spacer"></span>
                <span class="muted small">{{ 'gameAnalysis.depthLines' | translate:{ depth: a.targetDepth, lines: a.multiPv } }}</span>
                <button mat-icon-button [attr.aria-label]="'common.delete' | translate"
                        [matTooltip]="'common.delete' | translate" (click)="remove(a)">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
              <mat-progress-bar mode="determinate" [value]="percent(a)"></mat-progress-bar>
              <div class="ga-foot muted small">
                <span>{{ 'gameAnalysis.progress' | translate:{ done: a.analyzedPlies, total: a.plyCount } }}</span>
                @if (a.result) { <span>· {{ a.result }}</span> }
                @if (a.lastError) { <span class="err">· {{ a.lastError }}</span> }
              </div>
            </mat-card-content>
          </mat-card>
        }
      }
    </div>
  `,
  styles: [`
    .ga-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 12px; }
    .header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h1 { margin: 0; font-size: 1.5rem; }
    .intro { margin: 4px 0 14px; }
    .new-card { margin-bottom: 16px; }
    .full { width: 100%; }
    .new-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .depth { width: 120px; }
    .hint { flex: 1 1 220px; }
    .ga { margin-bottom: 10px; }
    .ga-head { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin-bottom: 8px; }
    .ga-title { font-weight: 600; text-decoration: none; color: inherit; }
    .ga-title:hover { text-decoration: underline; }
    .spacer { flex: 1 1 auto; }
    .chip { font-size: .72rem; padding: 2px 8px; border-radius: 10px; border: 1px solid currentColor; }
    .st-done { color: #2e7d32; }
    .st-failed { color: #c62828; }
    .st-running, .st-pending { color: #1976d2; }
    .ga-foot { display: flex; gap: 6px; flex-wrap: wrap; margin-top: 6px; }
    .err { color: #c62828; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class GameAnalysesComponent implements OnInit, OnDestroy {
  private service = inject(GameAnalysisService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  analyses: GameAnalysis[] = [];
  loading = true;
  creating = false;
  pgn = '';
  depth = GameAnalysisService.DefaultDepth;
  readonly depthOptions = JOB_DEPTH_OPTIONS;

  private poll?: Subscription;

  ngOnInit(): void {
    this.load();
    // 10-s-Takt wie auf der Auftragsseite — und nur, solange etwas offen ist.
    this.poll = interval(10000).subscribe(() => { if (this.hasOpen()) this.load(true); });
  }

  ngOnDestroy(): void { this.poll?.unsubscribe(); }

  hasOpen(): boolean {
    return this.analyses.some(a => a.status === 'pending' || a.status === 'running');
  }

  percent(a: GameAnalysis): number {
    return a.plyCount > 0 ? Math.round((100 * a.analyzedPlies) / a.plyCount) : 0;
  }

  private load(silent = false): void {
    if (!silent) this.loading = true;
    this.service.list().subscribe({
      next: list => { this.analyses = list; this.loading = false; this.cdr.markForCheck(); },
      // Poll-Fehler bleiben still (nächster Durchlauf kommt), der ERSTE Ladefehler nicht.
      error: () => {
        this.loading = false;
        if (!silent) this.snackbar.warn(this.translate.instant('gameAnalysis.loadFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  create(): void {
    const pgn = this.pgn.trim();
    if (!pgn || this.creating) return;
    this.creating = true;
    this.service.create({ pgn, targetDepth: this.depth }).subscribe({
      next: created => {
        this.creating = false;
        this.pgn = '';
        this.analyses = [created, ...this.analyses];
        this.snackbar.success(this.translate.instant('gameAnalysis.started'));
        this.cdr.markForCheck();
      },
      error: err => {
        this.creating = false;
        // Der Nutzer hat gerade etwas ausgelöst → konkrete Meldung (fehlende Engine, kaputtes PGN).
        this.snackbar.warn(err?.error?.message || this.translate.instant('gameAnalysis.startFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  remove(a: GameAnalysis): void {
    this.service.delete(a.id).subscribe({
      next: () => {
        this.analyses = this.analyses.filter(x => x.id !== a.id);
        this.cdr.markForCheck();
      },
      error: () => this.snackbar.warn(this.translate.instant('gameAnalysis.deleteFailed')),
    });
  }
}
