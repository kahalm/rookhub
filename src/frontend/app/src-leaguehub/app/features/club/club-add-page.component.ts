import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnInit, computed, effect, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { localStore, readJson, writeJson } from '@rh/core/local-json-store';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubDraft, ClubImportResult, OpenScan, ScanRef, ScoresheetStatus } from '../../core/club.models';
import { ANON_NAME, importSummary, loadErrorText, reasonText, scanAvailability, scanStateText, shortDateTime, uploadErrorText } from '../../core/club-format';
import { SECONDS_PER_MOVE, SecondsTicker, formatClock, readingSeconds } from '@rh/features/games/scoresheet-timing';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { CHESSBASE_MAX_UPLOAD_BYTES, chessBaseErrorText, chessBaseNote, chessBaseSelection, packForUpload } from '../../core/chessbase-upload';
import { partLabel, pgnPortions, portionNote } from '../../core/pgn-portions';
import { ClubImportReviewComponent } from './club-import-review.component';
import { ImportReview } from './import-review';
import { AccessGateComponent } from '../../shared/access-gate.component';

const POLL_MS = 3000;
/** Ohne Konto merkt sich der Browser die Schlüssel seiner Einlesungen — sonst fände er sie nach dem Neuladen nicht. */
const ANON_KEYS = 'lh-anon-scans';
type Kind = 'pgn' | 'formular';

/** Die gemerkten Schlüssel je Teilen-Link. */
export function anonKeys(share: string): string[] {
  return (readJson<Record<string, string[]>>(localStore(), ANON_KEYS) ?? {})[share] ?? [];
}

export function rememberAnonKey(share: string, key: string, keep = true, store = ANON_KEYS): void {
  const all = readJson<Record<string, string[]>>(localStore(), store) ?? {};
  const list = (all[share] ?? []).filter(k => k !== key);
  all[share] = keep ? [key, ...list].slice(0, 20) : list;
  writeJson(localStore(), store, all);
}

/** Ohne Konto: die Schlüssel der eigenen Entwürfe (0.595.0) — wie bei den Formularen. */
const ANON_DRAFT_KEYS = 'lh-anon-drafts';
export const draftKeys = (share: string): string[] =>
  (readJson<Record<string, string[]>>(localStore(), ANON_DRAFT_KEYS) ?? {})[share] ?? [];
export const rememberDraftKey = (share: string, key: string, keep = true): void => rememberAnonKey(share, key, keep, ANON_DRAFT_KEYS);

/** Wie lange nach der letzten Änderung der Übersicht gespeichert wird (ms) — Tippen und Klicken sollen nicht je Zug senden. */
const SAVE_DEBOUNCE_MS = 1500;

/**
 * Partien hinzufügen — angemeldet (`/verein/neu`, Vereinsgruppe) ODER ohne Konto über einen Teilen-Link
 * (`/s/:token/hochladen`). Viele auf einmal als PGN (erst die Übersicht „wer gegen wen", Spieler korrigieren, dann
 * importieren) — oder EIN Partieformular fotografieren, lesen lassen und auf der Korrekturseite prüfen.
 * Spieler von Schwaz werden standardmäßig durch „Schwaz" ersetzt (Wunsch des Nutzers).
 */
