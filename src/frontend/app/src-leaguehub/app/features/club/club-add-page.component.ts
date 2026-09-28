import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { ClubImportResult, ScoresheetScan, ScoresheetStatus } from '../../core/club.models';
import { importSummary, reasonText, scanAvailability, scanStateText, uploadErrorText } from '../../core/club-format';

const POLL_MS = 3000;
type Kind = 'pgn' | 'formular';

/**
 * Partien hinzufügen (`/verein/neu`): viele auf einmal als PGN — oder EIN Partieformular fotografieren, von RookHubs
 * Leser einlesen lassen und danach auf `/verein/formular/:id` prüfen. Beides mit „Meinen Namen durch Schwaz ersetzen",
 * standardmäßig an (Wunsch des Nutzers). Nur für die Vereinsgruppe (`league.contribute`).
 */
@Component({
  selector: 'lh-club-add-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    @if (!allowed) {
      <section class="gate">
        <h2>Nicht freigeschaltet</h2>
        <p>Angemeldet als {{ username }}. Partien hinzufügen dürfen Admins und die Vereinsgruppe von SK Schwaz.</p>
      </section>
    } @else {
      <section class="club-intro">
        <h2>Partien hinzufügen</h2>
        <p class="muted">Angenommen wird jede Partie, in der mindestens ein Ligaspieler sitzt — geprüft an den Meldelisten
          aller Saisonen. Vom Datum bleibt nur das Jahr.</p>
      </section>

      <div class="seg club-kind" role="tablist" aria-label="Art">
        <button type="button" role="tab" [attr.aria-selected]="kind() === 'pgn'" [attr.aria-pressed]="kind() === 'pgn'"
                (click)="setKind('pgn')">PGN-Datei</button>
        <button type="button" role="tab" [attr.aria-selected]="kind() === 'formular'" [attr.aria-pressed]="kind() === 'formular'"
                (click)="setKind('formular')">Partieformular</button>
      </div>


      @if (kind() === 'pgn') {
        <section class="panel">
          <label class="anon-toggle">
            <input type="checkbox" [checked]="anonymize()" (change)="anonymize.set($any($event.target).checked)" />
            <span><b>Meinen Namen durch „Schwaz" ersetzen</b>
              <span class="muted">Dann wird weder gespeichert, wer hinter „Schwaz" steht, noch wer hochgeladen hat — so kann niemand
                gezielt gegen dich vorbereiten. Deine Seite wird über dein Profil gefunden (Nachname oder FIDE-ID).</span></span>
          </label>
          <label class="field">PGN-Datei
            <input type="file" accept=".pgn,application/x-chess-pgn,text/plain" (change)="pickFile($event)" />
          </label>
          <label class="field">… oder hier einfügen
            <textarea rows="6" spellcheck="false" placeholder="[Event &quot;…&quot;]&#10;1. e4 c5 2. Nf3 …" [value]="pgn()"
                      (input)="pgn.set($any($event.target).value)"></textarea>
          </label>
          <div class="actions">
            <button type="button" class="btn-pri" [disabled]="importing() || !pgn().trim()" (click)="importPgn()">
              {{ importing() ? 'Lade hoch …' : 'Partien hochladen' }}</button>
            <span class="update-msg" [class.err]="!!importError()" role="status">{{ importError() ?? '' }}</span>
          </div>
          @if (result(); as r) {
            <p class="result" role="status"><b>{{ summary(r) }}</b>
              @if (r.added) { <a routerLink="/verein">Zu den Vereinspartien</a> }</p>
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
              selbst, bevor etwas gespeichert wird.</p>
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
                    <option value="auto" [selected]="side() === 'auto'">automatisch (Name im Profil)</option>
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
              @for (sc of scans(); track sc.id) {
                <li>
                  <span>{{ sc.white || '?' }} – {{ sc.black || '?' }}</span>
                  <span class="muted">{{ stateText(sc) }}</span>
                  @if (sc.status === 'done') { <a class="btn-sec" [routerLink]="['/verein/formular', sc.id]">Prüfen und übernehmen</a> }
                  @if (sc.status === 'failed') { <button type="button" class="btn-link" (click)="discard(sc)">Verwerfen</button> }
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
  private readonly api = inject(ClubApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly allowed = this.auth.has('league.contribute');
  readonly username = this.auth.currentUser?.username ?? '';

  readonly kind = signal<Kind>('pgn');
  readonly anonymize = signal(true);
  readonly pgn = signal('');
  readonly importing = signal(false);
  readonly importError = signal<string | null>(null);
  readonly result = signal<ClubImportResult | null>(null);

  readonly status = signal<ScoresheetStatus | null>(null);
  readonly availability = computed(() => { const s = this.status(); return s ? scanAvailability(s) : null; });
  readonly language = signal('auto');
  readonly side = signal<'auto' | 'white' | 'black'>('auto');
  readonly photo = signal<File | null>(null);
  readonly uploading = signal(false);
  readonly scanError = signal<string | null>(null);
  readonly scans = signal<ScoresheetScan[]>([]);

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

  async pickFile(ev: Event): Promise<void> {
    const f = (ev.target as HTMLInputElement).files?.[0];
    if (!f) return;
    this.pgn.set(await f.text());
    this.result.set(null);
  }

  async importPgn(): Promise<void> {
    this.importing.set(true);
    this.importError.set(null);
    this.result.set(null);
    try {
      const r = await this.api.importPgn(this.pgn(), this.anonymize());
      this.result.set(r);
      if (r.added) this.pgn.set('');
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.importError.set(e?.error?.reason ? reasonText(e.error.reason)
        : e?.status === 403 ? 'Dafür fehlt dir die Berechtigung (Vereinsmitglieder).' : 'Hochladen hat nicht geklappt.');
    } finally {
      this.importing.set(false);
    }
  }

  private async loadStatus(): Promise<void> {
    try { this.status.set(await this.api.scoresheetStatus()); }
    catch (err) { this.scanError.set(uploadErrorText(err)); }
  }

  private async loadScans(): Promise<void> {
    if (this.kind() === 'formular' && !this.status()) await this.loadStatus();
    try {
      const list = await this.api.scans();
      if (this.destroyed) return;
      this.scans.set(list);
      if (list.some(s => s.status === 'pending' || s.status === 'running')) this.schedulePoll();
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
      const scan = await this.api.upload(file, this.language(), this.side());
      this.scans.set([scan, ...this.scans().filter(s => s.id !== scan.id)]);
      this.photo.set(null);
      await this.loadStatus();
      this.schedulePoll();
    } catch (err) {
      this.scanError.set(uploadErrorText(err));
    } finally {
      this.uploading.set(false);
    }
  }

  async discard(sc: ScoresheetScan): Promise<void> {
    try {
      await this.api.discard(sc.id);
      this.scans.set(this.scans().filter(s => s.id !== sc.id));
    } catch {
      this.scanError.set('Verwerfen hat nicht geklappt.');
    }
  }
}
