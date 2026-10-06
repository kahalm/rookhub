import { ChangeDetectionStrategy, Component, DestroyRef, Inject, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatDialogModule, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { interval } from 'rxjs';
import { AnalysisJob, AnalysisJobLive, AnalysisJobsService } from '../analysis/analysis-jobs.service';
import { mapBrokerLine, toDisplayLines } from '../analysis/engine-lines.util';
import type { EngineAnalyseLine } from '../analysis/external-engine.service';
import { DeepKind, DeepLine, DeepStored, deepAhead, deepProgress, prunedEarly } from './deep-analysis.util';

export interface DeepAnalysisDialogData {
  fen: string;
  stored: DeepStored;
}

/** Wie oft die Aufträge ganz nachgeladen werden (Takte à 1 s) — die Linien schreibt der Server alle 5 s. */
const JOB_REFRESH_TICKS = 3;

/** Eine Seite des Fensters (Stockfish oder Lc0): Auftrag, laufender Stand, Ziel. */
class DeepSide {
  readonly job = signal<AnalysisJob | null>(null);
  readonly live = signal<AnalysisJobLive | null>(null);
  constructor(readonly kind: DeepKind, public target: number) {}
  get open(): boolean { const s = this.job()?.status; return !!s && s !== 'done' && s !== 'failed'; }
}

/**
 * „Tiefe Analyse" (0.686.0, gewünscht 2026-10-06): die Stellung auf dem Brett mit Stockfish bis Tiefe 40 und Lc0 bis
 * 500 000 Knoten (oder bis Lc0 per Smart Pruning aufhört). Je Engine ein Fortschrittsbalken; die Linien bleiben die
 * HINTERLEGTEN der Partie-Analyse, bis der neue Auftrag weiter ist (`deepAhead`) — dann laufen sie live mit.
 * Die Aufträge gehören dem Nutzer und rechnen weiter, wenn das Fenster zu ist (Auftragsliste).
 */
@Component({
  selector: 'app-deep-analysis-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, RouterLink, MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'games.deep.title' | translate }}</h2>
    <mat-dialog-content>
      @if (startFailed()) {
        <p class="error">{{ 'games.deep.startFailed' | translate }}</p>
      }
      @for (s of sides; track s.kind) {
        @let p = progress(s);
        @let j = s.job();
        <section class="side" [class.lc0]="s.kind === 'lc0'">
          <div class="side-head">
            <span class="name">{{ s.kind === 'sf' ? 'Stockfish' : 'Lc0' }}</span>
            <span class="state">
              @if (s.kind === 'sf') {
                {{ 'games.deep.depth' | translate: { now: p.now, target: p.target } }}
              } @else {
                {{ 'games.deep.nodes' | translate: { now: (p.now | number), target: (p.target | number) } }}
              }
              · {{ stateKey(s) | translate }}
            </span>
          </div>
          <mat-progress-bar [mode]="j && s.open && p.now === 0 ? 'indeterminate' : 'determinate'" [value]="p.percent" />
          @if (s.kind === 'lc0' && pruned(s)) {
            <p class="hint">{{ 'games.deep.pruned' | translate: { nodes: (p.now | number) } }}</p>
          }
          @if (ahead(s)) {
            <div class="label">{{ 'games.deep.live' | translate }}</div>
          } @else if (s.kind === 'sf' && data.stored.sf; as st) {
            <div class="label">{{ 'games.deep.storedDepth' | translate: { depth: st.depth } }}</div>
          } @else if (s.kind === 'lc0' && data.stored.lc0; as st) {
            <div class="label">{{ 'games.deep.storedNodes' | translate: { nodes: (st.nodes | number) } }}</div>
          }
          <ol class="lines">
            @for (l of linesOf(s); track $index) {
              <li><span class="line-eval" [class.white]="l.positive">{{ l.evalText }}</span><span class="line-san">{{ l.san }}</span></li>
            } @empty {
              @if (!(s.kind === 'lc0' && noLc0())) { <li class="muted">{{ 'games.deep.noLines' | translate }}</li> }
            }
          </ol>
          @if (s.kind === 'lc0' && noLc0()) { <p class="muted">{{ 'games.deep.noLc0' | translate }}</p> }
          @if (j?.lastError && j?.status !== 'done') { <p class="error">{{ j?.lastError }}</p> }
        </section>
      }
      <p class="muted small">{{ 'games.deep.keeps' | translate }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <a mat-button routerLink="/analysis/jobs" mat-dialog-close><mat-icon>list</mat-icon> {{ 'analysisJobs.view.toJobs' | translate }}</a>
      <button mat-flat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .side { display: flex; flex-direction: column; gap: 4px; margin-bottom: 14px; min-width: min(520px, 80vw); }
    .side-head { display: flex; justify-content: space-between; gap: 8px; flex-wrap: wrap; font-size: .85rem; }
    .name { font-weight: 600; }
    .side.lc0 .name { color: #ff9800; }
    .side.lc0 { --mdc-linear-progress-active-indicator-color: #ff9800; --mat-progress-bar-active-indicator-color: #ff9800; }
    .state { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 70%, transparent); }
    .label { font-size: .72rem; font-weight: 600; opacity: .7; margin-top: 4px; }
    .lines { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; font-size: .82rem; }
    .lines li { display: flex; gap: 8px; align-items: baseline; min-width: 0; }
    .line-eval { flex: 0 0 auto; min-width: 3.4em; text-align: center; padding: 0 4px; border-radius: 3px; font-weight: 600;
      font-variant-numeric: tabular-nums; background: #403e3b; color: #fff; }
    .line-eval.white { background: #fff; color: #262421; box-shadow: inset 0 0 0 1px rgba(0,0,0,.2); }
    .line-san { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .78rem; }
    .hint { font-size: .78rem; margin: 2px 0; }
    .error { color: var(--rh-error); font-size: .8rem; }
  `],
})
export class DeepAnalysisDialogComponent implements OnInit {
  private readonly api = inject(AnalysisJobsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly sides = [new DeepSide('sf', 40), new DeepSide('lc0', 500_000)];
  readonly startFailed = signal(false);
  /** Kein Lc0 angemeldet — der Server legt dann nur Stockfish an. */
  readonly noLc0 = signal(false);
  private tick = 0;
  private loading = false;
  private liveLoading = false;

  constructor(@Inject(MAT_DIALOG_DATA) public data: DeepAnalysisDialogData) {}

  progress(s: DeepSide) { return deepProgress(s.kind, s.job(), s.live(), s.target); }
  ahead(s: DeepSide): boolean { return deepAhead(s.kind, s.job(), this.data.stored); }
  pruned(s: DeepSide): boolean { return prunedEarly(s.job(), s.target); }
  storedOf(s: DeepSide) { return s.kind === 'sf' ? this.data.stored.sf : this.data.stored.lc0; }

  /** Die neuen Linien, sobald sie weiter sind als die hinterlegten — sonst die hinterlegten. */
  linesOf(s: DeepSide): DeepLine[] {
    const j = s.job();
    if (j && this.ahead(s)) {
      try {
        const raw = JSON.parse(j.resultJson!) as EngineAnalyseLine;
        return toDisplayLines(j.fen, mapBrokerLine(j.fen, raw, j.multiPv), 10);
      } catch { /* kaputte Zeile: hinterlegte zeigen */ }
    }
    return this.storedOf(s)?.lines ?? [];
  }

  stateKey(s: DeepSide): string {
    const j = s.job();
    if (!j) return s.kind === 'lc0' && this.noLc0() ? 'games.deep.none' : 'games.deep.starting';
    return 'analysisJobs.status.' + j.status;
  }

  ngOnInit(): void {
    this.api.startDeep(this.data.fen).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => {
        this.sides[0].target = r.stockfishDepth;
        this.sides[1].target = r.lc0Nodes;
        this.sides[0].job.set(r.stockfish);
        this.sides[1].job.set(r.lc0);
        this.noLc0.set(!r.lc0);
      },
      error: () => this.startFailed.set(true),
    });
    interval(1_000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.onTick());
  }

  private onTick(): void {
    const open = this.sides.filter(s => s.open);
    if (open.length === 0) return;
    this.tick++;
    this.loadLive();
    if (this.tick % JOB_REFRESH_TICKS === 0) this.loadJobs(open);
  }

  private loadJobs(open: DeepSide[]): void {
    if (this.loading) return;
    this.loading = true;
    let pending = open.length;
    for (const s of open) {
      this.api.get(s.job()!.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: j => { s.job.set(j); if (j.status !== 'running') s.live.set(null); if (--pending === 0) this.loading = false; },
        error: () => { if (--pending === 0) this.loading = false; },
      });
    }
  }

  private loadLive(): void {
    if (this.liveLoading) return;
    this.liveLoading = true;
    this.api.live().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: rows => {
        this.liveLoading = false;
        for (const s of this.sides) s.live.set(rows.find(r => r.id === s.job()?.id) ?? null);
      },
      error: () => { this.liveLoading = false; },
    });
  }
}