@Component({
  selector: 'lh-club-add-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ClubImportReviewComponent, AccessGateComponent],
  template: `
    @if (!allowed) {
      <!-- UX-033: wer schon lesen darf, gehört zur Lesegruppe — ihm fehlt nur das Beitragsrecht. -->
      @if (canRead) {
        <lh-access-gate text="Du kannst die Vereinspartien lesen. Zum Hinzufügen fehlt dir noch die Freigabe — frag einen Verwalter."
                        purpose="das Hinzufügen von Vereinspartien" [back]="{ link: '/verein', label: 'Zu den Vereinspartien' }" />
      } @else {
        <lh-access-gate text="Partien hinzufügen dürfen Admins und die Vereinsgruppe von SK Schwaz." />
      }
    } @else {
      <section class="club-intro">
        @if (share) { <p><a class="back-link" [routerLink]="['/s', share]">← Zur Begegnung</a></p> }
        <h2>Partien hinzufügen</h2>
        <p class="muted">Angenommen wird jede Partie mit einem bekannten Gegner — geprüft an den Meldelisten aller Saisonen,
          sonst am Spielerverzeichnis der Megabase. Vom Datum bleibt nur das Jahr.@if (share) { Ohne Anmeldung — gespeichert wird nicht, wer hochgeladen hat. }</p>
      </section>

      <!-- UX-071: ein Umschalter wie die übrigen von LeagueHub (role=group + aria-pressed). Vorher role=tab MIT aria-pressed
           (axe: aria-allowed-attr, critical), ohne tabpanel und ohne Pfeiltasten. -->
      <div class="seg club-kind" role="group" aria-label="Art">
        <button type="button" [attr.aria-pressed]="kind() === 'pgn'" (click)="setKind('pgn')">PGN-Datei</button>
        <button type="button" [attr.aria-pressed]="kind() === 'formular'" (click)="setKind('formular')">Partieformular</button>
      </div>

      @if (kind() === 'pgn') {
        <section class="panel">
          @if (review(); as rv) {
            @if (dbNote(); as note) { <p class="small db-note" role="status">{{ note }}</p> }
            @if (portionNote(); as note) { <p class="small portion-note" role="status">{{ note }}</p> }
            @if (draft(); as d) {
              <p class="small muted draft-note">Die Liste liegt online als Entwurf — brichst du ab, machst du später unter „Deine offenen Listen" weiter@if (!share) {, und ein Verwalter kann den Import fertigstellen}.
                @if (resumedFrom(); as who) { <b>Du stellst die Liste von {{ who }} fertig.</b> }</p>
            }
            <lh-club-import-review [review]="rv" [client]="client" [pgn]="pgn()" [remembers]="!share" [draftId]="share ? null : (draft()?.id ?? null)"
                                   (imported)="done($event)" (cancel)="closeCurrent()" (savedChange)="onSaved($event)" />
          } @else {
            <label class="anon-toggle">
              <input type="checkbox" [checked]="replaceClub()" (change)="replaceClub.set($any($event.target).checked)" />
              <span><b>Spieler von Schwaz durch „{{ anon }}“ ersetzen</b>
                <span class="muted">Jeder, der in seiner jüngsten Saison für Schwaz gemeldet ist@if (!share) {, und du selbst}. Dann zeigt
                  LeagueHub nirgends, wer dahinter steht (der echte Name bleibt nur intern für Auswertungen des Vereins), und es wird
                  nicht gespeichert, wer hochgeladen hat — so kann niemand gezielt gegen uns vorbereiten.
                  In der Übersicht lässt sich das je Partie ändern.</span></span>
            </label>
            <label class="field">PGN-Datei
              <input type="file" accept=".pgn,application/x-chess-pgn,text/plain" [disabled]="busy()" (change)="pickFile($event)" />
              @if (readingFile()) { <span class="small muted"><b>Lese die Datei …</b></span> }
            </label>
            <label class="field">… oder eine ChessBase-Datenbank
              <input type="file" multiple [disabled]="busy()" (change)="pickChessBase($event)" />
              <span class="small muted">Alle Dateien der Datenbank auswählen (z. B. MeineSpiele.2cbh, .2cbg, .2lid … bzw. .cbh,
                .cbg, .cbp …) oder ein ZIP davon. Gelesen wird die Hauptvariante, ohne Kommentare.
                @if (readingDb()) { <b>Lese die Datenbank …</b> }</span>
            </label>
            <div class="field">… oder eine öffentliche Lichess-Studie
              <div class="linkrow">
                <input type="url" inputmode="url" placeholder="https://lichess.org/study/…" [value]="studyUrl()"
                       (input)="studyUrl.set($any($event.target).value)" (keydown.enter)="$event.preventDefault(); loadStudy()" />
                <button type="button" class="btn-sec" [disabled]="busy() || !studyUrl().trim()" (click)="loadStudy()">
                  {{ loadingStudy() ? 'Lade …' : 'Laden' }}</button>
              </div>
            </div>
            <label class="field">… oder hier einfügen
              <textarea rows="6" spellcheck="false" placeholder="[Event &quot;…&quot;]&#10;1. e4 c5 2. Nf3 …" [value]="pgn()"
                        (input)="pgn.set($any($event.target).value)"></textarea>
            </label>
            <div class="actions">
              <button type="button" class="btn-pri" [disabled]="busy() || !pgn().trim()" (click)="startPreview()">
                {{ previewing() ? 'Lese …' : 'Partien prüfen' }}</button>
              <span class="muted small">Erst kommt eine Übersicht — gespeichert wird erst mit „Importieren“. Mehr als 500 Partien kommen in Paketen.</span>
              <span class="update-msg" [class.err]="!!importError()" role="status">{{ importError() ?? '' }}</span>
            </div>
          }
          @if (!review() && drafts().length) {
            <h3 class="club-h3">Deine offenen Listen</h3>
            <p class="small muted">Eingelesen, aber noch nicht (ganz) importiert — mit deinen Korrekturen gespeichert.</p>
            <ul class="scan-list">
              @for (d of drafts(); track d.ref) {
                <li>
                  <span>{{ draftTitle(d) }}</span>
                  <span class="muted">{{ draftState(d) }} · {{ when(d.updatedAt) }}</span>
                  <button type="button" class="btn-sec" [disabled]="resuming()" (click)="resume(d)">Weiter</button>
                  <button type="button" class="btn-link" (click)="discardDraft(d)">Verwerfen</button>
                </li>
              }
            </ul>
          }
          @if (!review() && isManager && othersDrafts().length) {
            <h3 class="club-h3">Offene Listen anderer <span class="muted small">(nur Verwalter)</span></h3>
            <p class="small muted">Eingereicht, aber nicht fertig importiert — auch über Teilen-Links. Du kannst den Import
              fertigstellen (mit den Korrekturen und Vorgaben des Einreichers) oder die Liste verwerfen.</p>
            <ul class="scan-list">
              @for (d of othersDrafts(); track d.ref) {
                <li>
                  <span>{{ draftTitle(d) }}</span>
                  <span class="muted">{{ draftState(d) }} · {{ d.viaShareLink ? 'über Teilen-Link' : (d.owner ?? 'mit Konto') }} · {{ when(d.updatedAt) }}</span>
                  <button type="button" class="btn-sec" [disabled]="resuming()" (click)="resume(d)">Fertigstellen</button>
                  <button type="button" class="btn-link" (click)="discardDraft(d)">Verwerfen</button>
                </li>
              }
            </ul>
          }
          @if (result(); as r) {
            <p class="result" role="status"><b>{{ summary(r) }}</b>
              @if (r.added && !share) { <a routerLink="/verein">Zu den Vereinspartien</a> }</p>
            @if (r.failed.length) {
              <div class="roster-scroll">
                <table class="rtable">
                  <thead><tr><th class="num">Nr.</th><th>Weiß</th><th>Schwarz</th><th>Warum nicht</th></tr></thead>
                  <tbody>
                    @for (f of r.failed; track f.index) {
                      <tr><td class="num">{{ f.index }}</td><td>{{ f.white ?? '?' }}</td><td>{{ f.black ?? '?' }}</td>
                        <td class="small">{{ reason(f.reason) }}</td></tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          }
        </section>
      } @else {
        <section class="panel">
          @if (status(); as s) {
            <!-- Anbieter und Übermittlung nennen: das Foto (Namen, Unterschriften) geht an Anthropic in den USA (A6-008). -->
            <p class="muted">{{ availability()!.text }} Das Foto liest Claude, ein KI-Modell von Anthropic (USA) — dafür wird
              es dorthin übertragen (<a routerLink="/privacy">Datenschutz</a>). Das dauert etwa {{ perMove }} Sekunden pro
              Zug; danach prüfst du die Züge und Namen selbst, bevor etwas gespeichert wird.</p>
            @if (availability()!.ok) {
              <label class="field">Foto des Formulars
                <input #photoInput type="file" accept="image/*" capture="environment" (change)="pickPhoto($event)" />
              </label>
              <div class="field-row">
                <label class="field">Notation
                  <select (change)="language.set($any($event.target).value)">
                    <option value="auto" [selected]="language() === 'auto'">automatisch erkennen</option>
                    @for (l of s.languages; track l.code) {
                      <option [value]="l.code" [selected]="language() === l.code">{{ l.name }} ({{ l.pieces }})</option>
                    }
                  </select>
                </label>
                <label class="field">Ich spiele
                  <select (change)="side.set($any($event.target).value)">
                    <option value="auto" [selected]="side() === 'auto'">{{ share ? 'wähle ich danach' : 'automatisch (Name im Profil)' }}</option>
                    <option value="white" [selected]="side() === 'white'">Weiß</option>
                    <option value="black" [selected]="side() === 'black'">Schwarz</option>
                  </select>
                </label>
              </div>
              <div class="actions">
                <button type="button" class="btn-pri" [disabled]="uploading() || !photo()" (click)="upload()">
                  {{ uploading() ? 'Lade hoch …' : 'Formular einlesen' }}</button>
              </div>
            }
          } @else if (statusError(); as e) {
            <!-- UX-034: früher „Lade …" für immer und darunter „Hochladen hat nicht geklappt", obwohl nichts hochgeladen war. -->
            <p class="err">Ob Einlesen gerade geht, ließ sich nicht prüfen. {{ e }}</p>
            <div class="actions"><button type="button" class="btn-sec" (click)="retryStatus()">Neu laden</button></div>
          } @else {
            <p class="muted">Lade …</p>
          }
          <span class="update-msg" [class.err]="!!scanError()" role="status">{{ scanError() ?? '' }}</span>

          @if (scans().length) {
            <h3 class="club-h3">Deine Formulare</h3>
            <ul class="scan-list">
              @for (sc of scans(); track sc.ref) {
                <li>
                  <span>{{ sc.scan.white || '?' }} – {{ sc.scan.black || '?' }}</span>
                  <span class="muted">{{ stateText(sc.scan) }}@if (isOpen(sc.scan)) {
                    <span class="scan-clock" [attr.aria-label]="'läuft seit ' + clockOf(sc.scan)"> · {{ clockOf(sc.scan) }}</span> }</span>
                  @if (sc.scan.status === 'done') { <a class="btn-sec" [routerLink]="scanLink(sc)">Prüfen und übernehmen</a> }
                  @if (sc.scan.status === 'failed') { <button type="button" class="btn-link" (click)="discard(sc)">Verwerfen</button> }
                </li>
              }
            </ul>
          }

          @if (isManager && othersOpen().length) {
            <h3 class="club-h3">Offene Formulare anderer <span class="muted small">(nur Verwalter)</span></h3>
            <p class="small muted">Hochgeladen, aber noch nicht geprüft — auch über Teilen-Links. Du kannst sie prüfen und
              übernehmen oder verwerfen, damit nichts liegen bleibt.</p>
            <ul class="scan-list">
              @for (o of othersOpen(); track o.scan.id) {
                <li>
                  <span>{{ o.scan.white || '?' }} – {{ o.scan.black || '?' }}</span>
                  <span class="muted">{{ stateText(o.scan) }}@if (isOpen(o.scan)) { <span class="scan-clock"> · {{ clockOf(o.scan) }}</span> }
                    · {{ o.viaShareLink ? 'über Teilen-Link' : 'mit Konto' }} · {{ when(o.scan.createdAt) }}</span>
                  @if (o.scan.status === 'done') { <a class="btn-sec" [routerLink]="['/verein/formular', o.scan.id]">Prüfen und übernehmen</a> }
                  <button type="button" class="btn-link" (click)="discardOther(o)">Verwerfen</button>
                </li>
              }
            </ul>
          }
        </section>
      }
    }
  `,
})
export class ClubAddPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Token des Teilen-Links (Weg ohne Anmeldung) — sonst `null`. */
  readonly share = this.route.snapshot.paramMap.get('token');
  private readonly clubApi = inject(ClubApiService);
  private readonly confirm = inject(ConfirmService);
  readonly client: ClubClient = this.clubApi.client(this.share);
  readonly allowed = !!this.share || this.auth.has('league.contribute');
  /** Ohne Beitragsrecht, aber mit Leserecht: die Sperrkarte nennt das fehlende Recht und führt zu den Vereinspartien. */
  readonly canRead = this.auth.has('league.view');
  readonly anon = ANON_NAME;

  readonly kind = signal<Kind>('pgn');
  readonly replaceClub = signal(true);
  readonly pgn = signal('');
  readonly previewing = signal(false);
  readonly studyUrl = signal('');
  readonly loadingStudy = signal(false);
  readonly importError = signal<string | null>(null);
  /** ChessBase: was aus der Datenbank wurde (gelesen, übersprungen) — steht über der Übersicht. */
  readonly dbNote = signal<string | null>(null);
  /** In Pakete geteilt: wie viele, und ob die übrigen als offene Listen liegen. */
  readonly portionNote = signal<string | null>(null);
  readonly readingDb = signal(false);
  /** Eine PGN-Datei wird gelesen und in die Übersicht geführt (von der Wahl bis die Übersicht steht). */
  readonly readingFile = signal(false);
  /** Läuft schon eine Liste (Datei, Datenbank, Studie oder Übersicht)? Dann startet kein zweiter Weg daneben — sonst
   *  liefen zwei Vorschauen parallel und legten Pakete doppelt bzw. einen verwaisten Entwurf ab (UX-037-Nacharbeit). */
  readonly busy = computed(() => this.readingFile() || this.readingDb() || this.loadingStudy() || this.previewing());
  readonly review = signal<ImportReview | null>(null);
  readonly result = signal<ClubImportResult | null>(null);

  // ── Entwürfe (0.595.0, Wunsch: „soll gleich online abgelegt werden — damit ein Admin den Import fertigstellen kann") ──
  /** Der Entwurf der Liste in der Übersicht; `pgn` = der Text, zu dem er gehört (eine neu eingefügte Liste ist ein neuer). */
  readonly draft = signal<(ClubDraft & { pgn: string }) | null>(null);
  readonly drafts = signal<ClubDraft[]>([]);
  readonly othersDrafts = signal<ClubDraft[]>([]);
  readonly resuming = signal(false);
  /** Stellt ein Verwalter die Liste eines anderen fertig: dessen Name (oder „einem Teilen-Link"). */
  readonly resumedFrom = signal<string | null>(null);
  /** Woher der Text kam — steht in der Liste der offenen Entwürfe. */
  private source = 'text';
  private label: string | null = null;
  /** Schon importierte Partien des Entwurfs (aus früheren Läufen) — die neuen kommen dazu. */
  private importedBefore: number[] = [];
  private saveTimer: ReturnType<typeof setTimeout> | null = null;

  readonly status = signal<ScoresheetStatus | null>(null);
  /** Der Status-Abruf (geht Einlesen gerade?) kam nicht — eigener Text, kein Upload-Fehler. */
  readonly statusError = signal<string | null>(null);
  readonly availability = computed(() => { const s = this.status(); return s ? scanAvailability(s) : null; });
  readonly language = signal('auto');
  readonly side = signal<'auto' | 'white' | 'black'>('auto');
  readonly photo = signal<File | null>(null);
  private readonly photoInput = viewChild<ElementRef<HTMLInputElement>>('photoInput');
  readonly uploading = signal(false);
  readonly scanError = signal<string | null>(null);
  readonly scans = signal<ScanRef[]>([]);

  readonly reason = reasonText;
  readonly summary = importSummary;
  readonly stateText = scanStateText;
  readonly when = shortDateTime;
  /** Verwalter sehen zusätzlich die offenen Formulare ALLER (Wunsch 2026-09-28: „damit die nicht im Limbo sind"). */
  readonly isManager = !this.share && this.auth.has('league.manage');
  readonly openScans = signal<OpenScan[]>([]);
  readonly othersOpen = computed(() => this.openScans().filter(o => !o.mine));
  readonly perMove = SECONDS_PER_MOVE;
  /** Mitlaufende Uhr, solange ein Formular gelesen wird (Wunsch 2026-09-28: „ein Timer, der raufzählt"). */
  private readonly ticker = new SecondsTicker();
  readonly isOpen = (s: { status: string }) => s.status === 'pending' || s.status === 'running';
  clockOf(s: { createdAt: string }): string {
    return formatClock(readingSeconds(s.createdAt, this.ticker.now()));
  }
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      if (this.pollTimer) clearTimeout(this.pollTimer);
      if (this.saveTimer) { clearTimeout(this.saveTimer); void this.flushSave(); }
      this.ticker.stop();
    });
    // Jede Korrektur in der Übersicht landet (gedrosselt) im Entwurf — auch die, die dann niemand mehr importiert.
    effect(() => {
      const rv = this.review();
      if (!rv || !this.draft()) return;
      rv.games();
      if (this.saveTimer) clearTimeout(this.saveTimer);
      this.saveTimer = setTimeout(() => void this.flushSave(), SAVE_DEBOUNCE_MS);
    });
  }

  private async flushSave(): Promise<void> {
    this.saveTimer = null;
    const d = this.draft(), rv = this.review();
    if (!d || !rv) return;
    try { await this.client.saveDraft(d.ref, { state: rv.snapshot() }); } catch { /* nächste Änderung versucht es wieder */ }
  }

  ngOnInit(): void {
    if (!this.allowed) return;
    if (this.route.snapshot.queryParamMap.get('art') === 'formular') this.kind.set('formular');
    const game = Number(this.route.snapshot.queryParamMap.get('partie'));
    if (!this.share && Number.isInteger(game) && game > 0) void this.loadSavedGame(game);
    void this.loadScans();
    void this.loadDrafts();
  }

  /** Die offenen Entwürfe — die eigenen, und für Verwalter die aller anderen. */
  async loadDrafts(): Promise<void> {
    try {
      const keys = this.share ? draftKeys(this.share) : [];
      const list = await this.client.drafts(keys);
      if (this.share) for (const k of keys) if (!list.some(d => d.ref === k)) rememberDraftKey(this.share, k, false);
      this.drafts.set(list);
      if (this.isManager) this.othersDrafts.set((await this.clubApi.allDrafts()).filter(d => !d.mine));
    } catch { /* Beiwerk — die Seite geht auch ohne */ }
  }

  draftTitle(d: ClubDraft): string {
    const src = d.source === 'lichess' ? 'Lichess-Studie' : d.source === 'rookhub' ? 'aus RookHub' : d.source === 'datei' ? 'Datei'
      : d.source === 'chessbase' ? 'ChessBase' : 'eingefügt';
    return `${d.label ?? src} (${d.gameCount} ${d.gameCount === 1 ? 'Partie' : 'Partien'})`;
  }

  draftState(d: ClubDraft): string {
    return d.importedCount ? `${d.importedCount} von ${d.gameCount} importiert` : 'noch nichts importiert';
  }

  /** Einen Entwurf wieder aufnehmen: Text laden, Übersicht neu lesen (für den Einreicher), Korrekturen wiederherstellen. */
  async resume(d: ClubDraft): Promise<void> {
    this.resuming.set(true);
    this.importError.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    this.result.set(null);
    try {
      const full = await this.client.draft(d.ref);
      this.pgn.set(full.pgn);
      this.importedBefore = full.imported;
      this.draft.set({ ...d, pgn: full.pgn });
      this.resumedFrom.set(this.foreignOwner(d));
      const preview = await this.client.preview(full.pgn, this.share ? null : full.id);
      this.review.set(ImportReview.restore(preview, full.state, this.replaceClub()));
    } catch {
      this.importError.set('Die Liste ließ sich nicht öffnen — vielleicht hat sie inzwischen jemand anderes fertiggestellt.');
      void this.loadDrafts();
    } finally {
      this.resuming.set(false);
    }
  }

  /** Die Liste eines anderen (nur Verwalter sehen die): dessen Name bzw. „einem Teilen-Link" — sonst null. */
  private foreignOwner(d: ClubDraft): string | null {
    return d.mine || !this.othersDrafts().some(o => o.ref === d.ref) ? null : d.viaShareLink ? 'einem Teilen-Link' : d.owner ?? 'jemand anderem';
  }

  async discardDraft(d: ClubDraft): Promise<void> {
    const who = this.foreignOwner(d);
    if (!(await firstValueFrom(this.confirm.ask(`Liste „${this.draftTitle(d)}"${who ? ` von ${who}` : ''} verwerfen? Schon importierte Partien bleiben, der Rest wird gelöscht.`)))) return;
    try {
      await this.client.deleteDraft(d.ref);
      if (this.share) rememberDraftKey(this.share, d.ref, false);
    } catch { /* schon weg */ }
    void this.loadDrafts();
  }

  /**
   * „Schließen" in der Übersicht: nur die Übersicht geht zu, der Entwurf bleibt samt Korrekturen unter „Deine offenen
   * Listen" (bzw. „Offene Listen anderer") liegen. Früher hieß der Knopf „Verwerfen" und löschte den Entwurf ohne
   * Rückfrage — beim Fertigstellen auch den eines anderen. Endgültig löschen geht nur noch aus der Liste, mit Rückfrage.
   */
  async closeCurrent(): Promise<void> {
    const d = this.draft(), rv = this.review();
    if (!d && rv && !(await firstValueFrom(this.confirm.ask('Die Liste liegt nicht online — schließt du die Übersicht, gehen deine Korrekturen verloren. Trotzdem schließen?')))) return;
    const unsaved = !!this.saveTimer;
    this.review.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    this.resetDraft();
    // Der Text liegt im Entwurf — stünde er noch im Feld, legte ein erneutes „Partien prüfen" einen zweiten Entwurf an.
    if (d) this.pgn.set('');
    // Die letzte Korrektur wartet vielleicht noch auf die Drossel — jetzt speichern, sonst fehlt sie beim Weitermachen.
    if (d && rv && unsaved) {
      try { await this.client.saveDraft(d.ref, { state: rv.snapshot() }); } catch { /* bleibt beim letzten Stand */ }
    }
    void this.loadDrafts();
  }

  /** Nach jeder gespeicherten Portion — damit ein Abbruch nicht vergisst, was schon drin ist. */
  onSaved(indices: number[]): void {
    const d = this.draft();
    if (!d) return;
    const all = [...new Set([...this.importedBefore, ...indices])];
    void this.client.saveDraft(d.ref, { imported: all }).catch(() => undefined);
  }

  private resetDraft(): void {
    if (this.saveTimer) { clearTimeout(this.saveTimer); this.saveTimer = null; }
    this.draft.set(null);
    this.resumedFrom.set(null);
    this.importedBefore = [];
  }

  /** Eine Partie aus RookHub (⋮ → „In die Vereins-Datenbank", Wunsch 2026-09-28) — gleich in die Übersicht, wie ein Upload. */
  async loadSavedGame(id: number): Promise<void> {
    this.kind.set('pgn');
    this.importError.set(null);
    try {
      this.pgn.set((await this.clubApi.savedGame(id)).pgn);
      this.loaded(this.pgn(), 'rookhub', `Partie ${id} aus RookHub`);
    } catch {
      this.importError.set('Die Partie aus RookHub ließ sich nicht laden — bist du hier mit demselben Konto angemeldet?');
      return;
    }
    await this.startPreview();
  }

  async discardOther(o: OpenScan): Promise<void> {
    if (!(await firstValueFrom(this.confirm.ask(`Formular ${o.scan.white || '?'} – ${o.scan.black || '?'} verwerfen? Foto und Lesung werden gelöscht.`)))) return;
    try {
      await this.client.discard(String(o.scan.id));
      this.openScans.update(list => list.filter(x => x.scan.id !== o.scan.id));
    } catch {
      this.scanError.set('Verwerfen hat nicht geklappt.');
    }
  }

  setKind(k: Kind): void {
    this.kind.set(k);
    void this.router.navigate([], { queryParams: { art: k === 'formular' ? 'formular' : null }, replaceUrl: true });
    if (k === 'formular' && !this.status()) void this.loadStatus();
  }

  scanLink(sc: ScanRef): unknown[] {
    return this.share ? ['/s', this.share, 'formular', sc.ref] : ['/verein/formular', sc.ref];
  }

  /** Eine PGN-Datei: lesen und gleich in die Übersicht — wie ChessBase-Datenbank und Lichess-Studie (UX-037). Vorher füllte
   *  die Wahl nur das Textfeld, das am Handy unter dem Rand lag: sichtbar passierte nichts, und dieselbe Datei noch einmal
   *  zu wählen löste kein `change` aus. */
  async pickFile(ev: Event): Promise<void> {
    const input = ev.target as HTMLInputElement;
    const f = input.files?.[0];
    input.value = '';                                                  // dieselbe Auswahl darf noch einmal kommen
    // Läuft schon eine Liste, ist das Feld gesperrt — kommt trotzdem eine Wahl durch, keine zweite Vorschau daneben.
    if (!f || this.busy()) return;
    this.readingFile.set(true);
    try {
      this.dbNote.set(null);
      this.portionNote.set(null);
      this.importError.set(null);
      try {
        this.pgn.set(await f.text());
      } catch {
        this.importError.set('Die Datei ließ sich nicht lesen.');
        return;
      }
      this.loaded(this.pgn(), 'datei', f.name);
      this.result.set(null);
      await this.startPreview();                                       // teilt in Pakete wie jede andere Liste
    } finally {
      this.readingFile.set(false);
    }
  }

  /**
   * Eine ChessBase-Datenbank (0.598.0): die nötigen Dateien gepackt hochladen, der Server liefert das PGN — ab dort wie
   * jede andere Liste, auch das Aufteilen in Pakete (`startPreview`).
   */
  async pickChessBase(ev: Event): Promise<void> {
    const input = ev.target as HTMLInputElement;
    const picked = Array.from(input.files ?? []);
    input.value = '';                                                  // dieselbe Auswahl darf noch einmal kommen
    if (!picked.length || this.busy()) return;
    this.importError.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    this.result.set(null);
    const { send, hasDatabase } = chessBaseSelection(picked);
    if (!hasDatabase) {
      this.importError.set('Keine ChessBase-Datenbank dabei — die Datei mit der Endung .cbh oder .2cbh muss mit (oder ein ZIP).');
      return;
    }
    this.readingDb.set(true);
    try {
      const packed = await Promise.all(send.map(packForUpload));
      if (packed.reduce((sum, p) => sum + p.blob.size, 0) > CHESSBASE_MAX_UPLOAD_BYTES) {
        this.importError.set('Die Datenbank ist zu groß für einen Upload (gepackt höchstens 15 MB).');
        return;
      }
      const r = await this.client.chessBase(packed);
      if (!r.converted) {
        this.importError.set(r.games ? chessBaseNote(r) + ' Nichts zu importieren.' : `${r.name}: keine Partien in der Datenbank.`);
        return;
      }
      this.dbNote.set(chessBaseNote(r));
      this.pgn.set(r.pgn);
      this.loaded(r.pgn, 'chessbase', `${r.name}.${r.format}`);
      await this.startPreview();                                       // teilt in Pakete wie jede andere Liste
    } catch (err) {
      this.importError.set(chessBaseErrorText(err));
    } finally {
      this.readingDb.set(false);
    }
  }

  /** Eine öffentliche Lichess-Studie holen (der Server ruft Lichess) — danach geht es weiter wie mit einer Datei. */
  async loadStudy(): Promise<void> {
    if (this.busy()) return;                                           // Enter im Feld umgeht den gesperrten Knopf
    this.loadingStudy.set(true);
    this.importError.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    try {
      this.pgn.set(await this.client.lichess(this.studyUrl().trim()));
      this.loaded(this.pgn(), 'lichess', this.studyUrl().trim());
      this.result.set(null);
      await this.startPreview();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.importError.set(e?.error?.reason ? reasonText(e.error.reason) : 'Die Studie ließ sich nicht laden.');
    } finally {
      this.loadingStudy.set(false);
    }
  }

  /**
   * Die Übersicht öffnen. Eine Liste über dem, was die Übersicht auf einmal nimmt (500 Partien, 5 Mio. Zeichen), wird
   * VORHER in Pakete geteilt (0.598.1, Wunsch „auch beim PGN-Upload alle einlesen und dann in Paketen anbieten"): das erste
   * geht in die Übersicht, die übrigen werden danach als offene Listen abgelegt — egal, woher die Liste kam.
   */
  async startPreview(): Promise<void> {
    this.previewing.set(true);
    this.importError.set(null);
    this.result.set(null);
    this.portionNote.set(null);
    let rest: { pgn: string; label: string | null }[] = [];
    let source = 'text';
    const parts = pgnPortions(this.pgn());
    if (parts.length > 1) {
      source = this.pgnSource(this.pgn());
      const label = this.pgnLabel(this.pgn());
      rest = parts.slice(1).map((pgn, i) => ({ pgn, label: partLabel(label, i + 1, parts.length) }));
      this.pgn.set(parts[0]);
      this.loaded(parts[0], source, partLabel(label, 0, parts.length));
      this.portionNote.set(portionNote(parts.length, null));
    }
    try {
      // Sofort online ablegen (egal, woher der Text kam) — geht das nicht (Deckel, Netz), läuft es ohne Entwurf weiter.
      const pgn = this.pgn();
      let d = this.draft();
      if (!d || d.pgn !== pgn) {
        if (d) { try { await this.client.deleteDraft(d.ref); } catch { /* egal */ } }
        this.resetDraft();
        try {
          const created = await this.client.createDraft(pgn, this.pgnSource(pgn), this.pgnLabel(pgn));
          if (this.share && created.key) rememberDraftKey(this.share, created.key);
          d = { ...created, pgn };
          this.draft.set(d);
        } catch { d = null; }
      }
      this.review.set(new ImportReview(await this.client.preview(pgn, this.share ? null : d?.id ?? null), this.replaceClub()));
      if (rest.length) void this.parkPortions(rest, source, parts.length);
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.importError.set(e?.error?.reason ? reasonText(e.error.reason)
        : e?.status === 404 && this.share ? 'Dieser Link ist abgelaufen.'
        : e?.status === 403 ? 'Dafür fehlt dir die Berechtigung (Vereinsmitglieder).' : 'Lesen hat nicht geklappt.');
    } finally {
      this.previewing.set(false);
    }
  }

  /** Die übrigen Pakete als offene Listen ablegen — erst NACH der Übersicht, damit das erste auch bei vollem Deckel seinen
   * Entwurf hat. Am Deckel (20 je Konto, 5 ohne) hört es auf und sagt, wie viele fehlen. */
  private async parkPortions(rest: { pgn: string; label: string | null }[], source: string, parts: number): Promise<void> {
    let parked = 0;
    for (const p of rest) {
      try {
        const created = await this.client.createDraft(p.pgn, source, p.label);
        if (this.share && created.key) rememberDraftKey(this.share, created.key);
        parked++;
      } catch { break; }
    }
    if (this.destroyed) return;
    if (this.portionNote()) this.portionNote.set(portionNote(parts, parked));
    void this.loadDrafts();
  }

  done(r: ClubImportResult): void {
    this.result.set(r);
    this.review.set(null);
    this.dbNote.set(null);
    this.portionNote.set(null);
    if (r.added) this.pgn.set('');
    // Fertig importiert: der Entwurf (samt Rohtext mit den Namen) geht.
    const d = this.draft();
    this.resetDraft();
    if (d) {
      void this.client.deleteDraft(d.ref).catch(() => undefined).then(() => this.loadDrafts());
      if (this.share) rememberDraftKey(this.share, d.ref, false);
    }
  }

  /** Woher der Text kam (Datei, Studie, RookHub) gilt nur, solange er unverändert ist — wer danach tippt, hat eingefügt. */
  private loaded(text: string, source: string, label: string | null): void {
    this.loadedText = text;
    this.source = source;
    this.label = label;
  }
  private loadedText: string | null = null;
  private pgnSource(pgn: string): string { return pgn === this.loadedText ? this.source : 'text'; }
  private pgnLabel(pgn: string): string | null { return pgn === this.loadedText ? this.label : null; }

  private async loadStatus(): Promise<void> {
    try {
      this.status.set(await this.client.scoresheetStatus());
      this.statusError.set(null);
    } catch (err) {
      // Nach einem gelungenen Upload bleibt der bisherige Stand stehen — ein Fehler hier ist KEIN gescheiterter Upload.
      this.statusError.set(loadErrorText(err));
    }
  }

  retryStatus(): void {
    this.statusError.set(null);
    void this.loadStatus();
  }

  private async loadScans(): Promise<void> {
    if (this.kind() === 'formular' && !this.status()) await this.loadStatus();
    try {
      const keys = this.share ? anonKeys(this.share) : [];
      const list = await this.client.scans(keys);
      if (this.destroyed) return;
      // Ohne Konto: Schlüssel übernommener/verworfener Einlesungen vergessen.
      if (this.share) for (const k of keys) if (!list.some(s => s.ref === k)) rememberAnonKey(this.share, k, false);
      this.scans.set(list);
      if (this.isManager) {
        try { this.openScans.set(await this.clubApi.openScans()); } catch { /* Beiwerk */ }
        if (this.destroyed) return;
      }
      const open = list.some(s => this.isOpen(s.scan)) || this.othersOpen().some(o => this.isOpen(o.scan));
      this.ticker.run(open);
      if (open) this.schedulePoll();
    } catch {
      // Ein Abruf, der nicht durchkommt (Funkloch am Handy), darf das Nachfragen nicht beenden — sonst bleibt „wird gelesen"
      // stehen, obwohl das Formular längst fertig ist (gemeldet 2026-09-28: Uhr lief, Partie seit Minuten gelesen).
      if (this.scans().some(s => this.isOpen(s.scan))) this.schedulePoll();
    }
  }

  /** Zurück auf der Seite (Handy: App gewechselt, Bildschirm aus): gleich nachsehen statt auf den nächsten Takt zu warten. */
  @HostListener('document:visibilitychange')
  onVisible(): void {
    if (document.visibilityState !== 'visible' || !this.pollTimer) return;
    clearTimeout(this.pollTimer);
    this.pollTimer = null;
    void this.loadScans();
  }

  private schedulePoll(): void {
    if (this.pollTimer || this.destroyed) return;
    this.pollTimer = setTimeout(() => { this.pollTimer = null; void this.loadScans(); }, POLL_MS);
  }

  pickPhoto(ev: Event): void {
    this.photo.set((ev.target as HTMLInputElement).files?.[0] ?? null);
  }

  async upload(): Promise<void> {
    const file = this.photo();
    if (!file) return;
    this.uploading.set(true);
    this.scanError.set(null);
    try {
      const sc = await this.client.upload(file, this.language(), this.side());
      if (this.share) rememberAnonKey(this.share, sc.ref);
      this.scans.set([sc, ...this.scans().filter(s => s.ref !== sc.ref)]);
      this.photo.set(null);
      // UX-037: das Feld zeigte sonst weiter den Dateinamen, während „Formular einlesen" gesperrt blieb.
      const input = this.photoInput()?.nativeElement;
      if (input) input.value = '';
      await this.loadStatus();
      this.schedulePoll();
    } catch (err) {
      this.scanError.set(uploadErrorText(err));
    } finally {
      this.uploading.set(false);
    }
  }

  async discard(sc: ScanRef): Promise<void> {
    try {
      await this.client.discard(sc.ref);
      if (this.share) rememberAnonKey(this.share, sc.ref, false);
      this.scans.set(this.scans().filter(s => s.ref !== sc.ref));
    } catch {
      this.scanError.set('Verwerfen hat nicht geklappt.');
    }
  }
}
