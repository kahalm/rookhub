import { Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';
import { PreferencesService } from '../../core/preferences.service';
import { GameAnalysis } from '../analysis/game-analysis.service';
import { LibraryGame, LibraryService } from './library.service';
import { OpeningMove, OpeningTreeService } from './opening-tree.service';

/**
 * „Nach Stellung filtern": das Brett in der Grundstellung, daneben der Eröffnungsbaum. Man klickt
 * sich eine Variante entlang und sieht die Partien, die dort hindurchgehen.
 *
 * <p>Die Namenssuche daneben beantwortet „ich weiß, wie die Partie heißt". Diese hier beantwortet
 * die andere Frage, die man vor dem Üben stellt: „was gibt es zu meiner Eröffnung?" — und das ist
 * keine Textsuche.</p>
 *
 * <p><b>Zwei Quellen, ein Baum.</b> „Nur gerechnete" zeigt, was sofort spielbar ist; „alle" zählt
 * den ganzen Rohbestand mit, und von dort lässt sich eine Partie direkt anfordern. Der Umschalter
 * ist der Punkt: ohne ihn führt der Baum entweder in eine fast leere Auswahl oder auf Partien, die
 * man nicht spielen kann.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-position-filter-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule,
    MatIconModule, TranslatePipe, ChessBoardComponent, LoadingSpinnerComponent],
  template: `
    <h2 mat-dialog-title>{{ 'guess.tree.title' | translate }}</h2>

    <mat-dialog-content class="pf">
      <div class="top">
        <div class="board">
          <app-chess-board [fen]="fen" [lastMove]="lastMove"
                           [boardTheme]="prefs.boardTheme" [pieceSet]="prefs.pieceSet" />
          <div class="nav">
            <button mat-icon-button (click)="back()" [disabled]="!plies.length"
                    [attr.title]="'guess.prevMove' | translate"><mat-icon>undo</mat-icon></button>
            <button mat-icon-button (click)="reset()" [disabled]="!plies.length"
                    [attr.title]="'guess.startPosition' | translate"><mat-icon>first_page</mat-icon></button>
            <span class="line">{{ lineText || ('guess.tree.startPosition' | translate) }}</span>
          </div>
        </div>

        <div class="moves">
          <mat-button-toggle-group [(ngModel)]="onlyPlayable" (change)="load()" class="scope">
            <mat-button-toggle [value]="true">{{ 'guess.tree.onlyPlayable' | translate }}</mat-button-toggle>
            <mat-button-toggle [value]="false">{{ 'guess.tree.all' | translate }}</mat-button-toggle>
          </mat-button-toggle-group>

          @if (loadingTree) {
            <app-loading-spinner />
          } @else {
            <p class="muted small">{{ 'guess.tree.total' | translate:{ total } }}</p>
            @if (!moves.length) {
              <p class="muted small">{{ 'guess.tree.noMoves' | translate }}</p>
            }
            <div class="mlist">
              @for (m of moves; track m.san) {
                <button type="button" class="mv" (click)="play(m)">
                  <span class="san">{{ m.san }}</span>
                  <span class="cnt">{{ m.games }}</span>
                  <span class="bar"><i [style.width.%]="share(m)"></i></span>
                </button>
              }
            </div>
          }
        </div>
      </div>

      <!-- Die Partien zur Stellung: spielen, oder aus dem Rohbestand anfordern. -->
      @if (loadingGames) {
        <app-loading-spinner />
      } @else if (onlyPlayable) {
        @for (g of playable; track g.id) {
          <div class="row">
            <span class="who">{{ g.title }}</span>
            <span class="spacer"></span>
            <button mat-stroked-button (click)="choose(g.id)">
              <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
            </button>
          </div>
        }
        @if (!playable.length) { <p class="muted small">{{ 'guess.tree.nonePlayable' | translate }}</p> }
      } @else {
        @for (g of library; track g.id) {
          <div class="row">
            <span class="who">{{ names(g) }}<span class="muted small"> · {{ meta(g) }}</span></span>
            <span class="spacer"></span>
            @if (g.gameAnalysisId) {
              <button mat-stroked-button (click)="choose(g.gameAnalysisId!)">
                <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
              </button>
            } @else {
              <button mat-flat-button color="primary" [disabled]="busy === g.id" (click)="request(g)">
                <mat-icon>hourglass_top</mat-icon> {{ 'guess.library.request' | translate }}
              </button>
            }
          </div>
        }
        @if (!library.length) { <p class="muted small">{{ 'guess.library.none' | translate }}</p> }
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .pf { min-width: min(820px, 88vw); }
    .top { display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 10px; }
    .board { flex: 0 1 320px; min-width: 240px; }
    .nav { display: flex; align-items: center; gap: 4px; margin-top: 4px; }
    .line { font-family: monospace; font-size: .8rem; opacity: .75; overflow-wrap: anywhere; }
    .moves { flex: 1 1 300px; min-width: 240px; display: flex; flex-direction: column; }
    .scope { margin-bottom: 8px; }
    .mlist { display: flex; flex-direction: column; gap: 2px; max-height: 330px; overflow-y: auto; }
    .mv { display: flex; align-items: center; gap: 8px; border: none; background: none; cursor: pointer;
          font: inherit; color: inherit; padding: 4px 6px; border-radius: 4px; text-align: left; }
    .mv:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .san { font-weight: 600; min-width: 56px; }
    .cnt { min-width: 48px; font-variant-numeric: tabular-nums; opacity: .7; font-size: .85rem; }
    .bar { flex: 1 1 auto; height: 6px; border-radius: 3px;
           background: color-mix(in srgb, currentColor 12%, transparent); overflow: hidden; }
    .bar i { display: block; height: 100%; background: color-mix(in srgb, currentColor 45%, transparent); }
    .row { display: flex; align-items: center; gap: 10px; padding: 6px 0; flex-wrap: wrap; }
    .row + .row { border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .who { min-width: 0; }
    .spacer { flex: 1 1 auto; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class PositionFilterDialogComponent implements OnInit {
  private tree = inject(OpeningTreeService);
  private library_ = inject(LibraryService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  private ref = inject(MatDialogRef<PositionFilterDialogComponent>);
  readonly prefs = inject(PreferencesService);

  /** So viele Partien holt die Liste unter dem Baum. Mehr liest ohnehin niemand. */
  private static readonly PageSize = 50;

  /** Das Brett führt der Dialog selbst mit — der Baum liefert nur Zugnamen. */
  private board = new Chess();
  plies: string[] = [];
  fen = this.board.fen();
  lastMove?: [string, string];

  onlyPlayable = true;
  moves: OpeningMove[] = [];
  total = 0;
  playable: GameAnalysis[] = [];
  library: LibraryGame[] = [];
  loadingTree = true;
  loadingGames = true;
  busy: number | null = null;

  get lineText(): string { return this.plies.join(' '); }

  ngOnInit(): void { this.load(); }

  /** Anteil an den Partien dieser Stellung — der Balken macht die Verteilung lesbar. */
  share(m: OpeningMove): number {
    const most = this.moves[0]?.games ?? 0;
    return most > 0 ? Math.max(4, Math.round(m.games * 100 / most)) : 0;
  }

  play(m: OpeningMove): void {
    // Der Zug muss auf dem Brett auch gehen — der Baum kennt nur Zeichenketten, und eine Partie
    // mit abweichender Ausgangsstellung koennte eine unmoegliche Fortsetzung liefern.
    let zug;
    try { zug = this.board.move(m.san); } catch { zug = null; }
    if (!zug) { this.snackbar.warn(this.translate.instant('guess.tree.moveFailed')); return; }
    this.plies.push(m.san);
    this.after(zug.from, zug.to);
  }

  back(): void {
    if (!this.plies.length) return;
    this.board.undo();
    this.plies.pop();
    const letzter = this.board.history({ verbose: true }).at(-1);
    this.after(letzter?.from, letzter?.to);
  }

  reset(): void {
    this.board = new Chess();
    this.plies = [];
    this.after();
  }

  private after(from?: string, to?: string): void {
    this.fen = this.board.fen();
    this.lastMove = from && to ? [from, to] : undefined;
    this.load();
  }

  load(): void {
    const line = this.lineText;
    this.loadingTree = true;
    this.loadingGames = true;
    this.cdr.markForCheck();

    this.tree.branch(line, this.onlyPlayable).subscribe({
      next: t => {
        this.moves = t.moves;
        this.total = t.total;
        this.loadingTree = false;
        this.cdr.markForCheck();
      },
      error: () => { this.loadingTree = false; this.cdr.markForCheck(); },
    });

    if (this.onlyPlayable) {
      this.tree.playable(line).subscribe({
        next: g => { this.playable = g; this.loadingGames = false; this.cdr.markForCheck(); },
        error: () => { this.loadingGames = false; this.cdr.markForCheck(); },
      });
    } else {
      this.tree.library(line, 1, PositionFilterDialogComponent.PageSize).subscribe({
        next: p => { this.library = p.items; this.loadingGames = false; this.cdr.markForCheck(); },
        error: () => { this.loadingGames = false; this.cdr.markForCheck(); },
      });
    }
  }

  request(g: LibraryGame): void {
    if (this.busy) return;
    this.busy = g.id;
    this.library_.request(g.id).subscribe({
      next: r => {
        this.busy = null;
        g.gameAnalysisId = r.analysis.id;
        this.snackbar.success(this.translate.instant(
          r.alreadyPlayable ? 'guess.library.alreadyThere' : 'guess.library.queued'));
        this.cdr.markForCheck();
      },
      error: err => {
        this.busy = null;
        const grund = err?.error?.reason;
        this.snackbar.warn(grund
          ? this.translate.instant('guess.upload.reason.' + grund)
          : this.translate.instant('guess.library.requestFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  /** Der Aufrufer bekommt die Analyse-Id und startet die Partie — der Dialog kennt keine Route. */
  choose(id: number): void { this.ref.close(id); }

  names(g: LibraryGame): string {
    const beide = [g.white, g.black].filter(Boolean).join(' – ');
    return beide || g.event || this.translate.instant('guess.untitled');
  }

  meta(g: LibraryGame): string {
    const teile: string[] = [];
    if (g.event) teile.push(g.event);
    if (g.playedOn) teile.push(g.playedOn.slice(0, 4));
    if (g.annotator) teile.push(g.annotator);
    return teile.join(' · ');
  }
}
