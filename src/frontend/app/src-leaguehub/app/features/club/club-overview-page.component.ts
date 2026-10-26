import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubOverview, LineupBoard, LineupMatch, LineupsApiService, points } from '../../core/lineups';
import { tn } from '../../core/league-format';
import { ownsTeam, ClubContextService } from '../../core/club-context.service';
import { BoardGameComponent } from '../../shared/board-game.component';
import { AccessGateComponent } from '../../shared/access-gate.component';

/**
 * Vereinsübersicht (`/verein/uebersicht`, 0.848.0, Wunsch 2026-10-26: „Übersichtsseite für den Verein — zeigt alle Begegnungen +
 * Partien + Marker neben Partien mit interessanten Stellungen"): je Liga der laufenden Saison jede Runde mit den Begegnungen der
 * eigenen Mannschaften, darunter die Bretter (Spieler, Farbe, Ergebnis) und die vorhandene Partie (`lh-board-game`: Nachspielen
 * mit Sprungmarken, Analyse). Ein Marker „◆ n" steht neben jeder Partie mit markierten Stellungen; „Nur mit interessanten
 * Stellungen" blendet die übrigen Bretter aus. Lesen mit `league.view`.
 */
@Component({
  selector: 'lh-club-overview-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BoardGameComponent, AccessGateComponent],
  template: `
    @if (!allowed) {
      <lh-access-gate text="Die Vereinsübersicht sehen Admins und die Vereinsgruppen der teilnehmenden Vereine." />
    } @else {
      <section class="ov-head">
        <h1>Übersicht@if (data()?.season; as s) { <span class="muted">{{ s }}</span>}</h1>
        <label class="ov-filter"><input type="checkbox" [checked]="onlyMarked()" (change)="onlyMarked.set(!onlyMarked())" />
          Nur mit interessanten Stellungen@if (markedTotal()) { ({{ markedTotal() }})}</label>
      </section>
      @if (error(); as e) {
        <p class="err" role="alert">{{ e }} <button type="button" class="btn-link" (click)="load()">Erneut versuchen</button></p>
      } @else if (data(); as d) {
        @if (!d.leagues.length) {
          <p class="muted">Noch keine Begegnungen mit Aufstellung in dieser Saison.</p>
        }
        @for (l of d.leagues; track l.tnr) {
          <section class="ov-league" [attr.aria-label]="l.name">
            <h2>{{ l.name }}</h2>
            @for (r of l.rounds; track r.number) {
              @let shown = visible(r.matches);
              @if (shown.length) {
                <h3 class="ov-round">Runde {{ r.number }}@if (r.date) {<span class="muted">&nbsp;· {{ date(r.date) }}</span>}</h3>
                @for (m of shown; track $index) {
                  <article class="lu-match own">
                    <h4 class="lu-head">
                      <span class="lu-teams">{{ tn(m.home) }} <span class="muted">–</span> {{ tn(m.away) }}</span>
                      @if (m.homePts != null || m.awayPts != null) { <span class="lu-score">{{ pts(m.homePts) }} : {{ pts(m.awayPts) }}</span> }
                    </h4>
                    <ol class="lu-boards">
                      @for (b of boards(m); track b.board) {
                        <li class="lu-board">
                          <span class="lu-no" [attr.aria-label]="'Brett ' + b.board">{{ b.board }}</span>
                          <span class="lu-home">
                            <span class="sq" [class.w]="b.homeColor !== 's'" [class.s]="b.homeColor === 's'" role="img"
                                  [attr.aria-label]="b.homeColor === 's' ? 'Heim hat Schwarz' : 'Heim hat Weiß'"></span>
                            {{ player(b.homePlayer, b.homeTitle) }}@if (b.homeElo) { <span class="muted lu-elo">{{ b.homeElo }}</span>}
                          </span>
                          <span class="lu-res">{{ b.result || '–' }}</span>
                          <span class="lu-away">{{ player(b.awayPlayer, b.awayTitle) }}@if (b.awayElo) { <span class="muted lu-elo">{{ b.awayElo }}</span>}</span>
                          @if (b.game; as g) {
                            <span class="lu-moves">
                              <lh-board-game [game]="g" [tnr]="l.tnr" [round]="r.number" [team]="m.home" [board]="b.board"
                                             [title]="'Brett ' + b.board + ': ' + (b.homePlayer ?? '–') + ' – ' + (b.awayPlayer ?? '–')"
                                             [flipped]="ownIsBlack(m, b)" [markCount]="markCount(b)" />
                            </span>
                          }
                        </li>
                      }
                    </ol>
                  </article>
                }
              }
            }
          </section>
        }
      } @else {
        <p class="muted">Lade Übersicht …</p>
      }
    }
  `,
  styles: [`
    .ov-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 6px 16px; margin: 0 0 8px; }
    .ov-head h1 { margin: 0; }
    .ov-head h1 .muted { font-size: 0.6em; margin-left: 8px; }
    .ov-filter { display: inline-flex; align-items: center; gap: 6px; font-size: 15px; }
    .ov-league { margin: 0 0 18px; display: grid; gap: 10px; }
    .ov-league h2 { margin: 12px 0 0; }
    .ov-round { margin: 6px 0 0; font: 600 17px/1.2 var(--cond); }
    .lu-match { background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 12px 14px 6px; }
    .lu-match.own { border-color: var(--ink); border-width: 2px; }
    .lu-head { margin: 0 0 6px; display: flex; flex-wrap: wrap; justify-content: space-between; gap: 4px 12px; font: 600 19px/1.2 var(--cond); }
    .lu-teams { overflow-wrap: anywhere; }
    .lu-score { font-weight: 700; white-space: nowrap; }
    .lu-boards { list-style: none; margin: 0; padding: 0; }
    .lu-board { display: grid; grid-template-columns: 1.6em minmax(0, 1fr) auto minmax(0, 1fr);
      grid-template-areas: "no home res away" ". moves moves moves"; gap: 2px 8px; align-items: baseline;
      padding: 6px 0; border-top: 1px solid var(--line); font-size: 15px; }
    .lu-no { grid-area: no; color: var(--muted); text-align: right; }
    .lu-home { grid-area: home; overflow-wrap: anywhere; }
    .lu-res { grid-area: res; font-weight: 700; white-space: nowrap; text-align: center; }
    .lu-away { grid-area: away; overflow-wrap: anywhere; text-align: right; }
    .lu-moves { grid-area: moves; display: grid; gap: 2px; min-width: 0; }
    .lu-elo { font-size: 13px; margin-left: 3px; }
    .lu-home .sq { display: inline-block; width: 11px; height: 11px; margin: 0 4px 0 0; vertical-align: baseline; }
    @media (max-width: 640px) {
      .lu-board { grid-template-columns: 1.6em minmax(0, 1fr) auto; grid-template-areas: "no home res" ". away ." ". moves moves"; }
      .lu-away { text-align: left; }
    }
  `],
})
export class ClubOverviewPageComponent implements OnInit {
  private readonly api = inject(LineupsApiService);
  private readonly auth = inject(AuthService);
  private readonly clubs = inject(ClubContextService);
  readonly allowed = this.auth.has('league.view');
  readonly data = signal<ClubOverview | null>(null);
  readonly error = signal<string | null>(null);
  readonly onlyMarked = signal(false);
  readonly tn = tn;
  readonly pts = points;
  /** Partien mit mindestens einer markierten Stellung — Zahl hinter dem Filter. */
  readonly markedTotal = computed(() => Object.values(this.data()?.marks ?? {}).filter(n => n > 0).length);

