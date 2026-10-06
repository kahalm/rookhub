import { BoardBadge, MOVE_CLASS_SYMBOLS, moveBadgeSvg } from './move-badge.util';
import { linkedSignal,
  ChangeDetectionStrategy, Component, DestroyRef, LOCALE_ID, computed, effect, inject, input, output, signal,
  untracked,
} from '@angular/core';
import { DecimalPipe, formatNumber } from '@angular/common';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { EvalGraphComponent, EvalGraphMark } from '../../shared/pgn-viewer/eval-graph.component';
import { formatEta } from '../../shared/eta.util';
import { BoardArrow } from '../../shared/pgn-viewer/chess-board.component';
import { localStore, readRaw, writeRaw } from '../../core/local-json-store';
import { bestMoveArrowAt, computerLinesAt } from './computer-lines.util';
import { GameExplanationMaster, GameExplanations, GamesService } from './games.service';
import { GameAnalysisAlternative, GameAnalysisService } from '../analysis/game-analysis.service';
import { isLc0Engine } from '../analysis/external-engine.service';
import {
  EvalScore, GameEvals, GameEvalsStatus, MOVE_CLASSES, MOVE_CLASS_COLORS, MoveClass, ReviewedMove, formatEval,
  reviewGame,
} from './game-review.util';
import { uciOf } from './move-tactics.util';
import { MistakesBySide, PlayedMove, collectMistakes } from './mistakes.util';
import { engineDisagreements, moveNumberLabel, stepDisagreement } from './engine-disagreement.util';

/** Zeichen je Klasse — die Tabelle steht in `move-badge.util.ts`, das Brett-Symbol benutzt dieselbe. */
const SYMBOLS = MOVE_CLASS_SYMBOLS;

/** Umschalter der Partieseite: die eigene Kurve, die zweite Analyse oder beide übereinander (0.682.0). */
export type EngineView = 'primary' | 'alt' | 'both';

function readView(): EngineView {
  const v = readRaw(localStore(), 'rookhub_game_engine_view');
  return v === 'alt' || v === 'both' ? v : 'primary';
}

/** Diese Klassen bekommen einen Punkt in der Kurve — die Züge, bei denen man hinsehen will. */
const MARKED: ReadonlySet<MoveClass> = new Set<MoveClass>(['brilliant', 'great', 'miss', 'mistake', 'blunder']);

