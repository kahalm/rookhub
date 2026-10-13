import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { AuthService } from '@rh/core/auth.service';
import { PLAYER_CARD_API } from './player-card-api';
import { MyGamesService } from '@lh/core/my-games.service';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SPEED } from '@lh/core/league-format';
import { cardDate, cardLang, cardPct, cardSanText } from './card-lang';
import { OpeningStats, PlayerCard, ProfileView, RecentGame, TreeFilter } from '@lh/core/league.models';
import { TREE_FILTER_KEY, effectiveTreeFilter, normalizeTreeFilter } from './tree-filter';
import { localStore, readJson, writeJson } from '@rh/core/local-json-store';
import { downloadBlob } from '@rh/shared/download.util';
import { GameReplayComponent } from './game-replay.component';
import { OnlineAccountsComponent } from './online-accounts.component';
import { OpeningTreeComponent } from './opening-tree.component';
import { TreeFilterBarComponent } from './tree-filter-bar.component';
import { TrainingLinesComponent } from './training-lines.component';

type Show = 'w' | 's' | 'b';

/** Eröffnungsnamen zum ersten Zug (Sprachschlüssel, e1-rk-prep-card-german); ohne Namen steht der Zug selbst da. */
const OPENINGS_WHITE: Record<string, string> = {
  c4: 'playerCard.opening.english', Nf3: 'playerCard.opening.reti', f4: 'playerCard.opening.bird', b3: 'playerCard.opening.larsen',
};
const OPENINGS_VS_E4: Record<string, string> = {
  c5: 'playerCard.opening.sicilian', e6: 'playerCard.opening.french', c6: 'playerCard.opening.caroKann',
  d6: 'playerCard.opening.pircPhilidor', g6: 'playerCard.opening.modern', d5: 'playerCard.opening.scandinavian',
  Nf6: 'playerCard.opening.alekhine', b6: 'playerCard.opening.owen', Nc6: 'playerCard.opening.nimzowitsch',
};
const OPENINGS_VS_D4: Record<string, string> = {
  Nf6: 'playerCard.opening.indian', f5: 'playerCard.opening.dutch', g6: 'playerCard.opening.modern',
  c5: 'playerCard.opening.benoni', Nc6: 'playerCard.opening.chigorin',
};

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
    <dialog #dlg class="card" [class.inline]="inline()" aria-labelledby="card-name" (click)="backdrop($event)" (close)="onClosed()">
      <div class="card-head">
        <div>
          <h2 id="card-name">{{ card()?.name || (loading() ? '…' : '') }}</h2>
          @if (card(); as c) {
            <p class="card-meta">
              @if (c.n) { {{ (c.n === 1 ? 'playerCard.gamesOne' : 'playerCard.games') | translate: { n: c.n } }} @if (c.years) { ({{ c.years[0] }}–{{ c.years[1] }}) } }
              @else { {{ 'playerCard.noGames' | translate }} }
              <span class="muted">{{ srcText(c) }}</span>@if (c.fide) { –
              <a [href]="'https://ratings.fide.com/profile/' + c.fide" target="_blank" rel="noopener">{{ 'playerCard.fideProfile' | translate }}</a> }
              @if (photosUrl(c.name); as u) { –
              <a class="card-photos" [href]="u" target="_blank" rel="noopener"
                 [title]="'playerCard.photosTitle' | translate">{{ 'playerCard.photos' | translate }}</a> }
            </p>
          }
        </div>
        <button type="button" class="card-close" [attr.aria-label]="'common.close' | translate" (click)="close()">✕</button>
      </div>
      <div class="card-body">
        @if (loading()) { <p class="muted">{{ 'playerCard.loading' | translate }}</p> }
        @if (error(); as e) { <p>{{ e | translate }}</p> }
        @if (card(); as c) {
          @if (replay(); as r) {
            <div class="actions">
              <button type="button" class="btn-sec" (click)="replay.set(null)">{{ 'playerCard.backToCard' | translate }}</button>
              @if (myGames.available) {
                <!-- Wunsch 2026-10-05 (0.663.0): die Partie gleich analysiert ansehen, wie auf RookHubs Partieseite -->
                <button type="button" class="btn-pri" [disabled]="gameBusy()" (click)="analyzeGame(r.pgn)"
                        [title]="'playerCard.analyzeTitle' | translate">{{ 'playerCard.analyze' | translate }}</button>
                <button type="button" class="btn-sec" [disabled]="gameBusy()" (click)="toMyGames(r.pgn)"
                        [title]="'playerCard.toMyGamesTitle' | translate">{{ 'playerCard.toMyGames' | translate }}</button>
                <button type="button" class="btn-sec" [disabled]="gameBusy()" (click)="shareGame(r.pgn)"
                        [title]="'playerCard.shareGameTitle' | translate">{{ 'playerCard.shareGame' | translate }}</button>
              }
            </div>
            @if (gameNote(); as n) { <p class="small game-note" [class.err]="n.err" role="status">{{ n.key | translate: n.params }}</p> }
            <lh-game-replay [pgn]="r.pgn" [flipped]="r.flipped" />
          } @else {
          @if (color()) {
            <p class="hint">{{ (color() === 'w' ? 'playerCard.boardWhite' : 'playerCard.boardBlack') | translate: { board: board(), name: lastName(c) } }}</p>
          }
          @if (c.n || c.online) {
            <!-- Bedienzeile in einer Hülle: die Spielervorbereitung legt Farbwahl, Filter und Werkzeuge in EINE umbrechende
                 Zeile und klappt am Handy alles außer der Farbwahl hinter „Filter ▾" (prep-card.scss, z1-rk-prep-vorlauf).
                 In LeagueHub bleibt alles untereinander, den Klappknopf gibt es nur inline. -->
            <div class="controls" [class.open]="controlsOpen()" [class.tools-open]="toolsOpen()">
            <div class="actions">
              @if (c.n) {
                <div class="seg" role="group" [attr.aria-label]="'playerCard.colorGroup' | translate">
                  @for (o of options; track o.k) {
                    <button type="button" [attr.aria-pressed]="show() === o.k" (click)="show.set(o.k)">{{ o.label | translate }}</button>
                  }
                </div>
              }
              @if (inline()) {
                <button type="button" class="btn-sec ctl-toggle" [attr.aria-expanded]="controlsOpen()"
                        (click)="controlsOpen.set(!controlsOpen())">{{ 'playerCard.filter' | translate }} {{ controlsOpen() ? '▴' : '▾' }}</button>
                <!-- z2-rk-prep-filter-aktionen: PGN, Eröffnungsbaum und Trainingslinien sind Werkzeuge, keine Filter — am
                     Handy hinter einem eigenen ⋮ statt hinter „Filter ▾". -->
                <button type="button" class="btn-sec ctl-tools" [attr.aria-expanded]="toolsOpen()" [attr.aria-label]="'playerCard.tools' | translate"
                        [title]="'playerCard.tools' | translate" (click)="toolsOpen.set(!toolsOpen())">⋮</button>
              }
              @if (c.n) {
                <button type="button" class="btn-sec" (click)="download(c)">{{ (c.n === 1 ? 'playerCard.downloadPgnOne' : 'playerCard.downloadPgn') | translate: { n: c.n } }}</button>
              }
              <button type="button" class="btn-sec tree-toggle" [attr.aria-expanded]="treeOpen()" (click)="treeOpen.set(!treeOpen())">
                {{ (treeOpen() ? 'playerCard.treeHide' : 'playerCard.treeShow') | translate }}</button>
            </div>
            <lh-tree-filter-bar [filter]="filter()" [active]="active()" [boardGames]="c.n" [onlineGames]="c.online ?? 0"
                                [unsureGames]="c.onlineUnsure ?? 0" [token]="token" [unsure]="unsureAllowed()" (changed)="setFilter($event)" />
            @if (!storedProfile()) {
              <p class="muted small filtered" role="status">
                @if (profile(); as p) { {{ (p.n === 1 ? 'playerCard.filteredOne' : 'playerCard.filtered') | translate: { n: p.n } }}@if (p.online) { {{ 'playerCard.filteredSplit' | translate: { board: p.board, online: p.online } }}}
                  @if (p.years) { · {{ p.years[0] }}–{{ p.years[1] }} } }
                @if (profileLoading()) { {{ 'playerCard.loadingShort' | translate }} }
                @if (profileError(); as e) { <span class="err">{{ e | translate }}</span> }
              </p>
            }
            @if (treeOpen()) {
              <lh-opening-tree [fide]="key(c)" [token]="token" [startColor]="show() === 's' ? 's' : 'w'" [filter]="active()" />
            }
            @if (training()) {
              <!-- Trainingslinien gegen diesen Spieler (2026-10-07): nur angemeldet, nie über einen Teilen-Link -->
              <!-- Vorgabe der Farbe aus der Begegnung (Brett bekannt): color() = SEINE Farbe dort (2026-10-10) -->
              <lh-training-lines [key]="key(c)" [name]="c.name ?? ''" [filter]="active()" [opponentColor]="board() !== null ? color() : null" />
            }
            </div>
          }
          @if (sections(); as sec) {
            @if (sec.n) {
              <!-- Jeder Abschnitt in einer eigenen Hülle: die Spielervorbereitung füllt damit ein Zwei-Spalten-Raster
                   paarweise (Weiß | gegen 1.e4, gegen 1.d4 | gegen andere — prep-card.scss, d2-rk-prep-wide,
                   z1-rk-prep-spalten); in LeagueHub bleiben sie untereinander. -->
              <div class="sides">
              @if (show() !== 's') {
                <div class="side side-white">
                <h3>{{ 'playerCard.withWhite' | translate }} <span class="muted">({{ (sec.white?.n === 1 ? 'playerCard.gamesOne' : 'playerCard.games') | translate: { n: sec.white?.n || 0 } }})</span></h3>
                <ng-container *ngTemplateOutlet="first; context: { s: sec.white, names: OPENINGS_WHITE }" />
                </div>
              }
              @if (show() !== 'w') {
                <div class="side side-black">
                <h3>{{ 'playerCard.vsE4' | translate }} <span class="muted">({{ sec.black_e4?.n || 0 }})</span></h3>
                <ng-container *ngTemplateOutlet="first; context: { s: sec.black_e4, names: OPENINGS_VS_E4 }" />
                </div>
                <div class="side side-black">
                <h3>{{ 'playerCard.vsD4' | translate }} <span class="muted">({{ sec.black_d4?.n || 0 }})</span></h3>
                <ng-container *ngTemplateOutlet="first; context: { s: sec.black_d4, names: OPENINGS_VS_D4 }" />
                </div>
                @if (sec.black_other?.n) {
                  <div class="side side-black">
                  <h3>{{ 'playerCard.vsOther' | translate }} <span class="muted">({{ sec.black_other?.n }})</span></h3>
                  <ng-container *ngTemplateOutlet="first; context: { s: sec.black_other, names: null }" />
                  </div>
                }
              }
              </div>
            } @else if (!storedProfile() && !profileLoading()) {
              <p class="muted">{{ 'playerCard.noFilterGames' | translate }}</p>
            }
          }
          @if (c.recent?.length) {
            <h3>{{ (show() === 'w' ? 'playerCard.recentWhite' : show() === 's' ? 'playerCard.recentBlack' : 'playerCard.recent') | translate }}
              <span class="muted small"> {{ 'playerCard.recentHint' | translate }}</span></h3>
            @if (replayError(); as e) { <p class="err small">{{ e | translate }}</p> }
            @if (recentShown(); as list) {
              @if (!list.length) { <p class="muted small">{{ (show() === 'w' ? 'playerCard.noGamesWhite' : 'playerCard.noGamesBlack') | translate }}</p> }
            } @else { <p class="muted small">{{ 'playerCard.loading' | translate }}</p> }
            <table class="recent">
              @for (g of recentShown() ?? []; track $index) {
                <tr tabindex="0" role="button" [class.busy]="replayLoading()"
                    [attr.aria-label]="'playerCard.replayAria' | translate: { name: g.vs || '?' }"
                    (click)="openGame(c, $index)" (keydown.enter)="openGame(c, $index)">
                  <td class="num muted">{{ date(g.date) }}</td>
                  <td><span class="sq mini" [class.w]="g.color === 'w'" [class.s]="g.color === 's'"
                            [attr.aria-label]="(g.color === 'w' ? 'playerCard.white' : 'playerCard.black') | translate"></span>{{ g.vs || '?' }}
                    @if (g.vs_elo) { <span class="muted">{{ g.vs_elo }}</span> }
                    <br><span class="muted">{{ g.event }}</span>
                    @if (speed(g.event ?? "")) { <span class="tag">{{ 'playerCard.blitzRapid' | translate }}</span> }</td>
                  <td class="muted opening">{{ moves(g.opening) }}</td>
                  <td class="num"><b>{{ g.score === null ? '–' : g.score === 0.5 ? '½' : g.score }}</b></td>
                </tr>
              }
            </table>
          }
          @if (c.accounts.length || canEdit() || token) {
            <h3>{{ 'playerCard.accountsTitle' | translate }}</h3>
            <lh-online-accounts class="acc" [fide]="accountsKey(c)" [accounts]="c.accounts" [canEdit]="canEdit()" [shareToken]="token"
                                (changed)="reloadCard()" />
            <p class="muted small-note">{{ 'playerCard.accountsNote' | translate }}</p>
          }
          }
          <p class="muted small-note spaced">@if (note(); as n) { {{ n }} } @else { {{ 'playerCard.sources' | translate }} }
            @if (c.online) { {{ 'playerCard.sourcesOnline' | translate }} }</p>
        }
      </div>
    </dialog>

    <ng-template #first let-s="s" let-names="names">
      @if (!s?.first?.length) { <p class="muted">{{ 'playerCard.noMoves' | translate }}</p> }
      @else {
        <table class="stats">
          @for (r of s.first; track r[0]) {
            <tr><td>@if (names?.[r[0]]; as k) { {{ k | translate }} } @else { {{ firstMove(names, r[0]) }} }</td><td class="num">{{ pct(share(r[1], s.n)) }}</td>
              <td class="num muted">{{ r[1] }}×</td><td class="num muted">{{ r[2] === null ? '' : ('playerCard.score' | translate: { p: pct(r[2]) }) }}</td></tr>
          }
        </table>
        @if (s.lines?.length) {
          <p class="muted sub">{{ 'playerCard.frequentLines' | translate }}</p>
          <table class="stats">
            @for (l of s.lines.slice(0, 4); track l[0]) {
              <tr><td>{{ moves(l[0]) }}</td><td class="num muted">{{ l[1] }}×</td><td class="num muted">{{ l[2] === null ? '' : pct(l[2]) }}</td></tr>
            }
          </table>
        }
      }
    </ng-template>
  `,
  imports: [NgTemplateOutlet, TranslatePipe, OpeningTreeComponent, GameReplayComponent, OnlineAccountsComponent, TreeFilterBarComponent, TrainingLinesComponent],
})
export class PlayerCardComponent {
  private readonly api = inject(PLAYER_CARD_API);
  private readonly auth = inject(AuthService);
  readonly myGames = inject(MyGamesService);
  /** Online-Konten pflegen: Verwalter, nie über einen Teilen-Link. */
  readonly canEdit = signal(false);
  /** Abschnitt „Trainingslinien": die API bietet ihn an, jemand ist angemeldet, kein Teilen-Link. */
  readonly training = signal(false);
  /** Spielervorbereitung am Handy: Filter und Werkzeuge aufgeklappt (Vorgabe zu, z1-rk-prep-vorlauf). */
  readonly controlsOpen = signal(false);
  /** Spielervorbereitung am Handy: Werkzeuge (PGN, Eröffnungsbaum, Trainingslinien) hinter ⋮ aufgeklappt (z2-rk-prep-filter-aktionen). */
  readonly toolsOpen = signal(false);
  /** Als Teil einer Seite statt als Dialog darüber (Spielervorbereitung): ohne Abdunkeln, schließt nicht beim Klick daneben. */
  readonly inline = input(false);
  /** Eigener Hinweis zu den Quellen statt des LeagueHub-Textes. */
  readonly note = input<string | null>(null);
  private readonly dlg = viewChild.required<ElementRef<HTMLDialogElement>>('dlg');

  readonly card = signal<PlayerCard | null>(null);
  readonly loading = signal(false);
  /** Fehler als Sprachschlüssel (`playerCard.*`), übersetzt im Template. */
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
  readonly gameNote = signal<{ key: string; params?: Record<string, unknown>; err: boolean } | null>(null);
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
      untracked(() => void this.loadProfile(this.key(c), f, key));
    });
    // Farbe gewählt (oder die Karte aus einer Brett-Zeile mit Farbe geöffnet): deren letzte Partien holen, einmal je Karte.
    effect(() => {
      const c = this.card(), s = this.show();
      if (c && s !== 'b' && !this.byColor()[s]) void this.loadColor(this.key(c), s);
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
      if (key === this.profileKey) this.profileError.set('playerCard.profileError');
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

  /**
   * Link „📷 Fotos" (2026-10-09): alle Fotos des Spielers auf der Liga-Foto-Seite; nicht über Teilen-Links und seit
   * 0.727.5 NUR für Admins (Live-Stand `AuthService.isAdminLive`) — sonst kein Link, kein Platzhalter.
   */
  photosUrl(name: string | null | undefined, host: string = location.hostname): string | null {
    return this.token || !this.auth.isAdminLive ? null : ligaFotosUrl(name, host);
  }
  /** Zählt die Öffnungen: eine späte Antwort für einen inzwischen anderen (oder geschlossenen) Spieler wird verworfen. */
  private seq = 0;

  readonly options: { k: Show; label: string }[] = [
    { k: 'w', label: 'playerCard.white' }, { k: 's', label: 'playerCard.black' }, { k: 'b', label: 'playerCard.both' },
  ];
  readonly OPENINGS_WHITE = OPENINGS_WHITE;
  readonly OPENINGS_VS_E4 = OPENINGS_VS_E4;
  readonly OPENINGS_VS_D4 = OPENINGS_VS_D4;
  /** Sprache der Karte: RookHub die der Oberfläche, LeagueHub deutsch (e1-rk-prep-card-german). */
  private readonly translate = inject(TranslateService, { optional: true });
  private get lang(): string { return cardLang(this.translate); }
  /** Zugfolge in den Figurenbuchstaben der Sprache. */
  moves(text: string | null | undefined): string { return cardSanText(text, this.lang); }
  /** Erster Zug ohne eigenen Eröffnungsnamen: „1.e4" mit Weiß, „1…d6" mit Schwarz; „gegen andere" bringt die Nummern mit. */
  firstMove(names: Record<string, string> | null, san: string): string {
    return this.moves(names === OPENINGS_WHITE ? `1.${san}` : names ? `1…${san}` : san);
  }
  pct(p: number): string { return cardPct(p, this.lang); }
  date(d: string | null | undefined): string { return cardDate(d, this.lang); }
  readonly speed = (e: string) => SPEED.test(e || '');

  /** Öffnet die Karte; <paramref name="token"/> = Teilen-Link (dann ohne Anmeldung). */
  async open(fide: string, color: 'w' | 's' | null, board: number | null, token: string | null): Promise<void> {
    this.token = token;
    this.canEdit.set(!token && this.api.accountsEditable !== false && this.auth.has('league.manage'));
    this.training.set(!token && !!this.api.trainingLines && this.auth.isLoggedIn);
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
    if (!d.open) { if (this.inline()) d.show(); else d.showModal(); }
    try {
      const c = await this.api.card(fide, token);
      if (my === this.seq) this.card.set(c);
    } catch {
      if (my === this.seq) this.error.set('playerCard.notFound');
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
      const fresh = await this.api.card(this.key(c), this.token);
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
        const r = await this.api.recent(this.key(c), this.token);
        if (my !== this.seq) return;
        this.recentGames = r.games;
      } catch {
        if (my === this.seq) this.replayError.set('playerCard.gameError');
        return;
      } finally {
        if (my === this.seq) this.replayLoading.set(false);
      }
    }
    const same = (g: RecentGame) => g.date === want.date && g.vs === want.vs && g.color === want.color;
    const list = this.recentGames;
    const hit = list[i] && same(list[i]) ? list[i] : list.find(same);
    if (!hit) { this.replayError.set('playerCard.gameGone'); return; }
    this.gameNote.set(null);
    this.replay.set({ pgn: hit.pgn, flipped: hit.color === 's' });
  }

  /** In RookHubs „Meine Partien" legen und gleich dorthin springen (Wunsch 2026-09-28). */
  async toMyGames(pgn: string): Promise<void> {
    const id = await this.saveGame(pgn);
    if (id === null) return;
    this.gameBusy.set(true);
    this.gameNote.set({ key: 'playerCard.noteSaved', err: false });
    try {
      await this.myGames.open(id);
    } finally {
      this.gameBusy.set(false);
    }
  }

  /** Ablegen, Analyse anstoßen (eine vorhandene — etwa die der Vereinspartie — wird wiederverwendet) und auf RookHubs
   *  Partieseite springen: Bewertungskurve, Fehler, Zugklassen (Wunsch 2026-10-05). */
  async analyzeGame(pgn: string): Promise<void> {
    const id = await this.saveGame(pgn);
    if (id === null) return;
    this.gameBusy.set(true);
    this.gameNote.set({ key: 'playerCard.noteAnalyze', err: false });
    try {
      await this.myGames.analyze(id);
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
          await nav.share({ url, title: this.translate?.instant('playerCard.shareTitle') ?? 'RookHub' });
          this.gameNote.set({ key: 'playerCard.shared', err: false });
          return;
        } catch (e) {
          if ((e as { name?: string })?.name === 'AbortError') return;     // im Teilen-Blatt abgebrochen
        }
      }
      try {
        await navigator.clipboard.writeText(url);
        this.gameNote.set({ key: 'playerCard.linkCopied', params: { url }, err: false });
      } catch {
        // Ohne Zwischenablage (verweigert, unsicherer Kontext) steht der Link da — man kann ihn abschreiben.
        this.gameNote.set({ key: 'playerCard.linkShown', params: { url }, err: false });
      }
    } catch {
      this.gameNote.set({ key: 'playerCard.linkFailed', err: true });
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
        this.gameNote.set({ key: 'playerCard.saveRejected', err: true });
        return null;
      }
      this.savedIds.set(pgn, id);
      return id;
    } catch {
      this.gameNote.set({ key: 'playerCard.saveFailed', err: true });
      return null;
    } finally {
      this.gameBusy.set(false);
    }
  }

  backdrop(ev: MouseEvent): void {
    if (!this.inline() && ev.target === this.dlg().nativeElement) this.close();
  }

  /** Womit die API den Spieler findet: `key`, sonst die FIDE-ID (LeagueHub). */
  key(c: PlayerCard): string {
    return c.key ?? c.fide;
  }

  /** Woran die Online-Konten hängen: die FIDE-ID, bei einem Ligaspieler ohne FIDE-ID sein n-Schlüssel (0.730.0). Der
   *  Schlüssel der Spielervorbereitung (Id im Bestand) ist KEIN Konten-Schlüssel. */
  accountsKey(c: PlayerCard): string {
    return c.fide || (c.key?.startsWith('n-') ? c.key : c.fide);
  }

  unsureAllowed(): boolean {
    return this.api.unsureAllowed?.() ?? true;
  }

  lastName(c: PlayerCard): string {
    return (c.name || '').split(',')[0] || (this.translate?.instant('playerCard.thePlayer') ?? '');
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
      blob = await this.api.pgn(this.key(c), this.token);
    } catch {
      this.error.set('playerCard.pgnError');
      return;
    }
    const name = `${(c.name || c.fide).split(',').map(x => x.trim()).join('_').replace(/[^\w\-äöüÄÖÜß]+/g, '')}_${c.fide || this.key(c)}.pgn`;
    if (!downloadBlob(blob, name)) this.error.set('playerCard.pgnError');
  }
}

/**
 * Adresse der Liga-Foto-Seite für einen Spieler (2026-10-09, Wunsch des Users: „für einen Spieler in LeagueHub einen
 * Fotobutton, der alle seine Fotos einblendet"). Die Seite liegt als `ligafotos.<Elterndomäne>` neben LeagueHub/RookHub
 * und filtert über `#spieler=<Name>` (Schreibweise „Nachname, Vorname" wie in den Aufstellungen). Nur auf Prod-Hosts:
 * Dev (`*-dev.…`), localhost und IP-Adressen haben keine Foto-Seite → `null`.
 */
export function ligaFotosUrl(name: string | null | undefined, host: string): string | null {
  const n = (name ?? '').trim();
  const parts = (host ?? '').toLowerCase().split('.');
  if (!n || parts.length < 3 || parts[0].endsWith('-dev') || /^\d+$/.test(parts[parts.length - 1])) return null;
  return `https://ligafotos.${parts.slice(1).join('.')}/#spieler=${encodeURIComponent(n)}`;
}
