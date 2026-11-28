import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, linkedSignal, output,
  signal, untracked, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { BoardArrow, ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { localizeSan } from '@rh/shared/chess/keyboard-move.util';
import { PreferencesService } from '@rh/core/preferences.service';
import { formatSecondsClock } from '@rh/shared/clock-format.util';
import { exitFullscreen, fullscreenSupported, isFullscreen, onFullscreenChange, requestFullscreen } from '@rh/shared/fullscreen/fullscreen.util';
import { isBoardHotkey } from '@rh/shared/keyboard.util';
import { LiveBoardStudyComponent } from './live-board-study.component';
import { LiveBoardsService } from './live-boards.service';
import { LiveStudy, readLiveFsPrefs } from './live-study';
import { chessTitle } from '../tournament-table.util';
import { LiveEval, LiveGame, LivePlayer } from './live-boards.models';
import { LiveAlert } from './live-vibrate';
import { analysisLinkTarget, evalText, lastMovePair, liveBestMove, liveMoveNumber, shownClock, whiteShare } from './live-boards.util';

/**
 * Ein Brett der Live-Ansicht: oben die Gegenseite, unten die eigene Seite (Name, Titel, Elo, Uhr), dazwischen das
 * geteilte Brett (`app-chess-board`, nur Anzeige — ein Klick tut vorerst nichts) mit markiertem letzten Zug und
 * optional dem Engine-Balken links. Am Zug: Uhr fett mit Akzentrahmen; fertig: Ergebnis als Chip.
 *
 * Vollbild (1.036.0, Wunsch 2026-11-27): der Vollbild-Knopf des Bretts schickt die GANZE Karte ins Vollbild (bzw. legt
 * sie ohne Element-Vollbild — iOS — fest über das Fenster): Brett groß, daneben/darunter die Zugliste zum Nachspielen,
 * die Browser-Engine mit Linien und „Selbst ziehen“ ({@link LiveStudy}, {@link LiveBoardStudyComponent}).
 */
@Component({
  selector: 'app-live-board-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, TranslatePipe, MatButtonModule, MatIconModule, LiveBoardStudyComponent],
  host: { '(document:keydown)': 'onKey($event)' },
  template: `
    <article #card class="lb-card" [class.lb-fs]="fs()" [class.lb-flash-result]="flash() === 'result'" [class.lb-flash-swing]="flash() === 'swing'" [class.lb-wide]="fs() && wide()" [attr.role]="fs() ? 'dialog' : null" [attr.aria-modal]="fs() ? 'true' : null" [attr.tabindex]="fs() ? -1 : null" [attr.aria-label]="('tournaments.live.boardN' | translate:{ board: board() }) + ': ' + game().white.name + ' – ' + game().black.name">
      <div class="lb-top">
        <span class="lb-board" [title]="caption() ?? ''">{{ 'tournaments.live.boardN' | translate:{ board: board() } }}@if (caption(); as c) { <span class="lb-caption">· {{ c }}</span> }</span>
        @if (!game().finished && moveNo(); as n) {
          <!-- Zuganzahl (Wunsch 2026-11-26): laufend der Zug, der gerade gespielt wird, fertig der letzte -->
          <span class="lb-move" [title]="(game().finished ? 'tournaments.live.moveTipEnd' : (game().whiteToMove ? 'common.whiteToMove' : 'common.blackToMove')) | translate">{{ 'tournaments.live.moveN' | translate:{ move: n } }}</span>
        }
        @if (game().finished) {
          <!-- Fertig: nur das Ergebnis, die Zugzahl steht im Tooltip (schmale Karte, 26.11.: „½–½“ brach in zwei Zeilen um) -->
          <span class="lb-result" [title]="moveNo() ? ('tournaments.live.moveN' | translate:{ move: moveNo() }) : null">{{ resultText() }}</span>
        }
        <!-- Nur dieses Brett drehen (Wunsch 2026-11-26): lokal für diesen Zuschauer, die Vorgabe (eigene Mannschaft unten) bleibt sonst -->
        <button mat-icon-button type="button" class="lb-icon lb-turn" (click)="turn.emit()" [attr.aria-pressed]="turned()"
                [attr.aria-label]="'tournaments.live.turnBoard' | translate" [title]="'tournaments.live.turnBoard' | translate">
          <mat-icon>swap_vert</mat-icon>
        </button>
        @if (analysisUrl(); as url) {
          <!-- Partie samt Zügen, Kopfzeilen und Uhren auf RookHubs Analysebrett, neuer Tab (1.026.0) -->
          <a mat-icon-button class="lb-icon lb-analyse" [href]="url" [target]="linkTarget" rel="noopener"
             [attr.aria-label]="'tournaments.live.openAnalysis' | translate" [title]="'tournaments.live.openAnalysis' | translate">
            <mat-icon>biotech</mat-icon>
          </a>
        }
        @if (removable()) {
          <!-- Hinzugefügtes Brett wieder herausnehmen (1.025.0) -->
          <button mat-icon-button type="button" class="lb-icon lb-remove" (click)="remove.emit()"
                  [attr.aria-label]="'tournaments.live.removeBoard' | translate" [title]="'tournaments.live.removeBoard' | translate">
            <mat-icon>close</mat-icon>
          </button>
        }
        @if (fs()) {
          <!-- Vollbild verlassen (auch Esc) — letzter Knopf im Kopf -->
          <button mat-icon-button type="button" class="lb-icon lb-fs-close" (click)="closeFs()"
                  [attr.aria-label]="'common.fullscreenExit' | translate" [title]="'common.fullscreenExit' | translate">
            <mat-icon>close_fullscreen</mat-icon>
          </button>
        }
      </div>
      <div class="lb-player lb-ptop" [class.to-move]="toMove(flipped())">
        <!-- Elo in eigener Zeile unter dem Namen (Wunsch 2026-11-27): lange Namen kürzen mit „…“, die Elo bleibt sichtbar -->
        <span class="lb-who">
          <span class="lb-name">@if (title(topPlayer()); as t) { <span class="lb-title">{{ t }}</span> }{{ topPlayer().name }}</span>
          @if (topPlayer().elo) { <span class="lb-elo">{{ topPlayer().elo }}</span> }
        </span>
        @if (onlyMove() && atLive() && toMove(flipped())) {
          <!-- „Nur ein Zug“ (1.033.0): neben der Uhr der Seite am Zug; verrät den Zug nicht (Pfeil nur mit „Besten Zug zeigen“) -->
          <span class="lb-only" [title]="'tournaments.live.onlyMoveTip' | translate:onlyMoveTip()">{{ 'tournaments.live.onlyMove' | translate }}</span>
        }
        @if (clock(flipped()); as c) { <span class="lb-clock" role="timer">{{ c }}</span> }
      </div>
      <div class="lb-boardrow" [class.measured]="boardBox() !== null">
        @if (barOn()) {
          <div class="lb-evalbar" [class.empty]="share() === null" [class.flipped]="flipped()" role="img"
               [style.height.px]="boardBox()?.height" [style.margin-top.px]="boardBox()?.top"
               [attr.aria-label]="share() === null ? ('tournaments.live.noEval' | translate) : ('tournaments.live.evalLabel' | translate:{ value: evalLabel() })">
            <div class="lb-evalwhite" [style.height.%]="share() ?? 0"></div>
          </div>
        }
        <div class="lb-cg" #cg>
          <app-chess-board [fen]="boardFen()" [lastMove]="boardLastMove()" [flipped]="flipped()" [arrows]="bestArrows()"
                           [boardTheme]="prefs.boardTheme" [pieceSet]="prefs.pieceSet"
                           [playable]="!!study()?.ownMoves()" (userMove)="study()?.play($event)"
                           [fullscreenMode]="fs() ? 'none' : 'custom'" (fullscreenRequest)="openFs()" />
        </div>
      </div>
      <div class="lb-player lb-pbot" [class.to-move]="toMove(!flipped())">
        <!-- Elo in eigener Zeile unter dem Namen (Wunsch 2026-11-27): lange Namen kürzen mit „…“, die Elo bleibt sichtbar -->
        <span class="lb-who">
          <span class="lb-name">@if (title(bottomPlayer()); as t) { <span class="lb-title">{{ t }}</span> }{{ bottomPlayer().name }}</span>
          @if (bottomPlayer().elo) { <span class="lb-elo">{{ bottomPlayer().elo }}</span> }
        </span>
        @if (onlyMove() && atLive() && toMove(!flipped())) {
          <!-- „Nur ein Zug“ (1.033.0): neben der Uhr der Seite am Zug; verrät den Zug nicht (Pfeil nur mit „Besten Zug zeigen“) -->
          <span class="lb-only" [title]="'tournaments.live.onlyMoveTip' | translate:onlyMoveTip()">{{ 'tournaments.live.onlyMove' | translate }}</span>
        }
        @if (clock(!flipped()); as c) { <span class="lb-clock" role="timer">{{ c }}</span> }
      </div>
      @if (showEval() && evalLabel() && atLive() && !(study()?.engineOn())) {
        <!-- Quelle dezent (1.015.0): Tooltip „Übertragung / Lichess-Cloud / RookHub-Engine“, Tiefe in der Zeile. -->
        <div class="lb-evaltext" [title]="('tournaments.live.evalSource.' + shownEval()!.source) | translate">
          {{ evalLabel() }}@if (shownEval()!.depth; as d) { · {{ 'tournaments.live.depth' | translate:{ depth: d } }} }@if (bestSan(); as san) { · <span class="lb-best" [title]="'tournaments.live.bestMoveLabel' | translate:{ move: san }">{{ san }}</span> }
        </div>
      }
      @if (study(); as st) {
        <app-live-board-study class="lb-side" [study]="st" [pgnState]="pgnState()" (retry)="loadPgn()" />
      }
    </article>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .lb-card {
      border: 1px solid color-mix(in srgb, currentColor 14%, transparent); border-radius: 8px; padding: 8px;
      display: flex; flex-direction: column; gap: 4px; background: var(--mat-sys-surface-container-low);
    }
    /* Kopf eng (Karte ab 240 px, bis zu drei Symbolknöpfe à 40 px): Abstand 4 px, „Brett n“ behält mind. 3,5 em, der Zusatz kürzt mit „…“ */
    .lb-top { display: flex; align-items: center; justify-content: space-between; gap: 4px; font-size: 0.85rem;
      color: var(--mat-sys-on-surface-variant); }
    .lb-board { font-weight: 500; min-width: 3.5em; flex: 1 1 auto; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-caption { font-weight: 400; }
    .lb-move { flex: 0 0 auto; font-variant-numeric: tabular-nums; white-space: nowrap; }
    /* Klein im Kopf der Karte, Tippziel bleibt 40 px (die Karte ist kein Formular) */
    .lb-icon { flex: 0 0 auto; width: 40px; height: 40px; padding: 8px; margin: -8px -4px -8px 0; }
    .lb-icon mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .lb-result { flex: 0 0 auto; white-space: nowrap; line-height: 1.5; font-weight: 700; color: var(--mat-sys-on-surface);
      padding: 0 8px; border-radius: 999px; border: 1px solid var(--mat-sys-outline); font-variant-numeric: tabular-nums; }
    .lb-player { display: flex; align-items: center; justify-content: space-between; gap: 8px; min-width: 0; }
    .lb-who { display: flex; flex-direction: column; min-width: 0; flex: 1 1 auto; }
    .lb-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-title { font-weight: 700; font-size: 0.85em; color: var(--rh-accent); margin-right: 0.25em; }
    /* Zwei Zeilen (Name, Elo) eng gesetzt: die Bretterwand muss bei 1920 × 1080 weiter zwei Reihen zeigen (Spec „Bretterwand“) */
    .lb-elo { color: var(--mat-sys-on-surface-variant); font-size: 0.75rem; line-height: 1.1; font-variant-numeric: tabular-nums; }
    .lb-clock { flex: 0 0 auto; font-variant-numeric: tabular-nums; padding: 0 6px; border-radius: 4px;
      border: 1px solid transparent; }
    .to-move .lb-clock { font-weight: 700; border-color: var(--rh-accent); }
    /* „Nur ein Zug“ (1.033.0): kleiner Akzent-Chip direkt vor der Uhr; niedriger als die Zeile → kein Sprung, der Name kürzt mit „…“ */
    .lb-only { flex: 0 0 auto; margin-left: auto; margin-right: -2px; white-space: nowrap; font-size: 0.72rem; font-weight: 600;
      line-height: 1.3; align-self: center; margin-block: -2px; padding: 0 6px; border-radius: 999px; color: var(--rh-accent);
      border: 1px solid var(--rh-accent);
      background: color-mix(in srgb, var(--rh-accent) 12%, transparent); }
    :host-context(html.eink) .lb-only { color: black; border-color: black; background: none; }
    /* Aufblitzen (1.042.0): Ergebnis in Akzentfarbe, Bewertungssprung in Warnfarbe — dreimal kurz Rahmen + getönte Fläche.
       Ohne Bewegung (prefers-reduced-motion, E-Ink) ein ruhiger Rahmen, solange die Klasse steht (3 s). Die Bedeutung trägt
       nicht die Farbe allein: Ergebnis-Chip bzw. Wertzeile ändern sich zugleich. */
    .lb-card.lb-flash-result { --lb-flash: var(--rh-accent); }
    .lb-card.lb-flash-swing { --lb-flash: var(--rh-warn); }
    .lb-card.lb-flash-result, .lb-card.lb-flash-swing { animation: lb-flash 0.6s ease-in-out 3; }
    @keyframes lb-flash {
      0%, 100% { box-shadow: 0 0 0 0 transparent; }
      50% { box-shadow: 0 0 0 3px var(--lb-flash); background: color-mix(in srgb, var(--lb-flash) 16%, var(--mat-sys-surface-container-low)); }
    }
    @media (prefers-reduced-motion: reduce) {
      .lb-card.lb-flash-result, .lb-card.lb-flash-swing { animation: none; box-shadow: 0 0 0 3px var(--lb-flash); }
    }
    :host-context(html.eink) .lb-card.lb-flash-result, :host-context(html.eink) .lb-card.lb-flash-swing {
      animation: none; box-shadow: 0 0 0 3px black; }
    /* Balken genau so hoch wie das gezeichnete Brett (Wunsch 2026-11-26: „0.0 genau zwischen 4 und 5“) — Chessground
       rastet auf ein Vielfaches von acht und steht oben links, die Zeile kann also etwas höher sein als das Brett. Höhe und
       Versatz misst boardBox(); ohne Messung (erstes Bild) streckt sich der Balken wie bisher. */
    .lb-boardrow { display: flex; gap: 4px; align-items: stretch; }
    .lb-boardrow.measured { align-items: flex-start; }
    .lb-cg { flex: 1 1 auto; min-width: 0; }
    .lb-evalbar { flex: 0 0 10px; box-sizing: border-box; position: relative; display: flex; flex-direction: column-reverse;
      background: color-mix(in srgb, black 80%, white); border: 1px solid var(--mat-sys-outline); border-radius: 2px; overflow: hidden; }
    .lb-evalbar.flipped { flex-direction: column; }
    .lb-evalbar.empty { background: transparent; border-style: dashed; }
    .lb-evalwhite { background: white; width: 100%; }
    .lb-best { font-weight: 600; color: var(--mat-sys-on-surface); }
    .lb-evaltext { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
    /* Vollbild (1.036.0): die Karte deckt das Fenster ab (echtes Element-Vollbild, ohne — iOS — fest übers Fenster).
       Hochformat: Brett volle Breite, darunter Zugliste + Engine, die Karte scrollt. Quer/breit: Brett so groß, wie die
       Höhe erlaubt, rechts das Seitenteil mit eigener Scrollfläche. */
    .lb-card.lb-fs { position: fixed; inset: 0; z-index: 1100; border: 0; border-radius: 0; padding: 8px 12px 12px;
      background: var(--mat-sys-surface); overflow-y: auto; overscroll-behavior: contain; gap: 6px; font-size: 1rem; }
    .lb-card.lb-fs::backdrop { background: var(--mat-sys-surface); }
    .lb-fs .lb-top { font-size: 0.95rem; }
    .lb-fs .lb-player { font-size: 1.05rem; }
    .lb-fs .lb-side { margin-top: 6px; }
    .lb-card.lb-fs.lb-wide { display: grid; overflow: hidden; column-gap: 16px; row-gap: 4px; align-content: start;
      --lb-fs-board: min(calc(100dvh - 150px), calc(100vw - 420px));
      grid-template-columns: calc(var(--lb-fs-board) + 14px) minmax(280px, 1fr);
      grid-template-rows: auto auto auto auto minmax(0, 1fr); height: 100dvh; box-sizing: border-box; }
    .lb-wide .lb-top { grid-column: 1 / -1; }
    .lb-wide .lb-ptop, .lb-wide .lb-boardrow, .lb-wide .lb-pbot, .lb-wide .lb-evaltext { grid-column: 1; }
    .lb-wide .lb-side { grid-column: 2; grid-row: 2 / -1; margin-top: 0; min-height: 0; overflow-y: auto; }
  `],
})
export class LiveBoardCardComponent {
  readonly prefs = inject(PreferencesService);
  private readonly translate = inject(TranslateService);
  readonly game = input.required<LiveGame>();
  readonly board = input.required<number>();
  /** Schwarz unten (z. B. die gewählte Mannschaft spielt an diesem Brett Schwarz). */
  readonly flipped = input(false);
  readonly showEval = input(false);
  /** Wann die Partie zuletzt kam und „jetzt" — für die laufende Uhr der Seite am Zug. */
  readonly receivedAt = input(0);
  readonly now = input(0);
  /** Kleine Zusatzzeile neben „Brett n“ (hinzugefügte Bretter: Begegnung bzw. Turnier). */
  readonly caption = input<string | null>(null);
  /** Zeigt ein ✕ zum Herausnehmen (hinzugefügte Bretter). */
  readonly removable = input(false);
  /** Link aufs Analysebrett (RookHub, neuer Tab); `null` = kein Knopf (z. B. ohne Partnerseite auf localhost). */
  readonly analysisUrl = input<string | null>(null);
  /** „Besten Zug zeigen“ (1.030.0): Pfeil + SAN des besten Zugs der Bewertung — nur mit Engine-Balken. */
  readonly showBest = input(false);
  /** Aufblitzen (1.042.0): die Seite setzt es für 3 s (`LIVE_FLASH_MS`) nach einem Ergebnis bzw. Bewertungssprung. */
  readonly flash = input<LiveAlert | null>(null);
  readonly remove = output<void>();
  /** „Brett drehen“ gedrückt — die Seite merkt es je Partie und gibt das neue `flipped` herein. */
  readonly turn = output<void>();
  /** Von Hand gedreht (gegen die Vorgabe) — nur für `aria-pressed`. */
  readonly turned = input(false);
  /** Lichess-Runde der Partie — fürs PGN (Zugliste) im Vollbild; ohne bleibt die Zugliste leer. */
  readonly roundId = input<string | null>(null);
  /** Analyse-Link: in der installierten App im selben Fenster, sonst neuer Tab. */
  readonly linkTarget = analysisLinkTarget();

