import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { FormsModule } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { TeamPlayersDialogComponent } from './team-players-dialog.component';
import { ShareTournamentDialogComponent } from './share-tournament-dialog.component';
import { Tournament, TournamentPlayer, TournamentTeam, DisplayPairing } from '../../core/models';

@Component({
  selector: 'app-public-tournament',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatTabsModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatIconModule, MatSnackBarModule, MatSlideToggleModule, MatSortModule, MatDialogModule, TranslateModule, LoadingSpinnerComponent],
  templateUrl: './public-tournament.component.html',
  styleUrls: ['./public-tournament.component.scss'],
})
export class PublicTournamentComponent implements OnInit {
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

  playerSort: Sort = { active: '', direction: '' };
  teamSort: Sort = { active: '', direction: '' };
  pairingSort: Sort = { active: '', direction: '' };

  private id!: string;

  constructor(
    private route: ActivatedRoute,
    private http: HttpClient,
    private snackBar: MatSnackBar,
    private dialog: MatDialog,
    private translate: TranslateService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id')!;
    this.loadLocalFavorites();
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
      error: () => { this.loading = false; }
    });
  }

  // --- Share ---

  share(): void {
    const url = window.location.origin + '/t/' + this.id;
    this.dialog.open(ShareTournamentDialogComponent, {
      data: { url },
      width: '400px',
      maxWidth: '95vw'
    });
  }

  // --- Data loading ---

  onTabChange(event: { index: number }): void {
    this.selectedTabIndex = event.index;
    if (event.index === 1 && this.teams.length === 0) this.loadTeams();
    if (event.index === 2 && this.pairings.length === 0) this.loadPairings();
  }

  loadPlayers(): void {
    this.playersLoading = true;
    this.http.get<TournamentPlayer[]>(`/api/tournaments/${this.id}/players`).subscribe({
      next: (p) => { this.players = p; this.playersLoading = false; },
      error: () => { this.playersLoading = false; }
    });
  }

  loadTeams(): void {
    this.teamsLoading = true;
    this.http.get<TournamentTeam[]>(`/api/tournaments/${this.id}/teams`).subscribe({
      next: (t) => { this.teams = t; this.teamsLoading = false; },
      error: () => { this.teamsLoading = false; }
    });
  }

  loadPairings(): void {
    this.pairingsLoading = true;
    // Response can be either TeamPairingResponse[] or TournamentPairing[] depending on tournament type
    this.http.get<any[]>(`/api/tournaments/${this.id}/pairings?round=${this.selectedRound}`).subscribe({
      next: (p) => {
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
      },
      error: () => { this.pairingsLoading = false; }
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

  // --- localStorage Favorites ---

  private get playerFavKey(): string { return `public_fav_players_${this.id}`; }
  private get teamFavKey(): string { return `public_fav_teams_${this.id}`; }
  private get filterKey(): string { return `public_fav_filter_${this.id}`; }

  private loadLocalFavorites(): void {
    try {
      const players = localStorage.getItem(this.playerFavKey);
      if (players) this.favoriteSnrs = new Set(JSON.parse(players));
      const teams = localStorage.getItem(this.teamFavKey);
      if (teams) this.favoriteTeamSnrs = new Set(JSON.parse(teams));
      const filter = localStorage.getItem(this.filterKey);
      if (filter) this.showFavoritesOnly = JSON.parse(filter);
    } catch {}
  }

  private saveLocalFavorites(): void {
    localStorage.setItem(this.playerFavKey, JSON.stringify([...this.favoriteSnrs]));
    localStorage.setItem(this.teamFavKey, JSON.stringify([...this.favoriteTeamSnrs]));
  }

  onFavoritesToggle(checked: boolean): void {
    this.showFavoritesOnly = checked;
    localStorage.setItem(this.filterKey, JSON.stringify(checked));
  }

  get hasFavorites(): boolean {
    return this.favoriteSnrs.size > 0 || this.favoriteTeamSnrs.size > 0;
  }

  get subtitle(): string {
    return [this.tournament?.location, this.tournament?.date].filter(Boolean).join(' | ');
  }

  get favoriteNames(): Set<string> {
    const names = new Set<string>();
    const favTeamNames = this.favoriteTeamNames;
    for (const p of this.players) {
      if (this.favoriteSnrs.has(p.snr) || (p.teamName && favTeamNames.has(p.teamName))) {
        names.add(p.name);
      }
    }
    return names;
  }

  get favoriteTeamNames(): Set<string> {
    const names = new Set<string>();
    for (const t of this.teams) {
      if (this.favoriteTeamSnrs.has(t.snr)) names.add(t.name);
    }
    for (const p of this.players) {
      if (this.favoriteSnrs.has(p.snr) && p.teamName) names.add(p.teamName);
    }
    return names;
  }

  get displayedPlayers(): TournamentPlayer[] {
    let data = this.players;
    if (this.showFavoritesOnly) {
      const favTeamNames = this.favoriteTeamNames;
      data = data.filter(p => this.favoriteSnrs.has(p.snr) || (p.teamName && favTeamNames.has(p.teamName)));
    }
    return this.sortData(data, this.playerSort);
  }

  get displayedTeams(): TournamentTeam[] {
    let data = this.teams;
    if (this.showFavoritesOnly) {
      const favTeams = this.favoriteTeamNames;
      data = data.filter(t => favTeams.has(t.name));
    }
    return this.sortData(data, this.teamSort);
  }

  get displayedPairings(): DisplayPairing[] {
    let data = this.pairings;
    if (this.showFavoritesOnly) {
      if (this.hasTeamPairings) {
        const favTeams = this.favoriteTeamNames;
        data = data.filter(p => favTeams.has(p.white) || favTeams.has(p.black));
      } else {
        const names = this.favoriteNames;
        data = data.filter(p => names.has(p.white) || names.has(p.black));
      }
    }
    return this.sortData(data, this.pairingSort);
  }

  isFavorite(player: TournamentPlayer): boolean {
    return this.favoriteSnrs.has(player.snr);
  }

  toggleFavorite(player: TournamentPlayer): void {
    if (this.favoriteSnrs.has(player.snr)) {
      this.favoriteSnrs.delete(player.snr);
      this.snackBar.open(this.translate.instant('tournaments.favorites.removedShort', { name: player.name }), this.translate.instant('common.close'), { duration: 1500 });
    } else {
      this.favoriteSnrs.add(player.snr);
      this.snackBar.open(this.translate.instant('tournaments.favorites.addedShort', { name: player.name }), this.translate.instant('common.close'), { duration: 1500 });
    }
    this.favoriteSnrs = new Set(this.favoriteSnrs);
    this.saveLocalFavorites();
  }

  isTeamFavorite(team: TournamentTeam): boolean {
    return this.favoriteTeamSnrs.has(team.snr);
  }

  toggleTeamFavorite(team: TournamentTeam): void {
    if (this.favoriteTeamSnrs.has(team.snr)) {
      this.favoriteTeamSnrs.delete(team.snr);
      this.snackBar.open(this.translate.instant('tournaments.favorites.removedShort', { name: team.name }), this.translate.instant('common.close'), { duration: 1500 });
    } else {
      this.favoriteTeamSnrs.add(team.snr);
      this.snackBar.open(this.translate.instant('tournaments.favorites.addedShort', { name: team.name }), this.translate.instant('common.close'), { duration: 1500 });
    }
    this.favoriteTeamSnrs = new Set(this.favoriteTeamSnrs);
    this.saveLocalFavorites();
  }

  // --- Team detail dialog ---

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
