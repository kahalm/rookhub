import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { LeagueApiService } from '../core/league-api.service';
import { PHASE_TEXT, pct, shareText, shortTeam, tn } from '../core/league-format';
import { Board, Fixture, FixturePairing, ForecastStats, ForecastTally, GameSources } from '../core/league.models';
import { GameSourcesComponent } from './game-sources.component';
import { thousands } from '../core/game-sources';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';
import { GameReplayComponent } from '@rh/shared/player-card/game-replay.component';
import { RouterLink } from '@angular/router';
import { HandoffService } from '@rh/core/handoff.service';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';

/**
 * Eine Begegnung: Kopf (Runde, Datum, Ort, Paarung), Brett-Prognosen (drei Kandidaten je Brett,
 * Farbe des Gegners), bei gespielten Runden die echte Aufstellung, die Meldeliste des Gegners und die
 * Knöpfe „Auf WhatsApp teilen" (Text) und „Link teilen" (nur im Admin-Bereich). Genutzt von der
 * Liga-Seite und der geteilten Ansicht (<see cref="shareToken"/> gesetzt = ohne Anmeldung).
 */
interface ShareOut { kind: 'text' | 'link' | 'info' | 'error'; text: string; copied?: boolean; url: string; token: string; until: string }

@Component({
  selector: 'lh-fixture',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PlayerCardComponent, GameSourcesComponent, GameReplayComponent, RouterLink],
  template: `
    @let e = fixture();
    @if (!e) {
      <div class="empty"><p>Für diese Auswahl gibt es keine Daten.</p></div>
    } @else if (e.bye) {
      <article class="fixture">
        <p class="when">Runde {{ round() }}@if (e.date) {, {{ e.date }}}</p>
        <h2 class="match">{{ tn(team()) }} ist spielfrei.</h2>
      </article>
    } @else {
      <article class="fixture">
        <p class="when">Runde {{ round() }}@if (e.date) {, {{ e.date }}}@if (e.venue) {, {{ e.venue }}}</p>
        <h2 class="match">
          <span [class.me]="e.home">{{ tn(home()) }}</span><span class="vs">–</span><span [class.me]="!e.home">{{ tn(away()) }}</span>
          @if (e.score) { <span class="score">{{ e.score }}</span> }
        </h2>
        <!-- 0.673.0, Wunsch 2026-10-05: bei gespielten Runden die Paarungen gleich unter dem Ergebnis, mit Partie, wo es eine gibt. -->
        @if (e.status === 'played' && pairings().length) {
          <table class="pairings">
            <caption class="sr-only">Brettpaarungen</caption>
            <tbody>
              @for (p of pairings(); track p.board) {
                <tr>
                  <td class="num">{{ p.board }}</td>
                  <td class="pw">{{ p.white ?? 'nicht besetzt' }}@if (p.whiteElo) { <span class="muted"> {{ p.whiteElo }}</span>}</td>
                  <td class="pr">{{ p.result }}</td>
                  <td class="pb">{{ p.black ?? 'nicht besetzt' }}@if (p.blackElo) { <span class="muted"> {{ p.blackElo }}</span>}</td>
                  <td class="pg">
                    @if (p.pgn) {
                      <button type="button" class="btn-link" [attr.aria-expanded]="openBoard() === p.board"
                              (click)="openBoard.set(openBoard() === p.board ? null : p.board)">Nachspielen</button>
                    }
                    <!-- 0.675.0: an einer Vereinspartie dieselben Wege wie in der Vereinsliste — angemeldet; über einen Teilen-Link nicht -->
                    @if (p.clubGameId && !shareToken()) {
                      @if (rookHub) { <button type="button" class="btn-link" (click)="openInRookHub(p.clubGameId)"
                                              title="Auf RookHubs Partieseite mit Bewertungskurve, Fehlern und Zug-Klassen">Analyse</button> }
                      @if (p.canEdit) {
                        <a class="btn-link" [routerLink]="['/verein']" [queryParams]="{ bearbeiten: p.clubGameId }"
                           title="Namen und Ergebnis ändern">Bearbeiten</a>
                        <a class="btn-link" [routerLink]="['/verein/partie', p.clubGameId, 'korrigieren']"
                           title="Züge nachbessern, wie beim ersten Prüfen des Formulars">Korrigieren</a>
                      }
                    }
                  </td>
                </tr>
                @if (openBoard() === p.board && p.pgn) {
                  <tr class="replay-row"><td colspan="5">
                    <lh-game-replay [pgn]="p.pgn" [flipped]="ownIsBlack(p)"
                                    [evalsUrl]="p.clubGameId && !shareToken() ? '/api/league/club/games/' + p.clubGameId + '/evals' : null" />
                  </td></tr>
                }
              }
            </tbody>
          </table>
        }
        @switch (e.status) {
          @case ('locked') {
            <p class="note">Die Prognose für diese Runde erscheint, sobald Runde {{ e.unlock_after }} gespielt ist.
              Erst dann ist bekannt, wer zuletzt gespielt hat, und das ist der stärkste Hinweis auf die nächste Aufstellung.</p>
          }
          @case ('nodata') {
            <p class="note">Für {{ tn(e.opp) }} gibt es noch keine Meldeliste.</p>
          }
          @default {
            <!-- Wunsch 2026-10-04: der Text am Anfang hinter zwei (i) — Partien der Begegnung und die Prognose samt Treffern. -->
            <div class="infos">
              @if (sources(); as s) {
                <p class="info-line">
                  <span>Partien {{ tn(e.opp) }}: <b>{{ gamesCount(s) }}</b></span>
                  <button type="button" class="chk-btn" [attr.aria-expanded]="openInfo() === 'games'" aria-label="Partien je Quelle"
                          title="Partien je Quelle" (click)="toggleInfo('games')">i</button>
                </p>
              }
              <p class="info-line">
                <!-- 0.659.4: „(Top 3: 61 %)" — die frühere „% korrekt" (Kalibrierung) war zu schmeichelhaft: auch gleichmäßiges
                     Raten wäre fast perfekt kalibriert. Top 3 = an wie vielen Brettern der Spieler unter den drei Vorschlägen war. -->
                <span>Prognose@if (stats(); as st) {@if (st.total.of) { (Top 3: {{ share(st.total.top3, st.total.of) }})}}</span>
                <button type="button" class="chk-btn" [attr.aria-expanded]="openInfo() === 'forecast'" aria-label="Wie die Prognose zustande kommt"
                        title="Wie die Prognose zustande kommt" (click)="toggleInfo('forecast')">i</button>
              </p>
            </div>
            @if (openInfo() === 'games' && sources(); as s) {
              <div class="info-panel"><lh-game-sources [sources]="s" [league]="leagueName()" [opponent]="e.opp" /></div>
            }
            @if (openInfo() === 'forecast') {
              <div class="info-panel">
                <p class="note">
                  @if (e.status === 'played') { Pro Brett oben, wer tatsächlich gespielt hat, darunter die Prognose, die vor der Runde galt. }
                  @else { Prognose für {{ tn(e.opp) }}: pro Brett die drei wahrscheinlichsten Spieler. }
                  @if (e.hit) { In alten Saisonen lagen in dieser Lage ({{ phaseText() }}) im Schnitt {{ hitText() }} von {{ e.boards?.length }} vorhergesagten Spielern richtig. }
                  Das Quadrat zeigt die Farbe des Gegners. In Klammer hinter dem Namen: Partien im Bestand.
                </p>
                @if (e.eval; as v) {
                  @if (v.top1 != null) {
                    <p class="note">In dieser Begegnung saß an {{ v.top1 }} von {{ v.of }} Brettern genau der erste Vorschlag, an {{ v.top2 }} der erste
                      oder zweite, an {{ v.top3 }} einer der drei.</p>
                  }
                }
                @if (stats(); as st) {
                  @if (st.total.fixtures) {
                    <p class="note">Bisher in der Saison {{ st.season }}, über alle Begegnungen aller Ligen (jede Begegnung zweimal: je eine Prognose
                      für jede Mannschaft): an wie vielen besetzten Brettern der tatsächliche Spieler der erste Vorschlag war (Platz 1), unter den
                      ersten beiden (Top 2) oder unter den dreien (Top 3). Klein darunter, wie oft es nach den angesagten Prozenten hätte
                      treffen sollen.</p>
                    <div class="src-scroll"><table class="src-tbl stats-tbl">
                      <thead><tr><th scope="col"></th><th scope="col" class="num">Prognosen</th><th scope="col" class="num">Platz 1</th>
                        <th scope="col" class="num">Top 2</th><th scope="col" class="num">Top 3</th></tr></thead>
                      <tbody>
                        <tr class="src-group"><th scope="rowgroup" colspan="5">Je Runde</th></tr>
                        @for (r of st.rounds; track r.round) {
                          <tr><td>Runde {{ r.round }}</td><td class="num">{{ r.fixtures }}</td>
                            <td class="num">{{ share(r.top1, r.of) }}<span class="exp">{{ exp(r.e1, r.of) }}</span></td>
                            <td class="num">{{ share(r.top2, r.of) }}<span class="exp">{{ exp(r.e2, r.of) }}</span></td>
                            <td class="num">{{ share(r.top3, r.of) }}<span class="exp">{{ exp(r.e3, r.of) }}</span></td></tr>
                        }
                        <tr class="src-group"><th scope="rowgroup" colspan="5">Je Liga</th></tr>
                        @for (l of st.leagues; track l.tnr) {
                          <tr [class.mine]="l.name === leagueName()">
                            <td>{{ l.name }}<span class="src-sub">{{ roundsText(l.rounds) }}</span></td><td class="num">{{ l.fixtures }}</td>
                            <td class="num">{{ share(l.top1, l.of) }}<span class="exp">{{ exp(l.e1, l.of) }}</span></td>
                            <td class="num">{{ share(l.top2, l.of) }}<span class="exp">{{ exp(l.e2, l.of) }}</span></td>
                            <td class="num">{{ share(l.top3, l.of) }}<span class="exp">{{ exp(l.e3, l.of) }}</span></td>
                          </tr>
                        }
                        <tr class="src-group total"><th scope="row">Gesamt</th><td class="num">{{ st.total.fixtures }}</td>
                          <td class="num">{{ share(st.total.top1, st.total.of) }}<span class="exp">{{ exp(st.total.e1, st.total.of) }}</span></td>
                            <td class="num">{{ share(st.total.top2, st.total.of) }}<span class="exp">{{ exp(st.total.e2, st.total.of) }}</span></td>
                            <td class="num">{{ share(st.total.top3, st.total.of) }}<span class="exp">{{ exp(st.total.e3, st.total.of) }}</span></td></tr>
                      </tbody>
                    </table></div>
                    @if (calibration(); as cal) {
                      <p class="note cal-h"><b>Sind die Prozente ehrlich?</b> Im Schnitt liegen angesagt und eingetroffen {{ cal.gap }} Prozentpunkte
                        auseinander. @if (cal.said) { Dem Spieler, der wirklich kam, gab die Prognose im Schnitt {{ cal.said }} %; Raten über die Meldeliste gäbe {{ cal.guess }} %. }</p>
                      <p class="note">Gemessen über alle Angaben ab 2 % aller Bretter, nach Höhe gruppiert, gewichtet nach der angesagten
                        Wahrscheinlichkeit. Sagt die Prognose 30 % und der Spieler kommt in 30 % der Fälle, stimmt sie — auch wenn er oft nicht
                        kommt. Das misst nur, ob die Prozente ehrlich sind, nicht wie scharf die Prognose ist: auch gleichmäßiges Raten wäre
                        fast perfekt ehrlich. Wie oft sie trifft, steht oben (Platz 1, Top 2, Top 3).</p>
                      <div class="src-scroll"><table class="src-tbl stats-tbl cal-tbl">
                        <thead><tr><th scope="col">Angabe</th><th scope="col" class="num">Fälle</th><th scope="col" class="num">angesagt</th>
                          <th scope="col" class="num">eingetroffen</th></tr></thead>
                        <tbody>
                          @for (b of cal.rows; track b.from) {
                            <tr><td>{{ b.from }}–{{ b.from + 10 }} %</td><td class="num">{{ b.n }}</td><td class="num">{{ b.said }} %</td>
                              <td class="num"><b>{{ b.came }} %</b></td></tr>
                          }
                        </tbody>
                      </table></div>
                    }
                  } @else {
                    <p class="note">Noch keine gespielte Runde mit Prognose in dieser Saison.</p>
                  }
                }
              </div>
            }
            <div class="share">
              <div class="actions">
                @if (e.status === 'open') { <button type="button" class="btn-sec" (click)="shareWhatsApp()">Auf WhatsApp teilen</button> }
                @if (canShareLink()) { <button type="button" class="btn-sec" (click)="shareLink()">Link teilen</button> }
              </div>
              @if (shareOut(); as o) {
                <div class="share-out">
                  @if (o.kind === 'text') {
                    <p>{{ o.copied ? 'Text kopiert. In WhatsApp einfügen oder direkt öffnen:' : 'Text markieren und kopieren oder direkt öffnen:' }}
                      <a [href]="waLink(o.text)" target="_blank" rel="noopener">In WhatsApp öffnen</a></p>
                    <pre class="share-text">{{ o.text }}</pre>
                  } @else if (o.kind === 'link') {
                    <p>Wer diesen Link hat, sieht ohne Anmeldung nur diese Begegnung: Prognose, Meldeliste und Spielerkarten mit Partien.
                      Der Link bleibt nach „Daten aktualisieren" aktuell und läuft am {{ o.until }} ab.</p>
                    <div class="linkrow">
                      <input type="text" readonly [value]="o.url" aria-label="Teilen-Link">
                      <button type="button" class="btn-sec" (click)="copy(o.url)">{{ copied() ? 'Kopiert' : 'Kopieren' }}</button>
                    </div>
                    <p class="actions">
                      <a [href]="waLink(linkMessage(o.url))" target="_blank" rel="noopener">In WhatsApp öffnen</a>
                      <button type="button" class="btn-sec" (click)="revoke(o.token)">Link widerrufen</button>
                    </p>
                  } @else {
                    <p [class.err]="o.kind === 'error'">{{ o.text }}</p>
                  }
                </div>
              }
            </div>
            <ol class="boards">
              @for (b of e.boards; track b.board) {
                <li class="board">
                  <span class="bno" [attr.aria-label]="'Brett ' + b.board">{{ b.board }}</span>
                  <span class="sq" [class.w]="b.opp_color === 'w'" [class.s]="b.opp_color === 's'" role="img"
                        [attr.aria-label]="colorName(b)" [attr.title]="colorName(b)"></span>
                  <div class="cands">
                    @if (b.actual; as a) {
                      <p class="played">Gespielt: <b>{{ a.n }}</b>@if (a.elo) { ({{ a.elo }})} – {{ a.score ?? '–' }} : {{ a.own ?? '–' }} gegen {{ a.vs }}
                        @if (!inTop(b) && b.actual_p != null) { <span class="miss">– war nicht unter den Vorschlägen ({{ pct(b.actual_p) }})</span> }</p>
                    }
                    @for (c of b.cand; track c.n; let i = $index) {
                      <div class="cand" [class.first]="i === 0">
                        <span class="name">
                          @if (c.fide) { <button type="button" class="pl" (click)="openCard(c.fide, b.opp_color, b.board)">{{ c.n }}</button> }
                          @else { {{ c.n }} }
                          @if (c.g) { <span class="g muted" [attr.title]="c.g + ' Partien im Bestand'">({{ c.g }})</span> }
                          @if (b.actual?.n === c.n) { <span class="hit">gespielt</span> }
                        </span>
                        <span class="elo">{{ c.elo ?? '–' }}</span>
                        <span class="pct">{{ pct(c.p) }}</span>
                        <span class="bar" aria-hidden="true"><i [style.width.%]="c.p * 100"></i></span>
                      </div>
                    }
                    @if (b.other >= 0.01) { <span class="other">jemand anderer: {{ pct(b.other) }}</span> }
                  </div>
                </li>
              }
            </ol>
            <details class="roster">
              <summary>Alle Gemeldeten von {{ tn(e.opp) }} mit Einsatz-Wahrscheinlichkeit</summary>
              <div class="roster-scroll"><table class="rtable">
                <thead><tr><th class="num">Meld.</th><th>Spieler</th><th class="num">Elo</th><th class="num">spielt</th>
                  <th class="num">Partien</th><th>Online</th><th class="hide-s">Vorsaison (Partien/Runden)</th><th class="hide-s">diese Saison</th></tr></thead>
                <tbody>
                  @for (r of e.roster; track $index) {
                    <tr>
                      <td class="num">{{ r.rb ?? '' }}</td>
                      <td>@if (r.fide) { <button type="button" class="pl" (click)="openCard(r.fide, null, null)">{{ r.n }}</button> } @else { {{ r.n }} }</td>
                      <td class="num">{{ r.elo ?? '–' }}</td>
                      <td class="num"><b>{{ pct(r.p) }}</b></td>
                      <td class="num">{{ r.g || '–' }}</td>
                      <td class="small acc-cell">
                        @for (a of r.acc; track a.url) {
                          <a [href]="a.url" target="_blank" rel="noopener" [attr.title]="a.site + ': ' + a.user + ' (' + a.conf + ')'">{{ a.site }}{{ a.conf === 'sicher' ? '' : '?' }}</a>{{ ' ' }}
                        } @empty { – }
                      </td>
                      <td class="small hide-s">{{ r.prev || '–' }}</td>
                      <td class="small hide-s">{{ r.cur || '–' }}</td>
                    </tr>
                  }
                </tbody>
              </table></div>
            </details>
          }
        }
      </article>
    }
    <lh-player-card />
  `,
})
export class FixtureViewComponent {
  private readonly api = inject(LeagueApiService);
  private readonly card = viewChild.required(PlayerCardComponent);