  private readonly service = inject(LiveBoardsService);
  /** Vollbild-Ansicht offen (1.036.0). */
  readonly fs = signal(false);
  /** Nachspielen/Engine/eigene Züge — nur solange das Vollbild offen ist. */
  readonly study = signal<LiveStudy | null>(null);
  readonly pgnState = signal<'loading' | 'ok' | 'error'>('loading');
  /** Quer und breit genug: Seitenteil neben dem Brett statt darunter. */
  readonly wide = signal(false);
  private readonly cardEl = viewChild<ElementRef<HTMLElement>>('card');
  /** Die Karte war wirklich im Element-Vollbild — verlässt der Browser es (Esc), schließt die Ansicht mit. */
  private realFs = false;
  private pgnTimer: ReturnType<typeof setTimeout> | null = null;
  private pgnBusy = false;
  private offFs?: () => void;
  private wideQuery: MediaQueryList | null = null;
  private readonly onWide = () => this.wide.set(!!this.wideQuery?.matches);

  /** Brett zeigt genau die Live-Stellung (außerhalb des Vollbilds immer). */
  readonly atLive = computed(() => this.study()?.atLive() ?? true);
  readonly boardFen = computed(() => this.study()?.fen() ?? this.game().fen);
  readonly boardLastMove = computed(() => { const st = this.study(); return st ? st.lastMove() : this.lastMove(); });
  /** Balken: wie bisher mit dem Schalter der Seite; im Vollbild auch, solange die Browser-Engine rechnet (dann ihr Wert). */
  readonly barOn = computed(() => this.showEval() || !!this.study()?.engineOn());

