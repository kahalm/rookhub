import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { MatMenuModule } from '@angular/material/menu';
import { PlayerSearchComponent } from './player-search.component';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { ClubGame, ClubGameDetail, ClubPairing, RosterPerson, SideDecision } from '../../core/club.models';
import { pairingText } from './import-review';
import { ClubContextService } from '../../core/club-context.service';
import { loadErrorText, reasonText } from '../../core/club-format';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { HandoffService } from '@rh/core/handoff.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { downloadBlob } from '@rh/shared/download.util';
import { de } from '../../core/league-format';
import { PlayerCardComponent } from '@rh/shared/player-card/player-card.component';
import { GameReplayComponent } from '@rh/shared/player-card/game-replay.component';
import { AccessGateComponent } from '../../shared/access-gate.component';

/**
 * Vereinspartien (`/verein`): was die Mitglieder hochgeladen haben, neueste Jahre zuerst. Lesen darf, wer LeagueHub
 * sieht (`league.view`); hinzufügen die Vereinsgruppe (`league.contribute`). Ein Klick auf einen Spieler mit FIDE-ID
 * öffnet seine Spielerkarte — die Vereinspartien stehen dort mit drin —, ohne FIDE-ID die Korrektur. Namen und Ergebnis
 * korrigiert, wer die Partie auch löschen darf (Wunsch 2026-09-28); „Schwaz" bleibt anonym.
 *
 * Jede Partie wird im Hintergrund analysiert (0.588.0), und die Analyse steht allen offen, die die Vereinspartien sehen
 * (Wunsch 2026-09-28, 0.593.0): die Spalte „Analyse" zeigt die Genauigkeit beider Seiten bzw. den Fortschritt,
 * „Nachspielen" klappt darunter Brett und Rückblick auf (Kurve, Zug-Klassen, Computer-Linien).
 */
type Side = 'white' | 'black';

