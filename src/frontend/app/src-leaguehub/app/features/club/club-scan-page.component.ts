import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnDestroy, OnInit, inject, signal, effect, untracked, viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '@rh/core/auth.service';
import { scrollIntoContainer } from '@rh/shared/pgn-viewer/move-list.component';
import { GameEvals } from '@rh/features/games/game-review.util';
import { SheetEditSession } from '@rh/features/games/sheet-edit-session';
import { SheetReadingStatusComponent } from '@rh/features/games/sheet-workbench/sheet-reading-status.component';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubGameDetail, ClubSheetState, LeagueScanState } from '../../core/club.models';
import { alignSheetPlies, pliesOfPgn, toServer } from '@rh/features/games/game-edit.util';
import { ClubContextService } from '../../core/club-context.service';
import { SheetPgnInput, isTransientError, loadErrorText, reasonText } from '../../core/club-format';
import { rookHubUrlForLeagueHub } from '@rh/core/partner-site';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { rememberAnonKey } from './club-anon-keys';
import { ScanHeaderForm } from './scan-header-form';
import { ClubSideFormComponent } from './club-side-form.component';
import { ScanExport } from './scan-export';
import { ScanMerge } from './scan-merge';
import { ScanExportActionsComponent } from './scan-export-actions.component';
import { ScanPhotos } from '@rh/features/games/sheet-workbench/scan-photos';
import { SheetWorkbenchComponent } from '@rh/features/games/sheet-workbench/sheet-workbench.component';
import { AccessGateComponent } from '../../shared/access-gate.component';

const POLL_MS = 3000;
/** So viele Abrufe hintereinander dürfen still scheitern (Neustart, Funkloch), bevor die Seite es sagt (UX-034). */
const SILENT_FAILURES = 3;

/**
 * Ein eingelesenes Partieformular prüfen und in die Vereins-Datenbank übernehmen (`/verein/formular/:id`).
 * Die Korrektur selbst ist DIESELBE wie in RookHub (`SheetEditSession`: Cursor-Brett, Lesarten, Ersetzen/Einfügen/
 * Löschen, nach jeder Änderung wird der Rest aus den Formular-Einträgen neu aufbereitet); dazu die Namen mit
 * Ligaspieler-Prüfung und „durch Schwaz ersetzen" je Seite (Vorgabe: Spieler von Schwaz und die eigene Seite). Angemeldet
 * unter `/verein/formular/:id`, ohne Konto über einen Teilen-Link unter `/s/:token/formular/:key`. Übernehmen oder
 * Verwerfen schließt die Einlesung — das Foto verschwindet.
 */
/** Gemerkte Abwahl von „Automatisch zu meinen Partien hinzufügen" (je Gerät). */
export const AUTO_MINE_KEY = 'lh-auto-my-games';
function readAutoMine(): boolean {
  try { return localStorage.getItem(AUTO_MINE_KEY) !== '0'; } catch { return true; }
}

