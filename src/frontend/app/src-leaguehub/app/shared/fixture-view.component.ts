import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { LeagueApiService } from '../core/league-api.service';
import { PHASE_TEXT, pct, shareText, shortTeam, tn } from '../core/league-format';
import { Board, Fixture, ForecastStats, ForecastTally, GameSources } from '../core/league.models';
import { GameSourcesComponent } from './game-sources.component';
import { thousands } from '../core/game-sources';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';

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
  imports: [PlayerCardComponent, GameSourcesComponent],
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
                <span>Prognose@if (stats(); as st) {@if (st.total.of) { · bisher <b>{{ share(st.total.players, st.total.of) }}</b> der Aufgestellten richtig}}@if (e.eval; as v) { · hier {{ v.players }} von {{ v.of }}}</span>
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
                  <p class="note">In dieser Begegnung: {{ v.players }} von {{ v.of }} Aufgestellten waren unter den {{ e.boards?.length }} wahrscheinlichsten,
                    an {{ v.boards }} Brettern saß genau der erste Vorschlag.</p>
                }
                @if (stats(); as st) {
                  @if (st.total.fixtures) {
                    <p class="note">Bisher in der Saison {{ st.season }}, über alle Begegnungen aller Ligen (jede Begegnung zweimal: je eine Prognose
                      für jede Mannschaft). „Spieler" = Aufgestellte unter den wahrscheinlichsten, „Brett" = erster Vorschlag genau am Brett.</p>
                    <div class="src-scroll"><table class="src-tbl stats-tbl">
                      <thead><tr><th scope="col"></th><th scope="col" class="num">Prognosen</th><th scope="col" class="num">Spieler</th><th scope="col" class="num">Brett</th></tr></thead>
                      <tbody>
                        <tr class="src-group"><th scope="rowgroup" colspan="4">Je Runde</th></tr>
                        @for (r of st.rounds; track r.round) {
                          <tr><td>Runde {{ r.round }}</td><td class="num">{{ r.fixtures }}</td>
                            <td class="num">{{ share(r.players, r.of) }}</td><td class="num">{{ share(r.boards, r.of) }}</td></tr>
                        }
                        <tr class="src-group"><th scope="rowgroup" colspan="4">Je Liga</th></tr>
                        @for (l of st.leagues; track l.tnr) {
                          <tr [class.mine]="l.name === leagueName()">
                            <td>{{ l.name }}<span class="src-sub">{{ roundsText(l.rounds) }}</span></td><td class="num">{{ l.fixtures }}</td>
                            <td class="num">{{ share(l.players, l.of) }}</td><td class="num">{{ share(l.boards, l.of) }}</td>
                          </tr>
                        }
                        <tr class="src-group total"><th scope="row">Gesamt</th><td class="num">{{ st.total.fixtures }}</td>
                          <td class="num">{{ share(st.total.players, st.total.of) }}</td><td class="num">{{ share(st.total.boards, st.total.of) }}</td></tr>
                      </tbody>
                    </table></div>
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

  constructor() {
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
  roundsText(rounds: (ForecastTally & { round: number })[]): string {
    return rounds.map(r => `R${r.round} ${this.share(r.players, r.of)}`).join(' · ');
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
