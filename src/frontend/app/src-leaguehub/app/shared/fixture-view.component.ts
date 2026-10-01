import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { LeagueApiService } from '../core/league-api.service';
import { PHASE_TEXT, pct, shareText, shortTeam, tn } from '../core/league-format';
import { Board, Fixture } from '../core/league.models';
import { PlayerCardComponent } from './player-card.component';

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
  imports: [PlayerCardComponent],
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
            <p class="note">
              @if (e.status === 'played') { Pro Brett oben, wer tatsächlich gespielt hat, darunter die Prognose, die vor der Runde galt. }
              @else { Prognose für {{ tn(e.opp) }}: pro Brett die drei wahrscheinlichsten Spieler. }
              @if (e.hit) { In alten Saisonen lagen in dieser Lage ({{ phaseText() }}) im Schnitt {{ hitText() }} von {{ e.boards?.length }} vorhergesagten Spielern richtig. }
              Das Quadrat zeigt die Farbe des Gegners.
            </p>
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
