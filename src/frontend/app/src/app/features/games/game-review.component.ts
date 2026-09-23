import {
  ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { EvalGraphComponent, EvalGraphMark } from '../../shared/pgn-viewer/eval-graph.component';
import { GamesService } from './games.service';
import {
  EvalScore, GameEvals, GameEvalsStatus, MOVE_CLASSES, MoveClass, formatEval, reviewGame,
} from './game-review.util';

/** Zeichen je Klasse — dieselben Symbole wie in der Schachnotation, wo es sie gibt. */
const SYMBOLS: Record<MoveClass, string> = {
  best: '★', excellent: '!', good: '✓', inaccuracy: '?!', mistake: '?', blunder: '??',
};

/**
 * Rückblick unter dem Brett: Bewertungskurve, Genauigkeit je Seite, Zug-Klassen und die Klasse des
 * AKTUELLEN Zugs — alles aus RookHubs eigener Partie-Analyse (`GET …/evals`), gerechnet in
 * `game-review.util.ts`.
 *
 * Solange die Analyse läuft, fragt die Komponente alle zehn Sekunden nach und zeigt die Kurve, soweit
 * sie steht; bei `done`/`failed`/`none` ruht sie. Ohne Analyse (`none`) zeigt sie NICHTS — die Seiten
 * zeigen dann ihren Knopf „Partie analysieren", und über `statusChange` wissen sie, wann er zu sperren
 * oder auszublenden ist. Fehler beim Nachfragen sind still (Hintergrund-Feed, der nächste Abruf heilt
 * sich selbst).
 */
@Component({
  selector: 'app-game-review',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EvalGraphComponent, TranslatePipe, MatTooltipModule, DecimalPipe],
  template: `
    @if (status() !== 'none') {
      <section class="review">
        <div class="head">
          <span class="title">{{ 'games.review.title' | translate }}</span>
          @if (running()) {
            <span class="progress">{{ 'games.review.pending' | translate: progress() }}</span>
          } @else if (status() === 'failed') {
            <span class="progress failed">{{ 'games.review.failed' | translate }}</span>
          }
        </div>
        <app-eval-graph [series]="review().series" [marks]="marks()" [currentIndex]="currentIndex()"
                        (moveClicked)="moveClicked.emit($event)" />
        @if (current(); as m) {
          <div [class]="'current ' + m.cls">
            <span class="badge">{{ symbol(m.cls) }} {{ ('games.review.class.' + m.cls) | translate }}</span>
            <span class="evals">{{ fmt(m.evalBefore) }} → {{ fmt(m.evalAfter) }}</span>
          </div>
        }
        <div class="table-wrap">
          <table class="summary">
            <thead>
              <tr>
                <th></th>
                <th class="acc-h">{{ 'games.review.accuracy' | translate }}</th>
                @for (c of classes; track c) {
                  <th><span [class]="'sym ' + c" [matTooltip]="('games.review.class.' + c) | translate"
                            [attr.aria-label]="('games.review.class.' + c) | translate">{{ symbol(c) }}</span></th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.key) {
                <tr [class]="'row-' + row.key">
                  <th>{{ ('games.review.' + row.key) | translate }}</th>
                  <td class="acc">
                    @if (row.summary.accuracy !== null) { {{ row.summary.accuracy | number:'1.1-1' }} % } @else { – }
                  </td>
                  @for (c of classes; track c) {
                    <td [class]="'count ' + c" [class.zero]="row.summary.counts[c] === 0">{{ row.summary.counts[c] }}</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .review { display: flex; flex-direction: column; gap: 6px; width: 100%; }
    .head { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; flex-wrap: wrap; }
    .title { font-weight: 600; font-size: 0.9rem; }
    .progress { font-size: 0.8rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .progress.failed { color: #e53935; }
    .current { display: flex; align-items: center; gap: 8px; font-size: 0.85rem; }
    .current .badge { padding: 1px 8px; border-radius: 10px; color: #fff; font-weight: 600; white-space: nowrap; }
    .current .evals { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 75%, transparent); }
    /* Eigenes overflow-x: auf einem schmalen Handy darf die Tabelle nicht die ganze Seite verbreitern. */
    .table-wrap { overflow-x: auto; }
    .summary { border-collapse: collapse; font-size: 0.8rem; min-width: 280px; width: 100%; }
    .summary th, .summary td { padding: 2px 4px; text-align: center; white-space: nowrap; }
    .summary tbody th { text-align: left; font-weight: 500; }
    .summary .acc-h, .summary .acc { text-align: right; font-variant-numeric: tabular-nums; }
    .summary .acc { font-weight: 600; }
    .summary .count { font-variant-numeric: tabular-nums; }
    .summary .count.zero { color: color-mix(in srgb, currentColor 35%, transparent); }
    .sym {
      display: inline-block; min-width: 20px; padding: 0 3px; border-radius: 9px; box-sizing: border-box;
      color: #fff; font-weight: 700; font-size: 0.75rem; line-height: 18px; cursor: default;
    }
    .sym.best, .current.best .badge { background: #5d9c37; }
    .sym.excellent, .current.excellent .badge { background: #7fa650; }
    .sym.good, .current.good .badge { background: #7b9a78; }
    .sym.inaccuracy, .current.inaccuracy .badge { background: #d9a520; }
    .sym.mistake, .current.mistake .badge { background: #ef8a2c; }
    .sym.blunder, .current.blunder .badge { background: #e53935; }
  `],
})
export class GameReviewComponent {
  private games = inject(GamesService);

