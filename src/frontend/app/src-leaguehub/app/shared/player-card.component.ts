import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { AuthService } from '@rh/core/auth.service';
import { LeagueApiService } from '../core/league-api.service';
import { MyGamesService } from '../core/my-games.service';
import { NAME_VS_D4, NAME_VS_E4, NAME_WHITE, SPEED, de, pgnDate } from '../core/league-format';
import { OpeningStats, PlayerCard, ProfileView, RecentGame, TreeFilter } from '../core/league.models';
import { TREE_FILTER_KEY, effectiveTreeFilter, normalizeTreeFilter } from '../core/tree-filter';
import { localStore, readJson, writeJson } from '@rh/core/local-json-store';
import { GameReplayComponent } from './game-replay.component';
import { OnlineAccountsComponent } from './online-accounts.component';
import { OpeningTreeComponent } from './opening-tree.component';
import { TreeFilterBarComponent } from './tree-filter-bar.component';

type Show = 'w' | 's' | 'b';

/**
 * Spielerkarte als Dialog: Partien (Lumbra + chess-results + Vereins-Datenbank), Eröffnungsprofil, letzte Partien,
 * Online-Konten, PGN-Download. Aus einer Brett-Zeile geöffnet steht standardmäßig NUR die Farbe, die der
 * Spieler an diesem Brett hat (Wunsch des Nutzers), umschaltbar Weiß / Schwarz / Beide. Ein Klick auf eine der letzten
 * Partien spielt sie nach (Wunsch 2026-09-28) — die PGNs kommen erst beim ersten Klick (`…/recent`).
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
          @if (replay(); as r) {
            <div class="actions">
              <button type="button" class="btn-sec" (click)="replay.set(null)">← Zurück zur Karte</button>
              @if (myGames.available) {
                <button type="button" class="btn-sec" [disabled]="gameBusy()" (click)="toMyGames(r.pgn)"
                        title="Legt die Partie in deinen Partien in RookHub ab und öffnet sie dort">Zu meinen Partien</button>
                <button type="button" class="btn-sec" [disabled]="gameBusy()" (click)="shareGame(r.pgn)"
                        title="Öffentlicher Link zur Partie in RookHub (sie liegt dafür in deinen Partien)">Partie teilen</button>
              }
            </div>
            @if (gameNote(); as n) { <p class="small game-note" [class.err]="n.err" role="status">{{ n.text }}</p> }
            <lh-game-replay [pgn]="r.pgn" [flipped]="r.flipped" />
          } @else {
          @if (color()) {
            <p class="hint">An Brett {{ board() }} spielt {{ lastName(c) }} mit <b>{{ color() === 'w' ? 'Weiß' : 'Schwarz' }}</b>.</p>
          }
          @if (c.n || c.online) {
            <div class="actions">
              @if (c.n) {
                <div class="seg" role="group" aria-label="Farbe">
                  @for (o of options; track o.k) {
                    <button type="button" [attr.aria-pressed]="show() === o.k" (click)="show.set(o.k)">{{ o.label }}</button>
                  }
                </div>
                <button type="button" class="btn-sec" (click)="download(c)">PGN herunterladen ({{ c.n }} Partien)</button>
              }
              <button type="button" class="btn-sec tree-toggle" [attr.aria-expanded]="treeOpen()" (click)="treeOpen.set(!treeOpen())">
                {{ treeOpen() ? 'Eröffnungsbaum schließen' : 'Eröffnungsbaum anzeigen' }}</button>
            </div>
            <lh-tree-filter-bar [filter]="filter()" [active]="active()" [boardGames]="c.n" [onlineGames]="c.online ?? 0"
                                [unsureGames]="c.onlineUnsure ?? 0" [token]="token" (changed)="setFilter($event)" />
            @if (!storedProfile()) {
              <p class="muted small filtered" role="status">
                @if (profile(); as p) { Gefiltert: {{ p.n }} Partien@if (p.online) { ({{ p.board }} am Brett, {{ p.online }} online)}
                  @if (p.years) { · {{ p.years[0] }}–{{ p.years[1] }} } }
                @if (profileLoading()) { — lade … }
                @if (profileError()) { <span class="err">{{ profileError() }}</span> }
              </p>
            }
            @if (treeOpen()) {
              <lh-opening-tree [fide]="c.fide" [token]="token" [startColor]="show() === 's' ? 's' : 'w'" [filter]="active()" />
            }
          }
          @if (sections(); as sec) {
            @if (sec.n) {
              @if (show() !== 's') {
                <h3>Mit Weiß <span class="muted">({{ sec.white?.n || 0 }} Partien)</span></h3>
                <ng-container *ngTemplateOutlet="first; context: { s: sec.white, names: nameWhite }" />
              }
              @if (show() !== 'w') {
                <h3>Mit Schwarz gegen 1.e4 <span class="muted">({{ sec.black_e4?.n || 0 }})</span></h3>
                <ng-container *ngTemplateOutlet="first; context: { s: sec.black_e4, names: nameE4 }" />
                <h3>Mit Schwarz gegen 1.d4 <span class="muted">({{ sec.black_d4?.n || 0 }})</span></h3>
                <ng-container *ngTemplateOutlet="first; context: { s: sec.black_d4, names: nameD4 }" />
                @if (sec.black_other?.n) {
                  <h3>Mit Schwarz gegen andere <span class="muted">({{ sec.black_other?.n }})</span></h3>
                  <ng-container *ngTemplateOutlet="first; context: { s: sec.black_other, names: null }" />
                }
              }
            } @else if (!storedProfile() && !profileLoading()) {
              <p class="muted">Keine Partien mit diesem Filter.</p>
            }
          }
          @if (c.recent?.length) {
            <h3>Letzte Partien@if (show() !== 'b') { mit {{ show() === 'w' ? 'Weiß' : 'Schwarz' }} }
              <span class="muted small">— anklicken zum Nachspielen</span></h3>
            @if (replayError()) { <p class="err small">{{ replayError() }}</p> }
            @if (recentShown(); as list) {
              @if (!list.length) { <p class="muted small">Keine Partien mit {{ show() === 'w' ? 'Weiß' : 'Schwarz' }}.</p> }
            } @else { <p class="muted small">Lade Partien …</p> }
            <table class="recent">
              @for (g of recentShown() ?? []; track $index) {
                <tr tabindex="0" role="button" [class.busy]="replayLoading()"
                    [attr.aria-label]="'Partie gegen ' + (g.vs || '?') + ' nachspielen'"
                    (click)="openGame(c, $index)" (keydown.enter)="openGame(c, $index)">
                  <td class="num muted">{{ date(g.date) }}</td>
                  <td><span class="sq mini" [class.w]="g.color === 'w'" [class.s]="g.color === 's'"
                            [attr.aria-label]="g.color === 'w' ? 'Weiß' : 'Schwarz'"></span>{{ g.vs || '?' }}
                    @if (g.vs_elo) { <span class="muted">{{ g.vs_elo }}</span> }
                    <br><span class="muted">{{ g.event }}</span>
                    @if (speed(g.event ?? "")) { <span class="tag">Blitz/Schnell</span> }</td>
                  <td class="muted">{{ de(g.opening ?? '') }}</td>
                  <td class="num"><b>{{ g.score === null ? '–' : g.score === 0.5 ? '½' : g.score }}</b></td>
                </tr>
              }
            </table>
          }
          @if (c.accounts.length || canEdit() || token) {
            <h3>Online-Konten</h3>
            <lh-online-accounts class="acc" [fide]="c.fide" [accounts]="c.accounts" [canEdit]="canEdit()" [shareToken]="token"
                                (changed)="reloadCard()" />
            <p class="muted small-note">„gesichert": das Konto gehört sicher diesem Spieler, „unsicher": nur vermutet.
              Über einen Teilen-Link erscheinen nur gesicherte. Ihre Partien holt LeagueHub im Hintergrund — im Eröffnungsbaum
              wählbar.</p>
          }
          }
          <p class="muted small-note spaced">Quellen: Lumbra's GigaBase (Turnierpartien, Stand Juli 2026), die ChessBase-Megabase, die Partiedatenbank von chess-results.com, Lichess-Übertragungen von Turnieren am Brett und die Vereinspartien von SK Schwaz (nur mit Jahr). Zuordnung über die FIDE-ID. Blitz- und Schnellschach sind mitgezählt.
            @if (c.online) { Online-Partien der eingetragenen Konten zählen im Eröffnungsprofil und im Baum, wenn oben „Brett + online" oder „Online" gewählt ist (unsichere Konten nur mit dem Schalter). }</p>
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
  imports: [NgTemplateOutlet, OpeningTreeComponent, GameReplayComponent, OnlineAccountsComponent, TreeFilterBarComponent],
})
export class PlayerCardComponent {
  private readonly api = inject(LeagueApiService);
  private readonly auth = inject(AuthService);
  readonly myGames = inject(MyGamesService);
  /** Online-Konten pflegen: Verwalter, nie über einen Teilen-Link. */
  readonly canEdit = signal(false);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  readonly card = signal<PlayerCard | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly color = signal<'w' | 's' | null>(null);
  readonly board = signal<number | null>(null);
  readonly show = signal<Show>('b');
  readonly treeOpen = signal(false);
  /** Der gemerkte Filter (je Gerät, für alle Spieler) — gilt für Eröffnungsprofil und Baum (0.617.0). */
  readonly filter = signal<TreeFilter>(normalizeTreeFilter(readJson(localStore(), TREE_FILTER_KEY)));
  /** Was gefragt wird — ohne Online-Partien nur das Brett, ohne Brettpartien gleich online. */
  readonly active = computed(() => {
    const c = this.card();
    return effectiveTreeFilter(this.filter(), c?.n ?? 0, c?.online ?? 0);
  });
  /** Nur Brettpartien aller Jahre: das ist die gespeicherte Karte, ohne eigenen Abruf. */
  readonly storedProfile = computed(() => this.active().source === 'board' && !this.active().years);
  /** Das Profil über gefilterte Partien (nur mit Filter). */
  readonly profile = signal<ProfileView | null>(null);
  readonly profileLoading = signal(false);
  readonly profileError = signal<string | null>(null);
  /** Was die Abschnitte zeigen: die Karte selbst oder das gefilterte Profil (`null` = wird geholt). */
  readonly sections = computed<PlayerCard | ProfileView | null>(() => this.storedProfile() ? this.card() : this.profile());
  private profileKey = '';
  /** Die gerade nachgespielte Partie (statt der Karte). */
  readonly replay = signal<{ pgn: string; flipped: boolean } | null>(null);
  readonly replayLoading = signal(false);
  readonly replayError = signal<string | null>(null);
  /** „Zu meinen Partien" / „Partie teilen" läuft gerade bzw. was dabei herauskam. */
  readonly gameBusy = signal(false);
  readonly gameNote = signal<{ text: string; err: boolean } | null>(null);
  /** Schon abgelegte Partien dieser Karte (PGN → Id in RookHub) — Teilen nach dem Ablegen fragt nicht noch einmal. */
  private readonly savedIds = new Map<string, number>();
  /** Die letzten Partien samt PGN — einmal je geöffneter Karte geholt. */
  private recentGames: RecentGame[] | null = null;
  /** Die letzten Partien JE FARBE (0.592.0): oben Weiß/Schwarz gewählt → die letzten acht dieser Farbe, samt PGN. */
  private readonly byColor = signal<Partial<Record<'w' | 's', RecentGame[]>>>({});
  /** Was die Liste zeigt: bei „Beide" die Liste der Karte, sonst die der Farbe (`null` = wird geholt). */
  readonly recentShown = computed<(RecentGame | NonNullable<PlayerCard['recent']>[number])[] | null>(() => {
    const c = this.card(), s = this.show();
    if (!c) return [];
    if (s === 'b') return c.recent ?? [];
    return this.byColor()[s] ?? null;
  });

  constructor() {
    // Filter gesetzt: das Profil über die gefilterten Partien holen — je Karte und Filter einmal.
    effect(() => {
      const c = this.card(), f = this.active(), stored = this.storedProfile();
      if (!c || stored) return;
      const key = `${this.seq}:${JSON.stringify(f)}`;
      if (key === this.profileKey) return;
      this.profileKey = key;
      untracked(() => void this.loadProfile(c.fide, f, key));
    });
    // Farbe gewählt (oder die Karte aus einer Brett-Zeile mit Farbe geöffnet): deren letzte Partien holen, einmal je Karte.
    effect(() => {
      const c = this.card(), s = this.show();
      if (c && s !== 'b' && !this.byColor()[s]) void this.loadColor(c.fide, s);
    });
  }

  private readonly loadingColor = new Set<string>();

  private async loadProfile(fide: string, f: TreeFilter, key: string): Promise<void> {
    this.profileLoading.set(true);
    this.profileError.set(null);
    try {
      const p = await this.api.profile(fide, this.token, f);
      if (key === this.profileKey) this.profile.set(p);
    } catch {
      if (key === this.profileKey) this.profileError.set('Das gefilterte Profil konnte nicht geladen werden.');
    } finally {
      if (key === this.profileKey) this.profileLoading.set(false);
    }
  }

  setFilter(f: TreeFilter): void {
    this.filter.set(normalizeTreeFilter(f));
    writeJson(localStore(), TREE_FILTER_KEY, this.filter());   // nur eine Bequemlichkeit — scheitert still
  }

  private async loadColor(fide: string, color: 'w' | 's'): Promise<void> {
    const key = `${this.seq}:${color}`;
    if (this.loadingColor.has(key)) return;
    this.loadingColor.add(key);
    const my = this.seq;
    try {
      const r = await this.api.recent(fide, this.token, color);
      if (my === this.seq) this.byColor.update(m => ({ ...m, [color]: r.games }));
    } catch {
      if (my === this.seq) this.byColor.update(m => ({ ...m, [color]: [] }));
    } finally {
      this.loadingColor.delete(key);
    }
  }
  token: string | null = null;
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
    this.canEdit.set(!token && this.auth.has('league.manage'));
    this.color.set(color);
    this.board.set(board);
    this.show.set(color ?? 'b');
    this.card.set(null);
    this.treeOpen.set(false);
    this.profile.set(null);
    this.profileError.set(null);
    this.profileLoading.set(false);
    this.replay.set(null);
    this.replayError.set(null);
    this.gameNote.set(null);
    this.savedIds.clear();
    this.recentGames = null;
    this.byColor.set({});
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

  /** Nach einer Änderung an den Online-Konten: Karte frisch (Konten, Stand des Abrufs, Partienzahl). */
  async reloadCard(): Promise<void> {
    const c = this.card();
    if (!c) return;
    const my = this.seq;
    try {
      const fresh = await this.api.card(c.fide, this.token);
      if (my === this.seq) this.card.set(fresh);
    } catch { /* die alte Karte bleibt stehen */ }
  }

  onClosed(): void {
    this.seq++;
    this.card.set(null);
    this.replay.set(null);
    this.loading.set(false);
  }

  /** Eine der letzten Partien nachspielen. Gefunden wird sie über Datum, Gegner und Farbe — die Liste zum Nachspielen
   * ist frisch gerechnet, die Karte kann älter sein. */
  async openGame(c: PlayerCard, i: number): Promise<void> {
    // Nach Farbe gefiltert: die Zeile bringt ihr PGN schon mit.
    const shown = this.recentShown()?.[i];
    if (shown && 'pgn' in shown && shown.pgn) {
      this.gameNote.set(null);
      this.replay.set({ pgn: shown.pgn, flipped: shown.color === 's' });
      return;
    }
    const want = c.recent?.[i];
    if (!want || this.replayLoading()) return;
    const my = this.seq;
    this.replayError.set(null);
    if (!this.recentGames) {
      this.replayLoading.set(true);
      try {
        const r = await this.api.recent(c.fide, this.token);
        if (my !== this.seq) return;
        this.recentGames = r.games;
      } catch {
        if (my === this.seq) this.replayError.set('Die Partie konnte nicht geladen werden.');
        return;
      } finally {
        if (my === this.seq) this.replayLoading.set(false);
      }
    }
    const same = (g: RecentGame) => g.date === want.date && g.vs === want.vs && g.color === want.color;
    const list = this.recentGames;
    const hit = list[i] && same(list[i]) ? list[i] : list.find(same);
    if (!hit) { this.replayError.set('Diese Partie ist nicht mehr da — bitte die Karte neu öffnen.'); return; }
    this.gameNote.set(null);
    this.replay.set({ pgn: hit.pgn, flipped: hit.color === 's' });
  }

  /** In RookHubs „Meine Partien" legen und gleich dorthin springen (Wunsch 2026-09-28). */
  async toMyGames(pgn: string): Promise<void> {
    const id = await this.saveGame(pgn);
    if (id === null) return;
    this.gameBusy.set(true);
    this.gameNote.set({ text: 'Liegt in deinen Partien — RookHub wird geöffnet …', err: false });
    try {
      await this.myGames.open(id);
    } finally {
      this.gameBusy.set(false);
    }
  }

  /** RookHubs öffentlichen Link zur Partie teilen: am Handy das Teilen-Blatt, sonst in die Zwischenablage. */
  async shareGame(pgn: string): Promise<void> {
    const id = await this.saveGame(pgn);
    if (id === null) return;
    this.gameBusy.set(true);
    try {
      const url = await this.myGames.shareUrl(id);
      const nav = navigator as Navigator & { share?: (d: ShareData) => Promise<void> };
      if (typeof nav.share === 'function' && matchMedia('(pointer: coarse)').matches) {
        try {
          await nav.share({ url, title: 'Partie auf RookHub' });
          this.gameNote.set({ text: 'Geteilt.', err: false });
          return;
        } catch (e) {
          if ((e as { name?: string })?.name === 'AbortError') return;     // im Teilen-Blatt abgebrochen
        }
      }
      try {
        await navigator.clipboard.writeText(url);
        this.gameNote.set({ text: `Link kopiert: ${url}`, err: false });
      } catch {
        // Ohne Zwischenablage (verweigert, unsicherer Kontext) steht der Link da — man kann ihn abschreiben.
        this.gameNote.set({ text: `Link zur Partie: ${url}`, err: false });
      }
    } catch {
      this.gameNote.set({ text: 'Der Link konnte nicht geholt werden.', err: true });
    } finally {
      this.gameBusy.set(false);
    }
  }

  private async saveGame(pgn: string): Promise<number | null> {
    const known = this.savedIds.get(pgn);
    if (known !== undefined) return known;
    this.gameBusy.set(true);
    this.gameNote.set(null);
    try {
      const id = await this.myGames.save(pgn);
      if (id === null) {
        this.gameNote.set({ text: 'Diese Partie lässt sich nicht übernehmen.', err: true });
        return null;
      }
      this.savedIds.set(pgn, id);
      return id;
    } catch {
      this.gameNote.set({ text: 'Übernehmen hat nicht geklappt.', err: true });
      return null;
    } finally {
      this.gameBusy.set(false);
    }
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
    const parts = Object.entries(c.src || {}).map(([k, v]) => `${k} ${v}`);
    if (c.online) parts.push(`online ${c.online}`);                       // nur im Eröffnungsbaum gezählt
    return parts.length ? ` – ${parts.join(', ')}` : '';
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
