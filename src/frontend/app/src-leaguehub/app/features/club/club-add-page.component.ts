import { FilePickDirective } from '../../shared/file-pick.directive';
import { BatchUploadComponent } from '../../shared/batch-upload.component';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnInit, computed, effect, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { anonKeys, rememberAnonKey } from './club-anon-keys';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubImportResult, OpenScan, ScanRef, ScoresheetStatus } from '../../core/club.models';
import { importSummary, loadErrorText, reasonText, scanAvailability, scanStateText, shortDateTime, uploadErrorText, waitClock } from '../../core/club-format';
import { SecondsTicker, readingSeconds } from '@rh/features/games/scoresheet-timing';
import { ClubContextService } from '../../core/club-context.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { ClubImportReviewComponent } from './club-import-review.component';
import { ClubDraftStore } from './club-draft-store';
import { PgnSourcePanelComponent } from './pgn-source-panel.component';
import { AccessGateComponent } from '../../shared/access-gate.component';

const POLL_MS = 3000;
type Kind = 'pgn' | 'formular';

/**
 * Partien hinzufügen — angemeldet (`/verein/neu`, Vereinsgruppe) ODER ohne Konto über einen Teilen-Link
 * (`/s/:token/hochladen`). Viele auf einmal als PGN (erst die Übersicht „wer gegen wen", Spieler korrigieren, dann
 * importieren) — oder EIN Partieformular fotografieren, lesen lassen und auf der Korrekturseite prüfen.
 * Spieler des Vereins werden standardmäßig durch seinen Namen ersetzt (Wunsch des Nutzers; „Schwaz", „Weilheim" … — 0.698.0).
 */
