import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnDestroy, OnInit, computed, inject, signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgClass } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ChessBoardComponent, UserBoardMove } from '@rh/shared/pgn-viewer/chess-board.component';
import { SheetEditSession } from '@rh/features/games/sheet-edit-session';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { LeagueScanState, RosterPerson, SideMatch } from '../../core/club.models';
import { ANON_NAME, normalizeResult, reasonText, yearOf } from '../../core/club-format';
import { rememberAnonKey } from './club-add-page.component';
import { PlayerSearchComponent } from './player-search.component';
import { de } from '../../core/league-format';

type Side = 'white' | 'black';
const POLL_MS = 3000;
const MATCH_DEBOUNCE_MS = 400;

/**
 * Ein eingelesenes Partieformular prüfen und in die Vereins-Datenbank übernehmen (`/verein/formular/:id`).
 * Die Korrektur selbst ist DIESELBE wie in RookHub (`SheetEditSession`: Cursor-Brett, Lesarten, Ersetzen/Einfügen/
 * Löschen, nach jeder Änderung wird der Rest aus den Formular-Einträgen neu aufbereitet); dazu die Namen mit
 * Ligaspieler-Prüfung und „durch Schwaz ersetzen" je Seite (Vorgabe: Spieler von Schwaz und die eigene Seite). Angemeldet
 * unter `/verein/formular/:id`, ohne Konto über einen Teilen-Link unter `/s/:token/formular/:key`. Übernehmen oder
 * Verwerfen schließt die Einlesung — das Foto verschwindet.
 */
