import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { LeagueApiService } from '../core/league-api.service';
import { NAME_VS_D4, NAME_VS_E4, NAME_WHITE, SPEED, de, pgnDate } from '../core/league-format';
import { OpeningStats, PlayerCard } from '../core/league.models';

type Show = 'w' | 's' | 'b';

/**
 * Spielerkarte als Dialog: Partien (Lumbra + chess-results + Vereins-Datenbank), Eröffnungsprofil, letzte Partien,
 * Online-Konten, PGN-Download. Aus einer Brett-Zeile geöffnet steht standardmäßig NUR die Farbe, die der
 * Spieler an diesem Brett hat (Wunsch des Nutzers), umschaltbar Weiß / Schwarz / Beide.
 */
@Component({
  selector: 'lh-player-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog #dlg class="card" aria-labelledby="card-name" (click)="backdrop($event)" (close)="onClosed()">
      <div class="card-head">
        <div>
          <h2 id="card-name">{{ card()?.name || (loading() ? '…' : '') }}</h2>
          @if (card(); as c) {
            <p class="card-meta">
              @if (c.n) { {{ c.n }} Partien @if (c.years) { ({{ c.years[0] }}–{{ c.years[1] }}) } }
              @else { Keine Partien gefunden }
              <span class="muted">{{ srcText(c) }}</span> –
              <a [href]="'https://ratings.fide.com/profile/' + c.fide" target="_blank" rel="noopener">FIDE-Profil</a>
            </p>
          }
        </div>
        <button type="button" class="card-close" aria-label="Schließen" (click)="close()">✕</button>
      </div>
      <div class="card-body">
        @if (loading()) { <p class="muted">Lade Partien …</p> }
        @if (error()) { <p>{{ error() }}</p> }
        @if (card(); as c) {
          @if (color()) {
            <p class="hint">An Brett {{ board() }} spielt {{ lastName(c) }} mit <b>{{ color() === 'w' ? 'Weiß' : 'Schwarz' }}</b>.</p>
          }
          @if (c.n) {
            <div class="actions">
              <div class="seg" role="group" aria-label="Farbe">
                @for (o of options; track o.k) {
                  <button type="button" [attr.aria-pressed]="show() === o.k" (click)="show.set(o.k)">{{ o.label }}</button>
                }
              </div>
              <button type="button" class="btn-sec" (click)="download(c)">PGN herunterladen ({{ c.n }} Partien)</button>
            </div>
            @if (show() !== 's') {
              <h3>Mit Weiß <span class="muted">({{ c.white?.n || 0 }} Partien)</span></h3>
              <ng-container *ngTemplateOutlet="first; context: { s: c.white, names: nameWhite }" />
            }
            @if (show() !== 'w') {
              <h3>Mit Schwarz gegen 1.e4 <span class="muted">({{ c.black_e4?.n || 0 }})</span></h3>
              <ng-container *ngTemplateOutlet="first; context: { s: c.black_e4, names: nameE4 }" />
              <h3>Mit Schwarz gegen 1.d4 <span class="muted">({{ c.black_d4?.n || 0 }})</span></h3>
              <ng-container *ngTemplateOutlet="first; context: { s: c.black_d4, names: nameD4 }" />
              @if (c.black_other?.n) {
                <h3>Mit Schwarz gegen andere <span class="muted">({{ c.black_other.n }})</span></h3>
                <ng-container *ngTemplateOutlet="first; context: { s: c.black_other, names: null }" />
              }
            }
          }
          @if (c.recent?.length) {
            <h3>Letzte Partien</h3>
            <table>
              @for (g of c.recent; track $index) {
                <tr>
                  <td class="num muted">{{ date(g.date) }}</td>
                  <td><span class="sq mini" [class.w]="g.color === 'w'" [class.s]="g.color === 's'"
                            [attr.aria-label]="g.color === 'w' ? 'Weiß' : 'Schwarz'"></span>{{ g.vs || '?' }}
                    @if (g.vs_elo) { <span class="muted">{{ g.vs_elo }}</span> }
                    <br><span class="muted">{{ g.event }}</span>
                    @if (speed(g.event)) { <span class="tag">Blitz/Schnell</span> }</td>
                  <td class="muted">{{ de(g.opening) }}</td>
                  <td class="num"><b>{{ g.score === null ? '–' : g.score === 0.5 ? '½' : g.score }}</b></td>
                </tr>
              }
            </table>
          }
          @if (c.accounts.length) {
            <h3>Online</h3>
            <p class="acc">
              @for (a of c.accounts; track a.url) {
                <a [href]="a.url" target="_blank" rel="noopener">{{ a.site }}: {{ a.user }}</a>
                <span class="tag">{{ a.conf === 'sicher' ? 'sicher' : 'wahrscheinlich' }}</span><br>
              }
            </p>
            <p class="muted small-note">Nur Konten, die der Spieler selbst mit seinem Namen verbunden hat. „wahrscheinlich": Klarname und Land passen, der Name ist unter FIDE-Spielern eindeutig.</p>
          }
          <p class="muted small-note spaced">Quellen: Lumbra's GigaBase (Turnierpartien, Stand Juli 2026), die Partiedatenbank von chess-results.com und die Vereinspartien von SK Schwaz (nur mit Jahr). Zuordnung über die FIDE-ID. Blitz- und Schnellschach sind mitgezählt.</p>
        }
      </div>
    </dialog>

    <ng-template #first let-s="s" let-names="names">
      @if (!s?.first?.length) { <p class="muted">Keine Partien mit Zügen.</p> }
      @else {
        <table>
          @for (r of s.first; track r[0]) {
            <tr><td>{{ names?.[r[0]] || de(r[0]) }}</td><td class="num">{{ share(r[1], s.n) }} %</td>
              <td class="num muted">{{ r[1] }}×</td><td class="num muted">{{ r[2] === null ? '' : 'Score ' + r[2] + ' %' }}</td></tr>
          }
        </table>
        @if (s.lines?.length) {
          <p class="muted sub">Häufigste Zugfolgen</p>
          <table>
            @for (l of s.lines.slice(0, 4); track l[0]) {
              <tr><td>{{ de(l[0]) }}</td><td class="num muted">{{ l[1] }}×</td><td class="num muted">{{ l[2] === null ? '' : l[2] + ' %' }}</td></tr>
            }
          </table>
        }
      }
    </ng-template>
  `,
  imports: [NgTemplateOutlet],
})
export class PlayerCardComponent {
  private readonly api = inject(LeagueApiService);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  readonly card = signal<PlayerCard | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly color = signal<'w' | 's' | null>(null);
  readonly board = signal<number | null>(null);
  readonly show = signal<Show>('b');
  private token: string | null = null;
  /** Zählt die Öffnungen: eine späte Antwort für einen inzwischen anderen (oder geschlossenen) Spieler wird verworfen. */
  private seq = 0;

  readonly options: { k: Show; label: string }[] = [{ k: 'w', label: 'Weiß' }, { k: 's', label: 'Schwarz' }, { k: 'b', label: 'Beide' }];
  readonly nameWhite = NAME_WHITE;
  readonly nameE4 = NAME_VS_E4;
  readonly nameD4 = NAME_VS_D4;
  readonly de = de;
  readonly date = pgnDate;
  readonly speed = (e: string) => SPEED.test(e || '');

  /** Öffnet die Karte; <paramref name="token"/> = Teilen-Link (dann ohne Anmeldung). */
  async open(fide: string, color: 'w' | 's' | null, board: number | null, token: string | null): Promise<void> {
    this.token = token;
    this.color.set(color);
    this.board.set(board);
    this.show.set(color ?? 'b');
    this.card.set(null);
    this.error.set(null);
    this.loading.set(true);
    const my = ++this.seq;
    const d = this.dlg().nativeElement;
    if (!d.open) d.showModal();
    try {
      const c = await this.api.card(fide, token);
      if (my === this.seq) this.card.set(c);
    } catch {
      if (my === this.seq) this.error.set('Keine Partien gefunden.');
    } finally {
      if (my === this.seq) this.loading.set(false);
    }
  }

  close(): void {
    this.dlg().nativeElement.close();
  }

  onClosed(): void {
    this.seq++;
    this.card.set(null);
    this.loading.set(false);
  }

  backdrop(ev: MouseEvent): void {
    if (ev.target === this.dlg().nativeElement) this.close();
  }

  lastName(c: PlayerCard): string {
    return (c.name || '').split(',')[0] || 'der Spieler';
  }

  share(n: number, total: number): number {
    return total ? Math.round((100 * n) / total) : 0;
  }

  srcText(c: PlayerCard): string {
    const s = Object.entries(c.src || {}).map(([k, v]) => `${k} ${v}`).join(', ');
    return s ? ` – ${s}` : '';
  }

  async download(c: PlayerCard): Promise<void> {
    let blob: Blob;
    try {
      blob = await this.api.pgn(c.fide, this.token);
    } catch {
      this.error.set('Die PGN-Datei konnte nicht geladen werden.');
      return;
    }
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${(c.name || c.fide).split(',').map(x => x.trim()).join('_').replace(/[^\w\-äöüÄÖÜß]+/g, '')}_${c.fide}.pgn`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 5000);
  }
}
