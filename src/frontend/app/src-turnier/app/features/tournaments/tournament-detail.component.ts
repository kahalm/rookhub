import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleChange, MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SnackbarService } from '@rh/core/snackbar.service';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Sort } from '@angular/material/sort';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { LoadingSpinnerComponent } from '@rh/shared/loading-spinner/loading-spinner.component';
import { NotificationService } from '@rh/core/notification.service';
import { ShareTournamentDialogComponent } from './share-tournament-dialog.component';
import { TeamPlayersDialogComponent } from './team-players-dialog.component';
import { TournamentTablesComponent } from './tournament-tables.component';
import { Tournament, TournamentGroup, TournamentPlayer, TournamentTeam, DisplayPairing, Subscription } from '@rh/core/models';
import { OpenTournamentService } from '../../core/open-tournament.service';
import { PLAYER_COLUMNS, TEAM_COLUMNS, PAIRING_COLUMNS, sortTableData, toDisplayPairings } from './tournament-table.util';
import { TournamentDetailService } from './tournament-detail.service';
import { computeFavoriteNames, filterPlayersByFavorites, filterTeamsByFavorites, filterPairingsByFavorites } from './tournament-favorites.util';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-tournament-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatButtonModule, MatButtonToggleModule, MatIconModule, MatTooltipModule, MatProgressBarModule, MatDialogModule, TranslatePipe, LoadingSpinnerComponent, TournamentTablesComponent],
  templateUrl: './tournament-detail.component.html',
  styleUrls: ['./tournament-detail.component.scss'],
})
export class TournamentDetailComponent implements OnInit, OnDestroy {
  tournament: Tournament | null = null;
  players: TournamentPlayer[] = [];
  teams: TournamentTeam[] = [];
  pairings: DisplayPairing[] = [];
  rounds: number[] = [];
  selectedRound = 1;
  loading = true;
  /**
   * Warum kein Turnier da ist: `notFound` (404 — nie geholt oder entfernt) oder `error` (Server/Netz).
   * Ohne das blieb unter der Navbar eine leere Seite, und nur ein 3-s-Snackbar sagte etwas
   * (Codereview F6-013).
   */
  loadFailure: 'notFound' | 'error' | null = null;
  playersLoading = false;
  teamsLoading = false;
  pairingsLoading = false;

  playerColumns = PLAYER_COLUMNS;
  teamColumns = TEAM_COLUMNS;
  pairingColumns = PAIRING_COLUMNS;
  showFavoritesOnly = false;
  favoriteSnrs: Set<number> = new Set();
  favoriteTeamSnrs: Set<number> = new Set();
  selectedTabIndex = 0;
  hasTeamPairings = false;

  // Cached display data (refreshed via refreshDisplayed* methods)
  displayedPlayers: TournamentPlayer[] = [];
  displayedTeams: TournamentTeam[] = [];
  displayedPairings: DisplayPairing[] = [];
  private _favoriteTeamNames = new Set<string>();
  private _favoriteNames = new Set<string>();

  // Sort states
  playerSort: Sort = { active: '', direction: '' };
  teamSort: Sort = { active: '', direction: '' };
  pairingSort: Sort = { active: '', direction: '' };

  subscription: Subscription | null = null;
  /** Alle Abos des Nutzers — das dieses Turniers wird erst gewaehlt, wenn auch das Turnier da ist (siehe `matchSubscription`). */
  private subscriptions: Subscription[] | null = null;
  toggling = false;
  refreshing = false;
  monitoring = false;
  monitorActiveUntil: Date | null = null;
  monitorToggling = false;
  private pollInterval: ReturnType<typeof setInterval> | null = null;
  private monitorPollInterval: ReturnType<typeof setInterval> | null = null;
  private lastKnownRounds = 0;

  private static readonly TAB_NAMES = ['players', 'teams', 'pairings'];
  private id!: string;

  constructor(private route: ActivatedRoute, private router: Router, private api: TournamentDetailService, private snackbar: SnackbarService, private dialog: MatDialog, private notificationService: NotificationService, private translate: TranslateService) {}

  private readonly opener = inject(OpenTournamentService);
  /** Welche Gruppe gerade geoeffnet wird — sperrt die Leiste, bis die Seite wechselt. */
  readonly opening = this.opener.opening;