  ngOnInit(): Promise<void> | void {
    if (this.allowed) return this.load();
  }

  async load(): Promise<void> {
    this.error.set(null);
    try {
      this.data.set(await this.api.overview());
    } catch (err) {
      const s = err instanceof HttpErrorResponse ? err.status : 0;
      this.error.set(s === 403 ? 'Die Vereinsübersicht ist für dein Konto nicht freigeschaltet.'
        : `Übersicht nicht geladen (${s ? `HTTP ${s}` : 'keine Verbindung'}).`);
    }
  }

  markCount(b: LineupBoard): number {
    const id = b.game?.clubGameId;
    return id ? this.data()?.marks[String(id)] ?? 0 : 0;
  }

  /** Mit Filter nur Bretter, deren Partie markierte Stellungen hat. */
  boards(m: LineupMatch): LineupBoard[] {
    return this.onlyMarked() ? m.boards.filter(b => this.markCount(b) > 0) : m.boards;
  }

  visible(matches: LineupMatch[]): LineupMatch[] {
    return this.onlyMarked() ? matches.filter(m => this.boards(m).length > 0) : matches;
  }

  player(name: string | null, title: string | null): string {
    if (!name) return 'nicht besetzt';
    return title ? `${title} ${name}` : name;
  }

  /** „2026-10-24" → „24.10.2026". */
  date(iso: string): string {
    return iso.split('-').reverse().join('.');
  }

  /** Brett aus Sicht des eigenen Spielers (wie in den Aufstellungen): spielt der Verein als Gast, hat er die Gegenfarbe. */
  ownIsBlack(m: LineupMatch, b: LineupBoard): boolean {
    const prefix = this.clubs.current()?.teamPrefix ?? null;
    const ownIsAway = ownsTeam(prefix, m.away) && !ownsTeam(prefix, m.home);
    const homeBlack = b.homeColor === 's';
    return ownIsAway ? !homeBlack : homeBlack;
  }
}