/** Diese Grundklassen sind Fehler — nur zu ihnen gibt es Erklärungen (Spiegel von `GameMistakes` am Server). */
const ERROR_CLASSES: ReadonlySet<MoveClass> = new Set<MoveClass>(['inaccuracy', 'mistake', 'blunder']);

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
  imports: [EvalGraphComponent, TranslatePipe, MatTooltipModule, MatIconModule, MatButtonModule, MatButtonToggleModule, DecimalPipe],
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
          @if (alternative() && altEvals() && !engineHidden()) {
            <!-- Umschalter (0.682.0): dieselbe Partie hat eine zweite eigene Analyse, z. B. Lc0 neben Stockfish. „Beide"
                 legt ihre Kurve über die erste und zeigt beide Linienblöcke. Je Gerät gemerkt. -->
            <mat-button-toggle-group class="engine-view" [value]="view()" (change)="setView($event.value)"
                                     hideSingleSelectionIndicator [attr.aria-label]="'games.review.engineView' | translate">
              <mat-button-toggle value="primary">Stockfish</mat-button-toggle>
              <mat-button-toggle value="alt">{{ altLabel() }}</mat-button-toggle>
              <mat-button-toggle value="both">{{ 'games.review.engineBoth' | translate }}</mat-button-toggle>
            </mat-button-toggle-group>
          }
          @if (!engineHidden()) {
            <!-- Computer-Linien + Pfeil für den besten Zug: je Gerät gemerkt, im Fehler-Training aus (verriete die Lösung). -->
            <span class="toggles">
              <button mat-icon-button type="button" class="toggle lines-toggle" [class.on]="showLines()"
                      [attr.aria-pressed]="showLines()" (click)="toggleLines()"
                      [matTooltip]="'games.review.lines' | translate" [attr.aria-label]="'games.review.lines' | translate">
                <mat-icon>format_list_numbered</mat-icon>
              </button>
              @if (!liveEngine()) {
                <button mat-icon-button type="button" class="toggle arrow-toggle" [class.on]="showArrow()"
                        [attr.aria-pressed]="showArrow()" (click)="toggleArrow()"
                        [matTooltip]="'games.review.arrow' | translate" [attr.aria-label]="'games.review.arrow' | translate">
                  <mat-icon>north_east</mat-icon>
                </button>
              }
            </span>
          }
        </div>
        @if (lines().length) {
          @if (view() === 'both') { <div class="lines-label">Stockfish</div> }
          <ol class="lines">
            @for (l of lines(); track $index) {
              <li [class.played]="l.played">
                <span class="line-eval" [class.white]="l.whiteBetter">{{ l.evalText }}</span>
                <span class="line-san">{{ l.san }}</span>
              </li>
            }
          </ol>
        }
        @if (altLines().length) {
          <div class="lines-label alt">{{ altLabel() }}</div>
          <ol class="lines alt">
            @for (l of altLines(); track $index) {
              <li [class.played]="l.played">
                <span class="line-eval" [class.white]="l.whiteBetter">{{ l.evalText }}</span>
                <span class="line-san">{{ l.san }}</span>
              </li>
            }
          </ol>
        }
        @if (graphOpen()) {
          <app-eval-graph [series]="review().curve" [overlay]="overlay()" [marks]="marks()" [currentIndex]="currentIndex()"
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
          <!-- „Warum war das ein Fehler?" (0.534.0): geschrieben vom Sprachmodell auf eigener Hardware, nur aus den
               Linien der Analyse. Im Fehler-Training aus — der Text nennt den besseren Zug. -->
          @if (explanationFor(); as ex) {
            <p class="explain">💬 {{ ex.text }}</p>
            <!-- Meisterkommentar zur selben Stellung (0.542.0): die Quelle als Zeile, der Wortlaut zum Aufklappen —
                 im Original, er stammt aus der Sammlung und wird nicht übersetzt. -->
            @if (ex.master; as m) {
              <details class="explain-master">
                <summary>📖 {{ 'games.review.master' | translate: { game: masterGame(m) } }}
                  @if (m.annotator) { · {{ 'games.review.masterBy' | translate: { name: m.annotator } }} }</summary>
                <q>{{ m.text }}</q>
              </details>
            }
          }
        }
        @if (explainState() === 'can') {
          <button mat-stroked-button type="button" class="explain-btn" (click)="explain()">
            <mat-icon>psychology</mat-icon> {{ 'games.review.explain' | translate }}
          </button>
        } @else if (explainState() === 'running') {
          <span class="progress explaining">{{ 'games.review.explaining' | translate }}</span>
        }
        @if (disagreements().length) {
          <!-- Uneinige Züge (0.683.0): nur bei „Beide", nur wenn es welche gibt. Klick springt hin. -->
          <div class="disagree">
            <div class="dis-head">
              <span class="dis-title">{{ 'games.review.disagree' | translate: { n: disagreements().length } }}</span>
              <span class="dis-nav">
                <button mat-icon-button type="button" (click)="stepDisagree(-1)"
                        [attr.aria-label]="'games.review.disagreePrev' | translate"><mat-icon>chevron_left</mat-icon></button>
                <span class="dis-pos">{{ disagreePosition() }}</span>
                <button mat-icon-button type="button" (click)="stepDisagree(1)"
                        [attr.aria-label]="'games.review.disagreeNext' | translate"><mat-icon>chevron_right</mat-icon></button>
              </span>
            </div>
            @if (currentDisagreement(); as d) {
              <div class="dis-current">
                <span>Stockfish</span>
                <span class="sym" [style.background]="color(d.primary)">{{ symbol(d.primary) }}</span>
                {{ ('games.review.class.' + d.primary) | translate }}
                <span class="dis-sep">·</span>
                <span class="alt-name">{{ altLabel() }}</span>
                <span class="sym" [style.background]="color(d.alt)">{{ symbol(d.alt) }}</span>
                {{ ('games.review.class.' + d.alt) | translate }}
              </div>
            }
            <div class="dis-list">
              @for (d of disagreements(); track d.ply) {
                <button type="button" class="dis-chip" [class.on]="d.ply === currentIndex()" (click)="moveClicked.emit(d.ply)">
                  <span class="dis-move">{{ disagreeLabel(d.ply) }}</span>
                  <span class="sym" [style.background]="color(d.primary)">{{ symbol(d.primary) }}</span>
                  <span class="dis-vs">vs</span>
                  <span class="sym" [style.background]="color(d.alt)">{{ symbol(d.alt) }}</span>
                </button>
              }
            </div>
          </div>
        }
        <div class="table-wrap">
          <table class="summary">
            <thead>
              <tr>
                <th></th>
                <th class="acc-h">{{ 'games.review.accuracy' | translate }}</th>
                @if (altReview()) { <th class="acc-h alt">{{ altLabel() }}</th> }
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
                  @if (altReview()) {
                    <td class="acc alt">
                      @if (row.altAccuracy != null) { {{ row.altAccuracy | number:'1.1-1' }} % } @else { – }
                    </td>
                  }
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
    /* Umschalter Stockfish | Lc0 | beide: klein, damit er neben Überschrift und Schaltern in eine Zeile passt. */
    .engine-view { --mat-button-toggle-height: 28px; font-size: 0.78rem; margin-left: auto; }
    .engine-view + .toggles { margin-left: 0; }
    .lines-label { font-size: 0.72rem; font-weight: 600; opacity: 0.7; margin: 4px 0 1px; }
    .lines-label.alt { color: #ff9800; opacity: 1; }
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
    .explain { margin: 2px 0 0; font-size: 0.88rem; line-height: 1.35; }
    .explain-master { margin: 2px 0 0; font-size: 0.8rem; line-height: 1.35; color: color-mix(in srgb, currentColor 70%, transparent); }
    .explain-master summary { cursor: pointer; }
    .explain-master q { display: block; margin: 4px 0 0 1.2em; font-style: italic; }
    .explain-btn { align-self: flex-start; }
    .explaining { font-size: 0.85rem; }
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
    .summary .alt { color: #ff9800; }
    .disagree { display: flex; flex-direction: column; gap: 4px; }
    .dis-head { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .dis-title { font-size: 0.82rem; font-weight: 600; }
    .dis-nav { display: inline-flex; align-items: center; margin-left: auto; font-size: 0.78rem; font-variant-numeric: tabular-nums; }
    .dis-nav button { --mat-icon-button-state-layer-size: 28px; width: 28px; height: 28px; padding: 2px; }
    .dis-current { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 6px; font-size: 0.8rem; }
    .dis-current .alt-name { color: #ff9800; font-weight: 600; }
    .dis-sep { opacity: 0.5; }
    .dis-list { display: flex; flex-wrap: wrap; gap: 4px; }
    .dis-chip {
      display: inline-flex; align-items: center; gap: 4px; padding: 2px 6px; border-radius: 6px; cursor: pointer;
      border: 1px solid color-mix(in srgb, currentColor 20%, transparent); background: none; color: inherit; font: inherit;
      font-size: 0.78rem;
    }
    .dis-chip.on { border-color: #ff9800; box-shadow: 0 0 0 1px #ff9800; }
    .dis-move { font-weight: 600; }
    .dis-vs { opacity: 0.6; font-size: 0.7rem; }
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
  private analyses = inject(GameAnalysisService);
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

  /** Kurve und Computer-Linien gleich offen (Korrekturseite, 0.672.5: dort sucht man gerade nach Fehlern). */
  expanded = input<boolean>(false);

  /** Im Fehler-Training: keine Computer-Linien und kein Pfeil — sie verrieten die Lösung. */
  engineHidden = input<boolean>(false);

  /** Die Live-Engine läuft (0.681.0): die vorberechneten Linien des Partiezugs bleiben sichtbar — gewünscht 2026-10-06,
   *  „die vorberechneten stockfishlines … wenn die für den aktuellen zug existieren einblenden". Pfeil, Zugsymbol und
   *  Erklärung bleiben weg: das Brett gehört dann der Live-Engine und trägt deren Pfeil. */
  liveEngine = input<boolean>(false);

  /** Das Brett zeigt gerade eine eigene Nebenvariante — die Linien des Partiezugs passen dann nicht zur Stellung
   *  und bleiben weg, bis man zur Partie zurückkehrt. */
  offGame = input<boolean>(false);

  /** Weitere eigene Analysen derselben Partie suchen und als Umschalter anbieten (0.682.0) — nur mit Anmeldung, die
   *  Suche fragt das eigene Konto. Gewünscht 2026-10-06: Lc0 neben Stockfish, auch beide zugleich. */
  withAlternatives = input<boolean>(false);

  /** Pfeil, Zugsymbol und Erklärung: weg im Training (verrieten die Lösung) und neben der Live-Engine. */
  private readonly boardMarksHidden = computed(() => this.engineHidden() || this.liveEngine());

  /** „Warum war das ein Fehler?" nachfragen (`…/explanations` neben `…/evals`). Aus, wo es den Endpunkt nicht gibt
   *  (Vereinspartien in LeagueHub, 0.593.0) — sonst fragte jede Partie ins Leere, und jede Antwort wäre ein 404. */
  withExplanations = input<boolean>(true);

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
  /** Die Klasse des aktuellen Zugs als Symbol am Zielfeld (seit 0.557.0, wie chess.com beim Durchsehen) — die Seite
   *  legt es auf ihr Brett. Leer im Fehler-Training und mit der Live-Engine: dort zeigt das Brett etwas anderes. */
  badgeChange = output<BoardBadge | null>();

  static readonly PollMs = 10_000;
  /** Während der Vertiefung (zweiter Durchgang, 0.523.0) gemächlicher — die Analyse ist schon nutzbar. */
  static readonly RefinePollMs = 60_000;
  /** Während Erklärungen entstehen: alle 5 s nachfragen (eine Erklärung braucht auf der Spark ein paar Sekunden). */
  static readonly ExplainPollMs = 5_000;
  static readonly LinesKey = 'rookhub_game_lines';
  static readonly ArrowKey = 'rookhub_game_arrow';
  /** Gemerkte Ansicht des Umschalters (`primary` | `alt` | `both`), je Gerät. */
  static readonly ViewKey = 'rookhub_game_engine_view';

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

  /** Die zweite Analyse derselben Partie (z. B. Lc0) und ihre Bewertungen — `null`, solange es keine gibt. */
  readonly alternative = signal<GameAnalysisAlternative | null>(null);
  readonly altEvals = signal<GameEvals | null>(null);
  private readonly engineView = signal<EngineView>(readView());
  /** Was gezeigt wird: ohne zweite Analyse immer die eigene Kurve der Seite. */
  readonly view = computed<EngineView>(() => this.alternative() && this.altEvals() ? this.engineView() : 'primary');
  /** Bewertungen für Klassen, Kurve, Linien und Pfeil: bei „Lc0" die der zweiten Analyse — die Buchzüge kommen
   *  weiter aus der eigenen, sie hängen am Repertoire des Betrachters und nicht an der Engine. */
  readonly shown = computed<GameEvals | null>(() => {
    const primary = this.evals();
    const alt = this.altEvals();
    return this.view() === 'alt' && alt ? { ...alt, bookPlies: primary?.bookPlies ?? [] } : primary;
  });
  readonly altLabel = computed(() => {
    const a = this.alternative();
    if (!a) return '';
    return isLc0Engine(a.engineName) ? 'Lc0' : (a.engineName ?? 'Engine');
  });
  /** Bei „Beide": die Kurve der zweiten Analyse über der ersten. */
  readonly altReview = computed(() => this.view() === 'both'
    ? reviewGame(this.altEvals(), this.fens(), this.ucis()) : null);
  readonly overlay = computed(() => this.altReview()?.curve ?? null);
  /** Bei „Beide" (0.683.0): Züge, die die beiden Analysen wirklich verschieden sehen — anspringbar. */
  readonly disagreements = computed(() => {
    const alt = this.altReview();
    return alt && !this.engineHidden() ? engineDisagreements(this.review(), alt) : [];
  });
  readonly currentDisagreement = computed(() =>
    this.disagreements().find(d => d.ply === this.currentIndex()) ?? null);
  readonly review = computed(() => reviewGame(this.shown(), this.fens(), this.ucis()));
  /** Die Kurve ist standardmäßig ZU und klappt nur auf Wunsch auf — bewusst nicht gemerkt: „standardmäßig". */
  readonly graphOpen = linkedSignal(() => this.expanded());
  /** Schalter je Gerät (localStorage — reine Anzeige-Vorliebe); mit `expanded` von Anfang an an. */
  readonly showLines = linkedSignal(() => this.expanded() || readRaw(localStore(), GameReviewComponent.LinesKey) === '1');
  readonly showArrow = signal(readRaw(localStore(), GameReviewComponent.ArrowKey) === '1');
  readonly lines = computed(() => this.showLines() && !this.engineHidden() && !this.offGame()
    ? computerLinesAt(this.shown(), this.fens(), this.currentIndex()) : []);
  /** Bei „Beide" die Linien der zweiten Analyse als eigener Block. */
  readonly altLines = computed(() => this.view() === 'both' && this.showLines() && !this.engineHidden() && !this.offGame()
    ? computerLinesAt(this.altEvals(), this.fens(), this.currentIndex()) : []);
  readonly arrows = computed<BoardArrow[]>(() => {
    if (!this.showArrow() || this.boardMarksHidden()) return [];
    const best = bestMoveArrowAt(this.shown(), this.currentIndex());
    return best ? [best] : [];
  });
  readonly badge = computed<BoardBadge | null>(() => {
    if (this.boardMarksHidden()) return null;
    const m = this.current();
    const move = this.moves()[this.currentIndex()];
    return m && move?.to ? { square: move.to, svg: moveBadgeSvg(m.cls) } : null;
  });
  /** `altAccuracy` nur bei „Beide": die Genauigkeit der zweiten Analyse als eigene Spalte (Variante A, 0.683.0). */
  readonly rows = computed(() => [
    { key: 'white', summary: this.review().white, altAccuracy: this.altReview()?.white.accuracy },
    { key: 'black', summary: this.review().black, altAccuracy: this.altReview()?.black.accuracy },
  ]);
  /** Bei „Beide" tragen die uneinigen Züge einen orangen Punkt statt ihres Klassen-Punkts. */
  readonly marks = computed<EvalGraphMark[]>(() => {
    const dis = new Set(this.disagreements().map(d => d.ply));
    const own = this.review().moves
      .filter((m): m is ReviewedMove => !!m && MARKED.has(m.cls) && !dis.has(m.ply))
      .map(m => ({ ply: m.ply, kind: m.cls, color: MOVE_CLASS_COLORS[m.cls] }));
    return [...own, ...[...dis].map(ply => ({ ply, kind: 'disagree', color: GameReviewComponent.AltColor }))];
  });

  /** Farbe der zweiten Analyse (Kurve, Linien, Spalte, uneinige Züge). */
  static readonly AltColor = '#ff9800';

  /** „17... Qe7" für die Knöpfe der uneinigen Züge. */
  disagreeLabel(ply: number): string {
    return `${moveNumberLabel(this.fens()[ply], ply)} ${this.moves()[ply]?.san ?? ''}`.trim();
  }
  stepDisagree(dir: 1 | -1): void {
    const ply = stepDisagreement(this.disagreements(), this.currentIndex(), dir);
    if (ply !== null) this.moveClicked.emit(ply);
  }
  disagreePosition(): string {
    const list = this.disagreements();
    const i = list.findIndex(d => d.ply === this.currentIndex());
    return i >= 0 ? `${i + 1} / ${list.length}` : `– / ${list.length}`;
  }
  readonly mistakes = computed(() => collectMistakes(this.review(), this.shown(), this.fens(), this.moves()));
  readonly current = computed(() => {
    const i = this.currentIndex();
    return i >= 0 ? this.review().moves[i] ?? null : null;
  });

  /** „Warum war das ein Fehler?" (0.534.0): Erklärungen der Partie in der Sprache der Oberfläche. */
  readonly explanations = signal<GameExplanations | null>(null);
  /** Der Text zum aktuellen Zug — nicht im Fehler-Training (er nennt den besseren Zug). */
  readonly explanationFor = computed(() => {
    const m = this.current();
    const e = this.explanations();
    if (!m || !e || this.boardMarksHidden()) return null;
    return e.items.find(x => x.ply === m.ply) ?? null;
  });

  /** „Anderssen – Kieseritzky, London 1851" — die Kopfzeile der Meisterpartie. */
  masterGame(m: GameExplanationMaster): string {
    const event = m.event ? `, ${m.event}` : '';
    const year = m.year ? ` ${m.year}` : '';
    return `${m.white || '?'} – ${m.black || '?'}${event}${year}`;
  }
  /** Knopf „Fehler erklären lassen": nur der Besitzer, nur wenn es Fehler gibt und noch keine Erklärung. Keine Sperrzeit
   *  — ein Auftrag auf Zuruf, dafür steht die Spark auch tagsüber bereit (0.585.0). */
  readonly explainState = computed<'can' | 'running' | null>(() => {
    const e = this.explanations();
    if (!e || this.boardMarksHidden() || this.status() !== 'done') return null;
    if (e.running) return 'running';
    const hasErrors = this.review().moves.some(m => !!m && ERROR_CLASSES.has(m.base));
    if (!hasErrors || e.items.length > 0) return null;
    return e.canGenerate ? 'can' : null;
  });

  private loadSub?: Subscription;
  private pollSub?: Subscription;
  private explainSub?: Subscription;
  private explainPoll?: Subscription;
  private explainKey: string | null = null;
  private lastStatus: GameEvalsStatus | null = null;

  constructor() {
    // Neue Adresse = neue Partie: sofort laden, ein laufendes Nachfragen der alten verwerfen.
    effect(() => {
      this.evalsUrl();
      untracked(() => this.reload());
    });
    // Zweite Analyse derselben Partie suchen (0.682.0) — einmal je Zugfolge, nur mit Anmeldung.
    effect(() => {
      const enabled = this.withAlternatives();
      const ucis = this.ucis();
      untracked(() => this.findAlternative(enabled, ucis));
    });
    // Der Pfeil folgt Stellung, Schalter und Analyse — die Seite legt ihn auf ihr Brett.
    effect(() => {
      const a = this.arrows();
      untracked(() => this.arrowsChange.emit(a));
    });
    effect(() => {
      const b = this.badge();
      untracked(() => this.badgeChange.emit(b));
    });
    // Jede neue Analyse-Antwort kann Aufgaben bringen — die Seite erfährt es über die Ausgabe.
    effect(() => {
      const m = this.mistakes();
      untracked(() => this.mistakesChange.emit(m));
    });
    // Erklärungen: sobald die Analyse fertig ist, und neu bei einem Sprachwechsel.
    effect(() => {
      const url = this.withExplanations() ? this.evalsUrl() : null;
      const done = this.status() === 'done';
      const lang = this.language();
      untracked(() => this.loadExplanations(url, done, lang));
    });
    inject(DestroyRef).onDestroy(() => {
      this.stop();
      this.altSub?.unsubscribe();
      this.altPoll?.unsubscribe();
      this.bookSub?.unsubscribe();
      this.explainSub?.unsubscribe();
      this.explainPoll?.unsubscribe();
    });
  }

  private altSub?: Subscription;
  private altPoll?: Subscription;
  private altKey: string | null = null;

  /**
   * Die zweite Analyse: unter den eigenen Analysen derselben Zugfolge die neueste mit AUSDRÜCKLICH gewählter Engine
   * (Lc0 bevorzugt) — eine ohne Engine-Angabe rechnete auf den Hintergrund-Engines und ist dasselbe wie die Kurve der
   * Seite. Ihre Bewertungen werden gleich mitgeladen; solange sie noch rechnet, alle zehn Sekunden neu.
   */
  private findAlternative(enabled: boolean, ucis: string[]): void {
    const key = enabled && ucis.length ? ucis.join(' ') : null;
    if (key === this.altKey) return;
    this.altKey = key;
    this.altSub?.unsubscribe();
    this.altPoll?.unsubscribe();
    this.alternative.set(null);
    this.altEvals.set(null);
    if (!key) return;
    this.altSub = this.analyses.sameGame(ucis).subscribe({
      next: list => {
        const explicit = list.filter(a => a.engineId);
        const pick = explicit.find(a => isLc0Engine(a.engineName)) ?? explicit[0] ?? null;
        this.alternative.set(pick);
        if (pick) this.loadAltEvals(pick.id);
      },
      error: () => { /* still: ohne zweite Analyse bleibt es bei der Kurve der Seite */ },
    });
  }

  private loadAltEvals(id: number): void {
    this.altPoll?.unsubscribe();
    this.altSub = this.games.evals(this.analyses.evalsUrl(id)).subscribe({
      next: e => {
        this.altEvals.set(e);
        if (e.status === 'pending' || e.status === 'running')
          this.altPoll = timer(GameReviewComponent.PollMs).subscribe(() => this.loadAltEvals(id));
      },
      error: () => this.altEvals.set(null),
    });
  }

  setView(view: EngineView): void {
    this.engineView.set(view);
    writeRaw(localStore(), GameReviewComponent.ViewKey, view);
  }

  private language(): string {
    return this.translate.currentLang() || this.translate.getFallbackLang() || 'en';
  }

  private loadExplanations(evalsUrl: string | null, done: boolean, lang: string, force = false): void {
    const key = evalsUrl && done ? `${evalsUrl}|${lang}` : null;
    if (!force && key === this.explainKey) return;
    this.explainKey = key;
    this.explainSub?.unsubscribe();
    this.explainPoll?.unsubscribe();
    if (!key || !evalsUrl) {
      this.explanations.set(null);
      return;
    }
    // Still: ohne Modell auf eigener Hardware (oder ohne Anmeldung am eigenen Endpunkt) gibt es schlicht keinen Text.
    this.explainSub = this.games.explanations(this.games.explanationsUrl(evalsUrl), lang).subscribe({
      next: e => this.applyExplanations(e),
      error: () => this.explanations.set(null),
    });
  }

  /** Erzeugen lassen — läuft im Hintergrund; bis es fertig ist, fragt die Komponente alle 5 s nach. */
  explain(): void {
    const url = this.evalsUrl();
    if (!url) return;
    this.explainSub?.unsubscribe();
    this.explainSub = this.games.requestExplanations(this.games.explanationsUrl(url), this.language()).subscribe({
      next: e => this.applyExplanations(e),
      error: () => this.explanations.update(e => e ? { ...e, canGenerate: false } : e),
    });
  }

  private applyExplanations(e: GameExplanations): void {
    this.explanations.set(e);
    this.explainPoll?.unsubscribe();
    if (e.running) {
      this.explainPoll = timer(GameReviewComponent.ExplainPollMs).subscribe(() =>
        this.loadExplanations(this.evalsUrl(), this.status() === 'done', this.language(), true));
    }
  }

  /** Sofort neu laden — nach „Partie analysieren", damit die Kurve nicht bis zum nächsten Takt wartet. */
  reload(): void {
    this.stop();
    const url = this.evalsUrl();
    if (!url) {
      this.apply(null);
      return;
    }
    // Kurve und Buchzüge getrennt (0.664.0): die Buchzüge brauchen das Stellungs-Set der Repertoires, und das baut der
    // Server nach fünf Minuten Leerlauf neu auf (2–4 s) — die Auswertung wartete darauf. Erst die schnelle Antwort,
    // dann EINMAL je Adresse die volle; deren Buchzüge werden nachgetragen. Die Vereins-Adresse kennt keine.
    const split = !url.includes('/league/club/');
    this.loadSub = this.games.evals(split ? `${url}?book=0` : url).subscribe({
      next: evals => {
        this.apply({ ...evals, bookPlies: evals.bookPlies?.length ? evals.bookPlies : this.evals()?.bookPlies ?? [] });
        this.scheduleIfBusy();
        if (split && this.bookLoadedFor !== url) {
          this.bookLoadedFor = url;
          this.bookSub?.unsubscribe();
          this.bookSub = this.games.evals(url).subscribe({
            next: full => this.evals.update(e => e ? { ...e, bookPlies: full.bookPlies ?? [] } : e),
            error: () => { this.bookLoadedFor = null; },   // der nächste Takt versucht es noch einmal
          });
        }
      },
      // Still: ein Aussetzer beim Nachfragen heilt der nächste Takt. Lief die Analyse, bleibt der Takt.
      error: () => this.scheduleIfBusy(),
    });
  }

  /** Für welche Adresse die Buchzüge schon geholt sind — einmal genügt, die Stellungen ändern sich nicht. */
  private bookLoadedFor: string | null = null;
  private bookSub?: Subscription;

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
