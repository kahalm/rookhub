import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { FormsModule } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { NotificationService } from '../../core/notification.service';
import { ShareTournamentDialogComponent } from './share-tournament-dialog.component';
import { TeamPlayersDialogComponent } from './team-players-dialog.component';
import { Tournament, TournamentPlayer, TournamentTeam, DisplayPairing, Subscription, TournamentFavorite } from '../../core/models';

@Component({
  selector: 'app-tournament-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatTabsModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatIconModule, MatSnackBarModule, MatProgressBarModule, MatSlideToggleModule, MatSortModule, MatDialogModule, TranslateModule, LoadingSpinnerComponent],
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

  playerColumns = ['fav', 'snr', 'title', 'name', 'fideId', 'elo', 'country', 'team', 'board'];
  teamColumns = ['fav', 'rank', 'name', 'points'];
  pairingColumns = ['board', 'white', 'result', 'black'];
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

  constructor(private route: ActivatedRoute, private router: Router, private http: HttpClient, private snackBar: MatSnackBar, private dialog: MatDialog, private notificationService: NotificationService, private translate: TranslateService) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id')!;
    const tab = this.route.snapshot.queryParams['tab'];
    const tabIndex = TournamentDetailComponent.TAB_NAMES.indexOf(tab);
    if (tabIndex >= 0) this.selectedTabIndex = tabIndex;
    this.loadFavorites();
    this.http.get<Tournament>(`/api/tournaments/${this.id}`).subscribe({
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
      error: () => { this.loading = false; this.snackBar.open(this.translate.instant('tournaments.detail.loadTournamentFailed'), this.translate.instant('common.close'), { duration: 3000 }); }
    });
    this.loadSubscription();
    this.loadMonitorStatus();
  }

  ngOnDestroy(): void {
    if (this.pollInterval) clearInterval(this.pollInterval);
    this.stopMonitorPoll();
  }

  loadSubscription(): void {
    this.http.get<Subscription[]>('/api/subscriptions').subscribe({
      next: (subs) => {
        this.subscription = subs.find(s => s.crawlerTournamentId === this.id) ?? null;
      },
      error: () => this.snackBar.open(this.translate.instant('tournaments.detail.loadSubscriptionFailed'), this.translate.instant('common.close'), { duration: 3000 })
    });
  }

  subscribe(): void {
    this.toggling = true;
    this.http.post<Subscription>('/api/subscriptions', {
      crawlerTournamentId: this.id,
      tournamentName: this.tournament?.name ?? ''
    }).subscribe({
      next: (sub) => {
        this.subscription = sub;
        this.toggling = false;
        this.snackBar.open(this.translate.instant('tournaments.actions.subscribed'), this.translate.instant('common.close'), { duration: 2000 });
      },
      error: (err) => {
        this.toggling = false;
        this.snackBar.open(err.error?.message || this.translate.instant('tournaments.actions.failed'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  unsubscribe(): void {
    if (!this.subscription) return;
    this.toggling = true;
    this.http.delete(`/api/subscriptions/${this.subscription.id}`).subscribe({
      next: () => {
        this.subscription = null;
        this.toggling = false;
        this.snackBar.open(this.translate.instant('tournaments.actions.unsubscribed'), this.translate.instant('common.close'), { duration: 2000 });
      },
      error: () => {
        this.toggling = false;
        this.snackBar.open(this.translate.instant('tournaments.actions.unsubscribeFailed'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  loadMonitorStatus(): void {
    this.http.get<any>(`/api/tournament-monitors/${this.id}`).subscribe({
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
      this.http.delete(`/api/tournament-monitors/${this.id}`).subscribe({
        next: () => {
          this.monitoring = false;
          this.monitorActiveUntil = null;
          this.monitorToggling = false;
          this.stopMonitorPoll();
          this.snackBar.open(this.translate.instant('tournaments.monitor.stopped'), this.translate.instant('common.close'), { duration: 2000 });
        },
        error: () => {
          this.monitorToggling = false;
          this.snackBar.open(this.translate.instant('tournaments.monitor.stopFailed'), this.translate.instant('common.close'), { duration: 3000 });
        }
      });
    } else {
      this.notificationService.requestPermission();
      this.http.post<any>(`/api/tournament-monitors/${this.id}`, {}).subscribe({
        next: (res) => {
          this.monitoring = true;
          this.monitorActiveUntil = res.activeUntil ? new Date(res.activeUntil) : null;
          this.lastKnownRounds = res.lastKnownRounds || 0;
          this.monitorToggling = false;
          this.startMonitorPoll();
          this.snackBar.open(this.translate.instant('tournaments.monitor.activated'), this.translate.instant('common.close'), { duration: 2000 });
        },
        error: () => {
          this.monitorToggling = false;
          this.snackBar.open(this.translate.instant('tournaments.monitor.activateFailed'), this.translate.instant('common.close'), { duration: 3000 });
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
      this.http.get<any>(`/api/tournament-monitors/${this.id}`).subscribe({
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
            this.snackBar.open(this.translate.instant('tournaments.monitor.newRoundSnack', { round: newRound }), this.translate.instant('common.close'), { duration: 5000 });
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
    this.http.post<any>('/api/tournaments/crawl', {
      chessResultsId: crawlId,
      jobType: 'Full'
    }).subscribe({
      next: (job) => this.pollRefreshJob(job.id),
      error: () => {
        this.refreshing = false;
        this.snackBar.open(this.translate.instant('tournaments.detail.refreshStartFailed'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  private pollRefreshJob(jobId: number): void {
    this.pollInterval = setInterval(() => {
      this.http.get<any>(`/api/tournaments/crawl/${jobId}`).subscribe({
        next: (job) => {
          if (job.status === 'Completed') {
            if (this.pollInterval) clearInterval(this.pollInterval);
            this.pollInterval = null;
            this.refreshing = false;
            this.snackBar.open(this.translate.instant('tournaments.detail.dataRefreshed'), this.translate.instant('common.close'), { duration: 2000 });
            this.reloadAll();
          } else if (job.status === 'Failed') {
            if (this.pollInterval) clearInterval(this.pollInterval);
            this.pollInterval = null;
            this.refreshing = false;
            this.snackBar.open(job.errorMessage || this.translate.instant('tournaments.detail.refreshFailed'), this.translate.instant('common.close'), { duration: 3000 });
          }
        },
        error: () => {
          if (this.pollInterval) clearInterval(this.pollInterval);
          this.pollInterval = null;
          this.refreshing = false;
          this.snackBar.open(this.translate.instant('tournaments.list.crawlConnectionLost'), this.translate.instant('common.close'), { duration: 3000 });
        }
      });
    }, 2000);
  }

  private reloadAll(): void {
    this.http.get<Tournament>(`/api/tournaments/${this.id}`).subscribe({
      next: (t) => {
        this.tournament = t;
        if (t.totalRounds) {
          this.rounds = Array.from({ length: t.totalRounds }, (_, i) => i + 1);
        }
      },
      error: () => this.snackBar.open(this.translate.instant('tournaments.detail.reloadTournamentFailed'), this.translate.instant('common.close'), { duration: 3000 })
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
    this.http.get<TournamentPlayer[]>(`/api/tournaments/${this.id}/players`).subscribe({
      next: (p) => { this.players = p; this.playersLoading = false; this.refreshFavoriteHelpers(); this.refreshDisplayedPlayers(); },
      error: () => { this.playersLoading = false; this.snackBar.open(this.translate.instant('tournaments.detail.loadPlayersFailed'), this.translate.instant('common.close'), { duration: 3000 }); }
    });
  }

  loadTeams(): void {
    this.teamsLoading = true;
    this.http.get<TournamentTeam[]>(`/api/tournaments/${this.id}/teams`).subscribe({
      next: (t) => { this.teams = t; this.teamsLoading = false; this.refreshFavoriteHelpers(); this.refreshDisplayedTeams(); },
      error: () => { this.teamsLoading = false; this.snackBar.open(this.translate.instant('tournaments.detail.loadTeamsFailed'), this.translate.instant('common.close'), { duration: 3000 }); }
    });
  }

  loadPairings(): void {
    this.pairingsLoading = true;
    // Response can be either TeamPairingResponse[] or TournamentPairing[] depending on tournament type
    this.http.get<any[]>(`/api/tournaments/${this.id}/pairings?round=${this.selectedRound}`).subscribe({
      next: (p) => {
        // Detect team vs individual pairings based on data format
        if (p.length > 0 && p[0].homeTeam !== undefined) {
          this.hasTeamPairings = true;
          this.pairings = p.map((item): DisplayPairing => ({
            board: item.matchNumber,
            white: item.homeTeam,
            black: item.awayTeam,
            result: item.homeScore != null ? `${item.homeScore} : ${item.awayScore}` : ''
          }));
        } else {
          this.hasTeamPairings = false;
          this.pairings = p.map((item): DisplayPairing => ({
            board: item.boardNumber,
            white: item.white,
            black: item.black,
            result: item.result ?? ''
          }));
        }
        this.pairingsLoading = false;
        this.refreshDisplayedPairings();
      },
      error: () => { this.pairingsLoading = false; this.snackBar.open(this.translate.instant('tournaments.detail.loadPairingsFailed'), this.translate.instant('common.close'), { duration: 3000 }); }
    });
  }

  // --- Sorting ---

  private sortData<T>(data: T[], sort: Sort): T[] {
    if (!sort.active || sort.direction === '') return data;
    const dir = sort.direction === 'asc' ? 1 : -1;
    const key = sort.active === 'team' ? 'teamName' : sort.active === 'board' ? 'boardNumber' : sort.active;
    return [...data].sort((a: any, b: any) => {
      const valA = a[key] ?? '';
      const valB = b[key] ?? '';
      if (typeof valA === 'number' && typeof valB === 'number') return (valA - valB) * dir;
      return String(valA).localeCompare(String(valB)) * dir;
    });
  }

  // --- Favorites (server-side) ---

  private loadFavorites(): void {
    this.http.get<TournamentFavorite[]>(`/api/tournament-favorites?tournamentId=${this.id}`).subscribe({
      next: (favs) => {
        this.favoriteSnrs = new Set(favs.filter(f => f.playerSnr).map(f => f.playerSnr!));
        this.favoriteTeamSnrs = new Set(favs.filter(f => f.teamSnr).map(f => f.teamSnr!));
        this.favoriteIdMap = new Map(favs.filter(f => f.playerSnr).map(f => [f.playerSnr!, f.id]));
        this.teamFavoriteIdMap = new Map(favs.filter(f => f.teamSnr).map(f => [f.teamSnr!, f.id]));
        this.refreshAllDisplayed();
      },
      error: () => {}
    });
    this.http.get<{ showFavoritesOnly: boolean }>(`/api/tournament-favorites/settings/${this.id}`).subscribe({
      next: (s) => { this.showFavoritesOnly = s.showFavoritesOnly; this.refreshAllDisplayed(); },
      error: () => {}
    });
  }

  onFavoritesToggle(checked: boolean): void {
    this.showFavoritesOnly = checked;
    this.refreshAllDisplayed();
    this.http.put(`/api/tournament-favorites/settings/${this.id}`, { showFavoritesOnly: checked }).subscribe({
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
    const teamNames = new Set<string>();
    for (const t of this.teams) {
      if (this.favoriteTeamSnrs.has(t.snr)) teamNames.add(t.name);
    }
    for (const p of this.players) {
      if (this.favoriteSnrs.has(p.snr) && p.teamName) teamNames.add(p.teamName);
    }
    this._favoriteTeamNames = teamNames;

    const names = new Set<string>();
    for (const p of this.players) {
      if (this.favoriteSnrs.has(p.snr) || (p.teamName && teamNames.has(p.teamName))) {
        names.add(p.name);
      }
    }
    this._favoriteNames = names;
  }

  private refreshDisplayedPlayers(): void {
    let data = this.players;
    if (this.showFavoritesOnly) {
      data = data.filter(p => this.favoriteSnrs.has(p.snr) || (p.teamName && this._favoriteTeamNames.has(p.teamName)));
    }
    this.displayedPlayers = this.sortData(data, this.playerSort);
  }

  private refreshDisplayedTeams(): void {
    let data = this.teams;
    if (this.showFavoritesOnly) {
      data = data.filter(t => this._favoriteTeamNames.has(t.name));
    }
    this.displayedTeams = this.sortData(data, this.teamSort);
  }

  private refreshDisplayedPairings(): void {
    let data = this.pairings;
    if (this.showFavoritesOnly) {
      if (this.hasTeamPairings) {
        data = data.filter(p => this._favoriteTeamNames.has(p.white) || this._favoriteTeamNames.has(p.black));
      } else {
        data = data.filter(p => this._favoriteNames.has(p.white) || this._favoriteNames.has(p.black));
      }
    }
    this.displayedPairings = this.sortData(data, this.pairingSort);
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
      this.snackBar.open(this.translate.instant('tournaments.favorites.removed', { name: player.name }), this.translate.instant('common.close'), { duration: 1500 });
      this.http.delete(`/api/tournament-favorites/by-player/${this.id}/${player.snr}`).subscribe({
        next: () => { this.favoriteIdMap.delete(player.snr); },
        error: () => {}
      });
    } else {
      this.favoriteSnrs.add(player.snr);
      this.favoriteSnrs = new Set(this.favoriteSnrs);
      this.refreshAllDisplayed();
      this.snackBar.open(this.translate.instant('tournaments.favorites.added', { name: player.name }), this.translate.instant('common.close'), { duration: 1500 });
      this.http.post<TournamentFavorite>('/api/tournament-favorites', {
        crawlerTournamentId: this.id,
        playerSnr: player.snr
      }).subscribe({
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
      this.snackBar.open(this.translate.instant('tournaments.favorites.removed', { name: team.name }), this.translate.instant('common.close'), { duration: 1500 });
      this.http.delete(`/api/tournament-favorites/by-team/${this.id}/${team.snr}`).subscribe({
        next: () => { this.teamFavoriteIdMap.delete(team.snr); },
        error: () => {}
      });
    } else {
      this.favoriteTeamSnrs.add(team.snr);
      this.favoriteTeamSnrs = new Set(this.favoriteTeamSnrs);
      this.refreshAllDisplayed();
      this.snackBar.open(this.translate.instant('tournaments.favorites.added', { name: team.name }), this.translate.instant('common.close'), { duration: 1500 });
      this.http.post<TournamentFavorite>('/api/tournament-favorites/team', {
        crawlerTournamentId: this.id,
        teamSnr: team.snr
      }).subscribe({
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
    this.http.get<TournamentTeam>(`/api/tournaments/${this.id}/teams/${team.snr}`).subscribe({
      next: (result) => {
        this.dialog.open(TeamPlayersDialogComponent, {
          data: { teamName: result.name, players: result.players || [] },
          width: '500px',
          maxWidth: '95vw'
        });
      },
      error: () => {
        this.snackBar.open(this.translate.instant('tournaments.detail.loadTeamDetailsFailed'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }
}