  readonly leagueName = input.required<string>();
  /** chess-results-Turniernummer — nur im Admin-Bereich (zum Anlegen eines Teilen-Links). */
  readonly tnr = input<number | null>(null);
  readonly round = input.required<number>();
  readonly team = input.required<string>();
  readonly fixture = input<Fixture | undefined>(undefined);
  /** Turniernummer der Liga — für die Paarungen gespielter Runden (immer gesetzt, anders als `tnr`). */
  readonly leagueTnr = input<number | null>(null);
  /** Gesetzt = geteilte Ansicht: Karten/PGN über den Link, kein „Link teilen". */
  readonly shareToken = input<string | null>(null);
  /** Partien je Quelle (Gesamt | Liga | Begegnung) — steht seit 0.650.0 hinter dem (i) „Partien". */
  readonly sources = input<GameSources | null>(null);

  /** Welches (i) offen ist: Partien je Quelle oder Prognose samt Treffer-Statistik. */
  readonly openInfo = signal<'games' | 'forecast' | null>(null);
  readonly stats = signal<ForecastStats | null>(null);
  private statsFor: string | null | undefined = undefined;

  /** Ergebnis von „Auf WhatsApp teilen"/„Link teilen" — gehört zu DIESER Begegnung: wechselt Runde, Verein oder
   *  Liga, verschwindet es (sonst stünde der Link der vorigen Begegnung unter der neuen, und „In WhatsApp öffnen"
   *  schickte die alte Adresse mit den neuen Mannschaftsnamen). */
  readonly shareOut = linkedSignal({
    source: () => [this.fixture(), this.round(), this.team()] as const,
    computation: (): ShareOut | null => null,
  });
  readonly copied = signal(false);