  /**
   * Die Gruppen derselben Veranstaltung (chess-results „Turnierauswahl"): an einem Rallye-Tag
   * Gruppe A, B, Maedchen und Schnellschach, bei der Olympiade Open und Women. Unter zwei gibt es
   * nichts umzuschalten.
   */
  get groups(): TournamentGroup[] {
    const groups = this.tournament?.groups ?? [];
    return groups.length > 1 ? groups : [];
  }

  /**
   * In eine andere Gruppe wechseln — auf demselben Reiter. Ist sie noch nicht geholt, holt der
   * Dienst sie (bis zu zwei Minuten, mit Meldung). Die Leiste springt sofort auf die eigene Gruppe
   * zurueck: sie zeigt, wo man IST, und die neue Seite markiert dann die neue.
   */
  switchGroup(change: MatButtonToggleChange): void {
    const target = change.value as string;
    change.source.buttonToggleGroup.value = this.tournament?.chessResultsId;
    if (!target || target === this.tournament?.chessResultsId) return;
    this.opener.open(target, TournamentDetailComponent.TAB_NAMES[this.selectedTabIndex]);
  }

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id')!;
    const tab = this.route.snapshot.queryParams['tab'];
    const tabIndex = TournamentDetailComponent.TAB_NAMES.indexOf(tab);
    if (tabIndex >= 0) this.selectedTabIndex = tabIndex;
    this.loadFavorites();
    this.loadTournament();
    this.loadSubscription();
    this.loadMonitorStatus();
  }

  /** Das Turnier selbst; „Erneut versuchen" auf der Fehlerkarte ruft nur diesen Teil noch einmal. */
  loadTournament(): void {
    this.loading = true;
    this.loadFailure = null;
    this.api.getTournament(this.id).subscribe({
      next: (t) => {
        this.tournament = t;
        this.loading = false;
        this.matchSubscription();
        if (t.totalRounds) {
          this.rounds = Array.from({ length: t.totalRounds }, (_, i) => i + 1);
        }
        this.loadPlayers();
        this.loadTeams();
        if (this.selectedTabIndex === 2) this.loadPairings();
      },
      error: (err: unknown) => {
        this.loading = false;
        this.loadFailure = err instanceof HttpErrorResponse && err.status === 404 ? 'notFound' : 'error';
      }
    });
  }

  ngOnDestroy(): void {
    if (this.pollInterval) clearInterval(this.pollInterval);
    this.stopClubPoll();
    this.stopMonitorPoll();
  }

  loadSubscription(): void {
    this.api.getSubscriptions().subscribe({
      next: (subs) => {
        this.subscriptions = subs;
        this.matchSubscription();
      },
      error: () => this.snackbar.info(this.translate.instant('tournaments.detail.loadSubscriptionFailed'))
    });
  }

  /**
   * Das Abo dieses Turniers. Der Server speichert es unter der chess-results-NUMMER (wie Kalender und
   * Auto-Abo), die Route traegt aber die Crawler-DB-Id — ein Alt-Abo von hier steht noch unter ihr.
   * Beide gelten; die Nummer kennt die Seite erst mit dem Turnier, deshalb aus beiden Antworten.
   */
  private matchSubscription(): void {
    if (!this.subscriptions) return;
    const number = this.tournament?.chessResultsId?.replace(/^tnr/i, '');
    this.subscription = this.subscriptions.find(s =>
      s.crawlerTournamentId === this.id || (!!number && s.crawlerTournamentId === number)) ?? null;
  }

  subscribe(): void {
    this.toggling = true;
    this.api.subscribe(this.id, this.tournament?.name ?? '').subscribe({
      next: (sub) => {
        this.subscription = sub;
        this.subscriptions = [...(this.subscriptions ?? []).filter(s => s.id !== sub.id), sub];
        this.toggling = false;
        this.snackbar.success(this.translate.instant('tournamentDirectory.bookmarked'));
      },
      error: (err) => {
        this.toggling = false;
        this.snackbar.info(err.error?.message || this.translate.instant('tournamentDirectory.bookmarkError'));
      }
    });
  }

  unsubscribe(): void {
    if (!this.subscription) return;
    this.toggling = true;
    const subscriptionId = this.subscription.id;
    this.api.unsubscribe(subscriptionId).subscribe({
      next: () => {
        this.subscription = null;
        this.subscriptions = (this.subscriptions ?? []).filter(s => s.id !== subscriptionId);
        this.toggling = false;
        this.snackbar.success(this.translate.instant('tournamentDirectory.bookmarkRemoved'));
      },
      error: () => {
        this.toggling = false;
        this.snackbar.info(this.translate.instant('tournamentDirectory.bookmarkRemoveError'));
      }
    });
  }

  loadMonitorStatus(): void {
    this.api.getMonitor(this.id).subscribe({
      next: (res) => {
        this.monitoring = res.active;
        this.monitorActiveUntil = res.activeUntil ? new Date(res.activeUntil) : null;
        if (res.active && res.lastKnownRounds) {
          this.lastKnownRounds = res.lastKnownRounds;
          this.startMonitorPoll();
        }
      },
      error: () => {}
    });
  }

  toggleMonitor(): void {
    this.monitorToggling = true;
    if (this.monitoring) {
      this.api.stopMonitor(this.id).subscribe({
        next: () => {
          this.monitoring = false;
          this.monitorActiveUntil = null;
          this.monitorToggling = false;
          this.stopMonitorPoll();
          this.snackbar.success(this.translate.instant('tournaments.monitor.stopped'));
        },
        error: () => {
          this.monitorToggling = false;
          this.snackbar.info(this.translate.instant('tournaments.monitor.stopFailed'));
        }
      });
    } else {
      this.notificationService.requestPermission();
      this.api.startMonitor(this.id).subscribe({
        next: (res) => {
          this.monitoring = true;
          this.monitorActiveUntil = res.activeUntil ? new Date(res.activeUntil) : null;
          this.lastKnownRounds = res.lastKnownRounds || 0;
          this.monitorToggling = false;
          this.startMonitorPoll();
          this.snackbar.success(this.translate.instant('tournaments.monitor.activated'));
        },
        error: () => {
          this.monitorToggling = false;
          this.snackbar.info(this.translate.instant('tournaments.monitor.activateFailed'));
        }
      });
    }
  }

  private startMonitorPoll(): void {
    this.stopMonitorPoll();
    this.monitorPollInterval = setInterval(() => {
      // Stop if monitoring expired
      if (this.monitorActiveUntil && new Date() > this.monitorActiveUntil) {
        this.monitoring = false;
        this.monitorActiveUntil = null;
        this.stopMonitorPoll();
        return;
      }
      this.api.getMonitor(this.id).subscribe({
        next: (res) => {
          if (!res.active) {
            this.monitoring = false;
            this.monitorActiveUntil = null;
            this.stopMonitorPoll();
            return;
          }
          if (res.lastKnownRounds > this.lastKnownRounds) {
            const newRound = res.lastKnownRounds;
            this.lastKnownRounds = newRound;
            // Browser notification
            this.notificationService.notify(this.translate.instant('tournaments.monitor.newRoundTitle'), {
              body: this.translate.instant('tournaments.monitor.newRoundBody', { round: newRound }),
              icon: '/favicon.ico'
            });
            // Snackbar as fallback
            this.snackbar.warn(this.translate.instant('tournaments.monitor.newRoundSnack', { round: newRound }));
            // Reload data
            this.reloadAll();
            if (this.selectedTabIndex === 2) {
              this.selectedRound = newRound;
              this.loadPairings();
            }
          }
        }
      });
    }, 30000);
  }

  private stopMonitorPoll(): void {
    if (this.monitorPollInterval) {
      clearInterval(this.monitorPollInterval);
      this.monitorPollInterval = null;
    }
  }

  refresh(): void {
    if (!this.tournament?.chessResultsId) return;
    this.refreshing = true;
    // Strip tnr prefix if present - crawler adds it automatically
    const crawlId = this.tournament.chessResultsId.replace(/^tnr/i, '');
    this.api.startCrawl(crawlId).subscribe({
      next: (job) => this.pollRefreshJob(job.id),
      error: (err: unknown) => {
        // 409: fuer dieses Turnier laeuft schon ein Auftrag (Runden-Monitor, Nachtabruf, ein
        // anderer Nutzer) — kein Fehlschlag, die frischen Daten kommen ohnehin. Nennt der Crawler
        // die Nummer des laufenden Auftrags, wird er verfolgt wie ein eigener; sonst sagt die
        // Meldung, was los ist (Codereview 2026-09-29, I2-009).
        if (err instanceof HttpErrorResponse && err.status === 409) {
          const jobId = (err.error as { jobId?: unknown } | null)?.jobId;
          if (typeof jobId === 'number') { this.pollRefreshJob(jobId); return; }
          this.refreshing = false;
          this.snackbar.info(this.translate.instant('tournaments.detail.refreshAlreadyRunning'));
          return;
        }
        this.refreshing = false;
        this.snackbar.info(this.translate.instant('tournaments.detail.refreshStartFailed'));
      }
    });
  }

  private pollRefreshJob(jobId: number): void {
    this.pollInterval = setInterval(() => {
      this.api.getCrawlJob(jobId).subscribe({
        next: (job) => {
          if (job.status === 'Completed') {
            if (this.pollInterval) clearInterval(this.pollInterval);
            this.pollInterval = null;
            this.refreshing = false;
            this.snackbar.success(this.translate.instant('tournaments.detail.dataRefreshed'));
            this.reloadAll();
          } else if (job.status === 'Failed') {
            if (this.pollInterval) clearInterval(this.pollInterval);
            this.pollInterval = null;
            this.refreshing = false;
            this.snackbar.info(job.errorMessage || this.translate.instant('tournaments.detail.refreshFailed'));
          }
        },
        error: () => {
          if (this.pollInterval) clearInterval(this.pollInterval);
          this.pollInterval = null;
          this.refreshing = false;
          this.snackbar.info(this.translate.instant('tournaments.list.crawlConnectionLost'));
        }
      });
    }, 2000);
  }

  private reloadAll(): void {
    this.api.getTournament(this.id).subscribe({
      next: (t) => {
        this.tournament = t;
        if (t.totalRounds) {
          this.rounds = Array.from({ length: t.totalRounds }, (_, i) => i + 1);
        }
      },
      error: () => this.snackbar.info(this.translate.instant('tournaments.detail.reloadTournamentFailed'))
    });
    this.loadPlayers();
    this.teams = [];
    this.displayedTeams = [];   // sonst zeigt die Tabelle veraltete Zeilen trotz Count 0
    this.pairings = [];
    // Aktiven Tab sofort neu laden; inaktive Tabs laden via onTabChange (length === 0) neu.
    if (this.selectedTabIndex === 1) this.loadTeams();
    else if (this.selectedTabIndex === 2) this.loadPairings();
  }

  onTabChange(event: { index: number }): void {
    this.selectedTabIndex = event.index;
    const tabName = TournamentDetailComponent.TAB_NAMES[event.index];
    this.router.navigate([], { queryParams: { tab: tabName }, queryParamsHandling: 'merge', replaceUrl: true });
    if (event.index === 1 && this.teams.length === 0) this.loadTeams();
    if (event.index === 2 && this.pairings.length === 0) this.loadPairings();
  }

  loadPlayers(): void {
    this.playersLoading = true;
    this.api.getPlayers(this.id).subscribe({
      next: (p) => { this.players = p; this.playersLoading = false; this.refreshFavoriteHelpers(); this.refreshDisplayedPlayers(); },
      error: () => { this.playersLoading = false; this.snackbar.info(this.translate.instant('tournaments.detail.loadPlayersFailed')); }
    });
  }

  // --- Vereine nachtragen ---

  /** Laeuft die Vereinssuche fuer dieses Turnier? Traegt den Knopf-Zustand. */
  clubsBusy = false;
  private clubPoll: ReturnType<typeof setInterval> | null = null;
  private static readonly ClubPollMs = 5000;

  /**
   * Startet die Suche ueber die chess-results-Spielersuche (ein Abruf je Spieler, im Hintergrund)
   * und fragt nach, bis sie fertig ist — zwischendurch kommen die gefundenen Vereine schon in die
   * Tabelle, weil die Seite die Spieler neu laedt, sobald sich die Zahl der offenen aendert.
   */
  fillClubs(): void {
    if (this.clubsBusy) return;
    this.clubsBusy = true;
    this.api.fillClubs(this.id).subscribe({
      next: (res) => {
        if (!res.queued) { this.clubsBusy = false; this.loadPlayers(); return; }
        this.snackbar.info(this.translate.instant('tournaments.players.clubsStarted', { count: res.queued }));
        this.startClubPoll();
      },
      error: (err) => {
        // 409: fuer dieses Turnier laeuft schon eine Suche — dann eben mitwarten.
        if (err?.status === 409) { this.startClubPoll(); return; }
        this.clubsBusy = false;
        this.snackbar.info(this.translate.instant('tournaments.players.clubsFailed'));
      },
    });
  }

  private startClubPoll(): void {
    this.stopClubPoll();
    let lastPending: number | null = null;
    this.clubPoll = setInterval(() => {
      this.api.getClubStatus(this.id).subscribe({
        next: (status) => {
          if (lastPending !== null && status.pending !== lastPending) this.loadPlayers();
          lastPending = status.pending;
          if (!status.running) {
            this.stopClubPoll();
            this.clubsBusy = false;
            this.loadPlayers();
            this.snackbar.success(this.translate.instant('tournaments.players.clubsDone'));
          }
        },
        error: () => { /* still: der naechste Takt fragt wieder */ },
      });
    }, TournamentDetailComponent.ClubPollMs);
  }

  private stopClubPoll(): void {
    if (this.clubPoll) clearInterval(this.clubPoll);
    this.clubPoll = null;
  }

  loadTeams(): void {
    this.teamsLoading = true;
    this.api.getTeams(this.id).subscribe({
      next: (t) => { this.teams = t; this.teamsLoading = false; this.refreshFavoriteHelpers(); this.refreshDisplayedTeams(); },
      error: () => { this.teamsLoading = false; this.snackbar.info(this.translate.instant('tournaments.detail.loadTeamsFailed')); }
    });
  }

  loadPairings(): void {
    this.pairingsLoading = true;
    this.api.getPairings(this.id, this.selectedRound).subscribe({
      next: (p) => {
        const { pairings, hasTeamPairings } = toDisplayPairings(p);
        this.pairings = pairings;
        this.hasTeamPairings = hasTeamPairings;
        this.pairingsLoading = false;
        this.refreshDisplayedPairings();
      },
      error: () => { this.pairingsLoading = false; this.snackbar.info(this.translate.instant('tournaments.detail.loadPairingsFailed')); }
    });
  }

  onRoundChange(round: number): void {
    this.selectedRound = round;
    this.loadPairings();
  }

  // --- Sorting ---

  // --- Favorites (server-side) ---

  private loadFavorites(): void {
    this.api.getFavorites(this.id).subscribe({
      next: (favs) => {
        this.favoriteSnrs = new Set(favs.filter(f => f.playerSnr).map(f => f.playerSnr!));
        this.favoriteTeamSnrs = new Set(favs.filter(f => f.teamSnr).map(f => f.teamSnr!));
        this.refreshAllDisplayed();
      },
      error: () => {}
    });
    this.api.getFavoriteSettings(this.id).subscribe({
      next: (s) => { this.showFavoritesOnly = s.showFavoritesOnly; this.refreshAllDisplayed(); },
      error: () => {}
    });
  }

  /**
   * Der Filter wirkt sofort und bleibt auch bei einem Speicherfehler an (der Nutzer will die Favoriten
   * JETZT sehen) — aber er erfaehrt, dass die Einstellung beim naechsten Laden zurueckspringt.
   */
  onFavoritesToggle(checked: boolean): void {
    this.showFavoritesOnly = checked;
    this.refreshAllDisplayed();
    this.api.saveFavoriteSettings(this.id, checked).subscribe({
      error: () => this.snackbar.warn(this.translate.instant('tournaments.favorites.filterSaveFailed'))
    });
  }

  get hasFavorites(): boolean {
    return this.favoriteSnrs.size > 0 || this.favoriteTeamSnrs.size > 0;
  }

  get subtitle(): string {
    return [this.tournament?.location, this.tournament?.date].filter(Boolean).join(' | ');
  }

  onPlayerSort(sort: Sort): void {
    this.playerSort = sort;
    this.refreshDisplayedPlayers();
  }

  onTeamSort(sort: Sort): void {
    this.teamSort = sort;
    this.refreshDisplayedTeams();
  }

  onPairingSort(sort: Sort): void {
    this.pairingSort = sort;
    this.refreshDisplayedPairings();
  }

  private refreshFavoriteHelpers(): void {
    const { playerNames, teamNames } = computeFavoriteNames(this.players, this.teams, this.favoriteSnrs, this.favoriteTeamSnrs);
    this._favoriteNames = playerNames;
    this._favoriteTeamNames = teamNames;
  }

  private refreshDisplayedPlayers(): void {
    const data = this.showFavoritesOnly
      ? filterPlayersByFavorites(this.players, this.favoriteSnrs, this._favoriteTeamNames)
      : this.players;
    this.displayedPlayers = sortTableData(data, this.playerSort);
  }

  private refreshDisplayedTeams(): void {
    const data = this.showFavoritesOnly
      ? filterTeamsByFavorites(this.teams, this._favoriteTeamNames)
      : this.teams;
    this.displayedTeams = sortTableData(data, this.teamSort);
  }

  private refreshDisplayedPairings(): void {
    const data = this.showFavoritesOnly
      ? filterPairingsByFavorites(this.pairings, this.hasTeamPairings, this._favoriteNames, this._favoriteTeamNames)
      : this.pairings;
    this.displayedPairings = sortTableData(data, this.pairingSort);
  }

  private refreshAllDisplayed(): void {
    this.refreshFavoriteHelpers();
    this.refreshDisplayedPlayers();
    this.refreshDisplayedTeams();
    this.refreshDisplayedPairings();
  }

  isFavorite(player: TournamentPlayer): boolean {
    return this.favoriteSnrs.has(player.snr);
  }

  toggleFavorite(player: TournamentPlayer): void {
    const add = !this.favoriteSnrs.has(player.snr);
    this.favoriteSnrs = withSnr(this.favoriteSnrs, player.snr, add);
    this.refreshAllDisplayed();
    this.saveFavorite(
      add ? this.api.addPlayerFavorite(this.id, player.snr) : this.api.removePlayerFavorite(this.id, player.snr),
      add, player.name,
      () => { this.favoriteSnrs = withSnr(this.favoriteSnrs, player.snr, !add); });
  }

  isTeamFavorite(team: TournamentTeam): boolean {
    return this.favoriteTeamSnrs.has(team.snr);
  }

  toggleTeamFavorite(team: TournamentTeam): void {
    const add = !this.favoriteTeamSnrs.has(team.snr);
    this.favoriteTeamSnrs = withSnr(this.favoriteTeamSnrs, team.snr, add);
    this.refreshAllDisplayed();
    this.saveFavorite(
      add ? this.api.addTeamFavorite(this.id, team.snr) : this.api.removeTeamFavorite(this.id, team.snr),
      add, team.name,
      () => { this.favoriteTeamSnrs = withSnr(this.favoriteTeamSnrs, team.snr, !add); });
  }

  /**
   * Der Stern springt sofort um (optimistisch), die Bestaetigung kommt erst mit der Serverantwort.
   * Scheitert der Request (schlechter Empfang im Turniersaal, 500), springt der Stern zurueck und eine
   * Warnung sagt es — sonst waere der Favorit beim naechsten Laden still weg (vorgetaeuschtes Speichern).
   * 409 beim Hinzufuegen bzw. 404 beim Entfernen heissen: der Server hat den gewuenschten Stand schon.
   */
  private saveFavorite(request: Observable<unknown>, add: boolean, name: string, revert: () => void): void {
    const confirm = () => this.snackbar.quick(this.translate.instant(add ? 'tournaments.favorites.added' : 'tournaments.favorites.removed', { name }));
    request.subscribe({
      next: confirm,
      error: (err: HttpErrorResponse) => {
        if (err?.status === (add ? 409 : 404)) { confirm(); return; }
        revert();
        this.refreshAllDisplayed();
        this.snackbar.warn(this.translate.instant('tournaments.favorites.saveFailed', { name }));
      }
    });
  }

  // --- Team detail dialog ---

  share(): void {
    const url = window.location.origin + '/t/' + this.id;
    this.dialog.open(ShareTournamentDialogComponent, {
      data: { url },
      width: '400px',
      maxWidth: '95vw'
    });
  }

  showTeamPlayers(teamName: string): void {
    const team = this.teams.find(t => t.name === teamName);
    if (!team) return;
    this.api.getTeamDetails(this.id, team.snr).subscribe({
      next: (result) => {
        this.dialog.open(TeamPlayersDialogComponent, {
          data: { teamName: result.name, players: result.players || [] },
          width: '500px',
          maxWidth: '95vw'
        });
      },
      error: () => {
        this.snackbar.info(this.translate.instant('tournaments.detail.loadTeamDetailsFailed'));
      }
    });
  }
}

/** Kopie von `set` mit bzw. ohne `snr` (neue Referenz, damit die Tabellen neu filtern). */
function withSnr(set: Set<number>, snr: number, present: boolean): Set<number> {
  const next = new Set(set);
  if (present) next.add(snr); else next.delete(snr);
  return next;
}
