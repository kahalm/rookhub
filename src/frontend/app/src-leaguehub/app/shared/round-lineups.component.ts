import { ChangeDetectionStrategy, Component, effect, inject, input, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { tn } from '../core/league-format';
import { LineupBoard, LineupMatch, LineupsApiService, MovesKey, RoundLineups, points } from '../core/lineups';
import { BoardMovesComponent } from './board-moves.component';
import { BoardGameComponent } from './board-game.component';
import { ownsTeam } from '../core/club-context.service';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';

/**
 * Alle Aufstellungen einer Runde (2026-10-08, Wunsch: „für jede Runde einen Knopf, der mir alle Aufstellungen für diese Runde
 * anzeigt"): je Begegnung Heim – Gast mit Ergebnis, darunter die Bretter wie auf chess-results (Heimspieler mit Titel und Elo,
 * Farbe, Ergebnis, Gastspieler) und die ersten Züge. Begegnungen des eigenen Vereins hervorgehoben; ohne Brettpaarungen
 * „noch keine Aufstellung". Kein Tabellen-Layout: am Handy stehen die Spieler eines Bretts untereinander.
 * Seit 0.727.2 (Wunsch 2026-10-09: „namen sollten klickbar sein, selbe info wie bei der prognose") öffnen die Namen mit FIDE-ID
 * dieselbe Spielerkarte wie `lh-fixture` (`button.pl`, Vorgabe = Farbe des Spielers an diesem Brett); ohne FIDE-ID bloßer Text.
 */
@Component({
  selector: 'lh-round-lineups',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BoardMovesComponent, BoardGameComponent, PlayerCardComponent],
  template: `
    <section class="lu" aria-label="Aufstellungen der Runde">
      @if (error(); as e) {
        <p class="err">{{ e }} <button type="button" class="btn-link" (click)="load()">Erneut versuchen</button></p>
      } @else if (data(); as d) {
        @if (!d.matches.length) {
          <p class="muted">Für Runde {{ d.round }} gibt es noch keine Begegnungen.</p>
        }
        @for (m of d.matches; track $index) {
          <article class="lu-match" [class.own]="m.own">
            <h3 class="lu-head">
              <span class="lu-teams">{{ tn(m.home) }} <span class="muted">–</span> {{ tn(m.away) }}</span>
              @if (m.homePts != null || m.awayPts != null) { <span class="lu-score">{{ pts(m.homePts) }} : {{ pts(m.awayPts) }}</span> }
            </h3>
            @if (!m.boards.length) {
              <p class="muted lu-none">Noch keine Aufstellung.</p>
            } @else {
              <ol class="lu-boards">
                @for (b of m.boards; track b.board) {
                  <li class="lu-board">
                    <span class="lu-no" [attr.aria-label]="'Brett ' + b.board">{{ b.board }}</span>
                    <span class="lu-home">
                      <span class="sq" [class.w]="b.homeColor !== 's'" [class.s]="b.homeColor === 's'" role="img"
                            [attr.aria-label]="b.homeColor === 's' ? 'Heim hat Schwarz' : 'Heim hat Weiß'"></span>
                      @if (b.homeFide && b.homePlayer) {
                        <button type="button" class="pl" (click)="openCard(b.homeFide, homeColor(b), b.board)">{{ player(b.homePlayer, b.homeTitle) }}</button>
                      } @else { {{ player(b.homePlayer, b.homeTitle) }} }@if (b.homeElo) { <span class="muted lu-elo">{{ b.homeElo }}</span>}
                    </span>
                    <span class="lu-res">{{ b.result || '–' }}</span>
                    <span class="lu-away">
                      @if (b.awayFide && b.awayPlayer) {
                        <button type="button" class="pl" (click)="openCard(b.awayFide, awayColor(b), b.board)">{{ player(b.awayPlayer, b.awayTitle) }}</button>
                      } @else { {{ player(b.awayPlayer, b.awayTitle) }} }@if (b.awayElo) { <span class="muted lu-elo">{{ b.awayElo }}</span>}
                    </span>
                    <!-- 0.724.0: liegt die Partie vor, wird sie ausgewiesen — keine Zug-Eingabe; ein alter Handeintrag
                         steht nur noch grau als „ersetzt durch die Partie" da. -->
                    @if (b.game; as g) {
                      <span class="lu-moves">
                        <lh-board-game [game]="g" [tnr]="tnr()" [round]="round()" [team]="m.home" [board]="b.board"
                                       [title]="boardTitle(b)" [flipped]="ownIsBlack(m, b)" />
                        @if (b.moves) {
                          <lh-board-moves [key]="key(m, b)" [title]="boardTitle(b)" [initial]="b.moves" [replaced]="true"
                                          [canDelete]="!!b.canDeleteMoves" />
                        }
                      </span>
                    } @else if (b.moves || b.canEditMoves) {
                      <span class="lu-moves">
                        <lh-board-moves [key]="key(m, b)" [title]="boardTitle(b)" [initial]="b.moves" [canEdit]="b.canEditMoves"
                                        [flipped]="ownIsBlack(m, b)" />
                      </span>
                    }
                  </li>
                }
              </ol>
            }
          </article>
        }
      } @else {
        <p class="muted">Lade Aufstellungen …</p>
      }
    </section>
    <lh-player-card />
  `,
  styles: [`
    .lu { margin: 0 0 18px; display: grid; gap: 12px; }
    .lu-match { background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 12px 14px 6px; }
    .lu-match.own { border-color: var(--ink); border-width: 2px; }
    .lu-head { margin: 0 0 6px; display: flex; flex-wrap: wrap; justify-content: space-between; gap: 4px 12px; font: 600 19px/1.2 var(--cond); }
    .lu-teams { overflow-wrap: anywhere; }
    .lu-score { font-weight: 700; white-space: nowrap; }
    .lu-none { margin: 0 0 8px; font-size: 15px; }
    .lu-boards { list-style: none; margin: 0; padding: 0; }
    .lu-board { display: grid; grid-template-columns: 1.6em minmax(0, 1fr) auto minmax(0, 1fr);
      grid-template-areas: "no home res away" ". moves moves moves"; gap: 2px 8px; align-items: baseline;
      padding: 6px 0; border-top: 1px solid var(--line); font-size: 15px; }
    .lu-no { grid-area: no; color: var(--muted); text-align: right; }
    .lu-home { grid-area: home; overflow-wrap: anywhere; }
    .lu-res { grid-area: res; font-weight: 700; white-space: nowrap; text-align: center; }
    .lu-away { grid-area: away; overflow-wrap: anywhere; text-align: right; }
    .lu-away button.pl { text-align: right; }
    .lu-moves { grid-area: moves; display: grid; gap: 2px; min-width: 0; }
    .lu-elo { font-size: 13px; margin-left: 3px; }
    .lu-home .sq { display: inline-block; width: 11px; height: 11px; margin: 0 4px 0 0; vertical-align: baseline; }
    @media (max-width: 640px) {
      .lu-board { grid-template-columns: 1.6em minmax(0, 1fr) auto; grid-template-areas: "no home res" ". away ." ". moves moves"; }
      .lu-away, .lu-away button.pl { text-align: left; }
    }
  `],
})
export class RoundLineupsComponent {
  private readonly api = inject(LineupsApiService);
  private readonly card = viewChild.required(PlayerCardComponent);
  readonly tnr = input.required<number>();
  readonly round = input.required<number>();

