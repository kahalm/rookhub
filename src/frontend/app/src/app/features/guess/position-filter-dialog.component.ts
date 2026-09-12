import { Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { SnackbarService } from '../../core/snackbar.service';
import { PreferencesService } from '../../core/preferences.service';
import { AuthService } from '../../core/auth.service';
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
    MatIconModule, MatProgressBarModule, RouterLink, TranslatePipe, ChessBoardComponent],
  template: `
    <h2 mat-dialog-title>{{ 'guess.tree.title' | translate }}</h2>
    <!-- Ein duenner Balken statt zweier Spinner: bis 0.475.2 setzte jeder Klick beide Listen auf
         einen Spinner, der Dialog fiel auf halbe Hoehe zusammen und das Brett sprang in der
         Groesse — bei JEDEM Zug. Der alte Stand bleibt jetzt stehen und wird nur abgeblendet. -->
    <mat-progress-bar mode="indeterminate" [class.hidden]="!busyAny()" />

    <mat-dialog-content class="pf">
      <!-- Der Umschalter steht ueber der ganzen Breite, nicht in der Zugspalte: neben einem Brett
           passen seine zwei Schalter auf einem 400-px-Geraet nicht daneben, und sie koennen nicht
           schrumpfen — gemessen ragte er 42 px ueber den Dialogrand hinaus. -->
      <mat-button-toggle-group [(ngModel)]="onlyPlayable" (change)="scopeChanged()" class="scope">
        <mat-button-toggle [value]="true">{{ 'guess.tree.onlyPlayable' | translate }}</mat-button-toggle>
        <mat-button-toggle [value]="false">{{ 'guess.tree.all' | translate }}</mat-button-toggle>
      </mat-button-toggle-group>

      @if (!loggedIn) {
        <p class="muted small hint">{{ 'guess.tree.anonHint' | translate }}</p>
      }

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
          <div class="fade" [class.busy]="loadingTree">
            <p class="muted small">{{ 'guess.tree.total' | translate:{ total } }}</p>
            @if (!moves.length && !loadingTree) {
              <p class="muted small">{{ 'guess.tree.noMoves' | translate }}</p>
            }
            <div class="mlist">
              @for (m of moves; track m.san) {
                <button type="button" class="mv" (click)="play(m)" [disabled]="loadingTree">
                  <span class="san">{{ m.san }}</span>
                  <span class="cnt">{{ m.games | number }}</span>
                  <span class="bar"><i [style.width.%]="share(m)"></i></span>
                </button>
              }
            </div>
          </div>
        </div>
      </div>

      <!-- Die Partien zur Stellung: spielen, oder aus dem Rohbestand anfordern. -->
      <div class="games fade" [class.busy]="loadingGames">
        <div class="ghead">
          <h3>{{ 'guess.tree.games' | translate:{ count: gameCount } }}</h3>
          @if (!onlyPlayable && pages > 1) {
            <span class="spacer"></span>
            <button mat-icon-button [disabled]="page <= 1 || loadingGames" (click)="turn(-1)"
                    [attr.title]="'common.previous' | translate"><mat-icon>chevron_left</mat-icon></button>
            <span class="muted small pg">{{ 'guess.library.page' | translate:{ page, pages } }}</span>
            <button mat-icon-button [disabled]="page >= pages || loadingGames" (click)="turn(1)"
                    [attr.title]="'common.next' | translate"><mat-icon>chevron_right</mat-icon></button>
          }
        </div>

        @if (onlyPlayable) {
          @for (g of playable; track g.id) {
            <div class="row">
              <span class="who">{{ g.title }}</span>
              <span class="spacer"></span>
              <button mat-stroked-button (click)="choose(g.id)">
                <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
              </button>
            </div>
          }
          @if (!playable.length && !loadingGames) { <p class="muted small">{{ 'guess.tree.nonePlayable' | translate }}</p> }
        } @else {
          @for (g of library; track g.id) {
            <div class="row">
              <span class="who">{{ names(g) }}<span class="muted small"> · {{ meta(g) }}</span></span>
              <span class="spacer"></span>
              @if (g.gameAnalysisId) {
                <button mat-stroked-button (click)="choose(g.gameAnalysisId!)">
                  <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
                </button>
              } @else if (loggedIn) {
                <!-- Bewusst UMRANDET und nicht gefuellt: fuenfzig gefuellte Knoepfe untereinander
                     behaupten fuenfzig Hauptaktionen (UI-Dichte-Regel, CLAUDE.md). -->
                <button mat-stroked-button [disabled]="busy === g.id" (click)="request(g)">
                  <mat-icon>hourglass_top</mat-icon> {{ 'guess.library.request' | translate }}
                </button>
              } @else {
                <!-- Suchen darf jeder, rechnen lassen nicht: das verbraucht Rechenzeit, die
                     jemandem gehoert. An der Stelle des Knopfes steht deshalb der Grund. -->
                <a mat-button routerLink="/login" [queryParams]="{ returnUrl: '/guess' }"
                   (click)="ref.close()">
                  <mat-icon>lock_open</mat-icon> {{ 'guess.library.requestNeedsLogin' | translate }}
                </a>
              }
            </div>
          }
          @if (!library.length && !loadingGames) { <p class="muted small">{{ 'guess.library.none' | translate }}</p> }
        }
      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .pf { min-width: min(820px, 86vw); }
    mat-progress-bar.hidden { visibility: hidden; }
    .top { display: flex; gap: 16px; align-items: flex-start; margin-bottom: 10px; }
    /* FESTE Breite statt flex-basis mit Schrumpfen: das Brett misst seine Groesse EINMAL beim
       Aufbau, und eine Spalte, die waehrend des Dialog-Aufbaus noch wandert, liess es mit 256 px
       in einem 320-px-Kasten stehen — der Vollbild-Knopf sass dann 64 px neben dem Brett und
       darunter klaffte dieselbe Luecke. */
    .board { flex: 0 0 300px; }
    .nav { display: flex; align-items: center; gap: 4px; margin-top: 4px; }
    .line { font-family: monospace; font-size: .8rem; opacity: .75; overflow-wrap: anywhere; }
    .moves { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column; }
    /* Nur so breit wie seine zwei Schalter — ueber die volle Breite sah der Umschalter aus wie
       ein Eingabefeld mit einem leeren dritten Feld dahinter. */
    .scope { margin-bottom: 10px; }
    .mlist { display: flex; flex-direction: column; gap: 2px; max-height: 46vh; overflow-y: auto; }
    .mv { display: flex; align-items: center; gap: 8px; border: none; background: none; cursor: pointer;
          font: inherit; color: inherit; padding: 4px 6px; border-radius: 4px; text-align: left; }
    .mv:hover:not(:disabled) { background: color-mix(in srgb, currentColor 10%, transparent); }
    .mv:disabled { cursor: default; }
    .san { font-weight: 600; min-width: 56px; }
    .cnt { min-width: 56px; font-variant-numeric: tabular-nums; opacity: .7; font-size: .85rem; }
    .bar { flex: 1 1 auto; height: 8px; border-radius: 4px;
           background: color-mix(in srgb, currentColor 14%, transparent); overflow: hidden; }
    .bar i { display: block; height: 100%; background: currentColor; opacity: .55; }
    /* Nachladen ohne Umbau: der alte Stand bleibt stehen, wird nur blasser und unklickbar. */
    .fade { transition: opacity .15s ease; }
    .fade.busy { opacity: .45; pointer-events: none; }
    /* Umbrechen duerfen: Ueberschrift, Zaehler und Blaetterknoepfe passen auf einem 400-px-Geraet
       nicht in eine Zeile — ohne den Umbruch schob die Kopfzeile den ganzen Dialoginhalt
       43 px ueber den Rand (gemessen: scrollWidth 427 gegen clientWidth 384). */
    .ghead { display: flex; align-items: center; gap: 4px; margin: 4px 0 2px; flex-wrap: wrap; }
    .ghead .pg { white-space: nowrap; }
    .ghead h3 { margin: 0; font-size: .95rem; font-weight: 600; }
    .hint { margin: 0 0 8px; }
    .row { display: flex; align-items: center; gap: 10px; padding: 6px 0; flex-wrap: wrap; }
    .row + .row { border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .who { min-width: 0; }
    .spacer { flex: 1 1 auto; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
    /* Handy: das Brett ist die Beigabe, der Baum der Inhalt. Mit 300 px Brett blieben auf einem
       400-px-Geraet genau zwei Zuege sichtbar. */
    @media (max-width: 620px) {
      .pf { min-width: 0; }
      .board { flex: 0 0 42%; }
      .mlist { max-height: 38vh; }
      .san { min-width: 44px; }
      .cnt { min-width: 46px; }
    }
  `],
})
export class PositionFilterDialogComponent implements OnInit {
  private tree = inject(OpeningTreeService);
  private library_ = inject(LibraryService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  readonly ref = inject(MatDialogRef<PositionFilterDialogComponent>);
  private auth = inject(AuthService);
  readonly prefs = inject(PreferencesService);

  /** So viele Partien je Seite unter dem Baum. Der Rest ist ueber das Blaettern erreichbar —
   * frueher holte die Liste 50 Zeilen ohne jede Anzeige, wie viele es insgesamt sind. */
  private static readonly PageSize = 25;

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
  /** Wie viele Partien der Rohbestand zu dieser Stellung hat — die Grundlage des Blaetterns. */
  libraryTotal = 0;
  page = 1;
  loadingTree = true;
  loadingGames = true;
  busy: number | null = null;

  /** Ohne Konto: suchen ja, anfordern nein — der Dialog sagt es an beiden Stellen. */
  readonly loggedIn = this.auth.isLoggedIn;

  get lineText(): string { return this.plies.join(' '); }
  /** Der Balken oben laeuft, solange irgendetwas nachlaedt — die Listen bleiben derweil stehen. */
  busyAny(): boolean { return this.loadingTree || this.loadingGames; }
  get gameCount(): number { return this.onlyPlayable ? this.playable.length : this.libraryTotal; }
  get pages(): number {
    return Math.max(1, Math.ceil(this.libraryTotal / PositionFilterDialogComponent.PageSize));
  }

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

  /** Quelle gewechselt — die Seite faengt wieder vorn an, sonst stuende „Seite 4 von 1". */
  scopeChanged(): void { this.page = 1; this.load(); }

  turn(schritt: number): void {
    const ziel = this.page + schritt;
    if (ziel < 1 || ziel > this.pages) return;
    this.page = ziel;
    this.loadGames();
  }

  private after(from?: string, to?: string): void {
    this.fen = this.board.fen();
    this.lastMove = from && to ? [from, to] : undefined;
    this.page = 1;   // andere Stellung, andere Partien
    this.load();
  }

  load(): void {
    const line = this.lineText;
    this.loadingTree = true;
    this.cdr.markForCheck();

    this.tree.branch(line, this.onlyPlayable).subscribe({
      next: t => {
        // Nur uebernehmen, was zur JETZT angesehenen Stellung gehoert: wer schnell klickt, hat
        // zwei Abfragen unterwegs, und die aeltere darf die neuere nicht ueberschreiben.
        if (t.line !== this.lineText || t.onlyPlayable !== this.onlyPlayable) return;
        this.moves = t.moves;
        this.total = t.total;
        this.loadingTree = false;
        this.cdr.markForCheck();
      },
      error: () => { this.loadingTree = false; this.cdr.markForCheck(); },
    });

    this.loadGames();
  }

  private loadGames(): void {
    const line = this.lineText;
    const scope = this.onlyPlayable;
    this.loadingGames = true;
    this.cdr.markForCheck();

    if (scope) {
      this.tree.playable(line).subscribe({
        next: g => {
          if (line !== this.lineText || scope !== this.onlyPlayable) return;
          this.playable = g; this.loadingGames = false; this.cdr.markForCheck();
        },
        error: () => { this.loadingGames = false; this.cdr.markForCheck(); },
      });
      return;
    }

    this.tree.library(line, this.page, PositionFilterDialogComponent.PageSize).subscribe({
      next: p => {
        if (line !== this.lineText || scope !== this.onlyPlayable) return;
        this.library = p.items; this.libraryTotal = p.total;
        this.loadingGames = false; this.cdr.markForCheck();
      },
      error: () => { this.loadingGames = false; this.cdr.markForCheck(); },
    });
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
