import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, forkJoin } from 'rxjs';
import { ChessBoardComponent, BoardArrow } from '../../../shared/pgn-viewer/chess-board.component';
import { LoadErrorComponent } from '../../../shared/load-error/load-error.component';
import { PreferencesService } from '../../../core/preferences.service';
import { GamesService, SharedGame } from '../games.service';
import { ClubGameSummary, ClubGameSummaryService } from './club-game-summary.service';
import { ClubSummaryLinkComponent } from './club-summary-link.component';
import { DiagramSpec, InlinePart, SummaryBlock, moveLabel, parseSummary, positionsOf } from './summary-markdown.util';

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
    ChessBoardComponent, LoadErrorComponent, ClubSummaryLinkComponent],
  template: `
    <div class="summary-page">
      @if (loading()) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (failed()) {
        <mat-card class="load-card"><app-load-error [messageKey]="'games.loadError'" (retry)="load()" /></mat-card>
      } @else if (game(); as g) {
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
              @if (shareToken === null) { <app-club-summary-link [gameId]="gameId" [allowAgain]="true" [kind]="own ? 'own' : 'club'" /> }
            </div>
          </div>
          @if (summary()?.stale) {
            <p class="note stale"><mat-icon>info</mat-icon> {{ 'games.summary.stale' | translate }}</p>
          }
          @if (!summary()?.markdown) {
            <p class="note empty">{{ 'games.summary.none' | translate }}</p>
          } @else {
            <article class="summary-text">
              @for (b of blocks(); track $index) {
                @switch (b.kind) {
                  @case ('heading') {
                    @if (b.level === 1) { <h1><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></h1> }
                    @else if (b.level === 2) { <h2><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></h2> }
                    @else { <h3><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></h3> }
                  }
                  @case ('paragraph') { <p><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></p> }
                  @case ('quote') { <blockquote><ng-container *ngTemplateOutlet="inline; context: { $implicit: b.parts }" /></blockquote> }
                  @case ('rule') { <hr /> }
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
                          }
                        </figcaption>
                      </figure>
                    }
                  }
                }
              }
            </article>
            @if (summary()?.model) { <p class="note model">{{ 'games.summary.writtenBy' | translate: { model: summary()!.model } }}</p> }
          }
        </mat-card>
      }
    </div>

    <ng-template #inline let-parts>
      @for (p of parts; track $index) {
        @if (p.bold) { <strong>{{ p.text }}</strong> }
        @else if (p.italic) { <em>{{ p.text }}</em> }
        @else if (p.code) { <code>{{ p.text }}</code> }
        @else { {{ p.text }} }
      }
    </ng-template>
  `,
  styles: [`
    .summary-page { max-width: 860px; margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 48px; }
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
    .to-ply { margin-left: auto; }
  `],
})
export class ClubGameSummaryComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private games = inject(GamesService);
  private summaries = inject(ClubGameSummaryService);
  readonly preferences = inject(PreferencesService);

  gameId = 0;
  /** `/games/:id/summary` (0.923.0): eigene Partie statt Vereinspartie. */
  own = false;
  /** `/s/:token/club-games/:id/summary` (Wunsch 2026-11-09): über den LeagueHub-Teilen-Link, ohne Anmeldung, nur lesend. */
  shareToken: string | null = null;
  gameLink(): (string | number)[] {
    return this.shareToken !== null ? ['/s', this.shareToken, 'club-games', this.gameId] : [this.own ? '/games' : '/club-games', this.gameId];
  }
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly game = signal<SharedGame | null>(null);
  readonly summary = signal<ClubGameSummary | null>(null);
  readonly blocks = computed<SummaryBlock[]>(() => parseSummary(this.summary()?.markdown ?? ''));
  private readonly positions = computed(() => positionsOf(this.game()?.pgn ?? ''));

  ngOnInit(): void {
    this.gameId = Number(this.route.snapshot.paramMap.get('id'));
    this.own = this.route.snapshot.data?.['mode'] === 'own';
    if (this.route.snapshot.data?.['mode'] === 'club-share') this.shareToken = this.route.snapshot.paramMap.get('token') || '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    const token = this.shareToken;
    const load: Observable<[SharedGame, ClubGameSummary]> = token !== null
      ? forkJoin([this.games.sharedClubGame(token, this.gameId), this.summaries.getShared(token, this.gameId)])
      : this.own
      ? forkJoin([this.games.get(this.gameId), this.summaries.getOwn(this.gameId)])
      : forkJoin([this.games.clubGame(this.gameId), this.summaries.get(this.gameId)]);
    load.subscribe({
      next: ([g, s]) => { this.game.set(g); this.summary.set(s); this.loading.set(false); },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
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