  readonly topPlayer = computed<LivePlayer>(() => this.flipped() ? this.game().white : this.game().black);
  readonly bottomPlayer = computed<LivePlayer>(() => this.flipped() ? this.game().black : this.game().white);
  readonly lastMove = computed(() => lastMovePair(this.game().lastMove));
  /** Der Balken hält nach einem Zug den letzten Wert, bis für die neue Stellung einer da ist (Wunsch 2026-11-26: kein
   *  Leerlauf/Ausschlag direkt nach dem Zug). Eine neue Partie auf der Karte beginnt leer. */
  readonly shownEval = linkedSignal<{ id: string; eval: LiveEval | null }, LiveEval | null>({
    source: () => ({ id: this.game().id, eval: this.game().eval }),
    computation: (src, prev) => src.eval ?? (prev && prev.source.id === src.id ? prev.value : null),
  });
  /** Wert des Balkens: Browser-Engine (Vollbild, an) → ihre beste Linie; abseits der Live-Stellung ohne sie leer. */
  readonly barEval = computed<LiveEval | null>(() => {
    const st = this.study();
    if (st?.engineOn()) { const t = st.session.topEval(); return t ? { ...t, source: 'engine', depth: st.session.depth() } : null; }
    return this.atLive() ? this.shownEval() : null;
  });
  readonly share = computed(() => whiteShare(this.barEval()));
  readonly evalLabel = computed(() => evalText(this.shownEval()));
  /** Zugnummer aus den Halbzügen: läuft die Partie, der Zug am Brett (`plies/2 + 1`), fertig der letzte gespielte; vor dem ersten Zug nichts. */
  readonly moveNo = computed(() => liveMoveNumber(this.game()));
  /** Bester Zug NUR aus der Bewertung der aktuellen Stellung (`game().eval`) — der nach einem Zug gehaltene alte Wert
   *  (`shownEval`) zeigt keinen Pfeil, der gehörte zur Stellung davor. */
  readonly best = computed(() => this.showEval() && this.showBest() && !this.game().finished
    ? liveBestMove(this.game().fen, this.game().eval?.best) : null);
  readonly bestArrows = computed<BoardArrow[]>(() => {
    const st = this.study();
    if (st?.engineOn()) return st.arrows();
    if (!this.atLive()) return [];
    const b = this.best();
    return b ? [{ from: b.from, to: b.to, brush: 'blue' }] : [];
  });
  readonly bestSan = computed(() => {
    const b = this.best();
    return b ? localizeSan(b.san, this.translate.getCurrentLang()) : null;
  });
  /** „Nur ein Zug“ (1.033.0) — nur zur Bewertung der AKTUELLEN Stellung (nicht zum gehaltenen alten Wert), nur mit Balken,
   *  nur laufende Partie; gilt für die Seite am Zug. Die Regel rechnet der Server (`onlyMove`). */
  readonly onlyMove = computed(() => this.showEval() && !this.game().finished && this.game().eval?.onlyMove === true);
  /** Tooltip-Werte (Weiß-Sicht wie die Wertzeile): „bester +0.3, nächstbester −1.2“. */
  readonly onlyMoveTip = computed(() => {
    const ev = this.game().eval;
    return { best: evalText(ev), second: evalText(ev ? { ...ev, cp: ev.secondCp ?? null, mate: ev.secondMate ?? null } : null) };
  });
  readonly resultText = computed(() => this.game().result === '1/2-1/2' ? '½–½' : this.game().result.replace('-', '–'));