@Component({
  selector: 'lh-club-add-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FilePickDirective, RouterLink, ClubImportReviewComponent, AccessGateComponent, BatchUploadComponent, PgnSourcePanelComponent],
  templateUrl: './club-add-page.component.html',
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
  private readonly clubCtx = inject(ClubContextService);
  /** Name anonymisierter Spieler und des Vereins — der angemeldete bzw. der des Teilen-Links (0.698.0). */
  get anon(): string { return this.clubCtx.anonName(); }
  readonly clubName = this.clubCtx.clubName;

  readonly kind = signal<Kind>('pgn');
  /** Erklärtexte hinter (i) (d2-l-hinzufuegen-anon-text): Einleitung und Schalter „ersetzen". */
  readonly introInfo = signal(false);
  readonly anonInfo = signal(false);

  readonly status = signal<ScoresheetStatus | null>(null);
  /** Der Status-Abruf (geht Einlesen gerade?) kam nicht — eigener Text, kein Upload-Fehler. */
  readonly statusError = signal<string | null>(null);
  readonly availability = computed(() => { const s = this.status(); return s ? scanAvailability(s) : null; });
  readonly language = signal('auto');
  readonly side = signal<'auto' | 'white' | 'black'>('auto');
  readonly photo = signal<File | null>(null);
  /** Weitere Blätter desselben Formulars (Seite 2 und 3), `null` = Feld da, noch kein Foto gewählt. */
  readonly extraPhotos = signal<(File | null)[]>([]);
  readonly maxPages = 3;
  readonly maxPhotosPerPage = 4;
  readonly multiNote = signal<string | null>(null);
  /** Je weiterem Foto: gehört es zur Seite davor (dieselbe Seite noch einmal fotografiert, 0.736.0)? */
  readonly extraSame = signal<boolean[]>([]);
  /** Seite je Foto (das erste ist Seite 1) — geht als `layout` mit. */
  readonly layout = computed(() => {
    const pages = [1];
    for (const same of this.extraSame()) pages.push(same ? pages.at(-1)! : pages.at(-1)! + 1);
    return pages;
  });
  readonly photosOfLastPage = computed(() => { const l = this.layout(); return l.filter(p => p === l.at(-1)).length; });
  readonly extraLabels = computed(() => {
    const l = this.layout();
    return l.slice(1).map((page, i) => {
      const nth = l.slice(0, i + 2).filter(p => p === page).length;
      return nth > 1 ? `Seite ${page}, Foto ${nth}` : `Seite ${page}`;
    });
  });
  private readonly photoInput = viewChild<ElementRef<HTMLInputElement>>('photoInput');
  readonly uploading = signal(false);
  readonly scanError = signal<string | null>(null);
  readonly scans = signal<ScanRef[]>([]);

  readonly reason = reasonText;
  summary(r: ClubImportResult): string { return importSummary(r, this.anon); }
  readonly stateText = scanStateText;
  readonly when = shortDateTime;
  /** Verwalter sehen zusätzlich die offenen Formulare ALLER (Wunsch 2026-09-28: „damit die nicht im Limbo sind"). */
  readonly isManager = !this.share && this.auth.has('league.manage');
  /** Die PGN-Liste samt Online-Entwurf (Text, Übersicht, Pakete, offene Listen) — lebt so lange wie die Seite. */
  readonly store = new ClubDraftStore({
    client: this.client,
    share: this.share,
    isManager: this.isManager,
    allDrafts: () => this.clubApi.allDrafts(),
    savedGame: id => this.clubApi.savedGame(id),
    confirm: text => firstValueFrom(this.confirm.ask(text)),
  });
  readonly openScans = signal<OpenScan[]>([]);
  readonly othersOpen = computed(() => this.openScans().filter(o => !o.mine));
  /** Mitlaufende Uhr, solange ein Formular gelesen wird (Wunsch 2026-09-28: „ein Timer, der raufzählt"). */
  private readonly ticker = new SecondsTicker();
  readonly isOpen = (s: { status: string }) => s.status === 'pending' || s.status === 'running';
  clockOf(s: { createdAt: string }): string {
    return waitClock(readingSeconds(s.createdAt, this.ticker.now()));   // a1-l-formular-wartezeit
  }
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private destroyed = false;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      if (this.pollTimer) clearTimeout(this.pollTimer);
      this.store.dispose();
      this.ticker.stop();
    });
    // Jede Korrektur in der Übersicht landet (gedrosselt) im Entwurf — auch die, die dann niemand mehr importiert.
    effect(() => {
      const rv = this.store.review();
      if (!rv || !this.store.draft()) return;
      rv.games();
      this.store.scheduleSave();
    });
  }

  ngOnInit(): void {
    if (!this.allowed) return;
    if (this.share) this.clubCtx.useShare(this.share);   // ohne Konto: der Verein des Links (0.698.0)
    if (this.route.snapshot.queryParamMap.get('art') === 'formular') this.kind.set('formular');
    const game = Number(this.route.snapshot.queryParamMap.get('partie'));
    if (!this.share && Number.isInteger(game) && game > 0) void this.loadSavedGame(game);
    void this.loadScans();
    void this.store.loadDrafts();
  }

  /** Eine Partie aus RookHub (⋮ → „In die Vereins-Datenbank", Wunsch 2026-09-28) — gleich in die Übersicht, wie ein Upload. */
  async loadSavedGame(id: number): Promise<void> {
    this.kind.set('pgn');
    await this.store.loadSavedGame(id);
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

  /** Mehrere Fotos auf einmal (0.738.0): alle zeigen dieselbe Seite — ohne Hauptfoto wird das erste zum Hauptfoto, die
   *  übrigen sind weitere Fotos der letzten Seite. Was über {@link maxPhotosPerPage} geht, bleibt weg (mit Hinweis). */
  pickMany(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;
    // offene leere Zeilen weg, sonst stünden die neuen Fotos hinter einer Lücke
    const keep = this.extraPhotos().map((f, i) => ({ f, same: this.extraSame()[i] })).filter(x => x.f != null);
    this.extraPhotos.set(keep.map(x => x.f));
    this.extraSame.set(keep.map(x => x.same));
    if (!this.photo()) this.photo.set(files.shift()!);
    let skipped = 0;
    for (const f of files) {
      if (this.photosOfLastPage() >= this.maxPhotosPerPage) { skipped++; continue; }
      this.extraPhotos.set([...this.extraPhotos(), f]);
      this.extraSame.set([...this.extraSame(), true]);
    }
    this.multiNote.set(skipped ? `${skipped} ${skipped === 1 ? 'Foto' : 'Fotos'} nicht übernommen — höchstens ${this.maxPhotosPerPage} je Seite.` : null);
  }

  /** `same`: ein weiteres Foto der Seite davor statt einer neuen Seite (0.736.0). */
  addExtra(same = false): void {
    if (same ? this.photosOfLastPage() >= this.maxPhotosPerPage : (this.layout().at(-1) ?? 1) >= this.maxPages) return;
    this.extraPhotos.set([...this.extraPhotos(), null]);
    this.extraSame.set([...this.extraSame(), same]);
  }

  pickExtra(i: number, ev: Event): void {
    const list = [...this.extraPhotos()];
    list[i] = (ev.target as HTMLInputElement).files?.[0] ?? null;
    this.extraPhotos.set(list);
  }

  removeExtra(i: number): void {
    this.extraPhotos.set(this.extraPhotos().filter((_, j) => j !== i));
    this.extraSame.set(this.extraSame().filter((_, j) => j !== i));
  }

  async upload(): Promise<void> {
    const file = this.photo();
    if (!file) return;
    const picked = this.extraPhotos().map((f, i) => ({ f, page: this.layout()[i + 1] })).filter(x => x.f != null);
    const files = [file, ...picked.map(x => x.f!)];
    const layout = [1, ...picked.map(x => x.page)];
    this.uploading.set(true);
    this.scanError.set(null);
    try {
      const sc = await this.client.upload(files, this.language(), this.side(), layout);
      if (this.share) rememberAnonKey(this.share, sc.ref);
      this.scans.set([sc, ...this.scans().filter(s => s.ref !== sc.ref)]);
      this.photo.set(null);
      this.extraPhotos.set([]);
      this.extraSame.set([]);
      this.multiNote.set(null);
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
    // Ein gelesenes Formular kostet Arbeit — erst nachfragen; ein gescheitertes geht ohne Frage
    if (sc.scan.status === 'done'
      && !(await firstValueFrom(this.confirm.ask(`Formular ${sc.scan.white || '?'} – ${sc.scan.black || '?'} verwerfen? Foto und Lesung werden gelöscht.`)))) return;
    try {
      await this.client.discard(sc.ref);
      if (this.share) rememberAnonKey(this.share, sc.ref, false);
      this.scans.set(this.scans().filter(s => s.ref !== sc.ref));
    } catch {
      this.scanError.set('Verwerfen hat nicht geklappt.');
    }
  }
}
