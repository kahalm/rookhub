import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, interval } from 'rxjs';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { AnalysisJob, AnalysisJobsService } from './analysis-jobs.service';
import { EngineDisplayLine, formatElapsed, formatKiloNodes, formatKiloNps, mapBrokerLine, toDisplayLines } from './engine-lines.util';
import { ExternalEngineInfo, ExternalEngineService } from './external-engine.service';
import { JOB_DEPTH_OPTIONS, JOB_LINE_OPTIONS } from './analysis-job-dialog.component';
import type { EngineAnalyseLine } from './external-engine.service';

/**
 * Seite „Analyse-Aufträge" (`/analysis/jobs`): alle Hintergrund-Aufträge des Users mit Status,
 * erreichter Tiefe, Rechenzeit und der Bewertung der Hauptvariante. Aufklappen zeigt Brett + die
 * gespeicherten Linien — OHNE dass eine Engine anläuft (Ergebnis-Zeile wird wie der Live-Stream
 * abgebildet). Dort lassen sich Zieltiefe/Linien nachträglich ändern („weiter bis Tiefe 50";
 * mehr Linien = Suche startet neu, das bisherige Ergebnis bleibt sichtbar). Aktualisiert sich alle
 * 10 s, solange Aufträge offen sind.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-analysis-jobs',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatFormFieldModule, MatSelectModule, MatProgressSpinnerModule, TranslatePipe, ChessBoardComponent, LoadingSpinnerComponent],
  template: `
    <div class="jobs-container">
      <div class="header">
        <h1>{{ 'analysisJobs.title' | translate }}</h1>
        <a mat-stroked-button routerLink="/analysis"><mat-icon>arrow_back</mat-icon> {{ 'analysisJobs.toAnalysis' | translate }}</a>
      </div>
      <p class="muted intro">{{ 'analysisJobs.intro' | translate }}</p>

      @if (loading) {
        <app-loading-spinner />
      } @else if (jobs.length === 0) {
        <mat-card><mat-card-content>
          <p>{{ 'analysisJobs.empty' | translate }}</p>
          <p class="muted">{{ 'analysisJobs.emptyHint' | translate }}</p>
        </mat-card-content></mat-card>
      } @else {
        @for (job of jobs; track job.id) {
          <mat-card class="job" [class.expanded]="expandedId === job.id">
            <div class="job-head" role="button" tabindex="0" (click)="toggle(job)" (keydown.enter)="toggle(job)">
              <mat-icon class="chevron">{{ expandedId === job.id ? 'expand_more' : 'chevron_right' }}</mat-icon>
              <div class="job-main">
                <div class="job-title">
                  <span>{{ job.title || ('analysisJobs.untitled' | translate) }}</span>
                  <span class="status" [ngClass]="job.status">{{ ('analysisJobs.status.' + job.status) | translate }}</span>
                </div>
                <div class="job-meta">
                  {{ 'analysisJobs.depthOf' | translate:{ reached: job.reachedDepth, target: job.targetDepth } }}
                  · {{ 'analysisJobs.lines' | translate:{ count: job.multiPv } }}
                  · {{ formatElapsed(job.secondsSpent) }}
                  @if (speedOf(job); as sp) { · {{ sp }} }
                  @if (evalOf(job); as ev) { · <span class="eval" [class.neg]="!ev.positive">{{ ev.evalText }}</span> }
                </div>
                @if (job.lastError) { <div class="job-error"><mat-icon>error_outline</mat-icon> {{ job.lastError }}</div> }
              </div>
              <button mat-icon-button (click)="$event.stopPropagation(); openInBoard(job)"
                      [matTooltip]="'analysisJobs.openInBoard' | translate"><mat-icon>open_in_new</mat-icon></button>
              <button mat-icon-button (click)="$event.stopPropagation(); remove(job)"
                      [matTooltip]="'common.delete' | translate"><mat-icon>delete</mat-icon></button>
            </div>

            @if (expandedId === job.id) {
              <div class="job-body">
                <div class="board"><app-chess-board [fen]="job.fen" [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" /></div>
                <div class="detail">
                  @if (linesOf(job); as lines) {
                    @if (lines.length === 0) {
                      <p class="muted">{{ 'analysisJobs.noResult' | translate }}</p>
                    } @else {
                      <div class="lines">
                        @for (l of lines; track $index) {
                          <div class="line-row"><span class="line-eval" [class.neg]="!l.positive">{{ l.evalText }}</span><span class="line-san">{{ l.san }}</span></div>
                        }
                      </div>
                    }
                  }
                  <div class="edit">
                    <mat-form-field appearance="outline" class="num" subscriptSizing="dynamic">
                      <mat-label>{{ 'analysisJobs.dialog.depth' | translate }}</mat-label>
                      <mat-select [(ngModel)]="editDepth">
                        @for (d of depthOptions; track d) { <mat-option [value]="d">{{ d }}</mat-option> }
                      </mat-select>
                    </mat-form-field>
                    <mat-form-field appearance="outline" class="num" subscriptSizing="dynamic">
                      <mat-label>{{ 'analysisJobs.dialog.lines' | translate }}</mat-label>
                      <mat-select [(ngModel)]="editLines">
                        @for (n of lineOptions; track n) { <mat-option [value]="n">{{ n }}</mat-option> }
                      </mat-select>
                    </mat-form-field>
                    @if (engines.length > 0) {
                      <mat-form-field appearance="outline" class="engine" subscriptSizing="dynamic">
                        <mat-label>{{ 'analysisJobs.engine' | translate }}</mat-label>
                        <mat-select [(ngModel)]="editEngineId">
                          @for (e of engines; track e.id) { <mat-option [value]="e.id">{{ e.name }}</mat-option> }
                        </mat-select>
                      </mat-form-field>
                    }
                    <button mat-stroked-button [disabled]="saving || !dirty(job)" (click)="save(job)">
                      <mat-icon>save</mat-icon> {{ 'analysisJobs.apply' | translate }}
                    </button>
                    @if (job.status !== 'done') {
                      <button mat-stroked-button [disabled]="saving" (click)="restart(job)"
                              [matTooltip]="'analysisJobs.restartTooltip' | translate">
                        <mat-icon>restart_alt</mat-icon> {{ 'analysisJobs.restart' | translate }}
                      </button>
                    }
                  </div>
                  @if (nodesOf(job); as n) { <p class="muted small">{{ 'analysisJobs.nodes' | translate:{ nodes: n } }}</p> }
                  @if (editLines > job.multiPv) { <p class="muted small">{{ 'analysisJobs.moreLinesHint' | translate }}</p> }
                  <p class="muted small fen">{{ job.fen }}</p>
                </div>
              </div>
            }
          </mat-card>
        }
      }
    </div>
  `,
  styles: [`
    .jobs-container { padding: 1rem; max-width: min(var(--page-max-width, 1240px), 96vw); margin: 0 auto; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; flex-wrap: wrap; }
    .header h1 { margin: 0; }
    .intro { margin: .25rem 0 1rem; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
    .fen { font-family: 'Roboto Mono', monospace; word-break: break-all; }
    .job { margin-bottom: 10px; }
    .job-head { display: flex; align-items: center; gap: 8px; padding: 10px 12px; cursor: pointer; }
    .job-head:hover { background: color-mix(in srgb, currentColor 4%, transparent); }
    .chevron { flex: 0 0 auto; }
    .job-main { flex: 1; min-width: 0; }
    .job-title { display: flex; align-items: center; gap: 8px; font-weight: 500; }
    .job-meta { font-size: .85rem; color: color-mix(in srgb, currentColor 65%, transparent); margin-top: 2px; }
    .job-error { display: flex; align-items: center; gap: 4px; font-size: .8rem; color: #e65100; margin-top: 2px; }
    .job-error mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .status { font-size: .72rem; font-weight: 700; padding: 1px 8px; border-radius: 999px; text-transform: uppercase;
      background: color-mix(in srgb, currentColor 12%, transparent); }
    .status.running { background: rgba(46,125,50,.18); color: #2e7d32; }
    .status.paused { background: rgba(255,160,0,.18); color: #e65100; }
    .status.done { background: rgba(21,101,192,.15); color: #1565c0; }
    .status.failed { background: rgba(198,40,40,.15); color: #c62828; }
    .eval, .line-eval { font-family: 'Roboto Mono', monospace; font-weight: 600; color: #2e7d32; }
    .eval.neg, .line-eval.neg { color: #c62828; }
    .job-body { display: flex; gap: 16px; padding: 0 12px 12px 44px; align-items: flex-start; flex-wrap: wrap; }
    .board { width: 260px; flex: 0 0 auto; }
    .board app-chess-board { display: block; width: 260px; }
    .detail { flex: 1; min-width: 260px; }
    .lines { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
    .line-row { display: flex; gap: 10px; font-size: .9rem; }
    .line-eval { flex: 0 0 auto; min-width: 52px; }
    .line-san { font-family: 'Roboto Mono', monospace; }
    .edit { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    .num { width: 120px; }
    .engine { width: 220px; }
    @media (max-width: 700px) { .job-body { padding-left: 12px; } .board, .board app-chess-board { width: 100%; max-width: 320px; } }
  `],
})
export class AnalysisJobsComponent implements OnInit, OnDestroy {
  jobs: AnalysisJob[] = [];
  loading = true;
  expandedId: number | null = null;
  editDepth = 30;
  editLines = 3;
  editEngineId = '';
  /** Externe Engines des Kontos — für den Engine-Wechsel je Auftrag (inkl. Hintergrund-Engine). */
  engines: ExternalEngineInfo[] = [];
  saving = false;
  readonly depthOptions = JOB_DEPTH_OPTIONS;
  readonly lineOptions = JOB_LINE_OPTIONS;
  readonly formatElapsed = formatElapsed;
  private pollSub?: Subscription;
  /** Ergebnis-Zeilen je Auftrag — nur bei geändertem resultJson neu gemappt (Template-Getter dürfen nicht rechnen). */
  private linesCache = new Map<number, { json: string | null; fen: string; multiPv: number; lines: EngineDisplayLine[] }>();
  /** Geparste Roh-Zeile je Auftrag (für Tempo/Knoten) — gleiche Cache-Regel. */
  private rawCache = new Map<number, { json: string | null; raw: EngineAnalyseLine | null }>();

  /** Gespeicherte Broker-Zeile eines Auftrags, geparst; null ohne/mit kaputtem Ergebnis. */
  private rawOf(job: AnalysisJob): EngineAnalyseLine | null {
    const c = this.rawCache.get(job.id);
    if (c && c.json === job.resultJson) return c.raw;
    let raw: EngineAnalyseLine | null = null;
    if (job.resultJson) { try { raw = JSON.parse(job.resultJson) as EngineAnalyseLine; } catch { raw = null; } }
    this.rawCache.set(job.id, { json: job.resultJson, raw });
    return raw;
  }

  constructor(private jobsApi: AnalysisJobsService, private snackbar: SnackbarService, private translate: TranslateService,
              private router: Router, public preferences: PreferencesService, private cdr: ChangeDetectorRef,
              private externalEngines: ExternalEngineService) {}

  ngOnInit(): void {
    this.load();
    // Engine-Liste für den Wechsel je Auftrag (stiller Hintergrund-Feed; ohne sie bleibt die Auswahl aus).
    this.externalEngines.listEngines().subscribe({
      next: r => { this.engines = r.engines; this.cdr.markForCheck(); },
      error: () => {},
    });
    this.pollSub = interval(10_000).subscribe(() => {
      if (this.jobs.some(j => j.status === 'queued' || j.status === 'running' || j.status === 'paused')) this.load(true);
    });
  }

  ngOnDestroy(): void { this.pollSub?.unsubscribe(); }

  load(silent = false): void {
    if (!silent) this.loading = true;
    this.jobsApi.list().subscribe({
      next: jobs => { this.jobs = jobs; this.loading = false; this.cdr.markForCheck(); },
      error: () => {
        this.loading = false;
        if (!silent) this.snackbar.warn(this.translate.instant('analysisJobs.loadFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  toggle(job: AnalysisJob): void {
    if (this.expandedId === job.id) { this.expandedId = null; return; }
    this.expandedId = job.id;
    this.editDepth = job.targetDepth;
    this.editLines = job.multiPv;
    this.editEngineId = job.engineId;
  }

  /** Gibt es überhaupt etwas zu übernehmen? (sonst bleibt der Knopf aus) */
  dirty(job: AnalysisJob): boolean {
    return this.editDepth !== job.targetDepth || this.editLines !== job.multiPv || this.editEngineId !== job.engineId;
  }

  /** Suchtempo des gespeicherten Ergebnisses in kN/s (der Broker liefert nodes + verstrichene time). */
  speedOf(job: AnalysisJob): string | null {
    const raw = this.rawOf(job);
    if (!raw || !raw.time || !raw.nodes) return null;
    return formatKiloNps(raw.nodes * 1000 / raw.time);
  }

  /** Durchsuchte Stellungen des gespeicherten Ergebnisses (kN). */
  nodesOf(job: AnalysisJob): string | null {
    const raw = this.rawOf(job);
    return raw?.nodes ? formatKiloNodes(raw.nodes) : null;
  }

  restart(job: AnalysisJob): void {
    if (this.saving) return;
    this.saving = true;
    this.jobsApi.restart(job.id).subscribe({
      next: updated => {
        this.saving = false;
        this.jobs = this.jobs.map(j => j.id === updated.id ? updated : j);
        this.snackbar.success(this.translate.instant('analysisJobs.restarted'));
        this.cdr.markForCheck();
      },
      error: () => { this.saving = false; this.snackbar.warn(this.translate.instant('analysisJobs.updateFailed')); this.cdr.markForCheck(); },
    });
  }

  /** Alle gespeicherten Linien eines Auftrags (SAN), gecacht je resultJson. */
  linesOf(job: AnalysisJob): EngineDisplayLine[] {
    const c = this.linesCache.get(job.id);
    if (c && c.json === job.resultJson && c.fen === job.fen && c.multiPv === job.multiPv) return c.lines;
    const raw = this.rawOf(job);
    const lines: EngineDisplayLine[] = raw ? toDisplayLines(job.fen, mapBrokerLine(job.fen, raw, job.multiPv), 14) : [];
    this.linesCache.set(job.id, { json: job.resultJson, fen: job.fen, multiPv: job.multiPv, lines });
    return lines;
  }

  /** Bewertung der Hauptvariante für die Kopfzeile (null ohne Ergebnis). */
  evalOf(job: AnalysisJob): EngineDisplayLine | null {
    return this.linesOf(job)[0] ?? null;
  }

  save(job: AnalysisJob): void {
    if (this.saving) return;
    this.saving = true;
    this.jobsApi.update(job.id, { targetDepth: this.editDepth, multiPv: this.editLines, engineId: this.editEngineId }).subscribe({
      next: updated => {
        this.saving = false;
        this.jobs = this.jobs.map(j => j.id === updated.id ? updated : j);
        this.snackbar.success(this.translate.instant('analysisJobs.updated'));
        this.cdr.markForCheck();
      },
      error: () => { this.saving = false; this.snackbar.warn(this.translate.instant('analysisJobs.updateFailed')); this.cdr.markForCheck(); },
    });
  }

  remove(job: AnalysisJob): void {
    if (!confirm(this.translate.instant('analysisJobs.deleteConfirm'))) return;
    this.jobsApi.delete(job.id).subscribe({
      next: () => { this.jobs = this.jobs.filter(j => j.id !== job.id); if (this.expandedId === job.id) this.expandedId = null; this.cdr.markForCheck(); },
      error: () => this.snackbar.warn(this.translate.instant('analysisJobs.deleteFailed')),
    });
  }

  /** Im Analysebrett öffnen und dort MIT DERSELBEN Engine weiterrechnen: der Provider hat die Stellung
   *  noch in seiner Hashtabelle, die Suche ist also in Sekunden wieder auf der erreichten Tiefe (ein neuer
   *  Auftrag an dieselbe Engine ersetzt den laufenden — der Hintergrund-Auftrag pausiert dabei sauber). */
  openInBoard(job: AnalysisJob): void {
    this.router.navigate(['/analysis'], {
      queryParams: { fen: job.fen, engine: job.engineId, depth: job.targetDepth, lines: job.multiPv },
    });
  }
}