@Component({
  selector: 'lh-club-scan-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ClubSideFormComponent, ScanExportActionsComponent, AccessGateComponent, SheetWorkbenchComponent, SheetReadingStatusComponent],
  templateUrl: './club-scan-page.component.html',
})
export class ClubScanPageComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject(ElementRef);

  /** Token des Teilen-Links (ohne Anmeldung) — sonst `null`. */
  readonly share = this.route.snapshot.paramMap.get('token');
  private readonly clubApi = inject(ClubApiService);
  private readonly confirm = inject(ConfirmService);
  readonly client: ClubClient = this.clubApi.client(this.share);
  private readonly api = this.client;
  readonly allowed = !!this.share || this.auth.has('league.contribute');
  readonly canRead = this.auth.has('league.view');
  readonly backLink: unknown[] = this.share ? ['/s', this.share, 'hochladen'] : ['/verein/neu'];
  private readonly clubCtx = inject(ClubContextService);
  /** Name anonymisierter Spieler und des Vereins — der angemeldete bzw. der des Teilen-Links (0.698.0). */
  get anon(): string { return this.clubCtx.anonName(); }
  readonly clubName = this.clubCtx.clubName;

  /** Nummer (angemeldet) bzw. geheimer Schlüssel (ohne Konto) der Einlesung. */
  scanRef = '';
  /** Korrektur einer schon übernommenen Vereinspartie (Route `verein/partie/:id/korrigieren`, 0.660.0) — sonst `null`. */
  readonly gameId: number | null = this.route.snapshot.data['game'] ? Number(this.route.snapshot.paramMap.get('id')) : null;
  /** „Weiß – Schwarz (Jahr) · Ergebnis" der Vereinspartie (Korrektur-Modus). */
  readonly gameTitle = signal('');
  /** Analyse und Züge der gespeicherten Vereinspartie (Korrektur-Modus) — daraus zeichnet die Werkbank die Bewertungskurve (0.756.0). */
  readonly savedEvals = signal<GameEvals | null>(null);
  readonly savedSans = signal<string[]>([]);
  readonly state = signal<LeagueScanState | null>(null);
  readonly notFound = signal(false);
  /** Der Abruf scheitert (anderer Code als 404, oder vorübergehend mehrmals hintereinander) — Klartext statt „Lade …". */
  readonly loadError = signal<string | null>(null);
  private failures = 0;
  /** Die Fotos des Formulars (Seiten, weitere Fotos je Seite). */
  readonly ph = new ScanPhotos({
    fetchView: (page, view) => this.gameId != null ? this.api.clubSheetPhoto(this.gameId, page, view) : this.api.photo(this.scanRef, page, view),
    setPageSize: (page, w, h) => this.s.setPageSize(page, w, h),
  });
  /** Zusammenführen mit einer anderen Einlesung derselben Seite (nur angemeldet, beim Prüfen einer Einlesung). */
  readonly merger = new ScanMerge({
    scanRef: () => this.scanRef,
    scans: () => this.api.scans(),
    merge: (into, from) => this.api.merge(into, from),
    confirm: text => firstValueFrom(this.confirm.ask(text)),
    reload: () => this.reload(),
  });
  /** Hinweis zu den orangen Stellen hinter (i) (d2-l-korrigieren-layout). */
  readonly hintOpen = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  /** Wie viele ältere Fassungen derselben Partie das Übernehmen archiviert hat (2026-10-06). */
  readonly replacedOld = signal(0);
  private readonly doneDlg = viewChild<ElementRef<HTMLDialogElement>>('doneDlg');
  private readonly savePanel = viewChild<ElementRef<HTMLElement>>('savePanel');
  /** Wie viele unsichere Stellen es vorher gab — geht die Zahl von >0 auf 0, kommt der Hinweis. */
  private lastUncertain = -1;
  private readonly doneWatch = effect(() => {
    const n = this.s.uncertainLeft();
    if (this.state()?.scan.status !== 'done' || this.s.busy()) return;
    untracked(() => {
      if (this.lastUncertain > 0 && n === 0 && !this.saved()) {
        const d = this.doneDlg()?.nativeElement;
        if (d && !d.open) d.showModal();
      }
      this.lastUncertain = n;
    });
  });
  /** Übernommen — die Seite bleibt stehen, damit PGN und „Meine Partien" noch gehen. */
  readonly saved = signal(false);
  get loggedIn(): boolean { return this.auth.isLoggedIn; }
  /** „Automatisch zu meinen Partien hinzufügen": an, solange auf diesem Gerät nicht abgewählt. */
  readonly autoMine = signal(readAutoMine());
  setAutoMine(on: boolean): void {
    this.autoMine.set(on);
    try { if (on) localStorage.removeItem(AUTO_MINE_KEY); else localStorage.setItem(AUTO_MINE_KEY, '0'); } catch { /* nur Bequemlichkeit */ }
  }
  readonly rookHub = rookHubUrlForLeagueHub();

  /** Namen, Elo, Jahr, Ergebnis, Veranstaltung und Ligapaarung vor dem Übernehmen. */
  readonly f = new ScanHeaderForm({
    api: this.api,
    anon: () => this.anon,
    clubName: () => this.clubName(),
    pairingsEnabled: this.gameId == null,
  });

  readonly s = new SheetEditSession({
    resolve: (prefix, writtenFrom) => this.gameId != null
      ? this.api.clubResolve(this.gameId, prefix, writtenFrom)
      : this.api.resolve(this.scanRef, prefix, writtenFrom),
    moved: () => this.revealCursor(),
    resolveFailed: () => this.saveError.set('Den Rest neu zu lesen hat nicht geklappt — der bisherige Stand bleibt.'),
    bind: o => o.pipe(takeUntilDestroyed(this.destroyRef)),
  });

  /** PGN herunterladen/kopieren und „Zu meinen Partien" — mit den Namen wie im Formular. */
  readonly exporter = new ScanExport({
    input: () => this.pgnInput(),
    addToMyGames: pgn => this.clubApi.addToMyGames(pgn),
  });

  /** Wartendes Nachfragen — `null`, sobald der Takt abgelaufen ist (dann läuft der Abruf schon). */
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  /** Nur der jüngste `load()` zählt: „Neu laden" mitten in einem laufenden Abruf (der retryInterceptor hält einen 502
   * über 3 s offen) startete sonst eine zweite Nachfrage-Kette — doppelte Last, und nach dem Lesen liefen loadSheet,
   * Namen und Foto zweimal (Korrekturen überschrieben). Ein überholter Abruf tut nichts mehr. */
  private loadSeq = 0;
  private destroyed = false;

  ngOnInit(): void {
    if (!this.allowed) return;
    if (this.share) this.clubCtx.useShare(this.share);   // ohne Konto: der Verein des Links (0.698.0)
    this.scanRef = this.route.snapshot.paramMap.get('id') ?? this.route.snapshot.paramMap.get('key') ?? '';
    void (this.gameId != null ? this.loadGame() : this.load());
  }

  /** Zurück auf der Seite (Handy): ein wartendes Nachfragen gleich ausführen. */
  @HostListener('document:visibilitychange')
  onVisible(): void {
    if (document.visibilityState !== 'visible' || !this.pollTimer || this.state()?.scan.status === 'done') return;
    clearTimeout(this.pollTimer);
    this.pollTimer = null;
    void this.load();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.f.dispose();
    this.ph.dispose();
  }

  get canMerge(): boolean { return !this.share && this.loggedIn && this.gameId == null; }

  /** „Neu laden" auf der Fehlerkarte: gleich nachfragen, ein wartendes Nachfragen entfällt, ein laufender Abruf ist überholt. */
  reload(): void {
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.pollTimer = null;
    this.failures = 0;
    this.loadError.set(null);
    void this.load();
  }

  private poll(): void {
    this.pollTimer = setTimeout(() => { this.pollTimer = null; void this.load(); }, POLL_MS);
  }

  private async load(): Promise<void> {
    const my = ++this.loadSeq;
    let st: LeagueScanState;
    try {
      st = await this.api.scan(this.scanRef);
    } catch (err) {
      if (this.destroyed || my !== this.loadSeq) return;
      if (err instanceof HttpErrorResponse && err.status === 404) { this.notFound.set(true); return; }
      // Nur was von selbst vorbeigeht (0/502/503/504), wird im Hintergrund weiter nachgefragt — nach ein paar stillen
      // Fehlversuchen mit Hinweis. Jeder andere Code (500, 403, …) sagt es gleich und wartet auf „Neu laden".
      const transient = isTransientError(err);
      if (!transient || ++this.failures >= SILENT_FAILURES)
        this.loadError.set(loadErrorText(err) + (transient ? ' LeagueHub versucht es im Hintergrund weiter.' : ''));
      if (transient) this.poll();
      return;
    }
    if (this.destroyed || my !== this.loadSeq) return;
    this.failures = 0;
    this.loadError.set(null);
    this.state.set(st);
    this.ph.viewCounts.set(st.viewCounts ?? []);
    const reading = st.scan.status === 'pending' || st.scan.status === 'running';
    if (reading) {
      this.poll();
      return;
    }
    if (st.scan.status !== 'done') return;
    this.s.loadSheet({ plies: st.plies, unresolved: st.unresolved, unresolvedFrom: st.unresolvedFrom, boxes: st.boxes, written: st.written,
      pages: st.pages ?? [] });
    this.f.fill(st);
    await this.ph.load(st.pageCount ?? 1, page => this.api.photo(this.scanRef, page), () => !this.destroyed && my === this.loadSeq);
  }

  /**
   * Korrektur-Modus (0.660.0): die Vereinspartie laden und — solange aufbewahrt — ihr Formular mit Foto und Lesung, wie beim
   * ersten Prüfen. Der gespeicherte Stand je Halbzug gilt nur, wenn er zu den Zügen der Partie passt; sonst die Züge der
   * Partie mit den Formular-Einträgen daneben. Ohne Formular: nur Brett und Zugliste.
   */
  private async loadGame(): Promise<void> {
    const id = this.gameId!;
    let game: ClubGameDetail;
    try {
      game = await this.api.game(id);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 404) this.notFound.set(true);
      else this.loadError.set(loadErrorText(err));
      return;
    }
    if (!game.canDelete) { this.notFound.set(true); return; }
    this.gameTitle.set(`${game.white} – ${game.black}${game.year ? ` (${game.year})` : ''} · ${game.result === '*' ? 'Ergebnis offen' : game.result}`);
    const fromPgn = pliesOfPgn(game.pgn);
    this.savedSans.set(fromPgn.map(p => p.san));
    void this.api.gameEvals(id).then(e => this.savedEvals.set(e), () => { /* ohne Analyse keine Kurve */ });
    let sheet: ClubSheetState | null = null;
    try { sheet = await this.api.clubSheet(id); } catch { /* nichts aufbewahrt — Korrektur am Brett */ }
    const matches = !!sheet && sheet.plies.length === fromPgn.length && sheet.plies.every((p, i) => p.san === fromPgn[i].san);
    if (sheet && matches) {
      this.s.loadSheet(sheet);
    } else {
      this.s.loadSheet({ plies: sheet ? alignSheetPlies(sheet.plies, toServer(fromPgn)) : toServer(fromPgn), boxes: sheet?.boxes ?? [],
        written: sheet?.written ?? [], pages: sheet?.pages ?? [] });
    }
    this.state.set({ scan: { id, status: 'done' } } as unknown as LeagueScanState);
    if (sheet) this.ph.viewCounts.set(sheet.viewCounts ?? []);   // das zweite Exemplar derselben Seite (0.756.0)
    if (sheet) await this.ph.load(sheet.pageCount ?? 1, page => this.api.clubSheetPhoto(id, page), () => !this.destroyed);
  }

  /** Den Zug in der Zugliste sichtbar machen — NUR in deren eigenem Rollbereich. `scrollIntoView` rollte auch die Seite,
   * und am Handy verschwand damit nach jedem „Stimmt so" der Prüfteil oben (Ausschnitt, Lesarten, Knopf). */
  private revealCursor(): void {
    setTimeout(() => {
      const host = this.host.nativeElement as HTMLElement;
      const el = host.querySelector<HTMLElement>('.moves .ply.cursor');
      if (el) scrollIntoContainer(el);
      const mark = host.querySelector<HTMLElement>('.photo-mark');       // vergrößertes Foto: die Zeile ins Bild
      if (mark) scrollIntoContainer(mark);
    });
  }

  async save(): Promise<void> {
    const legal = this.s.plies().filter(p => !p.illegal);
    if (this.s.illegalCount() > 0
      && !(await firstValueFrom(this.confirm.ask(`${this.s.illegalCount()} Züge am Ende sind nicht legal und fallen weg. Trotzdem übernehmen?`)))) return;
    this.saving.set(true);
    this.saveError.set(null);
    if (this.gameId != null) {
      try {
        await this.api.correctMoves(this.gameId, legal.map(p => p.san), toServer(legal));
        this.saved.set(true);
      } catch (err) {
        const e = err instanceof HttpErrorResponse ? err : null;
        this.saveError.set(e?.error?.reason ? reasonText(e.error.reason) : 'Speichern hat nicht geklappt.');
      } finally {
        this.saving.set(false);
      }
      return;
    }
    try {
      const added = await this.api.addGame({
        moves: legal.map(p => p.san),
        white: this.f.name('white')().trim() || null,
        black: this.f.name('black')().trim() || null,
        whiteFide: this.f.fide('white')(),
        blackFide: this.f.fide('black')(),
        whiteElo: this.f.elo('white')(),
        blackElo: this.f.elo('black')(),
        whiteReplace: this.f.replace('white')(),
        blackReplace: this.f.replace('black')(),
        result: this.f.result(),
        event: this.f.event().trim() || null,
        year: this.f.year(),
        scanId: null,
        // Vorschläge geholt: die Wahl gilt (0 = keine); sonst entscheidet der Server über den eindeutigen Treffer.
        leagueGameId: this.f.pairingsLoaded ? this.f.pairingId() ?? 0 : null,
        date: this.f.sheetDate,
        // Der Stand je Zug (Zuordnung zum Formular, Lesarten) geht ins Archiv — „Korrigieren" zeigt ihn wie beim ersten Mal.
        plies: toServer(legal),
      }, this.scanRef);
      if (this.share) rememberAnonKey(this.share, this.scanRef, false);
      this.replacedOld.set(added?.replaced ?? 0);
      this.saved.set(true);
      if (this.loggedIn && this.autoMine() && !this.exporter.myGameId()) void this.exporter.addToMyGames();
    } catch (err) {
      const e = err instanceof HttpErrorResponse ? err : null;
      this.saveError.set(e?.error?.reason === 'illegal' ? `Ein Zug ist nicht legal: ${e.error.message}`
        : e?.error?.reason ? reasonText(e.error.reason) : 'Übernehmen hat nicht geklappt.');
    } finally {
      this.saving.set(false);
    }
  }

  closeDone(): void {
    this.doneDlg()?.nativeElement.close();
  }

  toSave(): void {
    this.savePanel()?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  /** Die geprüfte Partie: legale Züge, Namen wie im Formular. */
  private pgnInput(): SheetPgnInput {
    return {
      moves: this.s.plies().filter(p => !p.illegal).map(p => p.san),
      white: this.f.name('white')().trim() || null, black: this.f.name('black')().trim() || null,
      result: this.f.result(), event: this.f.event().trim() || null, year: this.f.year(),
    };
  }

  async discard(): Promise<void> {
    if (!(await firstValueFrom(this.confirm.ask('Formular verwerfen? Foto und Lesung werden gelöscht.')))) return;
    try {
      await this.api.discard(this.scanRef);
      if (this.share) rememberAnonKey(this.share, this.scanRef, false);
      void this.router.navigate(this.backLink, { queryParams: { art: 'formular' } });
    } catch {
      this.saveError.set('Verwerfen hat nicht geklappt.');
    }
  }
}