  /** Lage des gezeichneten Bretts (cg-board) in der Brettzeile — Balkenhöhe und -versatz. */
  readonly boardBox = signal<{ top: number; height: number } | null>(null);
  private readonly cgHost = viewChild<ElementRef<HTMLElement>>('cg');

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') return;
      const observed = new Set<Element>();
      const ro = new ResizeObserver(() => this.measureBoard(ro, observed));
      const host = this.cgHost()?.nativeElement;
      if (host) { ro.observe(host); observed.add(host); }
      this.measureBoard(ro, observed);
      destroyRef.onDestroy(() => ro.disconnect());
    });
    // Live-Ereignisse in die Vollbild-Ansicht: passt der Zug nicht an die Zugliste, das PGN neu holen (gebündelt).
    effect(() => {
      const g = this.game();
      const st = this.study();
      if (!st) return;
      untracked(() => { if (st.update(g) === 'stale' && st.loaded()) this.schedulePgn(); });
    });
    destroyRef.onDestroy(() => this.teardown());
  }

  /** Vollbild-Knopf des Bretts: Karte groß, Zugliste + Engine dazu. */
  openFs(): void {
    if (this.fs()) return;
    const st = new LiveStudy(undefined, readLiveFsPrefs());
    st.update(this.game());
    this.study.set(st);
    this.fs.set(true);
    if (typeof window !== 'undefined' && window.matchMedia) {
      this.wideQuery = window.matchMedia('(orientation: landscape) and (min-width: 600px)');
      this.onWide();
      this.wideQuery.addEventListener?.('change', this.onWide);
    }
    this.loadPgn();
    const el = this.cardEl()?.nativeElement;
    if (el && fullscreenSupported()) {
      this.offFs = onFullscreenChange(() => {
        const now = isFullscreen(el);
        if (now) this.realFs = true;
        else if (this.realFs) this.closeFs();
      });
      requestFullscreen(el).catch(() => { /* abgelehnt: die Karte deckt das Fenster trotzdem ab */ });
    }
    queueMicrotask(() => el?.focus({ preventScroll: true }));
  }

  closeFs(): void {
    if (!this.fs()) return;
    const wasReal = this.realFs && isFullscreen(this.cardEl()?.nativeElement);
    this.teardown();
    this.fs.set(false);
    this.study.set(null);
    if (wasReal) exitFullscreen().catch(() => { /* schon draußen */ });
  }

  /** PGN der Partie holen (Zugliste); ohne Lichess-Runde bleibt nur die Live-Stellung. */
  loadPgn(): void {
    const st = this.study();
    const round = this.roundId();
    if (!st) return;
    if (!round) { this.pgnState.set('error'); return; }
    if (this.pgnBusy) return;
    this.pgnBusy = true;
    if (!st.loaded()) this.pgnState.set('loading');
    this.service.pgn(round, this.game().id).subscribe({
      next: pgn => {
        this.pgnBusy = false;
        if (this.study() !== st) return;
        this.pgnState.set(st.loadPgn(pgn) ? 'ok' : 'error');
      },
      error: () => {
        this.pgnBusy = false;
        if (this.study() === st && !st.loaded()) this.pgnState.set('error');
      },
    });
  }

  /** Tasten im Vollbild: ←/→ Zug zurück/vor, Pos1/Ende, Esc schließt (auch ohne Element-Vollbild). */
  onKey(e: KeyboardEvent): void {
    const st = this.study();
    if (!st || !this.fs()) return;
    if (e.key === 'Escape') { e.preventDefault(); this.closeFs(); return; }
    if (!isBoardHotkey(e)) return;
    switch (e.key) {
      case 'ArrowLeft': st.prev(); break;
      case 'ArrowRight': st.next(); break;
      case 'Home': st.first(); break;
      case 'End': st.last(); break;
      default: return;
    }
    e.preventDefault();
  }

  private schedulePgn(): void {
    if (this.pgnTimer) return;
    this.pgnTimer = setTimeout(() => { this.pgnTimer = null; this.loadPgn(); }, 1500);
  }

  private teardown(): void {
    this.offFs?.(); this.offFs = undefined;
    this.realFs = false;
    if (this.pgnTimer) { clearTimeout(this.pgnTimer); this.pgnTimer = null; }
    this.wideQuery?.removeEventListener?.('change', this.onWide);
    this.wideQuery = null;
    this.study()?.destroy();
  }

  /** Misst cg-board relativ zur Brettzeile; beobachtet es zusätzlich, sobald Chessground es angelegt hat. */
  measureBoard(ro?: ResizeObserver, observed?: Set<Element>): void {
    const host = this.cgHost()?.nativeElement;
    const board = host?.querySelector('cg-board') as HTMLElement | null;
    if (!host || !board) return;
    if (ro && observed && !observed.has(board)) { ro.observe(board); observed.add(board); }
    const h = board.getBoundingClientRect();
    const row = host.getBoundingClientRect();
    if (h.height <= 0) return;
    const next = { top: Math.round(h.top - row.top), height: Math.round(h.height) };
    const cur = this.boardBox();
    if (!cur || cur.top !== next.top || cur.height !== next.height) this.boardBox.set(next);
  }

  title(p: LivePlayer): string | null { return chessTitle(p.title); }

  /** Die Seite (Weiß = true) ist am Zug — und die Partie läuft. */
  toMove(white: boolean): boolean { return !this.game().finished && this.game().whiteToMove === white; }

  clock(white: boolean): string | null {
    const s = shownClock(this.game(), white, this.receivedAt(), this.now() || this.receivedAt());
    return s === null ? null : formatSecondsClock(s);
  }
}
