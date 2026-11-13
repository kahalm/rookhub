import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, InjectionToken, OnInit, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { Move } from 'chess.js';
import { BoardArrow, ChessBoardComponent, UserBoardMove } from '../../../shared/pgn-viewer/chess-board.component';
import { MoveListComponent } from '../../../shared/pgn-viewer/move-list.component';
import { isBoardHotkey } from '../../../shared/keyboard.util';
import { PreferencesService } from '../../../core/preferences.service';
import { SanPipe } from '../../../shared/chess/san.pipe';
import { LIVE_LINES, LiveEngineSession, LiveMove } from '../live-engine-session';
import { positionsOf } from './summary-markdown.util';

/** Sprung, mit dem ein Brett startet (Blatt am Handy): Halbzug der Partie oder Zugfolge ab Partieanfang wie `showLine`. */
export type SummaryJump = { ply: number; flipped?: boolean } | { line: LiveMove[]; at: number };

/** Erzeugt die Engine-Sitzung des Bretts — in Specs durch eine Attrappe ersetzbar. */
export const SUMMARY_ENGINE_SESSION = new InjectionToken<() => LiveEngineSession>('SUMMARY_ENGINE_SESSION', {
  providedIn: 'root', factory: () => () => new LiveEngineSession(),
});

/**
 * Seitenbrett der Partie-Zusammenfassung (Wunsch 2026-11-09: „neben der …/summary die Partie sehen und durchklicken"):
 * Brett, Blättern und Zugliste der Hauptlinie. Die Halbzug-Zählung ist die der Diagramme (`ply` 0 = Anfangsstellung), sie
 * kommt aus demselben `positionsOf`; `showPly` ist der Sprung von einem Diagramm. Ab 900 px klebt das Brett neben dem
 * Text, darunter steht es unter dem Text.
 *
 * Varianten (Wunsch 2026-11-09): aus dem Text (`showLine`) oder selbst gezogen, ab jeder Stellung. Sie leben nur in diesem
 * Signal — nichts wird gespeichert, ein Neuladen oder Blättern in der Partie verwirft sie (Entscheidung User).
 *
 * Engine (Wunsch 2026-11-13, „freizug mit engine“): der Knopf `memory` schaltet Stockfish im Browser zu
 * ({@link LiveEngineSession}, dieselbe Technik wie die Live-Engine der Partieseite). Er rechnet die Stellung auf dem Brett —
 * Partie oder eigene Variante — mit Pfeil für den besten Zug; läuft ohne Anmeldung (die Engine ist eine statische Datei der
 * App). Am Handy steht dieselbe Komponente im Blatt über dem Text (`inSheet`, `SummaryBoardSheetComponent`), dort mit
 * Engine von Anfang an.
 */
