import {
  ChangeDetectionStrategy, Component, DestroyRef, LOCALE_ID, computed, effect, inject, input, output, signal,
  untracked,
} from '@angular/core';
import { DecimalPipe, formatNumber } from '@angular/common';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { EvalGraphComponent, EvalGraphMark } from '../../shared/pgn-viewer/eval-graph.component';
import { formatEta } from '../../shared/eta.util';
import { BoardArrow } from '../../shared/pgn-viewer/chess-board.component';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { bestMoveArrowAt, computerLinesAt } from './computer-lines.util';
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
  brilliant: '!!', great: '!', best: '★', excellent: '👍', good: '✓', book: '📖', inaccuracy: '?!', mistake: '?', miss: '✗',
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
  imports: [EvalGraphComponent, TranslatePipe, MatTooltipModule, MatIconModule, MatButtonModule, DecimalPipe],
  template: `
    @if (status() !== 'none') {
      <section class="review">
        <div class="head">
          <!-- Die Kurve ist standardmäßig zu (gewünscht 2026-09-24) — ein Klick auf die Überschrift klappt sie auf. -->
          <button type="button" class="title" (click)="toggleGraph()" [attr.aria-expanded]="graphOpen()">
            {{ 'games.review.title' | translate }}
            <mat-icon class="chevron">{{ graphOpen() ? 'expand_less' : 'expand_more' }}</mat-icon>
          </button>
          @if (running()) {
            <!-- „8 von 47" allein sagt nicht, wie lange noch — die Restdauer rechnet der Server. -->
            <span class="progress">
              {{ 'games.review.pending' | translate: progress() }}
              @if (eta(); as e) { · {{ 'gameAnalysis.eta' | translate: { eta: e } }} }
            </span>
          } @else if (refining()) {
            <!-- Zweiter Durchgang: die Analyse ist fertig und nutzbar, wird aber Stellung für Stellung genauer. -->
            <span class="progress">{{ 'games.review.refining' | translate: { done: evals()?.refined ?? 0, total: evals()?.total ?? 0 } }}</span>
          } @else if (status() === 'failed') {
            <span class="progress failed">{{ 'games.review.failed' | translate }}</span>
          }
          @if (!engineHidden()) {
            <!-- Computer-Linien + Pfeil für den besten Zug: je Gerät gemerkt, im Fehler-Training aus (verriete die Lösung). -->
            <span class="toggles">
              <button mat-icon-button type="button" class="toggle lines-toggle" [class.on]="showLines()"
                      [attr.aria-pressed]="showLines()" (click)="toggleLines()"
                      [matTooltip]="'games.review.lines' | translate" [attr.aria-label]="'games.review.lines' | translate">
                <mat-icon>format_list_numbered</mat-icon>
              </button>
              <button mat-icon-button type="button" class="toggle arrow-toggle" [class.on]="showArrow()"
                      [attr.aria-pressed]="showArrow()" (click)="toggleArrow()"
                      [matTooltip]="'games.review.arrow' | translate" [attr.aria-label]="'games.review.arrow' | translate">
                <mat-icon>north_east</mat-icon>
              </button>
            </span>
          }
        </div>
        @if (lines().length) {
          <ol class="lines">
            @for (l of lines(); track $index) {
              <li [class.played]="l.played">
                <span class="line-eval" [class.white]="l.whiteBetter">{{ l.evalText }}</span>
                <span class="line-san">{{ l.san }}</span>
              </li>
            }
          </ol>
        }
        @if (graphOpen()) {
          <app-eval-graph [series]="review().curve" [marks]="marks()" [currentIndex]="currentIndex()"
                          (moveClicked)="moveClicked.emit($event)" />
        }
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
    .head { display: flex; align-items: center; justify-content: space-between; gap: 8px; flex-wrap: wrap; }
    .title {
      display: inline-flex; align-items: center; gap: 2px; padding: 0; border: 0; background: none;
      color: inherit; font: inherit; font-weight: 600; font-size: 0.9rem; cursor: pointer;
    }
    .title .chevron { font-size: 20px; width: 20px; height: 20px; opacity: 0.7; }
    .progress { font-size: 0.8rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .progress.failed { color: #e53935; }
    .toggles { display: inline-flex; margin-left: auto; }
    .toggle { opacity: 0.45; --mat-icon-button-state-layer-size: 30px; width: 30px; height: 30px; padding: 3px; }
    .toggle mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .toggle.on { opacity: 1; color: #81b64c; }
    .lines { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; font-size: 0.82rem; }
    .lines li { display: flex; gap: 8px; align-items: baseline; min-width: 0; padding: 1px 4px; border-radius: 4px; }
    .lines li.played { background: color-mix(in srgb, currentColor 8%, transparent); }
    .line-eval {
      flex: 0 0 auto; min-width: 3.4em; text-align: center; padding: 0 4px; border-radius: 3px;
      font-weight: 600; font-variant-numeric: tabular-nums; background: #403e3b; color: #fff;
    }
    .line-eval.white { background: #fff; color: #262421; box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.2); }
    .line-san { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
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

  /** Im Fehler-Training: keine Computer-Linien und kein Pfeil — sie verrieten die Lösung. */
  engineHidden = input<boolean>(false);

  /** Klick in die Kurve — Halbzug-Index wie `currentMoveIndex`. */
  moveClicked = output<number>();
  /** Damit die Seite ihren Knopf sperren (läuft) oder ausblenden (fertig) kann. */
  statusChange = output<GameEvalsStatus>();
  /**
   * Die abfragbaren Fehler beider Seiten — die Seite bietet daraus „Eigene Fehler nachspielen" an.
   * Sie kommt hierher, weil hier die Analyse liegt; ein zweiter Abruf derselben Daten wäre Verschwendung.
   */
  mistakesChange = output<MistakesBySide>();

  /**
   * Der Pfeil für den besten Zug der Stellung auf dem Brett (leer = keiner) — die Seite legt ihn auf ihr
   * Brett. Von hier, weil hier die Analyse liegt; das Brett gehört der Seite.
   */
  arrowsChange = output<BoardArrow[]>();

  static readonly PollMs = 10_000;
  /** Während der Vertiefung (zweiter Durchgang, 0.523.0) gemächlicher — die Analyse ist schon nutzbar. */
  static readonly RefinePollMs = 60_000;
  static readonly LinesKey = 'rookhub_game_lines';
  static readonly ArrowKey = 'rookhub_game_arrow';

  readonly classes = MOVE_CLASSES;
  readonly evals = signal<GameEvals | null>(null);
  readonly status = computed<GameEvalsStatus>(() => this.evals()?.status ?? 'none');
  readonly running = computed(() => this.status() === 'pending' || this.status() === 'running');
  readonly refining = computed(() => this.status() === 'done' && !!this.evals()?.refining);
  readonly progress = computed(() => ({ done: this.evals()?.analyzed ?? 0, total: this.evals()?.total ?? 0 }));
  /** Restdauer als Text („11 min"), solange die Analyse läuft und der Server ein Tempo kennt. */
  readonly eta = computed(() => {
    const minutes = this.evals()?.etaMinutes;
    return this.running() && minutes ? formatEta(minutes, this.translate) : null;
  });
  readonly ucis = computed(() => this.moves().map(uciOf));
  readonly review = computed(() => reviewGame(this.evals(), this.fens(), this.ucis()));
  /** Die Kurve ist standardmäßig ZU und klappt nur auf Wunsch auf — bewusst nicht gemerkt: „standardmäßig". */
  readonly graphOpen = signal(false);
  /** Schalter je Gerät (localStorage — reine Anzeige-Vorliebe). */
  readonly showLines = signal(readRaw(localStore(), GameReviewComponent.LinesKey) === '1');
  readonly showArrow = signal(readRaw(localStore(), GameReviewComponent.ArrowKey) === '1');
  readonly lines = computed(() => this.showLines() && !this.engineHidden()
    ? computerLinesAt(this.evals(), this.fens(), this.currentIndex()) : []);
  readonly arrows = computed<BoardArrow[]>(() => {
    if (!this.showArrow() || this.engineHidden()) return [];
    const best = bestMoveArrowAt(this.evals(), this.currentIndex());
    return best ? [best] : [];
  });
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
    // Der Pfeil folgt Stellung, Schalter und Analyse — die Seite legt ihn auf ihr Brett.
    effect(() => {
      const a = this.arrows();
      untracked(() => this.arrowsChange.emit(a));
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
        this.scheduleIfBusy();
      },
      // Still: ein Aussetzer beim Nachfragen heilt der nächste Takt. Lief die Analyse, bleibt der Takt.
      error: () => this.scheduleIfBusy(),
    });
  }

  toggleGraph(): void { this.graphOpen.update(v => !v); }

  toggleLines(): void {
    this.showLines.update(v => !v);
    writeRaw(localStore(), GameReviewComponent.LinesKey, this.showLines() ? '1' : '0');
  }

  toggleArrow(): void {
    this.showArrow.update(v => !v);
    writeRaw(localStore(), GameReviewComponent.ArrowKey, this.showArrow() ? '1' : '0');
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
    if (m.cls === 'book') return this.translate.instant('games.review.bookHint');
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

  /** Nachfragen, solange gerechnet wird: im ersten Durchgang alle 10 s, während der Vertiefung einmal je Minute. */
  private scheduleIfBusy(): void {
    if (this.running()) this.schedule(GameReviewComponent.PollMs);
    else if (this.refining()) this.schedule(GameReviewComponent.RefinePollMs);
  }

  private schedule(ms: number): void {
    this.pollSub?.unsubscribe();
    this.pollSub = timer(ms).subscribe(() => this.reload());
  }

  private stop(): void {
    this.loadSub?.unsubscribe();
    this.pollSub?.unsubscribe();
    this.loadSub = this.pollSub = undefined;
  }
}
