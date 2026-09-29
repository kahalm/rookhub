import { ChangeDetectionStrategy, Component, DestroyRef, Inject, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { NgClass } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { interval } from 'rxjs';
import { BoardArrow, ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { PreferencesService } from '../../core/preferences.service';
import { AnalysisJob, AnalysisJobLive, AnalysisJobsService } from './analysis-jobs.service';
import { EngineDisplayLine, formatElapsed, formatKiloNodes, formatKiloNps, mapBrokerLine, toDisplayLines } from './engine-lines.util';
import type { EngineAnalyseLine } from './external-engine.service';

export interface AnalysisJobViewData {
  jobId: number;
  /** Stellung des Auftrags — das Brett steht damit sofort, bevor der Auftrag geladen ist. */
  fen: string;
  title?: string | null;
}

/** Wie oft der ganze Auftrag nachgeladen wird (Takte à 1 s), solange er nicht fertig ist. */
const JOB_REFRESH_TICKS = 5;

/**
 * „Brett + aktueller Stand" eines Hintergrund-Auftrags (von den gemerkten Stellungen aus). Zeigt die
 * gespeicherten Linien mit Pfeil für den besten Zug, Tiefe, Tempo und Rechenzeit — und startet dabei
 * KEINE Engine: „Im Analysebrett öffnen" mit der Auftrags-Engine würde den Auftrag pausieren (Live-Vorrang).
 * Solange er rechnet, kommt der laufende Stand im Sekundentakt (`/live`, ohne Datenbank), der ganze
 * Auftrag alle fünf Sekunden; fertig oder gescheitert ruht beides.
 */
@Component({
  selector: 'app-analysis-job-view-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgClass, RouterLink, MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    TranslatePipe, ChessBoardComponent],
  template: `
    <h2 mat-dialog-title>{{ (job()?.title || data.title) || ('remembered.analysisOrigin' | translate) }}</h2>
    <mat-dialog-content>
      @if (notFound()) {
        <p>{{ 'analysisJobs.view.notFound' | translate }}</p>
      } @else {
        <div class="body">
          <div class="board">
            <app-chess-board [fen]="fen()" [arrows]="arrows()" [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
          </div>
          <div class="detail">
            @if (job(); as j) {
              <div class="head">
                <span class="status" [ngClass]="j.status">{{ ('analysisJobs.status.' + j.status) | translate }}</span>
                <span>{{ 'analysisJobs.depthOf' | translate:{ reached: j.reachedDepth, target: j.targetDepth } }}</span>
                @if (j.status === 'running' && depthNow() > 0) {
                  <span class="now">{{ 'analysisJobs.runningAt' | translate:{ depth: depthNow() } }}</span>
                }
              </div>
              <div class="stats">
                <span><span class="muted">{{ 'analysisJobs.view.elapsed' | translate }}</span> {{ elapsed() }}</span>
                @if (speed(); as sp) { <span><span class="muted">{{ 'analysisJobs.view.speed' | translate }}</span> {{ sp }}</span> }
                <span>{{ 'analysisJobs.lines' | translate:{ count: j.multiPv } }}</span>
              </div>
              @if (statusHint(); as h) { <p class="muted small">{{ h | translate }}</p> }
              @if (j.lastError && j.status !== 'done') {
                <p class="error"><mat-icon>error_outline</mat-icon> {{ j.lastError }}</p>
              }
              @if (lines().length === 0) {
                <p class="muted">{{ 'analysisJobs.noResult' | translate }}</p>
              } @else {
                <div class="lines">
                  @for (l of lines(); track $index) {
                    <div class="line-row"><span class="line-eval" [class.neg]="!l.positive">{{ l.evalText }}</span><span class="line-san">{{ l.san }}</span></div>
                  }
                </div>
              }
              @if (nodes(); as n) { <p class="muted small">{{ 'analysisJobs.nodes' | translate:{ nodes: n } }}</p> }
              @if (j.status !== 'done' && j.status !== 'failed') {
                <p class="muted small keeps"><mat-icon>autorenew</mat-icon> {{ 'analysisJobs.view.keepsRunning' | translate }}</p>
              }
            } @else if (loadFailed()) {
              <p class="muted">{{ 'analysisJobs.loadFailed' | translate }}</p>
            } @else {
              <mat-spinner diameter="32" />
            }
          </div>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <a mat-button routerLink="/analysis/jobs" mat-dialog-close><mat-icon>list</mat-icon> {{ 'analysisJobs.view.toJobs' | translate }}</a>
      <button mat-flat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .body { display: flex; gap: 16px; align-items: flex-start; flex-wrap: wrap; }
    .board { width: 300px; flex: 0 0 auto; }
    .board app-chess-board { display: block; width: 300px; }
    .detail { flex: 1; min-width: 240px; }
    .head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; font-weight: 500; }
    .stats { display: flex; gap: 14px; flex-wrap: wrap; font-size: .85rem; margin: 6px 0; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
    .keeps { display: flex; align-items: center; gap: 4px; }
    .keeps mat-icon, .error mat-icon { font-size: 16px; width: 16px; height: 16px; flex: 0 0 auto; }
    .error { display: flex; align-items: center; gap: 4px; font-size: .8rem; color: #e65100; }
    .status { font-size: .72rem; font-weight: 700; padding: 1px 8px; border-radius: 999px; text-transform: uppercase;
      background: color-mix(in srgb, currentColor 12%, transparent); }
    .status.running { background: rgba(46,125,50,.18); color: #2e7d32; }
    .status.paused { background: rgba(255,160,0,.18); color: #e65100; }
    .status.done { background: rgba(21,101,192,.15); color: #1565c0; }
    .status.failed { background: rgba(198,40,40,.15); color: #c62828; }
    .now { padding: 0 6px; border-radius: 999px; font-size: .72rem; font-weight: 400;
      background: rgba(46,125,50,.16); color: #2e7d32; }
    .lines { display: flex; flex-direction: column; gap: 4px; margin: 8px 0; }
    .line-row { display: flex; gap: 10px; font-size: .9rem; }
    .line-eval { flex: 0 0 auto; min-width: 52px; font-family: 'Roboto Mono', monospace; font-weight: 600; color: #2e7d32; }
    .line-eval.neg { color: #c62828; }
    .line-san { font-family: 'Roboto Mono', monospace; }
    @media (max-width: 600px) { .board, .board app-chess-board { width: 100%; max-width: 320px; } }
  `],
})
export class AnalysisJobViewDialogComponent implements OnInit {
  readonly preferences = inject(PreferencesService);
  private readonly jobsApi = inject(AnalysisJobsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly job = signal<AnalysisJob | null>(null);
  /** Laufender Stand (Sekundentakt); null, solange der Auftrag nicht rechnet. */
  readonly live = signal<AnalysisJobLive | null>(null);
  readonly notFound = signal(false);
  readonly loadFailed = signal(false);
  private tick = 0;
  private loading = false;
  private liveLoading = false;

  constructor(@Inject(MAT_DIALOG_DATA) public data: AnalysisJobViewData) {}

  readonly fen = computed(() => this.job()?.fen ?? this.data.fen);

  /** Gespeicherte Broker-Zeile, geparst; null ohne/mit kaputtem Ergebnis. */
  private readonly raw = computed<EngineAnalyseLine | null>(() => {
    const json = this.job()?.resultJson;
    if (!json) return null;
    try { return JSON.parse(json) as EngineAnalyseLine; } catch { return null; }
  });

  private readonly mapped = computed(() => {
    const j = this.job();
    const raw = this.raw();
    return j && raw ? mapBrokerLine(j.fen, raw, j.multiPv) : [];
  });

  readonly lines = computed<EngineDisplayLine[]>(() => {
    const j = this.job();
    return j ? toDisplayLines(j.fen, this.mapped(), 14) : [];
  });

  /** Erster Zug jeder Linie als Pfeil — der beste kräftig, die übrigen blass. */
  readonly arrows = computed<BoardArrow[]>(() => this.mapped()
    .filter(l => (l.pvUci[0]?.length ?? 0) >= 4)
    .map((l, i) => ({ from: l.pvUci[0].slice(0, 2), to: l.pvUci[0].slice(2, 4), brush: i === 0 ? 'green' : 'paleBlue' })));

  readonly depthNow = computed(() => this.live()?.depth || this.job()?.currentDepth || 0);

  readonly elapsed = computed(() => formatElapsed(this.live()?.seconds ?? this.job()?.secondsSpent ?? 0));

  /** Tempo: solange gerechnet wird der laufende Wert, sonst der des gespeicherten Ergebnisses. */
  readonly speed = computed<string | null>(() => {
    const j = this.job();
    if (!j) return null;
    if (j.status === 'running') {
      const nps = this.live()?.nps || j.currentNps;
      if (nps) return formatKiloNps(nps);
    }
    const raw = this.raw();
    return raw?.time && raw.nodes ? formatKiloNps(raw.nodes * 1000 / raw.time) : null;
  });

  readonly nodes = computed(() => {
    const raw = this.raw();
    return raw?.nodes ? formatKiloNodes(raw.nodes) : null;
  });

  readonly statusHint = computed<string | null>(() => {
    switch (this.job()?.status) {
      case 'queued': return 'analysisJobs.view.queued';
      case 'paused': return 'analysisJobs.view.paused';
      case 'done': return 'analysisJobs.view.done';
      default: return null;
    }
  });

  ngOnInit(): void {
    this.load();
    interval(1_000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.onTick());
  }

  private onTick(): void {
    if (this.notFound()) return;
    this.tick++;
    const j = this.job();
    if (!j) { if (this.tick % JOB_REFRESH_TICKS === 0) this.load(); return; }
    if (j.status === 'done' || j.status === 'failed') return;
    if (j.status === 'running') {
      // Zwischen zwei Antworten läuft die Uhr lokal weiter — sonst hinge die Anzeige an der Antwortzeit.
      this.live.update(l => l ? { ...l, seconds: l.seconds + 1 } : l);
      this.loadLive();
    } else if (this.live()) {
      this.live.set(null);
    }
    if (this.tick % JOB_REFRESH_TICKS === 0) this.load();
  }

  private load(): void {
    if (this.loading) return;
    this.loading = true;
    this.jobsApi.get(this.data.jobId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: j => { this.loading = false; this.loadFailed.set(false); this.job.set(j); if (j.status !== 'running') this.live.set(null); },
      error: (e: HttpErrorResponse) => {
        this.loading = false;
        if (e.status === 404) this.notFound.set(true);
        else if (!this.job()) this.loadFailed.set(true);   // mit Stand: still, der nächste Takt heilt
      },
    });
  }

  private loadLive(): void {
    if (this.liveLoading) return;
    this.liveLoading = true;
    this.jobsApi.live().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: rows => { this.liveLoading = false; this.live.set(rows.find(r => r.id === this.data.jobId) ?? null); },
      error: () => { this.liveLoading = false; },
    });
  }
}