@Component({
  selector: 'app-summary-game-board',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, MoveListComponent, MatButtonModule, MatCardModule, MatIconModule, TranslatePipe, SanPipe],
  host: { '[class.in-sheet]': 'inSheet()' },
  template: `
    <mat-card class="side-card" [attr.aria-label]="'playerCard.replayGame' | translate">
      <div class="board-box">
        <app-chess-board [fen]="currentFen()" [lastMove]="currentLastMove()" [flipped]="isFlipped()" [arrows]="arrows()"
                         [playable]="true" (userMove)="onUserMove($event)"
                         [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
      </div>
      <div class="nav" role="group" [attr.aria-label]="'playerCard.movesGroup' | translate">
        <button mat-icon-button type="button" class="nav-start" [disabled]="variation() ? variation()!.at === 0 : ply() === 0" (click)="toStart()"
                [attr.aria-label]="'playerCard.toStart' | translate"><mat-icon>first_page</mat-icon></button>
        <button mat-icon-button type="button" class="nav-back" [disabled]="!variation() && ply() === 0" (click)="back()"
                [attr.aria-label]="'playerCard.stepBack' | translate"><mat-icon>chevron_left</mat-icon></button>
        <button mat-icon-button type="button" class="nav-forward" [disabled]="atEnd()" (click)="forward()"
                [attr.aria-label]="'playerCard.stepForward' | translate"><mat-icon>chevron_right</mat-icon></button>
        <button mat-icon-button type="button" class="nav-end" [disabled]="atEnd()" (click)="toEnd()"
                [attr.aria-label]="'playerCard.toEnd' | translate"><mat-icon>last_page</mat-icon></button>
        <button mat-icon-button type="button" class="nav-engine" [class.on]="engineOn()" (click)="toggleEngine()"
                [attr.aria-pressed]="engineOn()" [attr.aria-label]="'games.live.toggle' | translate"
                [attr.title]="'games.live.toggle' | translate"><mat-icon>memory</mat-icon></button>
      </div>
      @if (engine(); as s) {
        <div class="engine" aria-live="polite">
          <div class="engine-head">
            <span>{{ 'games.live.browser' | translate }}</span>
            @if (s.depth() > 0) { <span class="depth">{{ 'games.live.depth' | translate: { depth: s.depth() } }}</span> }
          </div>
          <ol class="engine-lines">
            <!-- Feste Zeilenzahl: beim Zugwechsel leert die Sitzung die Linien, ohne Platzhalter sprang die Zugliste darunter. -->
            @for (i of slots; track i) {
              @if (s.lines()[i]; as l) {
                <li><span class="line-eval" [class.white]="l.positive">{{ l.evalText }}</span> <span class="line-san">{{ l.san | san:'text' }}</span></li>
              } @else {
                <li class="empty" aria-hidden="true">&nbsp;</li>
              }
            }
          </ol>
        </div>
      }
      @if (variation(); as v) {
        <div class="variation" role="group" [attr.aria-label]="'games.summary.variation' | translate">
          <span class="var-label">{{ 'games.summary.variation' | translate }}</span>
          @for (m of variationMoves(); track $index) {
            <button type="button" class="var-move" [class.active]="$index + 1 === v.at" (click)="goToVariation($index + 1)">{{ m.number }}{{ m.san | san }}</button>
          }
          <button mat-button type="button" class="var-exit" (click)="exitVariation()"><mat-icon>undo</mat-icon> {{ 'games.summary.backToGame' | translate }}</button>
        </div>
      }
      <div class="moves">
        <app-move-list [moves]="moves()" [currentMoveIndex]="ply() - 1" (moveClicked)="goTo($event + 1)" />
      </div>
    </mat-card>
  `,
  styles: [`
    :host { display: block; min-width: 0; margin-top: 16px; }
    .side-card { padding: 12px; }
    .board-box { width: min(100%, 480px, max(240px, calc(100vh - 160px))); margin: 0 auto; }
    .nav { display: flex; justify-content: center; gap: 4px; margin: 4px 0; max-width: 100%; }
    .nav-engine.on { color: var(--rh-accent); }
    .engine { margin: 2px 0 6px; font-size: 0.875rem; min-width: 0; }
    .engine-head { display: flex; gap: 8px; color: var(--mat-sys-on-surface-variant); font-size: 0.8rem; }
    .engine-lines { list-style: none; margin: 2px 0 0; padding: 0; }
    .engine-lines li { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; line-height: 1.5; }
    .line-eval { display: inline-block; min-width: 3.5em; font-weight: 600; }
    .variation { display: flex; flex-wrap: wrap; align-items: center; gap: 2px 6px; margin: 4px 0; padding: 4px 8px; border-radius: 6px;
      background: color-mix(in srgb, var(--rh-accent) 8%, transparent); }
    .var-label { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); }
    .var-move { all: unset; cursor: pointer; padding: 0 3px; border-radius: 3px; }
    .var-move:hover, .var-move:focus-visible { background: color-mix(in srgb, currentColor 12%, transparent); }
    .var-move.active { font-weight: 700; color: var(--rh-accent); }
    .var-exit { margin-left: auto; }
    .moves { max-height: 40vh; overflow-y: auto; }
    app-move-list { display: block; }
    /* Ab 900 px wie Analysebrett und Löser (DESIGN 5.12): neben dem Text, klebt, nie höher als das Fenster —
       das Brett gibt der Höhe nach, die Zugliste scrollt in ihrem Kasten. */
    @media (min-width: 900px) {
      :host(:not(.in-sheet)) { position: sticky; top: 12px; align-self: start; margin-top: 0; }
      :host(:not(.in-sheet)) .side-card { max-height: calc(100vh - 24px); box-sizing: border-box; }
      :host(:not(.in-sheet)) .board-box { width: min(100%, max(240px, calc(100vh - 250px))); flex: 0 0 auto; }
      :host(:not(.in-sheet)) .nav { flex: 0 0 auto; }
      :host(:not(.in-sheet)) .moves { flex: 0 1 auto; min-height: 72px; max-height: none; }
    }
    /* Im Blatt am Handy (Wunsch 2026-11-13): ohne eigene Karte, das Brett lässt Platz für Engine und Zugliste darunter. */
    :host(.in-sheet) { margin-top: 0; }
    :host(.in-sheet) .side-card { padding: 0; box-shadow: none; background: transparent; }
    :host(.in-sheet) .board-box { width: min(100%, 440px, max(220px, calc(100dvh - 340px))); }
    :host(.in-sheet) .moves { max-height: 28dvh; }
  `],
})
export class SummaryGameBoardComponent implements OnInit {
  readonly preferences = inject(PreferencesService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly engineFactory = inject(SUMMARY_ENGINE_SESSION);

  readonly pgn = input.required<string>();
  /** Blickrichtung wie die Diagramme der Seite; ein Sprung von einem Diagramm übernimmt dessen Richtung. */
  readonly flipped = input(false);
  /** Im Blatt am Handy: nicht kleben, nicht scrollen, Tasten im eigenen Dialog. */
  readonly inSheet = input(false);
  /** Startstellung (Blatt am Handy), wie `showPly` bzw. `showLine`. */
  readonly jump = input<SummaryJump | null>(null);
  /** Engine schon beim Start an (Blatt am Handy). */
  readonly engineAtStart = input(false);

  readonly positions = computed(() => positionsOf(this.pgn()));
  readonly lastPly = computed(() => this.positions().sans.length);
  /** Halbzüge auf dem Brett, 0 = Anfangsstellung (wie `ply` der Diagramme). */
  readonly ply = signal(0);
  private readonly flipOverride = signal<boolean | null>(null);
  readonly isFlipped = computed(() => this.flipOverride() ?? this.flipped());

  /** Für die Zugliste: sie braucht nur Zug, Farbe und die Stellung davor (Zugnummer). */
  readonly moves = computed(() => {
    const { sans, fens } = this.positions();
    return sans.map((san, i) => ({ san, color: fens[i].split(' ')[1], before: fens[i] }) as unknown as Move);
  });

  /** Variante auf dem Brett: Abzweig `base` (= `ply`), ihre Züge, und wie viele davon stehen (0 = die Abzweigstellung). */
  readonly variation = signal<{ base: number; line: LiveMove[]; at: number } | null>(null);

  readonly currentFen = computed(() => {
    const v = this.variation();
    return v && v.at > 0 ? v.line[v.at - 1].fen : this.positions().fens[this.ply()];
  });
  readonly currentLastMove = computed<[string, string] | undefined>(() => {
    const v = this.variation();
    if (v && v.at > 0) return [v.line[v.at - 1].from, v.line[v.at - 1].to];
    return this.positions().lastMoves[this.ply()];
  });
  readonly atEnd = computed(() => {
    const v = this.variation();
    return v ? v.at === v.line.length : this.ply() === this.lastPly();
  });
  /** Züge der Variante mit Zugnummer: Weiß immer, Schwarz nur am Anfang. */
  readonly variationMoves = computed(() => {
    const v = this.variation();
    if (!v) return [];
    return v.line.map((m, i) => {
      const [, turn, , , , full] = (i ? v.line[i - 1].fen : this.positions().fens[v.base]).split(' ');
      return { san: m.san, number: turn === 'w' ? `${full}.` : i === 0 ? `${full}…` : '' };
    });
  });

  /** Laufende Engine-Sitzung; `null` = aus (dann rechnet nichts im Hintergrund). */
  readonly engine = signal<LiveEngineSession | null>(null);
  readonly engineOn = computed(() => this.engine() !== null);
  readonly slots = Array.from({ length: LIVE_LINES }, (_, i) => i);
  readonly arrows = computed<BoardArrow[]>(() => this.engine()?.arrows() ?? []);

  constructor() {
    // Die Stellung auf dem Brett rechnen — ohne eigene Nebenvariante der Sitzung heißt `sync` genau „rechne diese FEN“.
    effect(() => { const s = this.engine(); const fen = this.currentFen(); if (s && fen) s.sync(0, fen); });
    inject(DestroyRef).onDestroy(() => this.engine()?.destroy());
  }

  ngOnInit(): void {
    const j = this.jump();
    if (j) {
      if ('line' in j) this.showLine(j.line, j.at); else this.showPly(j.ply, j.flipped);
    }
    if (this.engineAtStart()) this.toggleEngine();
  }

  /** Engine an/aus; aus beendet die Sitzung (kein Rechnen im Hintergrund). */
  toggleEngine(): void {
    const s = this.engine();
    if (s) { s.destroy(); this.engine.set(null); } else this.engine.set(this.engineFactory());
  }

  /** Blättern in der Partie verwirft eine Variante. */
  goTo(ply: number): void {
    this.variation.set(null);
    this.ply.set(Math.max(0, Math.min(this.lastPly(), ply)));
  }

  goToVariation(at: number): void {
    this.variation.update(v => v && { ...v, at: Math.max(0, Math.min(v.line.length, at)) });
  }

  /** Zurück zur Partie, an die Abzweigstelle. */
  exitVariation(): void {
    this.variation.set(null);
  }

  toStart(): void { if (this.variation()) this.goToVariation(0); else this.goTo(0); }
  toEnd(): void { const v = this.variation(); if (v) this.goToVariation(v.line.length); else this.goTo(this.lastPly()); }
  forward(): void { const v = this.variation(); if (v) this.goToVariation(v.at + 1); else this.goTo(this.ply() + 1); }
  /** Am Anfang der Variante führt ← zurück in die Partie. */
  back(): void {
    const v = this.variation();
    if (!v) this.goTo(this.ply() - 1);
    else if (v.at > 0) this.goToVariation(v.at - 1);
    else this.exitVariation();
  }

  /** Eigener Zug: der nächste Zug der Linie läuft einfach weiter, jeder andere beginnt bzw. kürzt eine Variante. */
  onUserMove(m: UserBoardMove): void {
    const move: LiveMove = { san: m.san, uci: m.from + m.to + (m.promotion ?? ''), fen: m.fen, from: m.from, to: m.to };
    const v = this.variation();
    if (v) {
      if (v.line[v.at]?.san === m.san) this.goToVariation(v.at + 1);
      else this.variation.set({ ...v, line: [...v.line.slice(0, v.at), move], at: v.at + 1 });
    } else if (this.positions().sans[this.ply()] === m.san) {
      this.goTo(this.ply() + 1);
    } else {
      this.variation.set({ base: this.ply(), line: [move], at: 1 });
    }
  }

  /** Sprung von einem Diagramm. Steht das Brett unter dem Text, holt er es ins Bild. */
  showPly(ply: number, flipped?: boolean): void {
    this.goTo(ply);
    if (flipped !== undefined) this.flipOverride.set(flipped);
    this.reveal();
  }

  /** Sprung von einem Zug im Text: `line` läuft ab Partieanfang; solange sie der Partie folgt, ist es ein Partiezug, sonst Variante ab dem Abzweig. */
  showLine(line: LiveMove[], at: number): void {
    const { sans } = this.positions();
    let base = 0;
    while (base < line.length && base < sans.length && line[base].san === sans[base]) base++;
    if (at <= base) {
      this.goTo(at);
    } else {
      this.ply.set(base);
      this.variation.set({ base, line: line.slice(base), at: at - base });
    }
    this.reveal();
  }

  private reveal(): void {
    if (!this.inSheet() && window.matchMedia('(max-width: 899.98px)').matches) this.host.nativeElement.scrollIntoView({ block: 'nearest' });
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if ((event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') || !isBoardHotkey(event, this.inSheet() ? this.host.nativeElement : null)) return;
    event.preventDefault();
    if (event.key === 'ArrowLeft') this.back(); else this.forward();
  }
}