  readonly tn = tn;
  readonly pct = pct;
  readonly home = computed(() => (this.fixture()?.home ? this.team() : this.fixture()?.opp ?? ''));
  readonly away = computed(() => (this.fixture()?.home ? this.fixture()?.opp ?? '' : this.team()));
  readonly canShareLink = computed(() => !this.shareToken() && this.tnr() !== null);
  readonly phaseText = computed(() => PHASE_TEXT[this.fixture()?.phase ?? 'R1'] ?? this.fixture()?.phase);
  readonly hitText = computed(() => String(this.fixture()?.hit ?? '').replace('.', ','));

  /** Brettpaarungen samt Partien der gespielten Begegnung (0.673.0); späte Antworten einer anderen Begegnung fallen weg. */
  readonly pairings = signal<FixturePairing[]>([]);
  readonly openBoard = signal<number | null>(null);
  private pairingsFor = '';

  constructor() {
    effect(() => {
      const f = this.fixture(), round = this.round(), team = this.team(), token = this.shareToken(), tnr = this.leagueTnr();
      const key = `${token ?? tnr}|${round}|${team}|${f?.status}`;
      if (key === this.pairingsFor) return;
      this.pairingsFor = key;
      this.pairings.set([]);
      this.openBoard.set(null);
      if (f?.status !== 'played' || (!token && tnr == null)) return;
      this.api.fixtureGames(tnr, round, team, token).then(
        p => { if (this.pairingsFor === key) this.pairings.set(p); },
        () => { if (this.pairingsFor === key) this.pairings.set([]); });
    });
    effect(() => {
      const token = this.shareToken();
      if (this.statsFor === token) return;
      this.statsFor = token;
      this.api.forecastStats(token).then(s => { if (this.statsFor === token) this.stats.set(s); }, () => this.stats.set(null));
    });
  }