  readonly data = signal<RoundLineups | null>(null);
  readonly error = signal<string | null>(null);
  private loadedFor = '';
  readonly tn = tn;
  readonly pts = points;

  constructor() {
    effect(() => {
      const key = `${this.tnr()}|${this.round()}`;
      if (key !== this.loadedFor) void this.load();
    });
  }

  /** Späte Antworten einer anderen Runde fallen weg. */
  async load(): Promise<void> {
    const tnr = this.tnr(), round = this.round(), key = `${tnr}|${round}`;
    this.loadedFor = key;
    this.data.set(null);
    this.error.set(null);
    try {
      const d = await this.api.lineups(tnr, round);
      if (this.loadedFor === key) this.data.set(d);
    } catch (err) {
      if (this.loadedFor !== key) return;
      const s = err instanceof HttpErrorResponse ? err.status : 0;
      this.error.set(s === 404 ? 'Diese Runde gibt es in der Liga nicht.' : `Aufstellungen nicht geladen (${s ? `HTTP ${s}` : 'keine Verbindung'}).`);
    }
  }

  player(name: string | null, title: string | null): string {
    if (!name) return 'nicht besetzt';
    return title ? `${title} ${name}` : name;
  }

  /** Farbe des Heimspielers an diesem Brett (Vorgabe der Spielerkarte); unbekannt = keine Vorgabe. */
  homeColor(b: LineupBoard): 'w' | 's' | null {
    return b.homeColor === 's' ? 's' : b.homeColor === 'w' ? 'w' : null;
  }

  awayColor(b: LineupBoard): 'w' | 's' | null {
    const h = this.homeColor(b);
    return h === 'w' ? 's' : h === 's' ? 'w' : null;
  }

  /** Dieselbe Spielerkarte wie in der Prognose (`lh-fixture`); die Aufstellungen gibt es nur angemeldet, also ohne Teilen-Link. */
  openCard(fide: string, color: 'w' | 's' | null, board: number): void {
    void this.card().open(fide, color, board, null);
  }

  key(m: LineupMatch, b: LineupBoard): MovesKey {
    return { tnr: this.tnr(), round: this.round(), matchNo: m.matchNo ?? 0, board: b.board };
  }

  boardTitle(b: LineupBoard): string {
    return `Brett ${b.board}: ${b.homePlayer ?? '–'} – ${b.awayPlayer ?? '–'}`;
  }

  /** Brett aus Sicht des eigenen Spielers: spielt der eigene Verein als Gast, hat er die andere Farbe als der Heimspieler.
   *  Ohne eigene Mannschaft aus Sicht des Heimspielers. */
  ownIsBlack(m: LineupMatch, b: LineupBoard): boolean {
    const homeBlack = b.homeColor === 's';
    const ownIsAway = m.own && this.ownSide(m) === 'away';
    return ownIsAway ? !homeBlack : homeBlack;
  }

  /** Anfang der Mannschaftsnamen des Vereins (`teamPrefix`) — entscheidet, welche Seite einer eigenen Begegnung „wir" sind. */
  readonly teamPrefix = input<string | null | undefined>(null);
  private ownSide(m: LineupMatch): 'home' | 'away' {
    return ownsTeam(this.teamPrefix(), m.away) && !ownsTeam(this.teamPrefix(), m.home) ? 'away' : 'home';
  }
}
