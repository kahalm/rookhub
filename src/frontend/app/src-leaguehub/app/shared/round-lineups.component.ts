import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, viewChild, viewChildren } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { shortTeam, tn } from '../core/league-format';
import { GameMarkers, LineupBoard, LineupMatch, LineupsApiService, MovesKey, RoundLineups, points } from '../core/lineups';
import { BoardMovesComponent } from './board-moves.component';
import { BoardGameComponent } from './board-game.component';
import { GameBadgesComponent } from './game-badges.component';
import { openMarkedPositions } from './marked-positions-dialog.component';
import { ownsTeam } from '../core/club-context.service';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';

/**
 * Alle Aufstellungen einer Runde (2026-10-08, Wunsch: „für jede Runde einen Knopf, der mir alle Aufstellungen für diese Runde
 * anzeigt"): je Begegnung Heim – Gast mit Ergebnis, darunter die Bretter wie auf chess-results (Heimspieler mit Titel und Elo,
 * Farbe, Ergebnis, Gastspieler) und die ersten Züge. Begegnungen des eigenen Vereins hervorgehoben; ohne Brettpaarungen
 * „noch keine Aufstellung". Kein Tabellen-Layout: am Handy stehen die Spieler eines Bretts untereinander.
 * Seit 0.727.2 (Wunsch 2026-10-09: „namen sollten klickbar sein, selbe info wie bei der prognose") öffnen die Namen mit FIDE-ID
 * dieselbe Spielerkarte wie `lh-fixture` (`button.pl`, Vorgabe = Farbe des Spielers an diesem Brett); ohne FIDE-ID bloßer Text.
 * Seit 0.838.0 (Wunsch 2026-10-25: „die Aufstellung für die Runde soll auch bei den anonymen Links verfügbar sein") auch OHNE
 * Anmeldung über einen Teilen-Link (`shareToken`): Liga und Runde bestimmt der Server, nichts ist beschreibbar, die Spielerkarte
 * öffnet nur für die Meldeliste des Links (`coveredFides`), „Nachspielen" nur an der geteilten Begegnung (`shareTeam`).
 * Seit 0.850.0 (Wunsch 2026-10-27: „die Anzeige von + und !! auch, wenn man sich eine Runde anschaut") angemeldet dieselben
 * Marker „◆ n"/„!! n" an den Vereinspartien wie in der Vereinsübersicht (`GameMarkers`). Seit 0.862.0 (Wunsch 2026-10-27:
 * „auch für die Prognose — ganz oben ein +10") ganz oben „+n" für alle markierten Stellungen der Runde (Stellungsfenster).
 */