@Component({
  selector: 'lh-club-games-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgTemplateOutlet, PlayerCardComponent, PlayerSearchComponent, GameReplayComponent, AccessGateComponent, MatMenuModule],
  template: `
    @if (!allowed) {
      <lh-access-gate text="Die Vereinspartien sehen Admins und die Vereinsgruppen der teilnehmenden Vereine." />
    } @else {
      <section class="club-intro">
        <h2>Vereinspartien</h2>
        <p class="muted">Partien, die Mitglieder von {{ clubName() }} hochgeladen haben — sie stehen auch auf den Spielerkarten der
          Gegner. Vom Datum bleibt nur das Jahr; „{{ anon }}“ steht für ein Mitglied, das seinen Namen nicht zeigt.</p>
      </section>

      <form class="club-search" role="search" (submit)="$event.preventDefault(); search(q.value)">
        <input #q type="search" name="q" placeholder="Spieler oder Veranstaltung" aria-label="Suchen" [value]="query()" />
        <button type="submit" class="btn-sec">Suchen</button>
        @if (query()) { <button type="button" class="btn-link" (click)="q.value = ''; search('')">Alle zeigen</button> }
      </form>

      <div class="stand">
        @if (!firstLoadFailed()) { <span>{{ total() === null ? 'Lade …' : countText() }}</span> }
        @if (total()) { <button type="button" class="btn-sec" [disabled]="downloading()" (click)="download()">PGN herunterladen</button> }
        @if (canContribute) { <a class="btn-pri" routerLink="/verein/neu">Partien hinzufügen</a> }
        <span class="update-msg" [class.err]="!!error() && !firstLoadFailed()" role="status">{{ (firstLoadFailed() ? null : error()) ?? notice() ?? '' }}</span>
      </div>

      @if (firstLoadFailed()) {
        <!-- UX-034: kam schon der erste Abruf nicht, stand „Lade …" neben „Fehler 500." — ohne nächsten Schritt. -->
        <section class="gate">
          <h2>Vereinspartien nicht geladen</h2>
          <p>{{ error() }}</p>
          <div class="actions"><button type="button" class="btn-sec" (click)="search(query())">Erneut versuchen</button></div>
        </section>
      } @else if (total() === 0) {
        <section class="empty">
          <h2>{{ query() ? 'Nichts gefunden' : 'Noch keine Vereinspartien' }}</h2>
          @if (!query() && canContribute) {
            <p>Lade eine PGN-Datei hoch oder lies ein Partieformular ein — jede Partie mit einem Ligaspieler hilft der Vorbereitung.</p>
          }
        </section>
      } @else if (items().length) {
        <!-- UX-035 + UI-Sweep 2026-10-10 (l-club-mobile): am Handy (Container unter 640 px) steht jede Partie als Karte —
             „Weiß – Schwarz“, Ergebnis rechts, Jahr und Eröffnung grau, „Nachspielen“ und ⋮ rechtsbündig. Breit die Tabelle,
             Namen einzeilig, Eröffnung mit „…“, die Spalte „Analyse“ nur, wenn eine Partie einen Wert hat (l-club-table). -->
        <div class="roster-scroll club-scroll">
          <table class="rtable club-table">
            <thead><tr><th class="num">Jahr</th><th>Weiß</th><th>Schwarz</th><th class="num">Ergebnis</th>
              <th class="hide-s">Eröffnung</th><th class="num hide-s">Züge</th>
              @if (showAnalysis()) { <th class="num" title="Genauigkeit Weiß · Schwarz aus der Hintergrund-Analyse">Analyse</th> }
              <th class="acts"><span class="sr">Aktionen</span></th></tr></thead>
            <tbody>
              @for (g of items(); track g.id) {
                <tr class="game">
                  <td class="num">{{ g.year ?? '–' }}</td>
                  @for (k of sides; track k) {
                    <td class="side"><ng-container *ngTemplateOutlet="sideName; context: { $implicit: g, k: k }" /></td>
                  }
                  <td class="num">{{ resultText(g.result) }}</td>
                  <td class="hide-s small opening" [attr.title]="de(g.opening)">{{ de(g.opening) }}</td>
                  <td class="num hide-s small">{{ moves(g) }}</td>
                  @if (showAnalysis()) { <td class="num small" [attr.title]="analysisTitle(g)">{{ analysisText(g) }}</td> }
                  <td class="acts"><ng-container *ngTemplateOutlet="rowActs; context: { $implicit: g }" /></td>
                  <td class="card-cell">
                    <div class="gc-head">
                      <span class="gc-names"><span class="gc-side"><ng-container *ngTemplateOutlet="sideName; context: { $implicit: g, k: 'white' }" /></span>
                        <span class="gc-vs" aria-hidden="true">–</span>
                        <span class="gc-side"><ng-container *ngTemplateOutlet="sideName; context: { $implicit: g, k: 'black' }" /></span></span>
                      <b class="gc-result">{{ resultText(g.result) }}</b>
                    </div>
                    <div class="gc-meta muted small">{{ cardMeta(g) }}</div>
                    <div class="row-acts gc-acts"><ng-container *ngTemplateOutlet="rowActs; context: { $implicit: g }" /></div>
                  </td>
                </tr>
                @if (viewing(); as v) {
                  @if (v.id === g.id) {
                    <tr class="edit-row"><td colspan="9">
                      @if (v.game; as d) {
                        <lh-game-replay [pgn]="d.pgn" [evalsUrl]="d.analysis ? api.evalsUrl(d.id) : null" />
                        @if (!d.analysis) { <p class="small muted">Noch nicht analysiert — die Analyse läuft abends und am Wochenende.</p> }
                      } @else if (v.error) { <p class="err small">{{ v.error }}</p> }
                      @else { <p class="small muted">Lade …</p> }
                    </td></tr>
                  }
                }
                @if (editing(); as e) {
                  @if (e.id === g.id) {
                    <tr class="edit-row"><td colspan="9">
                      <div class="side-edit">
                        <div class="field-row">
                          @for (k of sides; track k) {
                            <div class="field">{{ k === 'white' ? 'Weiß' : 'Schwarz' }}
                              @if (isAnon(g, k)) { <span class="anon">{{ anon }}</span> <span class="small muted">bleibt anonym</span> }
                              @else {
                                <lh-player-search [client]="api" [text]="nameOf(g, k)" [label]="k === 'white' ? 'Weiß' : 'Schwarz'"
                                                  (textChange)="typed(k, $event)" (picked)="picked(k, $event)" />
                                @if (e[k]; as d) { <span class="small ok-text">{{ d.fide ? d.name + ' (FIDE ' + d.fide + ')' : 'getippt: ' + d.name }}</span> }
                                <label class="anon-inline small"><input type="checkbox" [checked]="!!e[k]?.replace" (change)="setReplace(g, k, $any($event.target).checked)" />
                                  durch „{{ anon }}" ersetzen</label>
                              }
                            </div>
                          }
                          <label class="field narrow">Ergebnis
                            <select (change)="setResult($any($event.target).value)">
                              @for (r of results; track r) { <option [value]="r" [selected]="e.result === r">{{ r === '*' ? 'unbekannt' : r }}</option> }
                            </select>
                          </label>
                          @if (e.pairings?.length) {
                            <label class="field wide">Ligapartie <span class="muted small">(Brett einer Ligarunde — das Jahr kommt dann aus dem Spielplan)</span>
                              <select class="pairing-pick" (change)="setPairing($any($event.target).value)">
                                <option value="" [selected]="e.pairingId == null">keine Ligapartie</option>
                                @for (p of e.pairings; track p.id) {
                                  <option [value]="p.id" [selected]="e.pairingId === p.id">{{ pairingText(p) }}</option>
                                }
                              </select>
                            </label>
                          }
                        </div>
                        <div class="actions">
                          <button type="button" class="btn-pri" [disabled]="saving()" (click)="save()">{{ saving() ? 'Speichere …' : 'Speichern' }}</button>
                          <button type="button" class="btn-link" (click)="editing.set(null)">Abbrechen</button>
                          @if (editError()) { <span class="err small">{{ editError() }}</span> }
                        </div>
                      </div>
                    </td></tr>
                  }
                }
              }
            </tbody>
          </table>
        </div>
        @if (items().length < (total() ?? 0)) {
          <p><button type="button" class="btn-sec" [disabled]="loading()" (click)="more()">Weitere laden</button></p>
        }
      }
      <lh-player-card />
    }
    <!-- Die Aktionen einer Partie — in der Spalte (breit) bzw. in der eigenen Zeile darunter (Handy); die jeweils andere
         Stelle blendet das CSS aus (display: none, also weder sichtbar noch für Vorleser oder Tab erreichbar). -->
    <!-- Ein Spielername — in der Tabelle UND in der Karte am Handy derselbe Baustein. -->
    <ng-template #sideName let-g let-k="k">
      @if (fideOf(g, k); as f) { <button type="button" class="pl" (click)="openCard(f, k === 'white' ? 'w' : 's')">{{ nameOf(g, k) }}</button> }
      @else if (isAnon(g, k)) { <span class="anon">{{ nameOf(g, k) }}</span> }
      @else if (inRoster(g, k)) { <span class="pl-nofide" title="Ligaspieler ohne FIDE-ID — dazu gibt es keine Spielerkarte">{{ nameOf(g, k) }}
                <span class="small muted">(ohne FIDE-ID)</span></span> }
      @else if (g.canDelete) { <button type="button" class="pl unknown" (click)="edit(g)"
                [attr.title]="'Kein Spieler zugeordnet (keine FIDE-ID, also keine Spielerkarte) — zum Zuordnen klicken'">{{ nameOf(g, k) }}
                <span class="small muted" aria-hidden="true">✎</span></button> }
      @else { <span>{{ nameOf(g, k) }}</span> }
      @if (k === 'white' ? g.whiteElo : g.blackElo) { <span class="small"> {{ k === 'white' ? g.whiteElo : g.blackElo }}</span> }
    </ng-template>
    <ng-template #rowActs let-g>
      <!-- Nachspielen bleibt sichtbar, der Rest steckt im ⋮ (Wunsch 2026-10-06: die Spalte machte die Tabelle zu breit). -->
      <button type="button" class="btn-link" [attr.aria-expanded]="viewing()?.id === g.id" (click)="view(g)"
              [attr.aria-label]="'Partie ' + g.white + ' – ' + g.black + ' nachspielen'">Nachspielen</button>
      @if (rookHub || g.canDelete) {
        <button type="button" class="btn-link more-btn" [matMenuTriggerFor]="moreMenu" [matMenuTriggerData]="{ g: g }"
                [attr.aria-label]="'Weitere Aktionen für ' + g.white + ' – ' + g.black" title="Weitere Aktionen">⋮</button>
      }
    </ng-template>
    <mat-menu #moreMenu="matMenu" xPosition="before">
      <ng-template matMenuContent let-g="g">
        @if (rookHub) { <button mat-menu-item type="button" (click)="openInRookHub(g)">Analyse</button> }
        @if (g.canDelete) {
          <button mat-menu-item type="button" (click)="edit(g)">Bearbeiten</button>
          <!-- 0.660.0: Züge nachbessern wie beim ersten Prüfen (mit dem aufbewahrten Formular) — gilt auch für alle Kopien -->
          <a mat-menu-item [routerLink]="['/verein/partie', g.id, 'korrigieren']">Korrigieren</a>
          <button mat-menu-item type="button" [disabled]="deleting() === g.id" (click)="remove(g)">Löschen</button>
        }
      </ng-template>
    </mat-menu>
  `,
})
export class ClubGamesPageComponent implements OnInit {
  readonly api = inject(ClubApiService).client();
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly card = viewChild(PlayerCardComponent);

