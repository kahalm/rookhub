import { SnackbarService } from '@rh/core/snackbar.service';
import { ChangeDetectionStrategy, Component, DestroyRef, HostListener, Injector, OnInit, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService, ClubClient } from '../../core/club-api.service';
import { ClubImportResult } from '../../core/club.models';
import { importSummary, reasonText, shortDateTime } from '../../core/club-format';
import { ClubContextService } from '../../core/club-context.service';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';
import { firstValueFrom } from 'rxjs';
import { ClubImportReviewComponent } from './club-import-review.component';
import { ClubDraftStore } from './club-draft-store';
import { PgnSourcePanelComponent } from './pgn-source-panel.component';
import { ScoresheetUploadStore } from './scoresheet-upload-store';
import { ClubScoresheetPanelComponent } from './club-scoresheet-panel.component';
import { AccessGateComponent } from '../../shared/access-gate.component';
import { HandoffService } from '@rh/core/handoff.service';

type Kind = 'pgn' | 'formular' | 'brett';

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
  imports: [RouterLink, ClubImportReviewComponent, AccessGateComponent, PgnSourcePanelComponent, ClubScoresheetPanelComponent],
  templateUrl: './club-add-page.component.html',
})
export class ClubAddPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly handoff = inject(HandoffService);

  /** Token des Teilen-Links (Weg ohne Anmeldung) — sonst `null`. */
  readonly share = this.route.snapshot.paramMap.get('token');
  private readonly clubApi = inject(ClubApiService);
  private readonly confirm = inject(ConfirmService);
  private readonly injector = inject(Injector);
  readonly client: ClubClient = this.clubApi.client(this.share);
  readonly allowed = !!this.share || this.auth.has('league.contribute');
  /** Ohne Beitragsrecht, aber mit Leserecht: die Sperrkarte nennt das fehlende Recht und führt zu den Vereinspartien. */
  readonly canRead = this.auth.has('league.view');
  private readonly clubCtx = inject(ClubContextService);
  /** Name anonymisierter Spieler und des Vereins — der angemeldete bzw. der des Teilen-Links (0.698.0). */
  get anon(): string { return this.clubCtx.anonName(); }
  readonly clubName = this.clubCtx.clubName;

  readonly kind = signal<Kind>('pgn');
  /** „Am Analysebrett eingeben" (Wunsch 2026-11-09): nur mit Konto — das Brett liegt auf RookHub, und ein Teilen-Link
   *  trägt dorthin kein Recht auf die Vereinsdatenbank — und nur, wenn es ein RookHub zu diesem LeagueHub gibt. */
  readonly boardAvailable = !this.share && !!this.handoff.rookHubUrl;
  /** Erklärtexte hinter (i) (d2-l-hinzufuegen-anon-text): Einleitung und Schalter „ersetzen". */
  readonly introInfo = signal(false);
  readonly anonInfo = signal(false);

  readonly reason = reasonText;
  summary(r: ClubImportResult): string { return importSummary(r, this.anon); }
  readonly when = shortDateTime;
  /** Verwalter sehen zusätzlich die offenen Formulare und Listen ALLER (Wunsch 2026-09-28: „damit die nicht im Limbo sind"). */
  readonly isManager = !this.share && this.auth.has('league.manage');
  /** Die PGN-Liste samt Online-Entwurf (Text, Übersicht, Pakete, offene Listen) — lebt so lange wie die Seite. */
  readonly store = new ClubDraftStore({
    client: this.client,
    share: this.share,
    isManager: this.isManager,
    allDrafts: () => this.clubApi.allDrafts(),
    savedGame: id => this.clubApi.savedGame(id),
    confirm: text => firstValueFrom(this.confirm.ask(text)),
    // Erst bei Bedarf holen: die Snackbar hängt an MatSnackBar + Übersetzung (F4-003).
    warn: text => { try { this.injector.get(SnackbarService).warn(text); } catch { /* ohne Snackbar (Specs) still */ } },
  });
  /** Die Partieformulare (Fotos, Hochladen, eigene und fremde offene Formulare) — lebt so lange wie die Seite. */
  readonly sheets = new ScoresheetUploadStore({
    client: this.client,
    share: this.share,
    isManager: this.isManager,
    openScans: () => this.clubApi.openScans(),
    confirm: text => firstValueFrom(this.confirm.ask(text)),
    kind: () => this.kind() === 'formular' ? 'formular' : 'pgn',
  });

  /** Die Kopie in „Meine Partien" (dasselbe Konto) — die Übersicht ruft sie nur für die eigene Liste (`store.ownEntry`). */
  readonly addToMine = (pgn: string) => this.clubApi.addToMyGames(pgn);

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.sheets.dispose();
      this.store.dispose();
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
    const query = this.route.snapshot.queryParamMap;
    const art = query.get('art');
    if (art === 'formular') this.kind.set('formular');
    else if (art === 'brett' && this.boardAvailable) this.kind.set('brett');
    const entwurf = Number(query.get('entwurf'));
    if (!this.share && Number.isInteger(entwurf) && entwurf > 0) { void this.openBoardDraft(entwurf, Number(query.get('club'))); return; }
    const game = Number(query.get('partie'));
    if (!this.share && Number.isInteger(game) && game > 0) void this.loadSavedGame(game);
    void this.sheets.loadScans();
    void this.store.loadDrafts();
  }

  /** Vom Analysebrett übergeben (`?entwurf=<id>&club=<id>`): in den Verein des Entwurfs wechseln (auf dem Brett gewählt, kann
   *  ein anderer sein als der hier gemerkte), dann die Übersicht öffnen wie bei „Weiter" einer offenen Liste. */
  private async openBoardDraft(id: number, club: number): Promise<void> {
    if (Number.isInteger(club) && club > 0) {
      await firstValueFrom(this.clubCtx.ensure());
      if (this.clubCtx.current()?.id !== club && this.clubCtx.clubs().some(c => c.id === club)) {
        this.clubCtx.select(club);
        await firstValueFrom(this.clubCtx.reload());
      }
    }
    this.kind.set('pgn');
    await this.store.openDraft(String(id));
    void this.router.navigate([], { queryParams: { entwurf: null, club: null }, queryParamsHandling: 'merge', replaceUrl: true });
    void this.sheets.loadScans();
    void this.store.loadDrafts();
  }

  /** Zum Analysebrett; die Partie kommt von dort als Entwurf zurück. In der Adresse steht nur der Verein, nie das PGN. */
  openBoard(): void {
    const club = this.clubCtx.club()?.id;
    void this.handoff.jumpToRookHub(`analysis?verein=1${club ? `&club=${club}` : ''}`);
  }

  /** Eine Partie aus RookHub (⋮ → „In die Vereins-Datenbank", Wunsch 2026-09-28) — gleich in die Übersicht, wie ein Upload. */
  async loadSavedGame(id: number): Promise<void> {
    this.kind.set('pgn');
    await this.store.loadSavedGame(id);
  }

  setKind(k: Kind): void {
    this.kind.set(k);
    void this.router.navigate([], { queryParams: { art: k === 'pgn' ? null : k }, replaceUrl: true });
    if (k === 'formular' && !this.sheets.status()) void this.sheets.loadStatus();
  }

  /** Zurück auf der Seite (Handy: App gewechselt, Bildschirm aus): gleich nachsehen statt auf den nächsten Takt zu warten. */
  @HostListener('document:visibilitychange')
  onVisible(): void {
    this.sheets.onVisible();
  }
}
