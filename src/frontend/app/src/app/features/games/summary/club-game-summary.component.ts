import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, forkJoin } from 'rxjs';
import { ChessBoardComponent, BoardArrow } from '../../../shared/pgn-viewer/chess-board.component';
import { LoadErrorComponent } from '../../../shared/load-error/load-error.component';
import { PreferencesService } from '../../../core/preferences.service';
import { GamesService, SharedGame } from '../games.service';
import { AuthService } from '../../../core/auth.service';
import { MenuService } from '../../../core/menu.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { MaximNoteComponent } from '../../../shared/maxims/maxim-note.component';
import { ClubGameSummary, ClubGameSummaryService, EbookSuggestionInfo, SummaryExerciseGroup, SummarySource } from './club-game-summary.service';
import { SummaryExercisesComponent } from './summary-exercises.component';
import { ClubSummaryLinkComponent } from './club-summary-link.component';
import { SummaryGameBoardComponent, SummaryJump } from './summary-game-board.component';
import { SummaryBoardSheetComponent } from './summary-board-sheet.component';
import { SanPipe } from '../../../shared/chess/san.pipe';
import { GameTextPart, mergeGameSection } from './summary-game-text.util';
import { DiagramSpec, InlinePart, SummaryBlock, gameMovePairs, moveLabel, linkSummaryMoves, parseSummary, positionsOf, visibleSummaryBlocks, withGameBlock } from './summary-markdown.util';

/** Ein fertig aufgelöstes Diagramm: Stellung, letzter Zug, Pfeile, Blickrichtung. */
interface ResolvedDiagram {
  fen: string;
  lastMove?: [string, string];
  arrows: BoardArrow[];
  flipped: boolean;
  label: string;
  caption?: InlinePart[];
}

/**
 * Zusammenfassung einer Vereinspartie als eigene Seite (0.913.0, `/club-games/:id/summary`). Text von Claude (Markdown),
 * Diagramme der Schlüsselstellungen aus ```diagram-Blöcken: `ply` = Stellung nach so vielen Halbzügen DIESER Partie
 * (die FEN rechnet die Seite selbst — kein erfundenes Brett), `fen` für eine Stellung aus einer Variante.
 */
