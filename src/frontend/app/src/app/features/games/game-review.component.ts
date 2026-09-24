import {
  ChangeDetectionStrategy, Component, DestroyRef, LOCALE_ID, computed, effect, inject, input, output, signal,
  untracked,
} from '@angular/core';
import { DecimalPipe, formatNumber } from '@angular/common';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { EvalGraphComponent, EvalGraphMark } from '../../shared/pgn-viewer/eval-graph.component';
import { formatEta } from '../../shared/eta.util';
import { GamesService } from './games.service';
import {
  EvalScore, GameEvals, GameEvalsStatus, MOVE_CLASSES, MOVE_CLASS_COLORS, MoveClass, ReviewedMove, formatEval,
  reviewGame,
} from './game-review.util';
import { uciOf } from './move-tactics.util';
import { MistakesBySide, PlayedMove, collectMistakes } from './mistakes.util';

/**
 * Zeichen je Klasse — dieselben Symbole wie in der Schachnotation, wo es sie gibt. „!" gehört seit den
 * Sonderklassen dem Great (so auch bei chess.com); Excellent trägt deshalb den Daumen wie dort.
 */
const SYMBOLS: Record<MoveClass, string> = {
  brilliant: '!!', great: '!', best: '★', excellent: '👍', good: '✓', inaccuracy: '?!', mistake: '?', miss: '✗',
  blunder: '??',
};

/** Diese Klassen bekommen einen Punkt in der Kurve — die Züge, bei denen man hinsehen will. */
const MARKED: ReadonlySet<MoveClass> = new Set<MoveClass>(['brilliant', 'great', 'miss', 'mistake', 'blunder']);

/** Ab diesem Abstand ist er keine Zahl in Bauern mehr, sondern ein Matt auf der einen Seite. */
const MATE_GAP_PAWNS = 100;