  readonly allowed = this.auth.has('league.view');
  readonly canContribute = this.auth.has('league.contribute');
  private readonly clubCtx = inject(ClubContextService);
  /** Name anonymisierter Spieler und des Vereins (0.698.0, vorher fest „Schwaz"). */
  get anon(): string { return this.clubCtx.anonName(); }
  readonly clubName = this.clubCtx.clubName;
  readonly de = de;

  readonly items = signal<ClubGame[]>([]);
  /** Spalte „Analyse" nur, wenn mindestens eine Partie einen Wert hat — sonst stand dort nur „–" (UI-Sweep 2026-10-10). */
  readonly showAnalysis = computed(() => this.items().some(g => this.analysisText(g) !== '–'));
  readonly total = signal<number | null>(null);
  readonly query = signal('');
  readonly loading = signal(false);
  readonly downloading = signal(false);
  readonly deleting = signal<number | null>(null);
  readonly error = signal<string | null>(null);
  readonly sides: Side[] = ['white', 'black'];
  readonly rookHub = rookHubUrlForLeagueHub();
  private readonly handoff = inject(HandoffService);
  private readonly route = inject(ActivatedRoute);
  readonly results = ['1-0', '0-1', '1/2-1/2', '*'];
  /** Die Partie, die gerade korrigiert wird — je Seite die Festlegung (fehlt = unverändert) und das Ergebnis. */
  /** `pairings` = Brettpaarungen zur Auswahl (0.678.0), `null` solange sie laden oder nicht zu holen waren. */
  readonly editing = signal<{ id: number; white: SideDecision | null; black: SideDecision | null; result: string;
    pairingId: number | null; pairings: ClubPairing[] | null } | null>(null);
  readonly pairingText = pairingText;
  readonly saving = signal(false);
  readonly editError = signal<string | null>(null);
  /** Die Partie, die gerade nachgespielt wird (aufgeklappt unter ihrer Zeile). */
  readonly viewing = signal<{ id: number; game: ClubGameDetail | null; error: string | null } | null>(null);
  /** Rückmeldung der Formular-Korrektur („übernommen"), per Router-Zustand mitgebracht. */
  readonly notice = signal<string | null>((history.state as { msg?: string } | null)?.msg ?? null);
  /** Schon der erste Abruf kam nicht — dann gibt es keine Liste, nur die Fehlerkarte mit „Erneut versuchen". */
  readonly firstLoadFailed = computed(() => this.total() === null && !!this.error());
  private page = 1;
  private seq = 0;