@Component({
  selector: 'app-club-game-summary',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, NgTemplateOutlet, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe,
    ChessBoardComponent, LoadErrorComponent, ClubSummaryLinkComponent, SummaryGameBoardComponent, MaximNoteComponent, SanPipe,
    SummaryExercisesComponent],
  template: `
    <div class="summary-page" [class.with-board]="hasBoard()">
      @if (loading()) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (notFound()) {
        <!-- Ungültiger oder gelöschter Link (404): kein „Wiederholen“ — das brächte dasselbe Ergebnis. Wie die
             Partieseite selbst (shared-game, a1-rk-vereinspartie-fehler-sackgasse); andere Fehler bleiben unten. -->
        <!-- UI-Sweep 2026-11-10 (d10-rk-zusammenfassung-ladefehler-sackgasse): 403 = Sperrkarte mit Grund und Weg zur Startseite
             statt „Wiederholen“, das bei fehlendem Vereinszugang nie hilft (DESIGN 2 Grundsatz 10, 5.11). -->
        <mat-card class="empty-state not-found" [class.gate]="locked()">
          <mat-icon>{{ locked() ? 'lock' : 'link_off' }}</mat-icon>
          <p>{{ notFoundKey() | translate }}</p>
          <div class="gate-actions">
            @if (locked()) {
              <a mat-flat-button color="primary" routerLink="/" class="to-home">{{ 'app.notFound.home' | translate }}</a>
            } @else if (own) {
              <a mat-stroked-button routerLink="/games" class="to-games">{{ 'games.toMyGames' | translate }}</a>
            } @else {
              <a mat-stroked-button routerLink="/" class="to-home">{{ 'app.notFound.home' | translate }}</a>
            }
          </div>
        </mat-card>
      } @else if (failed()) {
        <mat-card class="load-card"><app-load-error [messageKey]="'games.loadError'" (retry)="load()" /></mat-card>
      } @else if (game(); as g) {
        <div class="summary-layout">
        <mat-card class="summary-card">
          <div class="head">
            <a mat-icon-button [routerLink]="gameLink()" class="back" [attr.aria-label]="'common.back' | translate">
              <mat-icon>arrow_back</mat-icon>
            </a>
            <div class="head-main">
              <span class="players"><strong>{{ g.white || '?' }}</strong> – <strong>{{ g.black || '?' }}</strong>
                @if (g.result && g.result !== '*') { <span class="result">{{ g.result }}</span> }</span>
              <span class="meta">{{ 'games.summary.title' | translate }}
                @if (summary()?.doneAt) { · {{ summary()!.doneAt | date:'mediumDate' }} }</span>
            </div>
            <div class="head-actions">
              <a mat-stroked-button [routerLink]="gameLink()" class="to-game">
                <mat-icon>sports_esports</mat-icon> {{ 'games.summary.toGame' | translate }}
              </a>
              <!-- UI-Sweep 2026-11-10 (d10-rk-zusammenfassung-anfordern-textknopf): ohne Text steht „Anfordern“ als Hauptknopf im Leerzustand. -->
              @if (summary()?.markdown && shareToken === null && gameShareToken === null && clubLinkToken === null) { <app-club-summary-link [gameId]="gameId" [allowAgain]="true" [kind]="own ? 'own' : 'club'" /> }
            </div>
          </div>
          @if (summary()?.stale) {
            <p class="note stale"><mat-icon>info</mat-icon> {{ 'games.summary.stale' | translate }}</p>
          }
          @if (!summary()?.markdown) {
            <!-- UI-Sweep 2026-11-10 (d10-rk-zusammenfassung-karte-ohne-polster): Leerzustand nach DESIGN 5.10 statt Fließtext an der Kante. -->
            <div class="empty-state empty">
              <mat-icon>article</mat-icon>
              <p>{{ 'games.summary.none' | translate }}</p>
              @if (shareToken === null && gameShareToken === null && clubLinkToken === null) {
                <app-club-summary-link [gameId]="gameId" [kind]="own ? 'own' : 'club'" [prominent]="true" />
              }
            </div>
          } @else {
            <article class="summary-text">
              @for (b of visibleBlocks(); track $index) {
                @switch (b.kind) {
                  @case ('heading') {
                    @if (b.level === 1) { <h1><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></h1> }
                    @else if (b.level === 2) { <h2><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></h2> }
                    @else { <h3><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></h3> }
                  }
                  @case ('paragraph') { <p><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></p> }
                  @case ('quote') { <blockquote><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></blockquote> }
                  @case ('rule') { <hr /> }
                  @case ('game') {
                    <!-- „Die Partie“ (Wunsch 2026-11-13): ganze Zugfolge aus dem PGN, jeder Halbzug ein Zug-Knopf; Paare brechen nie in sich um. -->
                    <section class="game-moves">
                      <h2>{{ 'games.summary.theGame' | translate }}</h2>
                      <p class="game-line">@for (pair of gameMoves(); track $index) {<span class="gm-pair"><span class="gm-no">{{ pair.number }}</span>@for (m of pair.moves; track m.ply) {<button type="button" class="gm" [class.gm-on]="m.ply === boardPly()" [attr.aria-current]="m.ply === boardPly() ? 'step' : null" (click)="showGamePly(m.ply)">{{ m.san | san }}</button>}</span>}@if (g.result && g.result !== '*') {<span class="gm-result">{{ g.result }}</span>}</p>
                    </section>
                  }
                  @case ('gameText') {
                    <!-- Verschmolzener Abschnitt „Die Partie“ (Wunsch 2026-11-14): Partiezüge fett im Text, Varianten in Klammern kleiner und gedämpft. -->
                    <p class="game-text">@for (seg of b.segments; track $index) {@if (seg.variation) {<span class="gt-var"><ng-container *ngTemplateOutlet="gameParts; context: { $implicit: seg.parts }" /></span>} @else {<ng-container *ngTemplateOutlet="gameParts; context: { $implicit: seg.parts }" />}}</p>
                  }
                  @case ('maxim') { <app-maxim-note class="maxim" [text]="b.value" /> }
                  @case ('book') {
                    @if (books().get(b.id); as info) {
                      <div class="book">
                        <div class="book-title">
                          @if (info.accessible) { <a [routerLink]="['/ebooks', info.id]"><strong>{{ info.title }}</strong></a> }
                          @else { <strong>{{ info.title }}</strong> }
                          @if (info.author) { <span> — {{ info.author }}</span> }
                          @if (info.year) { <span> ({{ info.year }})</span> }
                        </div>
                        @if (justBlocked().has(b.id)) {
                          <p class="book-why">{{ 'games.summary.neverSuggestDone' | translate }}
                            <button mat-button (click)="setBlocked(b.id, false)">{{ 'common.undo' | translate }}</button></p>
                        } @else {
                          <p class="book-why"><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.whyParts ?? [{ text: b.why }] }" /></p>
                          @if (loggedIn) {
                            <button mat-button class="never" (click)="setBlocked(b.id, true)">
                              <mat-icon>block</mat-icon> {{ 'games.summary.neverSuggest' | translate }}
                            </button>
                          }
                        }
                      </div>
                    }
                  }
                  @case ('list') {
                    @if (b.ordered) {
                      <ol>@for (it of b.items; track $index) { <li><ng-container *ngTemplateOutlet="inline; context: { $implicit: it }" /></li> }</ol>
                    } @else {
                      <ul>@for (it of b.items; track $index) { <li><ng-container *ngTemplateOutlet="inline; context: { $implicit: it }" /></li> }</ul>
                    }
                  }
                  @case ('table') {
                    <div class="table-scroll"><table>
                      <thead><tr>@for (c of b.head; track $index) { <th><ng-container *ngTemplateOutlet="inline; context: { $implicit: c }" /></th> }</tr></thead>
                      <tbody>@for (r of b.rows; track $index) { <tr>@for (c of r; track $index) { <td><ng-container *ngTemplateOutlet="inline; context: { $implicit: c }" /></td> }</tr> }</tbody>
                    </table></div>
                  }
                  @case ('diagram') {
                    @if (diagram(b.spec); as d) {
                      <figure class="diagram">
                        <div class="diagram-board">
                          <app-chess-board [fen]="d.fen" [lastMove]="d.lastMove" [arrows]="d.arrows" [flipped]="d.flipped"
                                           [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
                        </div>
                        <figcaption>
                          @if (d.label) { <strong>{{ d.label }}</strong> }
                          @if (d.caption) { <ng-container *ngTemplateOutlet="inline; context: { $implicit: d.caption }" /> }
                          @if (b.spec.ply !== undefined) {
                            <a [routerLink]="gameLink()" [queryParams]="{ ply: b.spec.ply }" class="to-ply">{{ 'games.summary.showInGame' | translate }}</a>
                            @if (hasBoard()) {
                              <button type="button" class="to-board" (click)="showOnBoard({ ply: b.spec.ply, flipped: d.flipped })">{{ 'games.summary.showOnBoard' | translate }}</button>
                            }
                          }
                        </figcaption>
                      </figure>
                    }
                  }
                }
              }
            </article>
            <!-- Empfohlene Übungen (0.960.0): nach der Literatur, aus den Daten der Partie — ohne Stellen kein Abschnitt. -->
            @if (exercises().length) {
              <app-summary-exercises [groups]="exercises()" [canTrain]="canTrain()" [training]="training()"
                                     (showPly)="showGamePly($event)" (train)="trainTactics()" />
            }
            @if (summary()?.model) { <p class="note model">{{ 'games.summary.writtenBy' | translate: { model: summary()!.model } }}</p> }
          }
        </mat-card>
        @if (hasBoard()) { <app-summary-game-board [pgn]="g.pgn" [flipped]="g.ownerSide === 'black'" /> }
        </div>
      }
    </div>

    <!-- Text ohne Leerraum drumherum: Leerzeichen um die Interpolation im Template machten „( 45.Ta1“ und „61.Kxg5 .“ (Partie 243). -->
    <ng-template #inline let-parts>
      @for (p of parts; track $index) {
        @if (p.line) { <button type="button" class="mv" [class.mv-b]="p.bold" [class.mv-i]="p.italic" (click)="showMove(p)">{{ p.text }}</button> }
        @else if (p.bold) { <strong>{{ p.text }}</strong> }
        @else if (p.italic) { <em>{{ p.text }}</em> }
        @else if (p.code) { <code>{{ p.text }}</code> }
        @else { <ng-container>{{ p.text }}</ng-container> }
      }
    </ng-template>

    <ng-template #gameParts let-parts>
      @for (p of parts; track $index) {
        @if (p.move) { <button type="button" class="gt-mv" [class.gt-game]="p.move.ply !== undefined" [class.gt-good]="p.mark === 'good'" [class.gt-bad]="p.mark === 'bad'"
                               [class.gt-on]="p.move.ply !== undefined && p.move.ply === boardPly()" [attr.aria-current]="p.move.ply !== undefined && p.move.ply === boardPly() ? 'step' : null"
                               (click)="showGameTextMove(p)">{{ p.text }}{{ p.glyph }}</button> }
        @else if (p.result) { @if (resultText(); as r) { <span class="gt-result">{{ r }}</span> } }
        @else if (p.bold) { <strong>{{ p.text }}</strong> }
        @else if (p.italic) { <em>{{ p.text }}</em> }
        @else if (p.code) { <code>{{ p.text }}</code> }
        @else { <ng-container>{{ p.text }}</ng-container> }
      }
    </ng-template>
  `,
  styles: [`
    .summary-page { max-width: 860px; margin: 0 auto; padding: 16px; }
    .summary-page.with-board { max-width: min(var(--page-max-width), 96vw); }
    .with-board .summary-text { max-width: 75ch; }
    @media (min-width: 900px) {
      .with-board .summary-layout { display: grid; grid-template-columns: minmax(0, 1fr) clamp(320px, 40vw, 520px); gap: 16px; align-items: start; }
    }
    .center { display: flex; justify-content: center; padding: 48px; }
    /* UI-Sweep 2026-11-10 (d10-rk-zusammenfassung-karte-ohne-polster): mat-card hat im M3-Thema kein Eigenpolster — Text,
       Kopf und Knöpfe klebten an der Kartenkante (DESIGN 3.2/5.2). Der Pfeil rückt so weit heraus, dass er mit dem Text fluchtet. */
    .summary-card, .load-card { padding: 16px; }
    .back { margin-left: -8px; }
    @media (max-width: 599.98px) { .summary-card, .load-card { padding: 12px; } }
    .gate-actions { display: flex; justify-content: center; gap: 8px; margin-top: 8px; }
    .head { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; margin-bottom: 8px; }
    .head-main { display: flex; flex-direction: column; flex: 1 1 240px; min-width: 0; }
    .players { font-size: 1.1rem; }
    .result { margin-left: 8px; }
    .meta, .note { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; }
    .head-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .note { display: flex; align-items: center; gap: 6px; }
    .note.stale { color: var(--rh-warning); }
    .summary-text { line-height: 1.6; overflow-wrap: anywhere; }
    .summary-text h1 { font-size: 1.5rem; margin: 16px 0 8px; }
    .summary-text h2 { font-size: 1.25rem; margin: 24px 0 8px; }
    .summary-text h3 { font-size: 1.05rem; margin: 20px 0 6px; }
    .summary-text .book { margin: 10px 0; padding: 8px 12px; border: 1px solid color-mix(in srgb, currentColor 14%, transparent); border-radius: 6px; }
    .summary-text .book-title a { color: inherit; }
    .summary-text .book-why { margin: 4px 0; }
    .summary-text .never { margin-left: -8px; opacity: 0.75; }
    .summary-text blockquote { margin: 12px 0; padding: 4px 12px; border-left: 3px solid var(--rh-accent);
      background: color-mix(in srgb, var(--rh-accent) 8%, transparent); }
    .summary-text code { font-family: monospace; font-size: 0.9em; }
    .table-scroll { overflow-x: auto; margin: 12px 0; }
    .table-scroll table { border-collapse: collapse; min-width: 100%; }
    .table-scroll th, .table-scroll td { border-bottom: 1px solid var(--mat-sys-outline-variant); padding: 4px 8px;
      text-align: left; vertical-align: top; }
    .diagram { margin: 16px auto; max-width: 360px; }
    .diagram-board { width: 100%; }
    .diagram figcaption { margin-top: 6px; font-size: 0.875rem; display: flex; flex-wrap: wrap; gap: 4px 8px; }
    .mv { all: unset; cursor: pointer; font-weight: 500; color: var(--rh-accent, inherit); border-radius: 3px;
      text-decoration: underline dotted; text-underline-offset: 2px; }
    .mv:hover, .mv:focus-visible { background: color-mix(in srgb, currentColor 12%, transparent); outline: none; }
    .mv.mv-b { font-weight: 700; }
    .mv.mv-i { font-style: italic; }
    .to-ply { margin-left: auto; }
    .game-line { line-height: 1.9; overflow-wrap: normal; }
    .gm-pair { display: inline-block; white-space: nowrap; margin-right: 0.6em; }
    .gm-no { color: var(--mat-sys-on-surface-variant); margin-right: 0.15em; }
    .gm { all: unset; cursor: pointer; padding: 0 2px; margin-right: 0.2em; border-radius: 3px; font-weight: 500; color: var(--rh-accent, inherit);
      text-decoration: underline dotted; text-underline-offset: 2px; }
    .gm:hover, .gm:focus-visible { background: color-mix(in srgb, currentColor 12%, transparent); outline: none; }
    .gm.gm-on { background: color-mix(in srgb, var(--rh-accent) 18%, transparent); font-weight: 700; text-decoration: none; }
    .gm-result { font-weight: 700; white-space: nowrap; }
    /* Verschmolzener Text: Partiezüge fett in Textfarbe, Varianten (Klammern) kleiner, kursiv, gedämpft; Zugzeichen färben den Zug. */
    .game-text { line-height: 1.75; }
    .gt-mv { all: unset; cursor: pointer; font-weight: 500; font-style: normal; font-variant-numeric: tabular-nums; white-space: nowrap;
      padding: 0 2px; margin: 0 -1px; border-radius: 4px; }
    .gt-mv.gt-game { font-weight: 700; }
    .gt-mv:hover, .gt-mv:focus-visible { background: color-mix(in srgb, var(--rh-accent) 16%, transparent); }
    .gt-mv:focus-visible { outline: 2px solid var(--rh-accent); outline-offset: 1px; }
    .gt-mv.gt-good { color: var(--rh-success); }
    .gt-mv.gt-bad { color: var(--rh-error); }
    .gt-mv.gt-on, .gt-mv.gt-on.gt-good, .gt-mv.gt-on.gt-bad { background: var(--rh-accent); color: var(--mat-sys-on-primary); }
    .gt-var { color: var(--mat-sys-on-surface-variant); font-size: 0.9em; font-style: italic; }
    .gt-result { font-weight: 700; white-space: nowrap; margin-left: 0.3em; }
    .to-board { background: none; border: 0; padding: 0; font: inherit; color: var(--rh-accent); text-decoration: underline; cursor: pointer; }
  `],
})
export class ClubGameSummaryComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private games = inject(GamesService);
  private summaries = inject(ClubGameSummaryService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  readonly preferences = inject(PreferencesService);

  gameId = 0;
  /** `/games/:id/summary` (0.923.0): eigene Partie statt Vereinspartie. */
  own = false;
  /** `/s/:token/club-games/:id/summary` (Wunsch 2026-11-09): über den LeagueHub-Teilen-Link, ohne Anmeldung, nur lesend. */
  shareToken: string | null = null;
  /** `/g/:token/summary` (Wunsch 2026-11-09): eigene Partie über ihren Teilen-Link, ohne Anmeldung, nur lesend. */
  gameShareToken: string | null = null;
  /** Link einer einzelnen Vereinspartie `/cg/:token` (2026-11-09): nur lesen. */
  clubLinkToken: string | null = null;
  gameLink(): (string | number)[] {
    if (this.clubLinkToken !== null) return ['/cg', this.clubLinkToken];
    if (this.gameShareToken !== null) return ['/g', this.gameShareToken];
    return this.shareToken !== null ? ['/s', this.shareToken, 'club-games', this.gameId] : [this.own ? '/games' : '/club-games', this.gameId];
  }
  readonly loading = signal(true);
  readonly failed = signal(false);
  /** Partie bzw. Link gibt es nicht (404/410) — eigener Zustand ohne „Wiederholen“ (UI-Sweep 10.11.2026). */
  readonly notFound = signal(false);
  /** Vereinspartie ohne Mitgliedschaft (403, UI-Sweep 2026-11-10 d10-rk-zusammenfassung-ladefehler-sackgasse): Sperrkarte. */
  readonly locked = signal(false);
  /** Text für {@link notFound}: über einen Teilen-Link „geteilte Partie gibt es nicht“, sonst „Partie gibt es nicht“. */
  notFoundKey(): string {
    if (this.locked()) return 'games.clubNoAccess';
    return this.shareToken !== null || this.gameShareToken !== null || this.clubLinkToken !== null
      ? 'games.shared.notFound' : 'games.notFound';
  }
  readonly game = signal<SharedGame | null>(null);
  readonly summary = signal<ClubGameSummary | null>(null);
  readonly blocks = computed<SummaryBlock[]>(() => parseSummary(this.summary()?.markdown ?? ''));
  /** „Empfohlene Literatur“ (Wunsch 2026-11-09): nur Bücher, die der Server kennt und dieser Leser nicht gesperrt hat. */
  readonly books = signal<ReadonlyMap<number, EbookSuggestionInfo>>(new Map());
  /** In dieser Sitzung gesperrt — die Zeile bleibt mit „Rückgängig“ stehen. */
  readonly justBlocked = signal<ReadonlySet<number>>(new Set());
  readonly loggedIn = inject(AuthService).isLoggedIn;
  /** „Empfohlene Übungen“ (0.960.0): Stellen der Partie mit ihren Aufgaben; leer = kein Abschnitt (auch bei Fehlern). */
  readonly exercises = signal<SummaryExerciseGroup[]>([]);
  /** Menü-Sichtbarkeit nur für Angemeldete (anonym gibt es „Taktiken trainieren“ nicht — und keinen Menü-Abruf dafür). */
  private readonly menuKeys = this.loggedIn ? toSignal(inject(MenuService).visible$, { initialValue: new Set<string>() }) : null;
  /** „Taktiken trainieren“ (Wunsch 2026-11-14): angemeldet UND mit dem Menüpunkt „Kurse“ — sonst wiese der Kurs-Löser ab. */
  readonly canTrain = computed(() => !!this.menuKeys?.().has('courses'));
  readonly training = signal(false);
  private readonly router = inject(Router);
  /** Ohne Bücher und ohne deren Überschrift, wenn von der Literatur-Liste nichts übrig bleibt. */
  /** Züge im Text werden klickbar, sobald es das Seitenbrett gibt, das sie zeigen kann. */
  private readonly mergedSection = computed(() => this.hasBoard() ? mergeGameSection(this.blocks(), this.positions()) : { blocks: this.blocks(), merged: false });
  private readonly linkedBlocks = computed<SummaryBlock[]>(() => this.hasBoard() ? linkSummaryMoves(this.mergedSection().blocks, this.positions()) : this.blocks());
  /**
   * Mit Brett (= es gibt Züge) steht „Die Partie“ immer da: erzählt der Text sie selbst (Abschnitt „## Die Partie“ mit den
   * Partiezügen, Wunsch 2026-11-14), bleibt es bei ihm; sonst (alte Zusammenfassungen) setzt die Seite die Zugliste aus dem PGN ein.
   */
  readonly visibleBlocks = computed<SummaryBlock[]>(() => {
    const shown = visibleSummaryBlocks(this.linkedBlocks(), b => this.books().has(b.id));
    return this.hasBoard() && !this.mergedSection().merged ? withGameBlock(shown) : shown;
  });
  /** Ergebnis hinter dem letzten Partiezug des verschmolzenen Texts („½–½“ statt „1/2-1/2“). */
  readonly resultText = computed(() => {
    const r = this.game()?.result;
    return !r || r === '*' ? '' : r === '1/2-1/2' ? '½–½' : r.replace('-', '–');
  });
  readonly gameMoves = computed(() => gameMovePairs(this.positions()));
  private readonly positions = computed(() => positionsOf(this.game()?.pgn ?? ''));
  /** Seitenbrett nur, wenn es Text gibt, neben dem es steht, und Züge zum Durchklicken. */
  readonly hasBoard = computed(() => !!this.summary()?.markdown && this.positions().sans.length > 0);
  readonly sideBoard = viewChild(SummaryGameBoardComponent);
  /** Halbzug, den das Seitenbrett zeigt (für die Hervorhebung in „Die Partie“); in einer Variante keiner. */
  readonly boardPly = computed(() => { const b = this.sideBoard(); return b && !b.variation() ? b.ply() : -1; });
  private readonly dialog = inject(MatDialog);
  private readonly breakpoints = inject(BreakpointObserver);

  ngOnInit(): void {
    this.gameId = Number(this.route.snapshot.paramMap.get('id'));
    this.own = this.route.snapshot.data?.['mode'] === 'own';
    if (this.route.snapshot.data?.['mode'] === 'club-share') this.shareToken = this.route.snapshot.paramMap.get('token') || '';
    if (this.route.snapshot.data?.['mode'] === 'club-link') this.clubLinkToken = this.route.snapshot.paramMap.get('token') || '';
    if (this.route.snapshot.data?.['mode'] === 'own-share') this.gameShareToken = this.route.snapshot.paramMap.get('token') || '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.notFound.set(false);
    this.locked.set(false);
    const token = this.shareToken;
    const ownToken = this.gameShareToken;
    const link = this.clubLinkToken;
    const load: Observable<[SharedGame, ClubGameSummary]> = link !== null
      ? forkJoin([this.games.linkedClubGame(link), this.summaries.getLinked(link)])
      : ownToken !== null
      ? forkJoin([this.games.getShared(ownToken), this.summaries.getSharedOwn(ownToken)])
      : token !== null
      ? forkJoin([this.games.sharedClubGame(token, this.gameId), this.summaries.getShared(token, this.gameId)])
      : this.own
      ? forkJoin([this.games.get(this.gameId), this.summaries.getOwn(this.gameId)])
      : forkJoin([this.games.clubGame(this.gameId), this.summaries.get(this.gameId)]);
    load.subscribe({
      next: ([g, s]) => { this.game.set(g); this.summary.set(s); this.loading.set(false); this.loadBooks(); this.loadExercises(); },
      error: (e: { status?: number } | null) => {
        // UI-Sweep 2026-11-10 (d10-rk-zusammenfassung-ladefehler-sackgasse): wie die Partieseite — 403 bei einer Vereinspartie
        // = gesperrt, sonst 4xx = gibt es nicht; nur Netz/5xx (und 408/429) behalten „Wiederholen“.
        const status = e?.status ?? 0;
        const locked = status === 403 && !this.own && this.gameShareToken === null;
        const gone = status >= 400 && status < 500 && status !== 408 && status !== 429;
        this.locked.set(locked);
        (gone ? this.notFound : this.failed).set(true);
        this.loading.set(false);
      },
    });
  }

  private loadBooks(): void {
    const ids = this.blocks().flatMap(b => b.kind === 'book' ? [b.id] : []);
    if (!ids.length) return;
    this.summaries.suggestions(ids).subscribe({
      next: list => this.books.set(new Map(list.filter(x => !x.blocked).map(x => [x.id, x]))),
      error: () => this.books.set(new Map()),   // still: dann eben keine Literatur-Liste
    });
  }

  /** Woher die Seite liest — dieselben fünf Wege für die Übungen. */
  private source(): SummarySource {
    if (this.clubLinkToken !== null) return { kind: 'club-link', token: this.clubLinkToken };
    if (this.gameShareToken !== null) return { kind: 'own-share', token: this.gameShareToken };
    if (this.shareToken !== null) return { kind: 'club-share', token: this.shareToken, id: this.gameId };
    return this.own ? { kind: 'own', id: this.gameId } : { kind: 'club', id: this.gameId };
  }

  /** Nur zu einem fertigen Text; ein Fehler lässt den Abschnitt still weg (Zusatz, kein Kern der Seite). */
  private loadExercises(): void {
    this.exercises.set([]);
    if (!this.summary()?.markdown) return;
    this.summaries.exercises(this.source()).subscribe({
      next: groups => this.exercises.set(groups.filter(g => g.exercises.length > 0)),
      error: () => this.exercises.set([]),
    });
  }

  /**
   * „Taktiken trainieren“: Kapitel dieser Partie im Kurs „Persönliche Taktiken“ anlegen bzw. öffnen und direkt in den
   * Kurs-Löser springen (sequenziell, wie jeder Kurs). Ein Kapitel ohne abfragbare Linie führt auf die Kursseite.
   */
  trainTactics(): void {
    if (this.training()) return;
    this.training.set(true);
    this.summaries.trainTactics(this.source(), this.translate.getCurrentLang() || 'en').subscribe({
      next: c => {
        this.training.set(false);
        void this.router.navigate(c.chapterIndex === null
          ? ['/courses', c.bookId] : ['/courses', c.bookId, 'chapter', c.chapterIndex, 'sequential']);
      },
      error: () => {
        this.training.set(false);
        this.snackbar.warn(this.translate.instant('games.summary.exercises.trainFailed'));
      },
    });
  }

  /** „Nicht mehr vorschlagen“ (je Konto) bzw. zurücknehmen. */
  setBlocked(id: number, blocked: boolean): void {
    this.summaries.setSuggestionBlocked(id, blocked).subscribe({
      next: () => this.justBlocked.update(s => { const n = new Set(s); if (blocked) n.add(id); else n.delete(id); return n; }),
      error: () => this.snackbar.warn(this.translate.instant('games.summary.neverSuggestFailed')),
    });
  }

  /** Klick auf einen Zug im Text: Partiezug oder Variante aufs Seitenbrett. */
  showMove(p: InlinePart): void {
    if (p.line && p.at) this.showOnBoard({ line: p.line, at: p.at });
  }

  /** Klick auf einen Zug im verschmolzenen Text: Partiezug als Halbzug (hebt ihn hervor), Variante als Zugfolge. */
  showGameTextMove(p: GameTextPart): void {
    if (!p.move) return;
    if (p.move.ply !== undefined) this.showOnBoard({ ply: p.move.ply }); else this.showOnBoard({ line: p.move.line, at: p.move.at });
  }

  /** Klick auf einen Zug in „Die Partie“. */
  showGamePly(ply: number): void {
    this.showOnBoard({ ply });
  }

  /**
   * Ab 900 px (Brett neben dem Text) springt das Seitenbrett. Darunter (Wunsch 2026-11-13) öffnet sich das Brett als Blatt
   * über dem Text — kein Sprung ans Seitenende, die Leseposition und das Seitenbrett bleiben, wie sie sind.
   */
  showOnBoard(jump: SummaryJump): void {
    const g = this.game();
    if (g && this.breakpoints.isMatched('(max-width: 899.98px)')) {
      SummaryBoardSheetComponent.open(this.dialog, { pgn: g.pgn, flipped: g.ownerSide === 'black', jump });
      return;
    }
    const board = this.sideBoard();
    if (!board) return;
    if ('line' in jump) board.showLine(jump.line, jump.at); else board.showPly(jump.ply, jump.flipped);
  }

  /** Diagramm-Block → Brett; `null`, wenn der Halbzug außerhalb der Partie liegt (dann lieber keins als ein falsches). */
  diagram(spec: DiagramSpec): ResolvedDiagram | null {
    const own = this.game()?.ownerSide === 'black';
    const flipped = spec.orientation ? spec.orientation === 'black' : own;
    const arrows: BoardArrow[] = spec.arrows.map(a => ({ ...a, brush: 'green' }));
    if (spec.ply !== undefined) {
      const { fens, lastMoves, sans } = this.positions();
      if (spec.ply >= fens.length) return null;
      return { fen: fens[spec.ply], lastMove: lastMoves[spec.ply], arrows, flipped,
        label: moveLabel(spec.ply, sans[spec.ply - 1]), caption: spec.caption };
    }
    return { fen: spec.fen!, arrows, flipped, label: '', caption: spec.caption };
  }

}