/**
 * Rückblick unter dem Brett: Bewertungskurve, Genauigkeit je Seite, Zug-Klassen und die Klasse des
 * AKTUELLEN Zugs — alles aus RookHubs eigener Partie-Analyse (`GET …/evals`), gerechnet in
 * `game-review.util.ts`. Brilliant/Great/Miss brauchen zusätzlich die Züge (`moves`), weil das Opfer
 * in der Stellung steckt; die Seiten reichen `game.moves` des PGN-Viewers herein.
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
            <!-- „8 von 47" allein sagt nicht, wie lange noch — die Restdauer rechnet der Server. -->
            <span class="progress">
              {{ 'games.review.pending' | translate: progress() }}
              @if (eta(); as e) { · {{ 'gameAnalysis.eta' | translate: { eta: e } }} }
            </span>
          } @else if (status() === 'failed') {
            <span class="progress failed">{{ 'games.review.failed' | translate }}</span>
          }
        </div>
        <app-eval-graph [series]="review().series" [marks]="marks()" [currentIndex]="currentIndex()"
                        (moveClicked)="moveClicked.emit($event)" />
        @if (current(); as m) {
          @let tip = hint(m);
          <div [class]="'current ' + m.cls">
            <span class="badge" [style.background]="color(m.cls)">{{ symbol(m.cls) }} {{ ('games.review.class.' + m.cls) | translate }}</span>
            <span class="evals">{{ fmt(m.evalBefore) }} → {{ fmt(m.evalAfter) }}</span>
            <!-- Als Text, nicht als Tooltip: am Handy gibt es kein Hover, und WARUM ein Zug brillant war, ist
                 die Auskunft, für die man hinsieht. -->
            @if (tip) { <span class="why">{{ tip }}</span> }
          </div>
        }
        <div class="table-wrap">
          <table class="summary">
            <thead>
              <tr>
                <th></th>
                <th class="acc-h">{{ 'games.review.accuracy' | translate }}</th>
                @for (c of classes; track c) {
                  <th><span [class]="'sym ' + c" [style.background]="color(c)"
                            [matTooltip]="('games.review.class.' + c) | translate"
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
    .current { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; font-size: 0.85rem; }
    .current .why { color: color-mix(in srgb, currentColor 70%, transparent); }
    /* Die chess.com-Farben sind hell (Gelb, Hellgrün) — ein Schatten hält die weiße Schrift darauf lesbar. */
    .current .badge {
      padding: 1px 8px; border-radius: 10px; color: #fff; font-weight: 600; white-space: nowrap;
      text-shadow: 0 1px 1px rgba(0, 0, 0, 0.45);
    }
    .current .evals { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 75%, transparent); }
    /* Eigenes overflow-x: auf einem schmalen Handy darf die Tabelle nicht die ganze Seite verbreitern. */
    .table-wrap { overflow-x: auto; }
    .summary { border-collapse: collapse; font-size: 0.8rem; min-width: 340px; width: 100%; }
    .summary th, .summary td { padding: 2px 4px; text-align: center; white-space: nowrap; }
    .summary tbody th { text-align: left; font-weight: 500; }
    .summary .acc-h, .summary .acc { text-align: right; font-variant-numeric: tabular-nums; }
    .summary .acc { font-weight: 600; }
    .summary .count { font-variant-numeric: tabular-nums; }
    .summary .count.zero { color: color-mix(in srgb, currentColor 35%, transparent); }
    .sym {
      display: inline-block; min-width: 20px; padding: 0 3px; border-radius: 9px; box-sizing: border-box;
      color: #fff; font-weight: 700; font-size: 0.75rem; line-height: 18px; cursor: default;
      text-shadow: 0 1px 1px rgba(0, 0, 0, 0.45);
    }
  `],
})
export class GameReviewComponent {
  private games = inject(GamesService);
  private translate = inject(TranslateService);
  private locale = inject(LOCALE_ID);

  /** `GET …/evals` der Partie; `null` = keine Kurve (die Komponente bleibt unsichtbar). */
  evalsUrl = input<string | null>(null);
  /** Stellungen wie im PGN-Viewer: `fens[0]` = Start, `fens[i+1]` = nach Zug i. */
  fens = input<string[]>([]);
  /** Aktueller Zug (`currentMoveIndex`, −1 = Startstellung). */
  currentIndex = input<number>(-1);
  /**
   * Die Partiezüge (chess.js-`Move` des PGN-Viewers, `game.moves`). Ohne sie gibt es kein Brilliant, Great
   * oder Miss — ob eine Figur geopfert wurde, steht in der Stellung, nicht in den Bewertungen.
   */
  moves = input<readonly PlayedMove[]>([]);

  /** Klick in die Kurve — Halbzug-Index wie `currentMoveIndex`. */
  moveClicked = output<number>();
  /** Damit die Seite ihren Knopf sperren (läuft) oder ausblenden (fertig) kann. */
  statusChange = output<GameEvalsStatus>();
  /**
   * Die abfragbaren Fehler beider Seiten — die Seite bietet daraus „Eigene Fehler nachspielen" an.
   * Sie kommt hierher, weil hier die Analyse liegt; ein zweiter Abruf derselben Daten wäre Verschwendung.
   */
  mistakesChange = output<MistakesBySide>();

  static readonly PollMs = 10_000;

  readonly classes = MOVE_CLASSES;
  readonly evals = signal<GameEvals | null>(null);
  readonly status = computed<GameEvalsStatus>(() => this.evals()?.status ?? 'none');
  readonly running = computed(() => this.status() === 'pending' || this.status() === 'running');
  readonly progress = computed(() => ({ done: this.evals()?.analyzed ?? 0, total: this.evals()?.total ?? 0 }));
  /** Restdauer als Text („11 min"), solange die Analyse läuft und der Server ein Tempo kennt. */
  readonly eta = computed(() => {
    const minutes = this.evals()?.etaMinutes;
    return this.running() && minutes ? formatEta(minutes, this.translate) : null;
  });
  readonly ucis = computed(() => this.moves().map(uciOf));
  readonly review = computed(() => reviewGame(this.evals(), this.fens(), this.ucis()));
  readonly rows = computed(() => [
    { key: 'white', summary: this.review().white },
    { key: 'black', summary: this.review().black },
  ]);
  readonly marks = computed<EvalGraphMark[]>(() => this.review().moves
    .filter((m): m is ReviewedMove => !!m && MARKED.has(m.cls))
    .map(m => ({ ply: m.ply, kind: m.cls, color: MOVE_CLASS_COLORS[m.cls] })));
  readonly mistakes = computed(() => collectMistakes(this.review(), this.evals(), this.fens(), this.moves()));
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
    // Jede neue Analyse-Antwort kann Aufgaben bringen — die Seite erfährt es über die Ausgabe.
    effect(() => {
      const m = this.mistakes();
      untracked(() => this.mistakesChange.emit(m));
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
  color(c: MoveClass): string { return MOVE_CLASS_COLORS[c]; }

  /**
   * Erklärung neben dem Abzeichen — bei den Sonderklassen sagt das Etikett allein nicht, WARUM: welche Figur
   * geopfert wurde, wie weit der Zweitbeste zurücklag, was verpasst wurde. Leer = keine Zeile.
   * `instant` statt Pipe, weil der Figurenname selbst übersetzt in den Satz muss; die Vorlage ruft die
   * Methode bei jedem Sprachwechsel neu auf (die Pipes daneben lösen die Prüfung aus).
   */
  hint(m: ReviewedMove): string {
    if (m.cls === 'brilliant' && m.sacrifice) {
      return this.translate.instant('games.review.sacrifice', {
        piece: this.translate.instant('games.review.piece.' + m.sacrifice.piece), square: m.sacrifice.square,
      });
    }
    if (m.cls === 'great') {
      // Mit einem Matt im Spiel ist der Abstand keine Zahl in Bauern — „992 Bauern" läse sich wie ein Fehler.
      return m.gapPawns != null && m.gapPawns < MATE_GAP_PAWNS && m.evalBefore.mate == null
        ? this.translate.instant('games.review.greatGap', { gap: formatNumber(m.gapPawns, this.locale, '1.1-1') })
        : this.translate.instant('games.review.greatOnly');
    }
    if (m.cls === 'miss') return this.translate.instant('games.review.missHint');
    return '';
  }
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
