import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, input, linkedSignal, output, signal,
  viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { PreferencesService } from '@rh/core/preferences.service';
import { formatSecondsClock } from '@rh/shared/clock-format.util';
import { chessTitle } from '../tournament-table.util';
import { LiveEval, LiveGame, LivePlayer } from './live-boards.models';
import { evalText, lastMovePair, liveMoveNumber, shownClock, whiteShare } from './live-boards.util';

/**
 * Ein Brett der Live-Ansicht: oben die Gegenseite, unten die eigene Seite (Name, Titel, Elo, Uhr), dazwischen das
 * geteilte Brett (`app-chess-board`, nur Anzeige — ein Klick tut vorerst nichts) mit markiertem letzten Zug und
 * optional dem Engine-Balken links. Am Zug: Uhr fett mit Akzentrahmen; fertig: Ergebnis als Chip.
 */
@Component({
  selector: 'app-live-board-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChessBoardComponent, TranslatePipe, MatButtonModule, MatIconModule],
  template: `
    <article class="lb-card" [attr.aria-label]="('tournaments.live.boardN' | translate:{ board: board() }) + ': ' + game().white.name + ' – ' + game().black.name">
      <div class="lb-top">
        <span class="lb-board">{{ 'tournaments.live.boardN' | translate:{ board: board() } }}@if (caption(); as c) { <span class="lb-caption">· {{ c }}</span> }</span>
        @if (!game().finished && moveNo(); as n) {
          <!-- Zuganzahl (Wunsch 2026-11-26): laufend der Zug, der gerade gespielt wird, fertig der letzte -->
          <span class="lb-move" [title]="(game().finished ? 'tournaments.live.moveTipEnd' : (game().whiteToMove ? 'common.whiteToMove' : 'common.blackToMove')) | translate">{{ 'tournaments.live.moveN' | translate:{ move: n } }}</span>
        }
        @if (game().finished) {
          <!-- Fertig: nur das Ergebnis, die Zugzahl steht im Tooltip (schmale Karte, 26.11.: „½–½“ brach in zwei Zeilen um) -->
          <span class="lb-result" [title]="moveNo() ? ('tournaments.live.moveN' | translate:{ move: moveNo() }) : null">{{ resultText() }}</span>
        }
        @if (analysisUrl(); as url) {
          <!-- Partie samt Zügen, Kopfzeilen und Uhren auf RookHubs Analysebrett, neuer Tab (1.026.0) -->
          <a mat-icon-button class="lb-icon lb-analyse" [href]="url" target="_blank" rel="noopener"
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
      </div>
      <div class="lb-player" [class.to-move]="toMove(flipped())">
        <span class="lb-name">
          @if (title(topPlayer()); as t) { <span class="lb-title">{{ t }}</span> }{{ topPlayer().name }}
          @if (topPlayer().elo) { <span class="lb-elo">{{ topPlayer().elo }}</span> }
        </span>
        @if (clock(flipped()); as c) { <span class="lb-clock" role="timer">{{ c }}</span> }
      </div>
      <div class="lb-boardrow" [class.measured]="boardBox() !== null">
        @if (showEval()) {
          <div class="lb-evalbar" [class.empty]="share() === null" [class.flipped]="flipped()" role="img"
               [style.height.px]="boardBox()?.height" [style.margin-top.px]="boardBox()?.top"
               [attr.aria-label]="share() === null ? ('tournaments.live.noEval' | translate) : ('tournaments.live.evalLabel' | translate:{ value: evalLabel() })">
            <div class="lb-evalwhite" [style.height.%]="share() ?? 0"></div>
          </div>
        }
        <div class="lb-cg" #cg>
          <app-chess-board [fen]="game().fen" [lastMove]="lastMove()" [flipped]="flipped()"
                           [boardTheme]="prefs.boardTheme" [pieceSet]="prefs.pieceSet" />
        </div>
      </div>
      <div class="lb-player" [class.to-move]="toMove(!flipped())">
        <span class="lb-name">
          @if (title(bottomPlayer()); as t) { <span class="lb-title">{{ t }}</span> }{{ bottomPlayer().name }}
          @if (bottomPlayer().elo) { <span class="lb-elo">{{ bottomPlayer().elo }}</span> }
        </span>
        @if (clock(!flipped()); as c) { <span class="lb-clock" role="timer">{{ c }}</span> }
      </div>
      @if (showEval() && evalLabel()) {
        <!-- Quelle dezent (1.015.0): Tooltip „Übertragung / Lichess-Cloud / RookHub-Engine“, Tiefe in der Zeile. -->
        <div class="lb-evaltext" [title]="('tournaments.live.evalSource.' + shownEval()!.source) | translate">
          {{ evalLabel() }}@if (shownEval()!.depth; as d) { · {{ 'tournaments.live.depth' | translate:{ depth: d } }} }
        </div>
      }
    </article>
  `,
  styles: [`
    :host { display: block; min-width: 0; }
    .lb-card {
      border: 1px solid color-mix(in srgb, currentColor 14%, transparent); border-radius: 8px; padding: 8px;
      display: flex; flex-direction: column; gap: 4px; background: var(--mat-sys-surface-container-low);
    }
    .lb-top { display: flex; align-items: center; justify-content: space-between; gap: 8px; font-size: 0.85rem;
      color: var(--mat-sys-on-surface-variant); }
    .lb-board { font-weight: 500; min-width: 0; flex: 1 1 auto; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-caption { font-weight: 400; }
    .lb-move { flex: 0 0 auto; font-variant-numeric: tabular-nums; white-space: nowrap; }
    /* Klein im Kopf der Karte, Tippziel bleibt 40 px (die Karte ist kein Formular) */
    .lb-icon { flex: 0 0 auto; width: 40px; height: 40px; padding: 8px; margin: -8px -4px -8px 0; }
    .lb-icon mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .lb-result { flex: 0 0 auto; white-space: nowrap; line-height: 1.5; font-weight: 700; color: var(--mat-sys-on-surface);
      padding: 0 8px; border-radius: 999px; border: 1px solid var(--mat-sys-outline); font-variant-numeric: tabular-nums; }
    .lb-player { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; min-width: 0; }
    .lb-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-title { font-weight: 700; font-size: 0.85em; color: var(--rh-accent); margin-right: 0.25em; }
    .lb-elo { margin-left: 0.4em; color: var(--mat-sys-on-surface-variant); font-size: 0.9em; font-variant-numeric: tabular-nums; }
    .lb-clock { flex: 0 0 auto; font-variant-numeric: tabular-nums; padding: 0 6px; border-radius: 4px;
      border: 1px solid transparent; }
    .to-move .lb-clock { font-weight: 700; border-color: var(--rh-accent); }
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
    .lb-evaltext { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
  `],
})
export class LiveBoardCardComponent {
  readonly prefs = inject(PreferencesService);
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
  readonly remove = output<void>();

  readonly topPlayer = computed<LivePlayer>(() => this.flipped() ? this.game().white : this.game().black);
  readonly bottomPlayer = computed<LivePlayer>(() => this.flipped() ? this.game().black : this.game().white);
  readonly lastMove = computed(() => lastMovePair(this.game().lastMove));
  /** Der Balken hält nach einem Zug den letzten Wert, bis für die neue Stellung einer da ist (Wunsch 2026-11-26: kein
   *  Leerlauf/Ausschlag direkt nach dem Zug). Eine neue Partie auf der Karte beginnt leer. */
  readonly shownEval = linkedSignal<{ id: string; eval: LiveEval | null }, LiveEval | null>({
    source: () => ({ id: this.game().id, eval: this.game().eval }),
    computation: (src, prev) => src.eval ?? (prev && prev.source.id === src.id ? prev.value : null),
  });
  readonly share = computed(() => whiteShare(this.shownEval()));
  readonly evalLabel = computed(() => evalText(this.shownEval()));
  /** Zugnummer aus den Halbzügen: läuft die Partie, der Zug am Brett (`plies/2 + 1`), fertig der letzte gespielte; vor dem ersten Zug nichts. */
  readonly moveNo = computed(() => liveMoveNumber(this.game()));
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
