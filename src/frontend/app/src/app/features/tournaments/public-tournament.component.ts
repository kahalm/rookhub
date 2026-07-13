import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { PublicTournamentService } from '../../core/public-tournament.service';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { SnackbarService } from '../../core/snackbar.service';
import { Sort } from '@angular/material/sort';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { TeamPlayersDialogComponent } from './team-players-dialog.component';
import { ShareTournamentDialogComponent } from './share-tournament-dialog.component';
import { TournamentTablesComponent } from './tournament-tables.component';
import { Tournament, TournamentPlayer, TournamentTeam, DisplayPairing } from '../../core/models';
import { PLAYER_COLUMNS, TEAM_COLUMNS, PAIRING_COLUMNS, sortTableData, toDisplayPairings } from './tournament-table.util';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-public-tournament',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatDialogModule, TranslatePipe, LoadingSpinnerComponent, TournamentTablesComponent],
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

  playerColumns = PLAYER_COLUMNS;
  teamColumns = TEAM_COLUMNS;
  pairingColumns = PAIRING_COLUMNS;
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
    private tournaments: PublicTournamentService,
    private snackbar: SnackbarService,
    private dialog: MatDialog,
    private translate: TranslateService
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id')!;
    this.loadLocalFavorites();
    this.tournaments.getTournament(this.id).subscribe({
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
    this.tournaments.getPlayers(this.id).subscribe({
      next: (p) => { this.players = p; this.playersLoading = false; },
      error: () => { this.playersLoading = false; }
    });
  }

  loadTeams(): void {
    this.teamsLoading = true;
    this.tournaments.getTeams(this.id).subscribe({
      next: (t) => { this.teams = t; this.teamsLoading = false; },
      error: () => { this.teamsLoading = false; }
    });
  }

  loadPairings(): void {
    this.pairingsLoading = true;
    // Response can be either TeamPairingResponse[] or TournamentPairing[] depending on tournament type
    this.tournaments.getPairings<any[]>(this.id, this.selectedRound).subscribe({
      next: (p) => {
        const { pairings, hasTeamPairings } = toDisplayPairings(p);
        this.pairings = pairings;
        this.hasTeamPairings = hasTeamPairings;
        this.pairingsLoading = false;
      },
      error: () => { this.pairingsLoading = false; }
    });
  }

  onRoundChange(round: number): void {
    this.selectedRound = round;
    this.loadPairings();
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
    return sortTableData(data, this.playerSort);
  }

  get displayedTeams(): TournamentTeam[] {
    let data = this.teams;
    if (this.showFavoritesOnly) {
      const favTeams = this.favoriteTeamNames;
      data = data.filter(t => favTeams.has(t.name));
    }
    return sortTableData(data, this.teamSort);
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
    return sortTableData(data, this.pairingSort);
  }

  isFavorite(player: TournamentPlayer): boolean {
    return this.favoriteSnrs.has(player.snr);
  }

  toggleFavorite(player: TournamentPlayer): void {
    if (this.favoriteSnrs.has(player.snr)) {
      this.favoriteSnrs.delete(player.snr);
      this.snackbar.quick(this.translate.instant('tournaments.favorites.removedShort', { name: player.name }));
    } else {
      this.favoriteSnrs.add(player.snr);
      this.snackbar.quick(this.translate.instant('tournaments.favorites.addedShort', { name: player.name }));
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
      this.snackbar.quick(this.translate.instant('tournaments.favorites.removedShort', { name: team.name }));
    } else {
      this.favoriteTeamSnrs.add(team.snr);
      this.snackbar.quick(this.translate.instant('tournaments.favorites.addedShort', { name: team.name }));
    }
    this.favoriteTeamSnrs = new Set(this.favoriteTeamSnrs);
    this.saveLocalFavorites();
  }

  // --- Team detail dialog ---

  showTeamPlayers(teamName: string): void {
    const team = this.teams.find(t => t.name === teamName);
    if (!team) return;
    this.tournaments.getTeam(this.id, team.snr).subscribe({
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