  /** `GET …/evals` der Partie; `null` = keine Kurve (die Komponente bleibt unsichtbar). */
  evalsUrl = input<string | null>(null);
  /** Stellungen wie im PGN-Viewer: `fens[0]` = Start, `fens[i+1]` = nach Zug i. */
  fens = input<string[]>([]);
  /** Aktueller Zug (`currentMoveIndex`, −1 = Startstellung). */
  currentIndex = input<number>(-1);

  /** Klick in die Kurve — Halbzug-Index wie `currentMoveIndex`. */
  moveClicked = output<number>();
  /** Damit die Seite ihren Knopf sperren (läuft) oder ausblenden (fertig) kann. */
  statusChange = output<GameEvalsStatus>();

  static readonly PollMs = 10_000;

  readonly classes = MOVE_CLASSES;
  readonly evals = signal<GameEvals | null>(null);
  readonly status = computed<GameEvalsStatus>(() => this.evals()?.status ?? 'none');
  readonly running = computed(() => this.status() === 'pending' || this.status() === 'running');
  readonly progress = computed(() => ({ done: this.evals()?.analyzed ?? 0, total: this.evals()?.total ?? 0 }));
  readonly review = computed(() => reviewGame(this.evals(), this.fens()));
  readonly rows = computed(() => [
    { key: 'white', summary: this.review().white },
    { key: 'black', summary: this.review().black },
  ]);
  readonly marks = computed<EvalGraphMark[]>(() => this.review().moves
    .filter(m => m?.cls === 'mistake' || m?.cls === 'blunder')
    .map(m => ({ ply: m!.ply, kind: m!.cls as 'mistake' | 'blunder' })));
  readonly current = computed(() => {
    const i = this.currentIndex();
    return i >= 0 ? this.review().moves[i] ?? null : null;
  });

  private loadSub?: Subscription;
  private pollSub?: Subscription;
  private lastStatus: GameEvalsStatus | null = null;

  constructor() {
    // Neue Adresse = neue Partie: sofort laden, ein laufendes Nachfragen der alten verwerfen.
    effect(() => {
      this.evalsUrl();
      untracked(() => this.reload());
    });
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  /** Sofort neu laden — nach „Partie analysieren", damit die Kurve nicht bis zum nächsten Takt wartet. */
  reload(): void {
    this.stop();
    const url = this.evalsUrl();
    if (!url) {
      this.apply(null);
      return;
    }
    this.loadSub = this.games.evals(url).subscribe({
      next: evals => {
        this.apply(evals);
        if (this.running()) this.schedule();
      },
      // Still: ein Aussetzer beim Nachfragen heilt der nächste Takt. Lief die Analyse, bleibt der Takt.
      error: () => { if (this.running()) this.schedule(); },
    });
  }

  symbol(c: MoveClass): string { return SYMBOLS[c]; }
  fmt(score: EvalScore): string { return formatEval(score); }

  private apply(evals: GameEvals | null): void {
    this.evals.set(evals);
    const status = evals?.status ?? 'none';
    if (status !== this.lastStatus) {
      this.lastStatus = status;
      this.statusChange.emit(status);
    }
  }

  private schedule(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = timer(GameReviewComponent.PollMs).subscribe(() => this.reload());
  }

  private stop(): void {
    this.loadSub?.unsubscribe();
    this.pollSub?.unsubscribe();
    this.loadSub = this.pollSub = undefined;
  }
}
