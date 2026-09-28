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
import { ClubApiService } from '../../core/club-api.service';
import { LeagueScanState, RosterPerson, SideMatch } from '../../core/club.models';
import { ANON_NAME, normalizeResult, reasonText, yearOf } from '../../core/club-format';
import { de } from '../../core/league-format';

const POLL_MS = 3000;
const MATCH_DEBOUNCE_MS = 400;

/**
 * Ein eingelesenes Partieformular prüfen und in die Vereins-Datenbank übernehmen (`/verein/formular/:id`).
 * Die Korrektur selbst ist DIESELBE wie in RookHub (`SheetEditSession`: Cursor-Brett, Lesarten, Ersetzen/Einfügen/
 * Löschen, nach jeder Änderung wird der Rest aus den Formular-Einträgen neu aufbereitet); dazu die Namen mit
 * Ligaspieler-Prüfung und „Meinen Namen durch Schwaz ersetzen" (Vorgabe an). Übernehmen oder Verwerfen schließt die
 * Einlesung — das Foto verschwindet.
 */
@Component({
  selector: 'lh-club-scan-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgClass, ChessBoardComponent],
  template: `
    @if (!allowed) {
      <section class="gate"><h2>Nicht freigeschaltet</h2>
        <p>Partieformulare einlesen dürfen Admins und die Vereinsgruppe von SK Schwaz.</p></section>
    } @else if (notFound()) {
      <section class="gate"><h2>Formular nicht gefunden</h2>
        <p>Es wurde schon übernommen oder verworfen. <a routerLink="/verein/neu" [queryParams]="{ art: 'formular' }">Zu deinen Formularen</a></p></section>
    } @else if (state(); as st) {
      <section class="club-intro">
        <p><a routerLink="/verein/neu" [queryParams]="{ art: 'formular' }">← Deine Formulare</a></p>
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
            <label class="field">Weiß
              <input list="lh-roster" [value]="white()" (input)="setName('white', $any($event.target).value)" maxlength="120" />
              <span class="match" [class.ok]="whiteMatch()?.league">{{ matchText(whiteMatch(), 'white') }}</span>
            </label>
            <label class="field narrow">Elo<input type="number" inputmode="numeric" min="500" max="3000" [value]="whiteElo() ?? ''"
                                             (input)="whiteElo.set(num($any($event.target).value))" /></label>
            <label class="field">Schwarz
              <input list="lh-roster" [value]="black()" (input)="setName('black', $any($event.target).value)" maxlength="120" />
              <span class="match" [class.ok]="blackMatch()?.league">{{ matchText(blackMatch(), 'black') }}</span>
            </label>
            <label class="field narrow">Elo<input type="number" inputmode="numeric" min="500" max="3000" [value]="blackElo() ?? ''"
                                             (input)="blackElo.set(num($any($event.target).value))" /></label>
            <label class="field narrow">Jahr<input type="number" inputmode="numeric" min="1900" [max]="maxYear" [value]="year() ?? ''"
                                              (input)="year.set(num($any($event.target).value))" /></label>
            <label class="field narrow">Ergebnis
              <select (change)="result.set($any($event.target).value)">
                @for (r of results; track r) { <option [value]="r" [selected]="result() === r">{{ r === '*' ? 'unbekannt' : r }}</option> }
              </select>
            </label>
            <label class="field wide">Veranstaltung <span class="muted small">(fällt bei „Schwaz" weg)</span>
              <input [value]="event()" (input)="event.set($any($event.target).value)" maxlength="200" [disabled]="anonymize()" />
            </label>
          </div>
          <datalist id="lh-roster">@for (p of suggestions(); track p.name + (p.fide ?? '')) { <option [value]="p.name">{{ p.teams.join(', ') }}</option> }</datalist>

          <div class="field-row">
            <span class="muted small">Ich spiele</span>
            <div class="seg" role="group" aria-label="Ich spiele">
              <button type="button" [attr.aria-pressed]="ownerSide() === 'white'" (click)="ownerSide.set('white')">Weiß</button>
              <button type="button" [attr.aria-pressed]="ownerSide() === 'black'" (click)="ownerSide.set('black')">Schwarz</button>
            </div>
          </div>
          <label class="anon-toggle">
            <input type="checkbox" [checked]="anonymize()" (change)="anonymize.set($any($event.target).checked)" />
            <span><b>Meinen Namen durch „Schwaz" ersetzen</b>
              <span class="muted">Dann wird weder gespeichert, wer hinter „Schwaz" steht, noch wer hochgeladen hat.</span></span>
          </label>

          <p class="preview"><span class="muted">Gespeichert wird:</span> <b>{{ preview() }}</b></p>
          @if (anonymize() && !ownerSide()) { <p class="err small">Wähle, welche Seite du gespielt hast.</p> }
          @else if (noLeaguePlayer()) { <p class="err small">Kein Ligaspieler erkannt — so wird die Partie nicht angenommen. Namen prüfen (Vorschläge beim Tippen).</p> }

          <div class="actions">
            <button type="button" class="btn-pri" [disabled]="saving() || s.busy() || (anonymize() && !ownerSide())" (click)="save()">
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
  private readonly api = inject(ClubApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject(ElementRef);

  readonly allowed = this.auth.has('league.contribute');
  readonly results = ['1-0', '0-1', '1/2-1/2', '*'];
  readonly maxYear = new Date().getFullYear() + 1;
  readonly de = de;

  scanId = 0;
  readonly state = signal<LeagueScanState | null>(null);
  readonly notFound = signal(false);
  readonly photoUrl = signal<string | null>(null);
  readonly zoom = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly white = signal('');
  readonly black = signal('');
  readonly whiteElo = signal<number | null>(null);
  readonly blackElo = signal<number | null>(null);
  readonly year = signal<number | null>(null);
  readonly result = signal('*');
  readonly event = signal('');
  readonly ownerSide = signal<'white' | 'black' | null>(null);
  readonly anonymize = signal(true);
  readonly whiteMatch = signal<SideMatch | null>(null);
  readonly blackMatch = signal<SideMatch | null>(null);
  readonly suggestions = signal<RosterPerson[]>([]);

  readonly s = new SheetEditSession({
    resolve: (prefix, writtenFrom) => this.api.resolve(this.scanId, prefix, writtenFrom),
    moved: () => this.revealCursor(),
    resolveFailed: () => this.saveError.set('Den Rest neu zu lesen hat nicht geklappt — der bisherige Stand bleibt.'),
    bind: o => o.pipe(takeUntilDestroyed(this.destroyRef)),
  });

  /** Wie die Partie in der Datenbank steht (Jahr · Weiß – Schwarz · Ergebnis). */
  readonly preview = computed(() => {
    const name = (side: 'white' | 'black') => {
      if (this.anonymize() && this.ownerSide() === side) return ANON_NAME;
      const m = side === 'white' ? this.whiteMatch() : this.blackMatch();
      return m?.name || (side === 'white' ? this.white() : this.black()).trim() || '?';
    };
    return [this.year() ?? 'ohne Jahr', `${name('white')} – ${name('black')}`, this.result() === '*' ? 'Ergebnis offen' : this.result()]
      .join(' · ');
  });
  /** Bleibt nach dem Ersetzen durch „Schwaz" noch ein Ligaspieler übrig? */
  readonly noLeaguePlayer = computed(() => {
    const w = this.whiteMatch(), b = this.blackMatch();
    if (!w || !b) return false;
    const skip = this.anonymize() ? this.ownerSide() : null;
    return !((skip !== 'white' && w.league) || (skip !== 'black' && b.league));
  });

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private matchTimer: ReturnType<typeof setTimeout> | null = null;
  private suggestTimer: ReturnType<typeof setTimeout> | null = null;
  private matchSeq = 0;
  private destroyed = false;

  ngOnInit(): void {
    if (!this.allowed) return;
    this.scanId = Number(this.route.snapshot.paramMap.get('id'));
    void this.load();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    for (const t of [this.pollTimer, this.matchTimer, this.suggestTimer]) if (t) clearTimeout(t);
    const url = this.photoUrl();
    if (url) URL.revokeObjectURL(url);
  }

  private async load(): Promise<void> {
    let st: LeagueScanState;
    try {
      st = await this.api.scan(this.scanId);
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
    this.white.set(st.white ?? '');
    this.black.set(st.black ?? '');
    this.year.set(yearOf(st.date));
    this.result.set(normalizeResult(st.result));
    this.event.set(st.event ?? '');
    this.ownerSide.set(st.ownerSide);
    void this.match();
    try {
      const blob = await this.api.photo(this.scanId);
      if (!this.destroyed) this.photoUrl.set(URL.createObjectURL(blob));
    } catch { /* ohne Foto geht die Korrektur trotzdem */ }
  }

  setName(side: 'white' | 'black', value: string): void {
    (side === 'white' ? this.white : this.black).set(value);
    if (this.matchTimer) clearTimeout(this.matchTimer);
    this.matchTimer = setTimeout(() => void this.match(), MATCH_DEBOUNCE_MS);
    if (this.suggestTimer) clearTimeout(this.suggestTimer);
    const q = value.trim();
    this.suggestTimer = setTimeout(async () => {
      if (q.length < 2) { this.suggestions.set([]); return; }
      try { this.suggestions.set(await this.api.players(q)); } catch { /* Vorschläge sind Beiwerk */ }
    }, MATCH_DEBOUNCE_MS);
  }

  private async match(): Promise<void> {
    const my = ++this.matchSeq;
    try {
      const m = await this.api.match(this.white(), this.black());
      if (my !== this.matchSeq) return;
      this.whiteMatch.set(m.white);
      this.blackMatch.set(m.black);
    } catch { /* die Prüfung macht der Server beim Übernehmen ohnehin */ }
  }

  matchText(m: SideMatch | null, side: 'white' | 'black'): string {
    if (this.anonymize() && this.ownerSide() === side) return `wird „${ANON_NAME}"`;
    if (!m) return '';
    if (m.ambiguous) return 'Ligaspieler (mehrere dieses Namens)';
    if (m.league) return m.fide ? `Ligaspieler: ${m.name}` : `Ligaspieler: ${m.name} (ohne FIDE-ID)`;
    return 'kein Ligaspieler';
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
        white: this.white().trim() || null,
        black: this.black().trim() || null,
        whiteElo: this.whiteElo(),
        blackElo: this.blackElo(),
        result: this.result(),
        event: this.event().trim() || null,
        year: this.year(),
        ownerSide: this.ownerSide(),
        anonymize: this.anonymize(),
        scanId: this.scanId,
      });
      void this.router.navigate(['/verein'], { state: { msg: 'Partie in die Vereins-Datenbank übernommen.' } });
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
      await this.api.discard(this.scanId);
      void this.router.navigate(['/verein/neu'], { queryParams: { art: 'formular' } });
    } catch {
      this.saveError.set('Verwerfen hat nicht geklappt.');
    }
  }
}
