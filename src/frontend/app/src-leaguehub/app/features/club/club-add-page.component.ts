import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { localStore, readJson, writeJson } from '@rh/core/local-json-store';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubImportResult, ScanRef, ScoresheetStatus } from '../../core/club.models';
import { ANON_NAME, importSummary, reasonText, scanAvailability, scanStateText, uploadErrorText } from '../../core/club-format';
import { ClubImportReviewComponent } from './club-import-review.component';
import { ImportReview } from './import-review';

const POLL_MS = 3000;
/** Ohne Konto merkt sich der Browser die Schlüssel seiner Einlesungen — sonst fände er sie nach dem Neuladen nicht. */
const ANON_KEYS = 'lh-anon-scans';
type Kind = 'pgn' | 'formular';

/** Die gemerkten Schlüssel je Teilen-Link. */
export function anonKeys(share: string): string[] {
  return (readJson<Record<string, string[]>>(localStore(), ANON_KEYS) ?? {})[share] ?? [];
}

export function rememberAnonKey(share: string, key: string, keep = true): void {
  const all = readJson<Record<string, string[]>>(localStore(), ANON_KEYS) ?? {};
  const list = (all[share] ?? []).filter(k => k !== key);
  all[share] = keep ? [key, ...list].slice(0, 20) : list;
  writeJson(localStore(), ANON_KEYS, all);
}

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
  imports: [RouterLink, ClubImportReviewComponent],
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nicht freigeschaltet</h2>
        <p>Angemeldet als {{ username }}. Partien hinzufügen dürfen Admins und die Vereinsgruppe von SK Schwaz.</p>
      </section>
    } @else {
      <section class="club-intro">
        @if (share) { <p><a [routerLink]="['/s', share]">← Zur Begegnung</a></p> }
        <h2>Partien hinzufügen</h2>
        <p class="muted">Angenommen wird jede Partie mit einem bekannten Gegner — geprüft an den Meldelisten aller Saisonen,
          sonst am Spielerverzeichnis der Megabase. Vom Datum bleibt nur das Jahr.@if (share) { Ohne Anmeldung — gespeichert wird nicht, wer hochgeladen hat. }</p>
      </section>

      <div class="seg club-kind" role="tablist" aria-label="Art">
        <button type="button" role="tab" [attr.aria-selected]="kind() === 'pgn'" [attr.aria-pressed]="kind() === 'pgn'"
                (click)="setKind('pgn')">PGN-Datei</button>
        <button type="button" role="tab" [attr.aria-selected]="kind() === 'formular'" [attr.aria-pressed]="kind() === 'formular'"
                (click)="setKind('formular')">Partieformular</button>
      </div>

      @if (kind() === 'pgn') {
        <section class="panel">
          @if (review(); as rv) {
            <lh-club-import-review [review]="rv" [client]="client" [pgn]="pgn()" [remembers]="!share" (imported)="done($event)" (cancel)="review.set(null)" />
          } @else {
            <label class="anon-toggle">
              <input type="checkbox" [checked]="replaceClub()" (change)="replaceClub.set($any($event.target).checked)" />
              <span><b>Spieler von Schwaz durch „{{ anon }}“ ersetzen</b>
                <span class="muted">Jeder, der in seiner jüngsten Saison für Schwaz gemeldet ist@if (!share) {, und du selbst}. Dann wird
                  weder gespeichert, wer dahinter steht, noch wer hochgeladen hat — so kann niemand gezielt gegen uns vorbereiten.
                  In der Übersicht lässt sich das je Partie ändern.</span></span>
            </label>
            <label class="field">PGN-Datei
              <input type="file" accept=".pgn,application/x-chess-pgn,text/plain" (change)="pickFile($event)" />
            </label>
            <div class="field">… oder eine öffentliche Lichess-Studie
              <div class="linkrow">
                <input type="url" inputmode="url" placeholder="https://lichess.org/study/…" [value]="studyUrl()"
                       (input)="studyUrl.set($any($event.target).value)" (keydown.enter)="$event.preventDefault(); loadStudy()" />
                <button type="button" class="btn-sec" [disabled]="loadingStudy() || !studyUrl().trim()" (click)="loadStudy()">
                  {{ loadingStudy() ? 'Lade …' : 'Laden' }}</button>
              </div>
            </div>
            <label class="field">… oder hier einfügen
              <textarea rows="6" spellcheck="false" placeholder="[Event &quot;…&quot;]&#10;1. e4 c5 2. Nf3 …" [value]="pgn()"
                        (input)="pgn.set($any($event.target).value)"></textarea>
            </label>
            <div class="actions">
              <button type="button" class="btn-pri" [disabled]="previewing() || !pgn().trim()" (click)="startPreview()">
                {{ previewing() ? 'Lese …' : 'Partien prüfen' }}</button>
              <span class="muted small">Erst kommt eine Übersicht — gespeichert wird erst mit „Importieren“.</span>
              <span class="update-msg" [class.err]="!!importError()" role="status">{{ importError() ?? '' }}</span>
            </div>
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
            <p class="muted">{{ availability()!.text }} Das Foto wird von einem Sprachmodell gelesen; danach prüfst du die Züge
              und Namen selbst, bevor etwas gespeichert wird.</p>
            @if (availability()!.ok) {
              <label class="field">Foto des Formulars
                <input type="file" accept="image/*" capture="environment" (change)="pickPhoto($event)" />
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
                  <span class="muted">{{ stateText(sc.scan) }}</span>
                  @if (sc.scan.status === 'done') { <a class="btn-sec" [routerLink]="scanLink(sc)">Prüfen und übernehmen</a> }
                  @if (sc.scan.status === 'failed') { <button type="button" class="btn-link" (click)="discard(sc)">Verwerfen</button> }
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
  readonly client: ClubClient = inject(ClubApiService).client(this.share);
  readonly allowed = !!this.share || this.auth.has('league.contribute');
  readonly username = this.auth.currentUser?.username ?? '';
  readonly anon = ANON_NAME;

  readonly kind = signal<Kind>('pgn');
  readonly replaceClub = signal(true);
  readonly pgn = signal('');
  readonly previewing = signal(false);
  readonly studyUrl = signal('');
  readonly loadingStudy = signal(false);
  readonly importError = signal<string | null>(null);
  readonly review = signal<ImportReview | null>(null);
  readonly result = signal<ClubImportResult | null>(null);

  readonly status = signal<ScoresheetStatus | null>(null);
  readonly availability = computed(() => { const s = this.status(); return s ? scanAvailability(s) : null; });
  readonly language = signal('auto');
  readonly side = signal<'auto' | 'white' | 'black'>('auto');
  readonly photo = signal<File | null>(null);
  readonly uploading = signal(false);
  readonly scanError = signal<string | null>(null);
  readonly scans = signal<ScanRef[]>([]);

  readonly reason = reasonText;
  readonly summary = importSummary;
  readonly stateText = scanStateText;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      if (this.pollTimer) clearTimeout(this.pollTimer);
    });
  }

  ngOnInit(): void {
    if (!this.allowed) return;
    if (this.route.snapshot.queryParamMap.get('art') === 'formular') this.kind.set('formular');
    void this.loadScans();
  }

  setKind(k: Kind): void {
    this.kind.set(k);
    void this.router.navigate([], { queryParams: { art: k === 'formular' ? 'formular' : null }, replaceUrl: true });
    if (k === 'formular' && !this.status()) void this.loadStatus();
  }

  scanLink(sc: ScanRef): unknown[] {
    return this.share ? ['/s', this.share, 'formular', sc.ref] : ['/verein/formular', sc.ref];
  }

  async pickFile(ev: Event): Promise<void> {
    const f = (ev.target as HTMLInputElement).files?.[0];
    if (!f) return;
    this.pgn.set(await f.text());
    this.result.set(null);
  }

  /** Eine öffentliche Lichess-Studie holen (der Server ruft Lichess) — danach geht es weiter wie mit einer Datei. */
  async loadStudy(): Promise<void> {
    this.loadingStudy.set(true);
    this.importError.set(null);
    try {
      this.pgn.set(await this.client.lichess(this.studyUrl().trim()));
      this.result.set(null);
      await this.startPreview();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.importError.set(e?.error?.reason ? reasonText(e.error.reason) : 'Die Studie ließ sich nicht laden.');
    } finally {
      this.loadingStudy.set(false);
    }
  }

  async startPreview(): Promise<void> {
    this.previewing.set(true);
    this.importError.set(null);
    this.result.set(null);
    try {
      this.review.set(new ImportReview(await this.client.preview(this.pgn()), this.replaceClub()));
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.importError.set(e?.error?.reason ? reasonText(e.error.reason)
        : e?.status === 404 && this.share ? 'Dieser Link ist abgelaufen.'
        : e?.status === 403 ? 'Dafür fehlt dir die Berechtigung (Vereinsmitglieder).' : 'Lesen hat nicht geklappt.');
    } finally {
      this.previewing.set(false);
    }
  }

  done(r: ClubImportResult): void {
    this.result.set(r);
    this.review.set(null);
    if (r.added) this.pgn.set('');
  }

  private async loadStatus(): Promise<void> {
    try { this.status.set(await this.client.scoresheetStatus()); }
    catch (err) { this.scanError.set(uploadErrorText(err)); }
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
      if (list.some(s => s.scan.status === 'pending' || s.scan.status === 'running')) this.schedulePoll();
    } catch { /* die Liste ist Beiwerk */ }
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