@Component({
  selector: 'lh-club-scan-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgClass, ChessBoardComponent, PlayerSearchComponent],
  template: `
    @if (!allowed) {
      <section class="gate"><h2>Nicht freigeschaltet</h2>
        <p>Partieformulare einlesen dürfen Admins und die Vereinsgruppe von SK Schwaz.</p></section>
    } @else if (notFound()) {
      <section class="gate"><h2>Formular nicht gefunden</h2>
        <p>Es wurde schon übernommen oder verworfen. <a [routerLink]="backLink" [queryParams]="{ art: 'formular' }">Zu deinen Formularen</a></p></section>
    } @else if (state(); as st) {
      <section class="club-intro">
        <p><a [routerLink]="backLink" [queryParams]="{ art: 'formular' }">← Deine Formulare</a></p>
        <h2>Partieformular prüfen</h2>
        @if (st.scan.status !== 'done') {
          <p class="muted" role="status">{{ st.scan.status === 'failed' ? 'Das Formular ließ sich nicht lesen.' : 'Das Formular wird noch gelesen …' }}</p>
        } @else {
          <p class="muted">Orange markiert sind unsichere Stellen: dort die richtige Lesart wählen oder den Zug am Brett spielen
            — danach wird der Rest neu gelesen. Pfeiltasten blättern.</p>
        }
      </section>

      @if (st.scan.status === 'done') {
        <div class="scan-layout" [class.with-photo]="!!photoUrl()">
          @if (photoUrl(); as src) {
            <section class="panel scan-photo">
              <div class="photo-scroll" [class.zoom]="zoom()">
                <img [src]="src" alt="Foto des Partieformulars" (load)="s.onPhotoLoad($event)" />
              </div>
              <p><button type="button" class="btn-link" (click)="zoom.set(!zoom())">{{ zoom() ? 'Kleiner' : 'Größer' }}</button></p>
              @if (s.crop(); as c) {
                <div class="crop">
                  <p class="small">Auf dem Formular@if (c.written) { : <b>{{ c.written }}</b> }</p>
                  <div class="crop-frame" [class.uncertain]="c.uncertain" [style.aspect-ratio]="c.view.aspect">
                    <img [src]="src" alt="" [style.width.%]="c.view.imgW" [style.height.%]="c.view.imgH"
                         [style.left.%]="c.view.left" [style.top.%]="c.view.top" />
                    <div class="crop-mark" [style.left.%]="c.view.markLeft" [style.top.%]="c.view.markTop"
                         [style.width.%]="c.view.markW" [style.height.%]="c.view.markH"></div>
                  </div>
                </div>
              }
            </section>
          }

          <section class="panel scan-board">
            <app-chess-board [fen]="s.cursorFen()" [lastMove]="s.lastMove()" [arrows]="s.arrows()" [flipped]="ownerSide() === 'black'"
                             [playable]="!s.busy()" (userMove)="onBoardMove($event)" />
            <div class="board-nav">
              <button type="button" class="btn-sec" (click)="s.go(0)" [disabled]="s.cursor() === 0" aria-label="Zum Anfang">⏮</button>
              <button type="button" class="btn-sec" (click)="s.go(s.cursor() - 1)" [disabled]="s.cursor() === 0" aria-label="Zug zurück">◀</button>
              <button type="button" class="btn-sec" (click)="s.go(s.cursor() + 1)" [disabled]="s.cursor() >= s.legalCount()" aria-label="Zug vor">▶</button>
              <button type="button" class="btn-sec" (click)="s.go(s.legalCount())" [disabled]="s.cursor() >= s.legalCount()" aria-label="Zum Ende">⏭</button>
            </div>
            <div class="board-nav">
              <div class="seg" role="group" aria-label="Zug am Brett">
                <button type="button" [attr.aria-pressed]="s.mode() === 'replace'" (click)="s.mode.set('replace')">Ersetzen</button>
                <button type="button" [attr.aria-pressed]="s.mode() === 'insert'" (click)="s.mode.set('insert')">Davor einfügen</button>
              </div>
              @if (s.uncertainLeft() > 0) {
                <button type="button" class="btn-sec warn" (click)="s.nextUncertain()">Nächste unsichere Stelle ({{ s.uncertainLeft() }})</button>
              }
            </div>
          </section>

          <section class="panel scan-side">
            <div class="cursor-panel" aria-live="polite">
              @if (s.busy()) { <p class="muted small">Lese den Rest neu …</p> }
              @if (s.current(); as p) {
                <p class="where">
                  <b>{{ s.plyLabel(s.cursor()) }}</b> <span class="san" [class.bad]="p.illegal">{{ de(p.san) }}</span>
                  @if (p.written) { <span class="muted small">auf dem Formular: {{ p.written }}</span> }
                  @if (p.match === 'inserted') { <span class="chip warn">nicht auf dem Formular</span> }
                  @if (p.uncertain && !p.confirmed) { <span class="chip warn">unsicher</span> }
                  @if (p.confirmed) { <span class="chip ok">bestätigt</span> }
                  @if (p.illegal) { <span class="chip bad">nicht legal</span> }
                </p>
                @if (p.options?.length && !p.illegal) {
                  <div class="options">
                    <span class="muted small">Mögliche Lesarten</span>
                    @for (o of p.options; track o.uci) {
                      <button type="button" class="option" [class.chosen]="o.uci === p.uci" [disabled]="s.busy()" (click)="s.choose(o)">
                        <b>{{ de(o.san) }}</b> <span class="small">passt zu {{ o.reach }} folgenden Einträgen</span>
                        @if (o.preview.length) { <span class="small muted">→ {{ de(o.preview.join(' ')) }}</span> }
                      </button>
                    }
                  </div>
                }
                <div class="actions">
                  @if (!p.illegal && !p.confirmed) { <button type="button" class="btn-sec" (click)="s.confirm()">Stimmt so</button> }
                  <button type="button" class="btn-sec" [disabled]="s.busy()" (click)="s.remove()">Zug löschen</button>
                </div>
                <p class="muted small">{{ s.mode() === 'insert' ? 'Ein Zug am Brett wird VOR diesem eingefügt.' : 'Ein Zug am Brett ersetzt diesen.' }}</p>
              } @else {
                <p class="muted small">Ende der Partie — ein Zug am Brett hängt einen an.</p>
                @if (s.unresolved().length) { <p class="small">Als Nächstes auf dem Formular: <b>{{ s.unresolved()[0] }}</b></p> }
              }
            </div>
            <div class="moves">
              @for (row of s.rows(); track row.no) {
                <span class="no">{{ row.no }}.</span>
                <button type="button" class="ply" [ngClass]="s.plyClass(row.white)" (click)="s.go(row.white)">{{ de(s.plies()[row.white].san) }}</button>
                @if (row.black !== null) {
                  <button type="button" class="ply" [ngClass]="s.plyClass(row.black)" (click)="s.go(row.black)">{{ de(s.plies()[row.black].san) }}</button>
                } @else { <span></span> }
              }
              <span class="no"></span>
              <button type="button" class="ply end" [class.cursor]="s.cursor() === s.plies().length" (click)="s.go(s.legalCount())">Ende</button>
            </div>
            @if (s.unresolved().length) {
              <p class="err small">{{ s.unresolved().length }} Einträge ließen sich nicht zuordnen: {{ s.unresolved().join(' ') }}</p>
            }
            @if (s.illegalCount() > 0) {
              <p class="err small">{{ s.illegalCount() }} Züge am Ende sind nicht legal — sie fallen beim Übernehmen weg.</p>
            }
          </section>
        </div>

        <section class="panel save-panel">
          <h3 class="club-h3">Partie</h3>
          <div class="save-grid">
            @for (k of sides; track k) {
              <div class="field">{{ k === 'white' ? 'Weiß' : 'Schwarz' }}
                <lh-player-search [client]="client" [text]="name(k)()" [label]="k === 'white' ? 'Weiß' : 'Schwarz'"
                                  (textChange)="setName(k, $event)" (picked)="pickPerson(k, $event)" />
                <span class="match" [class.ok]="(match(k)()?.league || match(k)()?.mega) && !replace(k)()">{{ matchText(k) }}</span>
                <label class="replace-row"><input type="checkbox" [checked]="replace(k)()" (change)="setReplace(k, $any($event.target).checked)" />
                  durch „{{ anon }}“ ersetzen</label>
              </div>
              <label class="field narrow">Elo<input type="number" inputmode="numeric" min="500" max="3000" [value]="elo(k)() ?? ''"
                                               (input)="elo(k).set(num($any($event.target).value))" [disabled]="replace(k)()" /></label>
            }
            <label class="field narrow">Jahr<input type="number" inputmode="numeric" min="1900" [max]="maxYear" [value]="year() ?? ''"
                                              (input)="year.set(num($any($event.target).value))" /></label>
            <label class="field narrow">Ergebnis
              <select (change)="result.set($any($event.target).value)">
                @for (r of results; track r) { <option [value]="r" [selected]="result() === r">{{ r === '*' ? 'unbekannt' : r }}</option> }
              </select>
            </label>
            <label class="field wide">Veranstaltung <span class="muted small">(fällt weg, sobald jemand „{{ anon }}“ heißt)</span>
              <input [value]="event()" (input)="event.set($any($event.target).value)" maxlength="200" [disabled]="anyReplaced()" />
            </label>
          </div>

          <div class="field-row">
            <span class="muted small">Ich spiele</span>
            <div class="seg" role="group" aria-label="Ich spiele">
              <button type="button" [attr.aria-pressed]="ownerSide() === 'white'" (click)="setOwner('white')">Weiß</button>
              <button type="button" [attr.aria-pressed]="ownerSide() === 'black'" (click)="setOwner('black')">Schwarz</button>
            </div>
            <span class="muted small">Spieler von Schwaz und deine Seite werden standardmäßig durch „{{ anon }}“ ersetzt — dann wird weder
              gespeichert, wer dahinter steht, noch wer hochgeladen hat.</span>
          </div>

          <p class="preview"><span class="muted">Gespeichert wird:</span> <b>{{ preview() }}</b></p>
          @if (problem(); as pr) { <p class="err small">{{ pr }}</p> }

          <div class="actions">
            <button type="button" class="btn-pri" [disabled]="saving() || s.busy()" (click)="save()">
              {{ saving() ? 'Übernehme …' : 'In die Vereins-Datenbank übernehmen' }}</button>
            <button type="button" class="btn-link" [disabled]="saving()" (click)="discard()">Formular verwerfen</button>
            <span class="update-msg" [class.err]="!!saveError()" role="status">{{ saveError() ?? '' }}</span>
          </div>
        </section>
      }
    } @else {
      <p class="muted">Lade …</p>
    }
  `,
})
export class ClubScanPageComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject(ElementRef);

  /** Token des Teilen-Links (ohne Anmeldung) — sonst `null`. */
  readonly share = this.route.snapshot.paramMap.get('token');
  readonly client: ClubClient = inject(ClubApiService).client(this.share);
  private readonly api = this.client;
  readonly allowed = !!this.share || this.auth.has('league.contribute');
  readonly backLink: unknown[] = this.share ? ['/s', this.share, 'hochladen'] : ['/verein/neu'];
  readonly results = ['1-0', '0-1', '1/2-1/2', '*'];
  readonly sides: Side[] = ['white', 'black'];
  readonly maxYear = new Date().getFullYear() + 1;
  readonly anon = ANON_NAME;
  readonly de = de;

  /** Nummer (angemeldet) bzw. geheimer Schlüssel (ohne Konto) der Einlesung. */
  scanRef = '';
  readonly state = signal<LeagueScanState | null>(null);
  readonly notFound = signal(false);
  readonly photoUrl = signal<string | null>(null);
  readonly zoom = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  private readonly sideState = {
    white: { name: signal(''), fide: signal<string | null>(null), elo: signal<number | null>(null),
      match: signal<SideMatch | null>(null), replace: signal(false), touched: false },
    black: { name: signal(''), fide: signal<string | null>(null), elo: signal<number | null>(null),
      match: signal<SideMatch | null>(null), replace: signal(false), touched: false },
  };
  readonly name = (k: Side) => this.sideState[k].name;
  readonly elo = (k: Side) => this.sideState[k].elo;
  readonly match = (k: Side) => this.sideState[k].match;
  readonly replace = (k: Side) => this.sideState[k].replace;
  readonly year = signal<number | null>(null);
  readonly result = signal('*');
  readonly event = signal('');
  readonly ownerSide = signal<Side | null>(null);
  readonly anyReplaced = computed(() => this.replace('white')() || this.replace('black')());

  readonly s = new SheetEditSession({
    resolve: (prefix, writtenFrom) => this.api.resolve(this.scanRef, prefix, writtenFrom),
    moved: () => this.revealCursor(),
    resolveFailed: () => this.saveError.set('Den Rest neu zu lesen hat nicht geklappt — der bisherige Stand bleibt.'),
    bind: o => o.pipe(takeUntilDestroyed(this.destroyRef)),
  });

  /** Wie die Partie in der Datenbank steht (Jahr · Weiß – Schwarz · Ergebnis). */
  readonly preview = computed(() => {
    const shown = (k: Side) => this.replace(k)() ? ANON_NAME : this.match(k)()?.name || this.name(k)().trim() || '?';
    return [this.year() ?? 'ohne Jahr', `${shown('white')} – ${shown('black')}`, this.result() === '*' ? 'Ergebnis offen' : this.result()]
      .join(' · ');
  });

  /** Was die Partie unübernehmbar macht — dieselbe Regel wie am Server (`LeagueClubService.Build`): bekannt ist, wer in
   * der Liga ODER im Megabase-Verzeichnis steht. */
  readonly problem = computed(() => {
    const w = this.match('white')(), b = this.match('black')();
    if (!w || !b) return null;
    const known = (m: SideMatch) => m.league || !!m.mega;
    if (!known(w) && !known(b))
      return 'Kein Spieler erkannt (weder in der Liga noch in der Megabase) — so wird die Partie nicht angenommen. Namen prüfen (Vorschläge beim Tippen).';
    if (!(known(w) && !this.replace('white')()) && !(known(b) && !this.replace('black')()))
      return 'Nach dem Ersetzen bleibt kein bekannter Gegner übrig — so wird die Partie nicht angenommen.';
    return null;
  });

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private matchTimer: ReturnType<typeof setTimeout> | null = null;
  private matchSeq = 0;
  private destroyed = false;

  ngOnInit(): void {
    if (!this.allowed) return;
    this.scanRef = this.route.snapshot.paramMap.get('id') ?? this.route.snapshot.paramMap.get('key') ?? '';
    void this.load();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    for (const t of [this.pollTimer, this.matchTimer]) if (t) clearTimeout(t);
    const url = this.photoUrl();
    if (url) URL.revokeObjectURL(url);
  }

  private async load(): Promise<void> {
    let st: LeagueScanState;
    try {
      st = await this.api.scan(this.scanRef);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 404) this.notFound.set(true);
      else this.pollTimer = setTimeout(() => void this.load(), POLL_MS);
      return;
    }
    if (this.destroyed) return;
    this.state.set(st);
    if (st.scan.status === 'pending' || st.scan.status === 'running') {
      this.pollTimer = setTimeout(() => void this.load(), POLL_MS);
      return;
    }
    if (st.scan.status !== 'done') return;
    this.s.loadSheet({ plies: st.plies, unresolved: st.unresolved, unresolvedFrom: st.unresolvedFrom, boxes: st.boxes, written: st.written });
    this.name('white').set(st.white ?? '');
    this.name('black').set(st.black ?? '');
    this.year.set(yearOf(st.date));
    this.result.set(normalizeResult(st.result));
    this.event.set(st.event ?? '');
    this.ownerSide.set(st.ownerSide);
    void this.runMatch();
    try {
      const blob = await this.api.photo(this.scanRef);
      if (!this.destroyed) this.photoUrl.set(URL.createObjectURL(blob));
    } catch { /* ohne Foto geht die Korrektur trotzdem */ }
  }

  /** Vorgabe „ersetzen": Spieler von Schwaz und die eigene Seite — bis der Nutzer das Häkchen selbst anfasst. */
  private applyDefault(k: Side): void {
    const st = this.sideState[k];
    if (st.touched) return;
    st.replace.set(!!st.match()?.club || this.ownerSide() === k);
  }

  setOwner(k: Side): void {
    this.ownerSide.set(k);
    for (const x of this.sides) this.applyDefault(x);
  }

  setReplace(k: Side, on: boolean): void {
    this.sideState[k].touched = true;
    this.sideState[k].replace.set(on);
  }

  setName(k: Side, value: string): void {
    const st = this.sideState[k];
    st.name.set(value);
    st.fide.set(null);
    if (this.matchTimer) clearTimeout(this.matchTimer);
    this.matchTimer = setTimeout(() => void this.runMatch(), MATCH_DEBOUNCE_MS);
  }

  /** Ein Treffer der Suche (Liga oder Megabase) bringt Namen und FIDE-ID mit — so ist der Spieler eindeutig. */
  pickPerson(k: Side, p: RosterPerson): void {
    const st = this.sideState[k];
    if (this.matchTimer) clearTimeout(this.matchTimer);
    st.name.set(p.name);
    st.fide.set(p.fide);
    const league = p.league ?? true;
    st.match.set({ league, ambiguous: false, name: p.name, fide: p.fide, club: p.club, candidates: [], mega: !league });
    this.applyDefault(k);
  }

  private async runMatch(): Promise<void> {
    const my = ++this.matchSeq;
    try {
      const m = await this.api.match(this.name('white')(), this.name('black')());
      if (my !== this.matchSeq) return;
      for (const k of this.sides) {
        if (this.sideState[k].fide()) continue;             // aus der Meldeliste gewählt: bleibt
        this.sideState[k].match.set(m[k]);
        this.applyDefault(k);
      }
    } catch { /* die Prüfung macht der Server beim Übernehmen ohnehin */ }
  }

  matchText(k: Side): string {
    const m = this.match(k)();
    if (this.replace(k)()) return m?.club ? `Spieler von Schwaz — wird „${ANON_NAME}“` : `wird „${ANON_NAME}“`;
    if (!m) return '';
    if (m.ambiguous) return 'Ligaspieler (mehrere dieses Namens — bitte aus den Vorschlägen wählen)';
    if (m.league && m.lastNameOnly) return `nur über den Nachnamen: ${m.name} — bitte prüfen`;
    const kept = m.alias ? ' (gemerkt)' : '';
    if (m.league) return `Ligaspieler: ${m.name}${m.fide ? '' : ' (ohne FIDE-ID)'}${m.club ? ' — Schwaz, nicht ersetzt' : ''}${kept}`;
    if (m.mega) return `nicht in Liga — aus der Megabase: ${m.name}${m.fide ? ` (FIDE ${m.fide})` : ''}${kept}`;
    return 'nicht erkannt';
  }

  num(v: string): number | null {
    const n = Number(v);
    return v.trim() && Number.isFinite(n) ? Math.round(n) : null;
  }

  onBoardMove(m: UserBoardMove): void {
    this.s.play(m.san);
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    const target = e.target as HTMLElement | null;
    if (target && /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)) return;
    if (e.key === 'ArrowLeft') { this.s.go(this.s.cursor() - 1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { this.s.go(this.s.cursor() + 1); e.preventDefault(); }
  }

  private revealCursor(): void {
    setTimeout(() => (this.host.nativeElement as HTMLElement).querySelector('.moves .ply.cursor')
      ?.scrollIntoView?.({ block: 'nearest' }));
  }

  async save(): Promise<void> {
    const legal = this.s.plies().filter(p => !p.illegal);
    if (this.s.illegalCount() > 0
      && !confirm(`${this.s.illegalCount()} Züge am Ende sind nicht legal und fallen weg. Trotzdem übernehmen?`)) return;
    this.saving.set(true);
    this.saveError.set(null);
    try {
      await this.api.addGame({
        moves: legal.map(p => p.san),
        white: this.name('white')().trim() || null,
        black: this.name('black')().trim() || null,
        whiteFide: this.sideState.white.fide(),
        blackFide: this.sideState.black.fide(),
        whiteElo: this.elo('white')(),
        blackElo: this.elo('black')(),
        whiteReplace: this.replace('white')(),
        blackReplace: this.replace('black')(),
        result: this.result(),
        event: this.event().trim() || null,
        year: this.year(),
        scanId: null,
      }, this.scanRef);
      if (this.share) rememberAnonKey(this.share, this.scanRef, false);
      void this.router.navigate(this.share ? ['/s', this.share, 'hochladen'] : ['/verein'],
        { queryParams: this.share ? { art: 'formular' } : {}, state: { msg: 'Partie in die Vereins-Datenbank übernommen.' } });
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.saveError.set(e?.error?.reason === 'illegal' ? `Ein Zug ist nicht legal: ${e.error.message}`
        : e?.error?.reason ? reasonText(e.error.reason) : 'Übernehmen hat nicht geklappt.');
    } finally {
      this.saving.set(false);
    }
  }

  async discard(): Promise<void> {
    if (!confirm('Formular verwerfen? Foto und Lesung werden gelöscht.')) return;
    try {
      await this.api.discard(this.scanRef);
      if (this.share) rememberAnonKey(this.share, this.scanRef, false);
      void this.router.navigate(this.backLink, { queryParams: { art: 'formular' } });
    } catch {
      this.saveError.set('Verwerfen hat nicht geklappt.');
    }
  }
}