  toggleInfo(which: 'games' | 'forecast'): void {
    this.openInfo.update(o => (o === which ? null : which));
  }

  /** Partien der Begegnung = Brett + online der Meldeliste des Gegners; ohne diesen Block alle. */
  gamesCount(s: GameSources): string {
    const n = s.opponent ? s.opponent.boardTotal + s.opponent.onlineTotal : s.boardTotal + s.onlineTotal;
    return thousands(n);
  }

  /** „64 %" — ohne Nenner „–". */
  share(n: number, of: number): string {
    return of ? `${Math.round((n / of) * 100)} %` : '–';
  }

  /** „R1 71 % · R2 58 %" — die Runden einer Liga (Spieler). */
  /** „erw. 31 %" — wie oft es nach den angesagten Prozenten hätte treffen sollen (`e*` in Tausendsteln je Brett). */
  exp(e: number | undefined, of: number): string {
    return of && e != null ? `erw. ${Math.round(e / of / 10)} %` : '';
  }

  /**
   * Kalibrierung: je Stufe angesagt Ø und eingetroffen in %, dazu die mittlere Abweichung — seit 0.659.0 gewichtet nach der
   * angesagten Wahrscheinlichkeit (eine 50-%-Angabe zählt mehr als eine 3-%-Angabe; nach Fällen gewichtet dominierten die
   * vielen kleinen Angaben) — und „korrekt" = 100 − Abweichung (Wunsch 2026-10-05). `said`/`guess`: was die Prognose dem gab,
   * der wirklich kam, und was gleichmäßiges Raten über die Meldeliste gäbe — ohne diese zweite Zahl wäre auch „jeder gleich"
   * perfekt kalibriert.
   */
  readonly calibration = computed(() => {
    const st = this.stats();
    const cal = st?.calibration?.filter(b => b.n > 0);
    if (!cal?.length) return null;
    const rows = cal.map(b => ({ from: b.from, n: b.n, said: Math.round(b.p / b.n / 10), came: Math.round((b.hits / b.n) * 100) }));
    const mass = cal.reduce((s, b) => s + b.p, 0);
    if (!mass) return null;
    const gap = cal.reduce((s, b) => s + Math.abs(b.p / b.n / 10 - (b.hits / b.n) * 100) * b.p, 0) / mass;
    const de = (x: number) => (Math.round(x * 10) / 10).toString().replace('.', ',');
    const t = st!.total;
    return {
      rows, gap: de(gap), score: de(Math.max(0, 100 - gap)),
      said: t.of && t.pa != null ? Math.round(t.pa / t.of / 10) : null,
      guess: t.of && t.pb != null ? Math.round(t.pb / t.of / 10) : null,
    };
  });