  ngOnInit(): void {
    if (!this.allowed) return;
    // `?bearbeiten=<id>` (0.675.0): aus den Paarungen einer gespielten Runde gleich diese Partie zum Bearbeiten öffnen
    const target = Number(this.route.snapshot.queryParamMap?.get('bearbeiten'));
    void this.load(1).then(() => { if (Number.isInteger(target) && target > 0) void this.openForEdit(target); });
  }

  /** Eine Partie zum Bearbeiten aufklappen — steht sie nicht auf der ersten Seite, wird sie vorn eingereiht. */
  private async openForEdit(id: number): Promise<void> {
    let g = this.items().find(x => x.id === id);
    if (!g) {
      try { g = await this.api.game(id); } catch { this.error.set('Die Partie konnte nicht geladen werden.'); return; }
      this.items.set([g, ...this.items()]);
    }
    if (g.canDelete) this.edit(g);
    else this.error.set('Diese Partie darfst du nicht ändern.');
  }

  search(q: string): void {
    this.query.set(q.trim());
    void this.load(1);
  }

  more(): void {
    void this.load(this.page + 1);
  }

  private async load(page: number): Promise<void> {
    const my = ++this.seq;
    this.loading.set(true);
    this.error.set(null);
    try {
      const r = await this.api.list(null, this.query() || null, page);
      if (my !== this.seq) return;
      this.page = r.page;
      this.items.set(page === 1 ? r.items : [...this.items(), ...r.items]);
      this.total.set(r.total);
    } catch (err) {
      if (my === this.seq) this.error.set(this.errorText(err));
    } finally {
      if (my === this.seq) this.loading.set(false);
    }
  }

