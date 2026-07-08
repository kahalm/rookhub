import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { SnackbarService } from '../../core/snackbar.service';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Sort } from '@angular/material/sort';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { NotificationService } from '../../core/notification.service';
import { ShareTournamentDialogComponent } from './share-tournament-dialog.component';
import { TeamPlayersDialogComponent } from './team-players-dialog.component';
import { TournamentTablesComponent } from './tournament-tables.component';
import { Tournament, TournamentPlayer, TournamentTeam, DisplayPairing, Subscription } from '../../core/models';
import { PLAYER_COLUMNS, TEAM_COLUMNS, PAIRING_COLUMNS, sortTableData, toDisplayPairings } from './tournament-table.util';
import { TournamentDetailService } from './tournament-detail.service';
import { computeFavoriteNames, filterPlayersByFavorites, filterTeamsByFavorites, filterPairingsByFavorites } from './tournament-favorites.util';

@Component({
  selector: 'app-tournament-detail',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatDialogModule, TranslateModule, LoadingSpinnerComponent, TournamentTablesComponent],
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

  // Map playerSnr -> server favorite ID for deletion
  private favoriteIdMap: Map<number, number> = new Map();
  private teamFavoriteIdMap: Map<number, number> = new Map();

  constructor(private route: ActivatedRoute, private router: Router, private api: TournamentDetailService, private snackbar: SnackbarService, private dialog: MatDialog, private notificationService: NotificationService, private translate: TranslateService) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id')!;
    const tab = this.route.snapshot.queryParams['tab'];
    const tabIndex = TournamentDetailComponent.TAB_NAMES.indexOf(tab);
    if (tabIndex >= 0) this.selectedTabIndex = tabIndex;
    this.loadFavorites();
    this.api.getTournament(this.id).subscribe({
      next: (t) => {
        this.tournament = t;
        this.loading = false;
        if (t.totalRounds) {
          this.rounds = Array.from({ length: t.totalRounds }, (_, i) => i + 1);
        }
        this.loadPlayers();
        this.loadTeams();
        if (this.selectedTabIndex === 2) this.loadPairings();
      },
      error: () => { this.loading = false; this.snackbar.info(this.translate.instant('tournaments.detail.loadTournamentFailed')); }
    });
    this.loadSubscription();
    this.loadMonitorStatus();
  }

  ngOnDestroy(): void {
    if (this.pollInterval) clearInterval(this.pollInterval);
    this.stopMonitorPoll();
  }

  loadSubscription(): void {
    this.api.getSubscriptions().subscribe({
      next: (subs) => {
        this.subscription = subs.find(s => s.crawlerTournamentId === this.id) ?? null;
      },
      error: () => this.snackbar.info(this.translate.instant('tournaments.detail.loadSubscriptionFailed'))
    });
  }

  subscribe(): void {
    this.toggling = true;
    this.api.subscribe(this.id, this.tournament?.name ?? '').subscribe({
      next: (sub) => {
        this.subscription = sub;
        this.toggling = false;
        this.snackbar.success(this.translate.instant('tournaments.actions.subscribed'));
      },
      error: (err) => {
        this.toggling = false;
        this.snackbar.info(err.error?.message || this.translate.instant('tournaments.actions.failed'));
      }
    });
  }

  unsubscribe(): void {
    if (!this.subscription) return;
    this.toggling = true;
    this.api.unsubscribe(this.subscription.id).subscribe({
      next: () => {
        this.subscription = null;
        this.toggling = false;
        this.snackbar.success(this.translate.instant('tournaments.actions.unsubscribed'));
      },
      error: () => {
        this.toggling = false;
        this.snackbar.info(this.translate.instant('tournaments.actions.unsubscribeFailed'));
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
      error: () => {
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
        this.favoriteIdMap = new Map(favs.filter(f => f.playerSnr).map(f => [f.playerSnr!, f.id]));
        this.teamFavoriteIdMap = new Map(favs.filter(f => f.teamSnr).map(f => [f.teamSnr!, f.id]));
        this.refreshAllDisplayed();
      },
      error: () => {}
    });
    this.api.getFavoriteSettings(this.id).subscribe({
      next: (s) => { this.showFavoritesOnly = s.showFavoritesOnly; this.refreshAllDisplayed(); },
      error: () => {}
    });
  }

  onFavoritesToggle(checked: boolean): void {
    this.showFavoritesOnly = checked;
    this.refreshAllDisplayed();
    this.api.saveFavoriteSettings(this.id, checked).subscribe({
      error: () => {}
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
    if (this.favoriteSnrs.has(player.snr)) {
      this.favoriteSnrs.delete(player.snr);
      this.favoriteSnrs = new Set(this.favoriteSnrs);
      this.refreshAllDisplayed();
      this.snackbar.quick(this.translate.instant('tournaments.favorites.removed', { name: player.name }));
      this.api.removePlayerFavorite(this.id, player.snr).subscribe({
        next: () => { this.favoriteIdMap.delete(player.snr); },
        error: () => {}
      });
    } else {
      this.favoriteSnrs.add(player.snr);
      this.favoriteSnrs = new Set(this.favoriteSnrs);
      this.refreshAllDisplayed();
      this.snackbar.quick(this.translate.instant('tournaments.favorites.added', { name: player.name }));
      this.api.addPlayerFavorite(this.id, player.snr).subscribe({
        next: (fav) => { this.favoriteIdMap.set(player.snr, fav.id); },
        error: () => {}
      });
    }
  }

  isTeamFavorite(team: TournamentTeam): boolean {
    return this.favoriteTeamSnrs.has(team.snr);
  }

  toggleTeamFavorite(team: TournamentTeam): void {
    if (this.favoriteTeamSnrs.has(team.snr)) {
      this.favoriteTeamSnrs.delete(team.snr);
      this.favoriteTeamSnrs = new Set(this.favoriteTeamSnrs);
      this.refreshAllDisplayed();
      this.snackbar.quick(this.translate.instant('tournaments.favorites.removed', { name: team.name }));
      this.api.removeTeamFavorite(this.id, team.snr).subscribe({
        next: () => { this.teamFavoriteIdMap.delete(team.snr); },
        error: () => {}
      });
    } else {
      this.favoriteTeamSnrs.add(team.snr);
      this.favoriteTeamSnrs = new Set(this.favoriteTeamSnrs);
      this.refreshAllDisplayed();
      this.snackbar.quick(this.translate.instant('tournaments.favorites.added', { name: team.name }));
      this.api.addTeamFavorite(this.id, team.snr).subscribe({
        next: (fav) => { this.teamFavoriteIdMap.set(team.snr, fav.id); },
        error: () => {}
      });
    }
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
