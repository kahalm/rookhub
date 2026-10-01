import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { interval } from 'rxjs';
import { BoardArrow, ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import {
  MoveComparison, MoveComparisonCandidate, MoveComparisonReply, MoveComparisonService, MoveComparisonSummary,
  MoveComparisonTest, numberedLine,
} from './move-comparison.service';

/** Was das Brett gerade zeigt: eine Stellung des Vergleichs samt Pfeil für den nächsten Zug. */
interface BoardView { key: string; fen: string; arrows: BoardArrow[]; lastMove?: [string, string]; caption: string; }

const POLL_MS = 3_000;

/**
 * „Züge vergleichen" (0.602.0) — das Ergebnis eines Vergleichs: die Kandidaten nach Stärke, je schwächerem Kandidaten
 * die Begründung und die Tabelle „beste Antworten des Gegners darauf — und was sie gegen den besten Zug taugen". Ein
 * Klick auf einen Zug, eine Antwort oder eine eigene Erwiderung stellt die Stellung aufs Brett. Solange gerechnet
 * wird, fragt die Seite alle drei Sekunden nach; fertig ruht sie.
 */
@Component({
  selector: 'app-move-comparison',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, MatTooltipModule,
    TranslatePipe, ChessBoardComponent],
  template: `
    <div class="cmp-page">
      <div class="header">
        <h1>{{ 'moveCompare.page.title' | translate }}</h1>
        <span class="head-actions">
          @if (data(); as d) {
            <a mat-stroked-button routerLink="/analysis" [queryParams]="{ fen: boardFen() }">
              <mat-icon>science</mat-icon> {{ 'moveCompare.page.openInBoard' | translate }}
            </a>
          }
          <a mat-stroked-button routerLink="/analysis"><mat-icon>arrow_back</mat-icon> {{ 'moveCompare.page.back' | translate }}</a>
        </span>
      </div>

      @if (notFound()) {
        <mat-card><mat-card-content><p>{{ 'moveCompare.page.notFound' | translate }}</p></mat-card-content></mat-card>
      } @else if (data(); as d) {
        <div class="status" [class.failed]="d.status === 'failed'">
          <span>{{ ('moveCompare.page.status.' + d.status) | translate:{ error: d.error ?? '' } }}</span>
          @if (running()) {
            <span class="muted">· {{ 'moveCompare.page.progress' | translate:{ pending: d.pending, total: d.total } }}</span>
          }
        </div>
        @if (running()) { <mat-progress-bar mode="indeterminate" /> }

        <div class="body">
          <div class="board-col">
            <app-chess-board [fen]="boardFen()" [arrows]="boardArrows()" [lastMove]="view()?.lastMove"
                             [flipped]="!d.whiteToMove" [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
            <div class="caption">
              <span>{{ view()?.caption ?? ('moveCompare.page.startPosition' | translate) }}</span>
              @if (view()) {
                <button mat-button type="button" (click)="reset()"><mat-icon>undo</mat-icon> {{ 'moveCompare.page.showStart' | translate }}</button>
              }
            </div>
            <p class="muted small">{{ 'moveCompare.page.clickHint' | translate }}</p>
          </div>

          <div class="detail-col">
            <mat-card class="cands">
              <mat-card-content>
                @for (c of d.candidates; track c.uci; let i = $index) {
                  <button type="button" class="cand-row" [class.active]="view()?.key === 'c:' + c.uci" (click)="showCandidate(c)">
                    <span class="rank">{{ c.state === 'done' && d.bestUci ? i + 1 : '·' }}</span>
                    <span class="san">{{ moveLabel(c.san) }}</span>
                    @if (c.isBest) { <span class="best">{{ 'moveCompare.page.best' | translate }}</span> }
                    <span class="spacer"></span>
                    @if (c.state === 'pending') {
                      <span class="muted small">{{ 'moveCompare.page.calculating' | translate:{ depth: c.depth } }}</span>
                    } @else if (c.state === 'failed') {
                      <span class="muted small">{{ 'moveCompare.page.failed' | translate }}</span>
                    }
                    @if (c.evalText) { <span class="ev" [class.neg]="isNeg(c.evalText)">{{ c.evalText }}</span> }
                  </button>
                  @if (c.replies.length > 0) {
                    <div class="line muted">{{ lineLabel(afterFen(c.uci), c.replies[0].line) }}</div>
                  }
                }
              </mat-card-content>
            </mat-card>

            @if (best(); as b) {
              @for (w of weaker(); track w.uci) {
                <mat-card class="pair">
                  <mat-card-content>
                    <h3>{{ 'moveCompare.page.whyBetter' | translate:{ best: moveLabel(b.san), weak: moveLabel(w.san) } }}</h3>
                    <div class="evals">
                      <span>{{ moveLabel(b.san) }} <span class="ev" [class.neg]="isNeg(b.evalText)">{{ b.evalText }}</span></span>
                      <span>{{ moveLabel(w.san) }} <span class="ev" [class.neg]="isNeg(w.evalText)">{{ w.evalText }}</span></span>
                    </div>
                    @if (w.explanation) {
                      <p class="expl">{{ w.explanation }}</p>
                    } @else if (d.status === 'explaining' || (d.explanationsAvailable && d.status !== 'done' && d.status !== 'failed')) {
                      <p class="muted small">{{ 'moveCompare.page.explaining' | translate }}</p>
                    } @else if (d.explanationsAvailable && d.status === 'done') {
                      <p class="muted small">{{ 'moveCompare.page.noExplanation' | translate }}</p>
                    }

                    <table class="tests">
                      <thead>
                        <tr>
                          <th>{{ 'moveCompare.page.repliesTo' | translate:{ move: moveLabel(w.san) } }}</th>
                          <th>{{ 'moveCompare.page.against' | translate:{ move: moveLabel(b.san) } }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (r of w.replies; track r.uci) {
                          <tr>
                            <td>
                              <button type="button" class="cell" [class.active]="view()?.key === 'r:' + w.uci + ':' + r.uci" (click)="showReply(w, r)">
                                <span class="san">{{ lineLabel(afterFen(w.uci), [r.san]) }}</span>
                                <span class="ev" [class.neg]="isNeg(r.evalText)">{{ r.evalText }}</span>
                              </button>
                            </td>
                            <td>
                              @if (testFor(w, r.uci); as t) {
                                @if (t.state === 'illegal') {
                                  <span class="na">{{ 'moveCompare.page.notPossible' | translate }}</span>
                                } @else if (t.state === 'pending') {
                                  <span class="muted small">{{ 'moveCompare.page.calculating' | translate:{ depth: t.depth } }}</span>
                                } @else if (t.state === 'failed') {
                                  <span class="muted small">{{ 'moveCompare.page.failed' | translate }}</span>
                                } @else {
                                  <button type="button" class="cell" [class.active]="view()?.key === 't:' + w.uci + ':' + t.replyUci" (click)="showTest(b, t)">
                                    <span class="ev" [class.neg]="isNeg(t.evalText)">{{ t.evalText }}</span>
                                    @if (t.line.length > 0) {
                                      <span class="muted small">{{ 'moveCompare.page.yourReply' | translate }}</span>
                                      <span class="san">{{ testLine(b, t) }}</span>
                                    }
                                  </button>
                                }
                              } @else {
                                <span class="muted small">{{ 'moveCompare.page.pending' | translate }}</span>
                              }
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </mat-card-content>
                </mat-card>
              }
            }
          </div>
        </div>
      } @else {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (history().length > 1) {
        <mat-card class="history">
          <mat-card-content>
            <h3>{{ 'moveCompare.page.history' | translate }}</h3>
            @for (h of history(); track h.id) {
              <div class="hist-row" [class.current]="h.id === currentId()">
                <a [routerLink]="['/analysis/compare', h.id]">{{ h.moves.join(' · ') }}</a>
                <span class="muted small">{{ h.createdAt | date:'short' }}</span>
                @if (h.bestSan) { <span class="best">{{ h.bestSan }}</span> }
                <span class="spacer"></span>
                <button mat-icon-button type="button" (click)="remove(h)" [matTooltip]="'moveCompare.page.delete' | translate">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            }
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .cmp-page { padding: 1rem; max-width: min(var(--page-max-width, 1240px), 96vw); margin: 0 auto; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; flex-wrap: wrap; }
    .header h1 { margin: 0; }
    .head-actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .status { margin: .5rem 0; display: flex; gap: 6px; flex-wrap: wrap; }
    .status.failed { color: #c62828; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
    .body { display: grid; grid-template-columns: minmax(260px, 380px) 1fr; gap: 16px; align-items: start; margin-top: 10px; }
    .board-col app-chess-board { display: block; width: 100%; }
    .caption { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-top: 6px; font-size: .9rem;
      font-family: 'Roboto Mono', monospace; }
    .detail-col { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
    .cand-row { display: flex; align-items: center; gap: 8px; width: 100%; padding: 6px 8px; border: 0; border-radius: 6px;
      background: transparent; color: inherit; font: inherit; cursor: pointer; text-align: left; }
    .cand-row:hover, .cell:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .cand-row.active, .cell.active { background: color-mix(in srgb, var(--mat-sys-primary, #1976d2) 14%, transparent); }
    .rank { width: 1.4em; text-align: right; font-weight: 600; }
    .san { font-family: 'Roboto Mono', monospace; }
    .line { font-family: 'Roboto Mono', monospace; font-size: .78rem; padding: 0 8px 6px 2.6em; }
    .spacer { flex: 1; }
    .best { font-size: .7rem; font-weight: 700; padding: 1px 8px; border-radius: 999px; background: rgba(46,125,50,.18); color: #2e7d32; }
    .ev { font-family: 'Roboto Mono', monospace; font-weight: 600; color: #2e7d32; }
    .ev.neg { color: #c62828; }
    .pair h3, .history h3 { margin: 0 0 6px; font-size: 1rem; }
    .evals { display: flex; gap: 18px; flex-wrap: wrap; font-size: .9rem; margin-bottom: 6px; }
    .expl { line-height: 1.5; margin: 6px 0 10px; }
    .tests { width: 100%; border-collapse: collapse; font-size: .88rem; }
    .tests th { text-align: left; font-weight: 600; font-size: .78rem; padding: 4px 6px;
      border-bottom: 1px solid color-mix(in srgb, currentColor 15%, transparent); }
    .tests td { padding: 2px 0; vertical-align: top; }
    .cell { display: flex; flex-wrap: wrap; gap: 6px; align-items: baseline; width: 100%; padding: 4px 6px; border: 0; border-radius: 6px;
      background: transparent; color: inherit; font: inherit; cursor: pointer; text-align: left; }
    .na { display: inline-block; padding: 4px 6px; font-weight: 600; color: #2e7d32; }
    .history { margin-top: 16px; }
    .hist-row { display: flex; align-items: center; gap: 10px; font-size: .88rem; }
    .hist-row.current a { font-weight: 600; }
    @media (max-width: 760px) { .body { grid-template-columns: 1fr; } .board-col { max-width: 420px; } }
  `],
})
export class MoveComparisonComponent implements OnInit {
  readonly preferences = inject(PreferencesService);
  private readonly api = inject(MoveComparisonService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  private readonly snackbar = inject(SnackbarService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);

  readonly data = signal<MoveComparison | null>(null);
  readonly notFound = signal(false);
  readonly view = signal<BoardView | null>(null);
  readonly history = signal<MoveComparisonSummary[]>([]);
  readonly currentId = signal(0);
  private loading = false;

  readonly running = computed(() => { const s = this.data()?.status; return !!s && s !== 'done' && s !== 'failed'; });
  readonly best = computed(() => this.data()?.candidates.find(c => c.isBest) ?? null);
  readonly weaker = computed(() => (this.data()?.candidates ?? []).filter(c => !c.isBest && c.state === 'done' && !!this.data()?.bestUci));
  readonly boardFen = computed(() => this.view()?.fen ?? this.data()?.fen ?? new Chess().fen());

  /** Ohne Auswahl: je Kandidat ein Pfeil — der beste grün, die übrigen blass. */
  readonly boardArrows = computed<BoardArrow[]>(() => {
    const v = this.view();
    if (v) return v.arrows;
    const d = this.data();
    if (!d) return [];
    return d.candidates.map(c => ({ from: c.uci.slice(0, 2), to: c.uci.slice(2, 4), brush: c.isBest ? 'green' : 'paleBlue' }));
  });

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(p => {
      const id = Number(p.get('id'));
      this.currentId.set(id);
      this.data.set(null);
      this.view.set(null);
      this.notFound.set(false);
      this.load();
      this.loadHistory();
    });
    interval(POLL_MS).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.running()) this.load();
    });
  }

  private load(): void {
    const id = this.currentId();
    if (!id || this.loading) return;
    this.loading = true;
    this.api.get(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: d => {
        this.loading = false;
        const wasRunning = this.running();
        this.data.set(d);
        if (wasRunning && !this.running()) this.loadHistory();
      },
      error: (e: HttpErrorResponse) => { this.loading = false; if (e.status === 404) this.notFound.set(true); },
    });
  }

  private loadHistory(): void {
    this.api.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: h => this.history.set(h), error: () => {} });
  }

  // ── Beschriftung ─────────────────────────────────────────────────────────────────────────────

  moveLabel(san: string): string {
    const d = this.data();
    return d ? numberedLine(d.fen, [san]) : san;
  }

  lineLabel(fen: string | null, sans: readonly string[]): string {
    return fen ? numberedLine(fen, sans, 6) : sans.slice(0, 6).join(' ');
  }

  /** Die eigene Erwiderung auf die Antwort nach dem besten Zug, nummeriert ab der Stellung danach. */
  testLine(best: MoveComparisonCandidate, t: MoveComparisonTest): string {
    const after = play(this.afterFen(best.uci), [t.replySan]);
    return this.lineLabel(after?.fen ?? null, t.line.slice(0, 4));
  }

  isNeg(ev: string | null | undefined): boolean {
    return !!ev && ev.startsWith('-') || !!ev && ev.startsWith('#-');
  }

  testFor(w: MoveComparisonCandidate, replyUci: string): MoveComparisonTest | null {
    return w.tests.find(t => t.replyUci === replyUci) ?? null;
  }

  afterFen(uci: string): string | null {
    const d = this.data();
    return d ? play(d.fen, [uci])?.fen ?? null : null;
  }

  // ── Brett ────────────────────────────────────────────────────────────────────────────────────

  reset(): void { this.view.set(null); }

  /** Stellung nach dem Kandidaten, Pfeil für die beste Antwort des Gegners. */
  showCandidate(c: MoveComparisonCandidate): void {
    const d = this.data();
    const after = d && play(d.fen, [c.uci]);
    if (!d || !after) return;
    const reply = c.replies[0];
    const arrow = reply ? arrowOf(after.fen, reply.san, 'red') : null;
    this.view.set({ key: 'c:' + c.uci, fen: after.fen, lastMove: after.last, arrows: arrow ? [arrow] : [],
      caption: this.moveLabel(c.san) });
  }

  /** Stellung nach dem schwächeren Kandidaten und der Antwort, Pfeil für den nächsten Zug der Linie. */
  showReply(w: MoveComparisonCandidate, r: MoveComparisonReply): void {
    const d = this.data();
    const after = d && play(d.fen, [w.uci, r.san]);
    if (!d || !after) return;
    const next = r.line[1];
    const arrow = next ? arrowOf(after.fen, next, 'green') : null;
    this.view.set({ key: 'r:' + w.uci + ':' + r.uci, fen: after.fen, lastMove: after.last, arrows: arrow ? [arrow] : [],
      caption: this.moveLabel(w.san) + ' ' + this.lineLabel(this.afterFen(w.uci), [r.san]) });
  }

  /** Stellung nach dem besten Zug und derselben Antwort, Pfeil für die eigene Erwiderung. */
  showTest(b: MoveComparisonCandidate, t: MoveComparisonTest): void {
    const d = this.data();
    const after = d && play(d.fen, [b.uci, t.replySan]);
    if (!d || !after) return;
    const next = t.line[0];
    const arrow = next ? arrowOf(after.fen, next, 'green') : null;
    this.view.set({ key: 't:' + (d.candidates.find(c => c.tests.includes(t))?.uci ?? '') + ':' + t.replyUci, fen: after.fen,
      lastMove: after.last, arrows: arrow ? [arrow] : [],
      caption: this.moveLabel(b.san) + ' ' + this.lineLabel(this.afterFen(b.uci), [t.replySan]) });
  }

  remove(h: MoveComparisonSummary): void {
    this.confirm.ask('moveCompare.page.deleteConfirm').subscribe(ok => {
      if (!ok) return;
      this.api.delete(h.id).subscribe({
        next: () => {
          this.history.set(this.history().filter(x => x.id !== h.id));
          if (h.id === this.currentId()) this.router.navigate(['/analysis']);
        },
        error: () => this.snackbar.warn(this.translate.instant('moveCompare.page.deleteFailed')),
      });
    });
  }
}

/** Züge (UCI oder SAN) ab einer Stellung spielen; `null`, wenn einer nicht geht. */
export function play(fen: string | null, moves: readonly string[]): { fen: string; last?: [string, string] } | null {
  if (!fen) return null;
  let chess: Chess;
  try { chess = new Chess(fen); } catch { return null; }
  let last: [string, string] | undefined;
  for (const m of moves) {
    try {
      const mv = /^[a-h][1-8][a-h][1-8][qrbn]?$/.test(m)
        ? chess.move({ from: m.slice(0, 2), to: m.slice(2, 4), promotion: m[4] })
        : chess.move(m);
      last = [mv.from, mv.to];
    } catch { return null; }
  }
  return { fen: chess.fen(), last };
}

/** Pfeil für einen SAN-Zug in der Stellung (`null`, wenn er dort nicht geht). */
export function arrowOf(fen: string, san: string, brush: string): BoardArrow | null {
  try {
    const mv = new Chess(fen).move(san);
    return { from: mv.from, to: mv.to, brush };
  } catch { return null; }
}