  countText(): string {
    const n = this.total() ?? 0;
    return `${n} ${n === 1 ? 'Partie' : 'Partien'}${this.query() ? ` zu „${this.query()}"` : ''}`;
  }

  resultText(r: string): string {
    return r === '1/2-1/2' ? '½–½' : r === '*' ? '–' : r.replace('-', '–');
  }

  moves(g: ClubGame): number {
    return Math.ceil(g.plies / 2);
  }

  nameOf(g: ClubGame, k: Side): string { return k === 'white' ? g.white : g.black; }
  fideOf(g: ClubGame, k: Side): string | null { return k === 'white' ? g.whiteFide : g.blackFide; }
  /** „Schwaz" ohne FIDE-ID an einer anonymisierten Partie — bleibt, wie es ist. */
  isAnon(g: ClubGame, k: Side): boolean { return g.anonymized && !this.fideOf(g, k) && this.nameOf(g, k) === this.anon; }
  /** Ligaspieler OHNE FIDE-ID (0.594.0): steht in einer Meldeliste, hat aber keine ID — keine Karte, aber auch nichts zuzuordnen,
   *  also kein Bleistift. Der bleibt Namen, die niemand kennt. Korrigieren geht weiter über „Bearbeiten". */
  inRoster(g: ClubGame, k: Side): boolean { return !!(k === 'white' ? g.whiteInRoster : g.blackInRoster); }

  edit(g: ClubGame): void {
    const cur = this.editing();
    if (cur?.id === g.id) { this.editing.set(null); return; }
    this.editing.set({ id: g.id, white: null, black: null, result: g.result, pairingId: g.leagueGameId ?? null, pairings: null });
    this.editError.set(null);
    void this.loadPairings(g.id);
  }

  /** Die Brettpaarungen, die diese Partie sein könnten — die aktuelle Zuordnung ist immer dabei. */
  private async loadPairings(id: number): Promise<void> {
    try {
      const list = await this.api.gamePairings(id);
      const e = this.editing();
      if (e?.id === id) this.editing.set({ ...e, pairings: list });
    } catch { /* ohne Auswahl bleibt die Zuordnung, wie sie ist */ }
  }