  /** „R1 31 % · R2 44 %" — Platz 1 je Runde einer Liga. */
  roundsText(rounds: (ForecastTally & { round: number })[]): string {
    return rounds.map(r => `R${r.round} ${this.share(r.top1, r.of)}`).join(' · ');
  }

  /** Brett aus Sicht des eigenen Vereins: Schwarz unten, wenn er an diesem Brett Schwarz hatte. */
  ownIsBlack(p: FixturePairing): boolean {
    const b = this.fixture()?.boards?.find(x => x.board === p.board);
    return b?.opp_color === 'w';
  }

  readonly rookHub = rookHubUrlForLeagueHub();
  private readonly handoff = inject(HandoffService);

  /** Die Vereinspartie auf RookHubs Partieseite (wie „Analyse" in der Vereinsliste), mit Einmal-Code angemeldet. */
  openInRookHub(id: number): void {
    void this.handoff.jumpToRookHub(`club-games/${id}`);
  }

  colorName(b: Board): string {
    return b.opp_color === 'w' ? 'Gegner hat Weiß' : 'Gegner hat Schwarz';
  }

  inTop(b: Board): boolean {
    return b.cand.some(c => c.n === b.actual?.n);
  }

  openCard(fide: string, color: 'w' | 's' | null, board: number | null): void {
    void this.card().open(fide, color, board, this.shareToken());
  }

