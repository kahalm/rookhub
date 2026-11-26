import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ChessBoardComponent } from '@rh/shared/pgn-viewer/chess-board.component';
import { PreferencesService } from '@rh/core/preferences.service';
import { formatSecondsClock } from '@rh/shared/clock-format.util';
import { chessTitle } from '../tournament-table.util';
import { LiveGame, LivePlayer } from './live-boards.models';
import { evalText, lastMovePair, shownClock, whiteShare } from './live-boards.util';

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
        @if (game().finished) {
          <span class="lb-result">{{ resultText() }}</span>
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
      <div class="lb-boardrow">
        @if (showEval()) {
          <div class="lb-evalbar" [class.empty]="share() === null" [class.flipped]="flipped()" role="img"
               [attr.aria-label]="share() === null ? ('tournaments.live.noEval' | translate) : ('tournaments.live.evalLabel' | translate:{ value: evalLabel() })">
            <div class="lb-evalwhite" [style.height.%]="share() ?? 0"></div>
          </div>
        }
        <div class="lb-cg">
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
        <div class="lb-evaltext" [title]="('tournaments.live.evalSource.' + game().eval!.source) | translate">
          {{ evalLabel() }}@if (game().eval!.depth; as d) { · {{ 'tournaments.live.depth' | translate:{ depth: d } }} }
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
    /* Klein im Kopf der Karte, Tippziel bleibt 40 px (die Karte ist kein Formular) */
    .lb-icon { flex: 0 0 auto; width: 40px; height: 40px; padding: 8px; margin: -8px -4px -8px 0; }
    .lb-icon mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .lb-result { font-weight: 700; color: var(--mat-sys-on-surface); padding: 0 8px; border-radius: 999px;
      border: 1px solid var(--mat-sys-outline); font-variant-numeric: tabular-nums; }
    .lb-player { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; min-width: 0; }
    .lb-name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .lb-title { font-weight: 700; font-size: 0.85em; color: var(--rh-accent); margin-right: 0.25em; }
    .lb-elo { margin-left: 0.4em; color: var(--mat-sys-on-surface-variant); font-size: 0.9em; font-variant-numeric: tabular-nums; }
    .lb-clock { flex: 0 0 auto; font-variant-numeric: tabular-nums; padding: 0 6px; border-radius: 4px;
      border: 1px solid transparent; }
    .to-move .lb-clock { font-weight: 700; border-color: var(--rh-accent); }
    .lb-boardrow { display: flex; gap: 4px; align-items: stretch; }
    .lb-cg { flex: 1 1 auto; min-width: 0; }
    .lb-evalbar { flex: 0 0 10px; position: relative; display: flex; flex-direction: column-reverse;
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
  readonly share = computed(() => whiteShare(this.game().eval));
  readonly evalLabel = computed(() => evalText(this.game().eval));
  readonly resultText = computed(() => this.game().result === '1/2-1/2' ? '½–½' : this.game().result.replace('-', '–'));

  title(p: LivePlayer): string | null { return chessTitle(p.title); }

  /** Die Seite (Weiß = true) ist am Zug — und die Partie läuft. */
  toMove(white: boolean): boolean { return !this.game().finished && this.game().whiteToMove === white; }

  clock(white: boolean): string | null {
    const s = shownClock(this.game(), white, this.receivedAt(), this.now() || this.receivedAt());
    return s === null ? null : formatSecondsClock(s);
  }
}