  setPairing(value: string): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, pairingId: value ? Number(value) : null });
  }

  typed(k: Side, text: string): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, [k]: text.trim() ? { name: text.trim(), fide: null, replace: false } : null });
  }

  /** Ein Spieler von Schwaz wird zu „Schwaz" — Vorgabe wie beim Hochladen (der Server erzwingt es ohnehin). */
  picked(k: Side, p: RosterPerson): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, [k]: { name: p.name, fide: p.fide, replace: p.club } });
  }

  setReplace(g: ClubGame, k: Side, on: boolean): void {
    const e = this.editing();
    if (!e) return;
    const cur = e[k] ?? { name: this.nameOf(g, k), fide: this.fideOf(g, k), replace: false };
    this.editing.set({ ...e, [k]: { ...cur, replace: on } });
  }

  /** Spalte „Analyse": fertig die Genauigkeit beider Seiten, sonst der Fortschritt. */
  /** „2026 · Sizilianisch …" — Jahr und Eröffnung in der Karte am Handy. */
  cardMeta(g: ClubGame): string {
    return [g.year ?? '–', de(g.opening) || null].filter(x => x !== null).join(' · ');
  }

  analysisText(g: ClubGame): string {
    const a = g.analysis;
    if (!a) return '–';
    if (a.status === 'done') return `${this.pct(a.accuracyWhite)} · ${this.pct(a.accuracyBlack)}`;
    if (a.status === 'failed') return '–';
    return a.total > 0 ? `${Math.floor(a.analyzed * 100 / a.total)} %` : '…';
  }

  analysisTitle(g: ClubGame): string {
    const a = g.analysis;
    if (!a) return 'Noch nicht analysiert';
    if (a.status === 'done') return 'Genauigkeit Weiß · Schwarz';
    if (a.status === 'failed') return 'Analyse gescheitert';
    return `Wird analysiert: ${a.analyzed} von ${a.total} Stellungen`;
  }

  private pct(v: number | null): string {
    return v === null ? '–' : `${Math.round(v)}`;
  }

  /** Nachspielen auf- bzw. zuklappen; die Partie (PGN + Stand) kommt erst beim Aufklappen. */
  async view(g: ClubGame): Promise<void> {
    if (this.viewing()?.id === g.id) { this.viewing.set(null); return; }
    this.viewing.set({ id: g.id, game: null, error: null });
    try {
      const d = await this.api.game(g.id);
      if (this.viewing()?.id === g.id) this.viewing.set({ id: g.id, game: d, error: null });
    } catch {
      if (this.viewing()?.id === g.id) this.viewing.set({ id: g.id, game: null, error: 'Die Partie konnte nicht geladen werden.' });
    }
  }

  /** Die Partie auf RookHubs Partieseite (0.653.0, Wunsch 2026-10-04: „wie aus Meine Partien, mit unten den vorberechneten
   *  Werten zum schnellen Durchgehen") — Brett, Bewertungskurve, Zug-Klassen und Computer-Linien aus der Hintergrund-Analyse
   *  des Vereins. Sprung mit Einmal-Code, damit man drüben gleich angemeldet ist. Vorher: das Analysebrett mit dem PGN. */
  openInRookHub(g: ClubGame): void {
    void this.handoff.jumpToRookHub(`club-games/${g.id}`);
  }

  setResult(r: string): void {
    const e = this.editing();
    if (e) this.editing.set({ ...e, result: r });
  }

  async save(): Promise<void> {
    const e = this.editing();
    if (!e) return;
    this.saving.set(true);
    this.editError.set(null);
    try {
      // Die Paarung geht nur mit, wenn es eine Auswahl gab (sonst bliebe sie unverändert); 0 = keine Ligapartie.
      const g = await this.api.updateGame(e.id, { white: e.white, black: e.black, result: e.result,
        ...(e.pairings ? { leagueGameId: e.pairingId ?? 0 } : {}) });
      this.items.set(this.items().map(x => x.id === g.id ? g : x));
      this.editing.set(null);
    } catch (err) {
      const reason = err instanceof HttpErrorResponse ? err.error?.reason : null;
      this.editError.set(reason === 'anonymous' ? `„${this.anon}“ bleibt anonym.` : reason ? reasonText(reason)
        : err instanceof HttpErrorResponse && err.status === 403 ? 'Diese Partie darfst du nicht ändern.' : 'Speichern hat nicht geklappt.');
    } finally {
      this.saving.set(false);
    }
  }

  openCard(fide: string, color: 'w' | 's'): void {
    void this.card()?.open(fide, color, null, null);
  }

  async remove(g: ClubGame): Promise<void> {
    if (!(await firstValueFrom(this.confirm.ask(`Partie ${g.white} – ${g.black}${g.year ? ` (${g.year})` : ''} aus der Vereins-Datenbank löschen?`)))) return;
    this.deleting.set(g.id);
    try {
      await this.api.deleteGame(g.id);
      this.items.set(this.items().filter(x => x.id !== g.id));
      this.total.set(Math.max(0, (this.total() ?? 1) - 1));
    } catch (err) {
      this.error.set(err instanceof HttpErrorResponse && err.status === 403
        ? 'Diese Partie darfst du nicht löschen.' : 'Löschen hat nicht geklappt.');
    } finally {
      this.deleting.set(null);
    }
  }

  async download(): Promise<void> {
    this.downloading.set(true);
    try {
      const blob = await this.api.pgn(null, this.query() || null);
      // downloadBlob wirft nicht, sondern meldet false (Codereview F8-006) — sonst passierte hier stumm nichts.
      if (!downloadBlob(blob, 'vereinspartien.pgn')) this.error.set('Die PGN-Datei konnte nicht geladen werden.');
    } catch {
      this.error.set('Die PGN-Datei konnte nicht geladen werden.');
    } finally {
      this.downloading.set(false);
    }
  }

  private errorText(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.status === 403) return 'Die Vereinspartien sind für dein Konto nicht freigeschaltet.';
    return loadErrorText(err);
  }
}