  waLink(text: string): string {
    return `https://wa.me/?text=${encodeURIComponent(text)}`;
  }

  linkMessage(url: string): string {
    return `${this.leagueName()} R${this.round()}: ${shortTeam(this.home())} – ${shortTeam(this.away())}, Gegner-Prognose je Brett:\n${url}`;
  }

  async shareWhatsApp(): Promise<void> {
    const text = shareText(this.leagueName(), this.team(), this.round(), this.fixture()!);
    const mobile = window.matchMedia('(pointer: coarse)').matches;
    if (mobile && navigator.share) {
      try { await navigator.share({ text }); return; } catch (err) {
        if ((err as Error)?.name === 'AbortError') return;   // im Teilen-Menü abgebrochen
      }
    }
    let copied = false;
    try { await navigator.clipboard.writeText(text); copied = true; } catch { /* ohne Berechtigung */ }
    this.shareOut.set({ kind: 'text', text, copied, url: '', token: '', until: '' });
  }

  async shareLink(): Promise<void> {
    this.shareOut.set({ kind: 'info', text: 'Link wird erstellt …', url: '', token: '', until: '' });
    try {
      const r = await this.api.createShare(this.tnr()!, this.round(), this.team());
      const url = `${location.origin}/s/${r.token}`;
      this.copied.set(false);
      this.shareOut.set({ kind: 'link', text: '', url, token: r.token, until: r.expires.split('-').reverse().join('.') });
    } catch (err) {
      const msg = (err as { error?: { message?: string } })?.error?.message ?? 'Link nicht erstellt.';
      this.shareOut.set({ kind: 'error', text: msg, url: '', token: '', until: '' });
    }
  }

  async copy(url: string): Promise<void> {
    try { await navigator.clipboard.writeText(url); this.copied.set(true); } catch { /* Feld ist markierbar */ }
  }

  async revoke(token: string): Promise<void> {
    try {
      await this.api.deleteShare(token);
      this.shareOut.set({ kind: 'info', text: 'Link widerrufen. Wer ihn öffnet, sieht nur noch „Link ungültig".', url: '', token: '', until: '' });
    } catch {
      this.shareOut.set({ kind: 'error', text: 'Widerrufen hat nicht geklappt.', url: '', token: '', until: '' });
    }
  }
}