@Component({
  selector: 'lh-round-lineups',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BoardMovesComponent, BoardGameComponent, PlayerCardComponent, GameBadgesComponent],
  template: `
    <section class="lu" aria-label="Aufstellungen der Runde">
      @if (error(); as e) {
        <p class="err" role="alert">{{ e }} <button type="button" class="btn-link" (click)="load()">Erneut versuchen</button></p>
      } @else if (data(); as d) {
        @if (!d.matches.length) {
          <p class="muted">Für Runde {{ d.round }} gibt es noch keine Begegnungen.</p>
        }
        @if (markers.markTotal(d.matches); as n) {
          <p class="lu-plus">Interessante Stellungen dieser Runde <lh-game-badges [marks]="n" (pick)="openMarks(d.matches, d.round)" /></p>
        }
        <!-- d8-l-fold-long-aufstellungen-handy: am Handy wurde die Seite 7,8 Bildschirme lang — eigene Begegnung zuerst, darüber
             eine Sprungleiste, fremde Begegnungen am Handy zugeklappt (Kopf mit Ergebnis bleibt sichtbar). -->
        @if (ordered().length > 1) {
          <nav class="lu-jump" aria-label="Zur Begegnung springen">
            @for (m of ordered(); track $index) {
              <button type="button" class="lu-chip" [class.own]="m.own" (click)="jump($index)">{{ short(m.home) }} – {{ short(m.away) }}</button>
            }
          </nav>
        }
        @for (m of ordered(); track $index) {
          <details class="lu-match" [class.own]="m.own" [id]="'lu-m-' + $index" [open]="m.own || !compact()">
            <summary class="lu-sum"><h3 class="lu-head">
              <span class="lu-teams">{{ tn(m.home) }} <span class="muted">–</span> {{ tn(m.away) }}</span>
              @if (m.homePts != null || m.awayPts != null) { <span class="lu-score">{{ pts(m.homePts) }} : {{ pts(m.awayPts) }}</span> }
            </h3></summary>
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
                      @if (b.homeFide && b.homePlayer && clickable(b.homeFide)) {
                        <button type="button" class="pl" (click)="openCard(b.homeFide, homeColor(b), b.board)">{{ player(b.homePlayer, b.homeTitle) }}</button>
                      } @else { {{ player(b.homePlayer, b.homeTitle) }} }@if (b.homeElo) { <span class="muted lu-elo">{{ b.homeElo }}</span>}
                    </span>
                    <span class="lu-res">{{ b.result || '–' }}</span>
                    <span class="lu-away">
                      @if (b.awayFide && b.awayPlayer && clickable(b.awayFide)) {
                        <button type="button" class="pl" (click)="openCard(b.awayFide, awayColor(b), b.board)">{{ player(b.awayPlayer, b.awayTitle) }}</button>
                      } @else { {{ player(b.awayPlayer, b.awayTitle) }} }@if (b.awayElo) { <span class="muted lu-elo">{{ b.awayElo }}</span>}
                    </span>
                    <!-- 0.724.0: liegt die Partie vor, wird sie ausgewiesen — keine Zug-Eingabe; ein alter Handeintrag
                         steht nur noch grau als „ersetzt durch die Partie" da. -->
                    @if (b.game; as g) {
                      <span class="lu-moves">
                        <lh-board-game [game]="g" [tnr]="d.tnr" [round]="d.round" [team]="m.home" [board]="b.board"
                                       [title]="boardTitle(b)" [flipped]="ownIsBlack(m, b)" [shareToken]="shareToken()" [shared]="isShared(m)"
                                       [markCount]="markers.markCount(b)" [brilliantPlies]="markers.brilliantPlies(b)" />
                        @if (b.moves) {
                          <lh-board-moves [key]="key(d, m, b)" [title]="boardTitle(b)" [initial]="b.moves" [replaced]="true"
                                          [canDelete]="!!b.canDeleteMoves" />
                        }
                      </span>
                    } @else if (b.moves || b.canEditMoves) {
                      <span class="lu-moves">
                        <lh-board-moves [key]="key(d, m, b)" [title]="boardTitle(b)" [initial]="b.moves" [canEdit]="b.canEditMoves"
                                        [flipped]="ownIsBlack(m, b)" />
                      </span>
                    }
                  </li>
                }
              </ol>
            }
          </details>
        }
      } @else {
        <p class="muted">Lade Aufstellungen …</p>
      }
    </section>
    <lh-player-card />
  `,
  styles: [`
    .lu { margin: 0 0 18px; display: grid; gap: 12px; }
    .lu-plus { margin: 0; display: flex; align-items: center; gap: 8px; font-size: 15px; }
    .lu-match { background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 12px 14px 6px; }
    .lu-match.own { border-color: var(--ink); border-width: 2px; }
    .lu-match { scroll-margin-top: 12px; }
    .lu-sum { display: flex; align-items: baseline; gap: 6px; cursor: pointer; list-style: none; }
    .lu-sum::-webkit-details-marker { display: none; }
    .lu-sum::before { content: '▾'; color: var(--muted); font-size: 14px; flex: none; }
    .lu-match:not([open]) > .lu-sum::before { content: '▸'; }
    .lu-match:not([open]) { padding-bottom: 12px; }
    .lu-match:not([open]) .lu-head { margin-bottom: 0; }
    .lu-sum .lu-head { flex: 1; min-width: 0; }
    .lu-sum:focus-visible { outline: 3px solid var(--focus); outline-offset: 2px; border-radius: 4px; }
    .lu-jump { display: flex; flex-wrap: wrap; gap: 6px; }
    .lu-chip { border: 1px solid var(--line); background: var(--surface); color: var(--ink); border-radius: 999px; min-height: 36px;
      padding: 4px 12px; font: 500 14px/1.2 var(--body); cursor: pointer; text-align: left; }
    .lu-chip:hover { background: var(--paper); }
    .lu-chip.own { border: 2px solid var(--ink); font-weight: 600; }
    .lu-chip:focus-visible { outline: 3px solid var(--focus); outline-offset: 2px; }
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
  private readonly dialog = inject(MatDialog);
  private readonly boardGames = viewChildren(BoardGameComponent);
  /** Angemeldet: Liga und Runde; über einen Teilen-Link (`shareToken`) bestimmt sie der Server, beide bleiben `null`. */
  readonly tnr = input<number | null>(null);
  readonly round = input<number | null>(null);
  /** 0.838.0: der Teilen-Link — Laden über `/api/league/s/{token}/lineups`, ohne Anmeldung, nur lesen. */
  readonly shareToken = input<string | null>(null);
  /** 0.838.0: die Mannschaft der geteilten Begegnung — nur dort kennt der Link die PGNs („Nachspielen") und „wir" fürs Brett. */
  readonly shareTeam = input<string | null>(null);
  /** 0.838.0: FIDE-IDs (bzw. Schlüssel) der Meldeliste des Links — nur für sie öffnet die Spielerkarte ohne Anmeldung. */
  readonly coveredFides = input<readonly string[] | null>(null);

  readonly data = signal<RoundLineups | null>(null);
  /** „◆ n"/„!! n" an den Vereinspartien wie in der Vereinsübersicht (0.850.0) — nur angemeldet, nicht über einen Teilen-Link. */
  readonly markers = new GameMarkers(this.api);
  readonly error = signal<string | null>(null);
  private loadedFor = '';
  readonly tn = tn;
  readonly short = shortTeam;
  readonly pts = points;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  /** Handy (wie die @media-Regel unten): fremde Begegnungen zugeklappt (d8). */
  readonly compact = signal(typeof matchMedia === 'function' && matchMedia('(max-width: 640px)').matches);
  /** Eigene Begegnung(en) zuerst, dann der Rest in der Reihenfolge des Servers (Tabellenreihenfolge). */
  readonly ordered = computed(() => {
    const ms = this.data()?.matches ?? [];
    return [...ms.filter(m => m.own), ...ms.filter(m => !m.own)];
  });

  constructor() {
    effect(() => {
      const key = this.loadKey();
      if (key !== this.loadedFor) void this.load();
    });
  }

  private loadKey(): string {
    return `${this.shareToken() ?? ''}|${this.tnr()}|${this.round()}`;
  }

  /** Späte Antworten einer anderen Runde fallen weg. */
  async load(): Promise<void> {
    const tnr = this.tnr(), round = this.round(), token = this.shareToken(), key = this.loadKey();
    this.loadedFor = key;
    this.data.set(null);
    this.markers.clear();
    this.error.set(null);
    if (!token && (tnr == null || round == null)) return;
    try {
      const d = token ? await this.api.lineups(null, null, token) : await this.api.lineups(tnr, round);
      if (this.loadedFor !== key) return;
      this.data.set(d);
      if (!token) void this.markers.load(GameMarkers.idsOf(d.matches));
    } catch (err) {
      if (this.loadedFor !== key) return;
      const s = err instanceof HttpErrorResponse ? err.status : 0;
      this.error.set(s === 404 ? (token ? 'Dieser Link ist abgelaufen oder wurde widerrufen.' : 'Diese Runde gibt es in der Liga nicht.')
        : `Aufstellungen nicht geladen (${s ? `HTTP ${s}` : 'keine Verbindung'}).`);
    }
  }

  /** „+n" ganz oben (0.862.0): alle markierten Stellungen der Runde; „In der Partie zeigen" klappt das Brett auf. */
  async openMarks(matches: readonly LineupMatch[], round: number): Promise<void> {
    const games = this.markers.markedBoards(matches).map(({ match, board }) => ({
      clubGameId: board.game!.clubGameId!,
      flipped: this.ownIsBlack(match, board),
      subtitle: `Runde ${round} · ${tn(match.home)} – ${tn(match.away)}, Brett ${board.board}`,
    }));
    const r = await openMarkedPositions(this.dialog, games);
    if (r) this.boardGames().find(c => c.game().clubGameId === r.clubGameId)?.showAt(r.start);
  }

  /** Sprungleiste: Begegnung aufklappen und in Sicht holen. */
  jump(i: number): void {
    const el = this.host.nativeElement.querySelector<HTMLDetailsElement>(`#lu-m-${i}`);
    if (!el) return;
    el.open = true;
    el.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  /** Angemeldet jeder Name mit FIDE-ID; über den Link nur die Meldeliste des Links (sonst antwortet der Server 404). */
  clickable(fide: string): boolean {
    return !this.shareToken() || (this.coveredFides()?.includes(fide) ?? false);
  }

  /** Die geteilte Begegnung des Links — nur dort kennt `/s/{token}/games` die Partien. */
  isShared(m: LineupMatch): boolean {
    const team = this.shareTeam();
    return !!team && (m.home === team || m.away === team);
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

  /** Dieselbe Spielerkarte wie in der Prognose (`lh-fixture`); über einen Teilen-Link mit dessen Token (ohne Anmeldung). */
  openCard(fide: string, color: 'w' | 's' | null, board: number): void {
    void this.card().open(fide, color, board, this.shareToken());
  }

  key(d: RoundLineups, m: LineupMatch, b: LineupBoard): MovesKey {
    return { tnr: d.tnr, round: d.round, matchNo: m.matchNo ?? 0, board: b.board };
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

  /** Anfang der Mannschaftsnamen des Vereins (`teamPrefix`) — entscheidet, welche Seite einer eigenen Begegnung „wir" sind;
   *  über einen Teilen-Link ist „wir" die geteilte Mannschaft (`shareTeam`). */
  readonly teamPrefix = input<string | null | undefined>(null);
  private ownSide(m: LineupMatch): 'home' | 'away' {
    const shared = this.shareTeam();
    if (shared) return m.away === shared && m.home !== shared ? 'away' : 'home';
    return ownsTeam(this.teamPrefix(), m.away) && !ownsTeam(this.teamPrefix(), m.home) ? 'away' : 'home';
  }
}
