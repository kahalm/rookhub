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
  template: `
    @if (loading) {
      <app-loading-spinner />
    } @else if (tournament) {
      <div class="detail-container">
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ tournament.name }}</mat-card-title>
            <mat-card-subtitle>{{ subtitle }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions class="action-bar">
            <a mat-raised-button [href]="'https://chess-results.com/tnr' + tournament.chessResultsId + '.aspx?lan=0'" target="_blank">
              <mat-icon>open_in_new</mat-icon><span class="btn-label"> {{ 'tournaments.actions.chessResults' | translate }}</span>
            </a>
            <button mat-raised-button (click)="share()">
              <mat-icon>share</mat-icon><span class="btn-label"> {{ 'tournaments.actions.share' | translate }}</span>
            </button>
          </mat-card-actions>
        </mat-card>

        <mat-tab-group [selectedIndex]="selectedTabIndex" (selectedTabChange)="onTabChange($event)">
          <mat-tab [label]="'tournaments.tabs.players' | translate:{ count: players.length }">
            @if (playersLoading) {
              <app-loading-spinner />
            } @else {
              @if (hasFavorites) {
                <div class="filter-bar">
                  <mat-slide-toggle [checked]="showFavoritesOnly" (change)="onFavoritesToggle($event.checked)">{{ 'tournaments.favoritesOnly' | translate }}</mat-slide-toggle>
                </div>
              }
              <!-- Desktop -->
              <div class="table-scroll desktop-only">
                <table mat-table [dataSource]="displayedPlayers" matSort (matSortChange)="playerSort = $event" class="full-width">
                  <ng-container matColumnDef="fav">
                    <th mat-header-cell *matHeaderCellDef></th>
                    <td mat-cell *matCellDef="let p">
                      <mat-icon class="fav-icon" [class.fav-active]="isFavorite(p)" (click)="toggleFavorite(p)">
                        {{ isFavorite(p) ? 'star' : 'star_border' }}
                      </mat-icon>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="snr">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.players.snr' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.snr }}</td>
                  </ng-container>
                  <ng-container matColumnDef="title">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.players.title' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.title }}</td>
                  </ng-container>
                  <ng-container matColumnDef="name">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.players.name' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.name }}</td>
                  </ng-container>
                  <ng-container matColumnDef="fideId">
                    <th mat-header-cell *matHeaderCellDef>{{ 'tournaments.players.fideId' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.fideId }}</td>
                  </ng-container>
                  <ng-container matColumnDef="elo">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.players.elo' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.elo }}</td>
                  </ng-container>
                  <ng-container matColumnDef="country">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.players.country' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.country }}</td>
                  </ng-container>
                  <ng-container matColumnDef="team">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ (hasTeamPairings ? 'tournaments.players.team' : 'tournaments.players.club') | translate }}</th>
                    <td mat-cell *matCellDef="let p">
                      @if (p.teamName && hasTeamPairings) {
                        <span class="team-link" (click)="showTeamPlayers(p.teamName)">{{ p.teamName }}</span>
                      } @else {
                        {{ p.teamName }}
                      }
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="board">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.players.boardShort' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.boardNumber }}</td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="playerColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: playerColumns;"></tr>
                </table>
              </div>

              <!-- Mobile -->
              <div class="mobile-only player-cards">
                @for (p of displayedPlayers; track p.snr) {
                  <div class="player-card" (click)="toggleFavorite(p)">
                    <div class="player-main">
                      <mat-icon class="fav-icon-sm" [class.fav-active]="isFavorite(p)">
                        {{ isFavorite(p) ? 'star' : 'star_border' }}
                      </mat-icon>
                      <span class="player-snr">{{ p.snr }}</span>
                      <span class="player-title" *ngIf="p.title">{{ p.title }}</span>
                      <span class="player-name">{{ p.name }}</span>
                    </div>
                    <div class="player-details">
                      @if (p.elo) { <span>{{ p.elo }}</span> }
                      @if (p.country) { <span>{{ p.country }}</span> }
                      @if (p.teamName) { <span>{{ p.teamName }}</span> }
                      @if (p.boardNumber) { <span>{{ 'tournaments.players.boardShort' | translate }} {{ p.boardNumber }}</span> }
                    </div>
                  </div>
                }
              </div>
            }
          </mat-tab>

          <mat-tab [label]="'tournaments.tabs.teams' | translate:{ count: teams.length }">
            @if (teamsLoading) {
              <app-loading-spinner />
            } @else {
              @if (hasFavorites) {
                <div class="filter-bar">
                  <mat-slide-toggle [checked]="showFavoritesOnly" (change)="onFavoritesToggle($event.checked)">{{ 'tournaments.favoritesOnly' | translate }}</mat-slide-toggle>
                </div>
              }
              <div class="table-scroll">
                <table mat-table [dataSource]="displayedTeams" matSort (matSortChange)="teamSort = $event" class="full-width">
                  <ng-container matColumnDef="fav">
                    <th mat-header-cell *matHeaderCellDef></th>
                    <td mat-cell *matCellDef="let t">
                      <mat-icon class="fav-icon" [class.fav-active]="isTeamFavorite(t)" (click)="toggleTeamFavorite(t)">
                        {{ isTeamFavorite(t) ? 'star' : 'star_border' }}
                      </mat-icon>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="rank">
                    <th mat-header-cell *matHeaderCellDef>{{ 'tournaments.teams.rank' | translate }}</th>
                    <td mat-cell *matCellDef="let t; let i = index">{{ i + 1 }}</td>
                  </ng-container>
                  <ng-container matColumnDef="name">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.teams.team' | translate }}</th>
                    <td mat-cell *matCellDef="let t">
                      <span class="team-link" (click)="showTeamPlayers(t.name)">{{ t.name }}</span>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="points">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.teams.points' | translate }}</th>
                    <td mat-cell *matCellDef="let t">{{ t.points }}</td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="teamColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: teamColumns;"></tr>
                </table>
              </div>
            }
          </mat-tab>

          <mat-tab [label]="'tournaments.tabs.pairings' | translate">
            <div class="round-selector">
              <mat-form-field appearance="outline">
                <mat-label>{{ 'tournaments.pairings.round' | translate }}</mat-label>
                <mat-select [(ngModel)]="selectedRound" (selectionChange)="loadPairings()">
                  @for (r of rounds; track r) {
                    <mat-option [value]="r">{{ 'tournaments.pairings.roundLabel' | translate:{ round: r } }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              @if (hasFavorites) {
                <mat-slide-toggle [checked]="showFavoritesOnly" (change)="onFavoritesToggle($event.checked)">{{ 'tournaments.favoritesOnly' | translate }}</mat-slide-toggle>
              }
            </div>
            @if (pairingsLoading) {
              <app-loading-spinner />
            } @else {
              <!-- Desktop -->
              <div class="table-scroll desktop-only">
                <table mat-table [dataSource]="displayedPairings" matSort (matSortChange)="pairingSort = $event" class="full-width">
                  <ng-container matColumnDef="board">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'tournaments.pairings.board' | translate }}</th>
                    <td mat-cell *matCellDef="let p">{{ p.board }}</td>
                  </ng-container>
                  <ng-container matColumnDef="white">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ (hasTeamPairings ? 'tournaments.pairings.home' : 'tournaments.pairings.white') | translate }}</th>
                    <td mat-cell *matCellDef="let p">
                      @if (hasTeamPairings) {
                        <span class="team-link" (click)="showTeamPlayers(p.white)">{{ p.white }}</span>
                      } @else {
                        {{ p.white }}
                      }
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="result">
                    <th mat-header-cell *matHeaderCellDef>{{ 'tournaments.pairings.result' | translate }}</th>
                    <td mat-cell *matCellDef="let p" class="result-cell">{{ p.result }}</td>
                  </ng-container>
                  <ng-container matColumnDef="black">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ (hasTeamPairings ? 'tournaments.pairings.away' : 'tournaments.pairings.black') | translate }}</th>
                    <td mat-cell *matCellDef="let p">
                      @if (hasTeamPairings) {
                        <span class="team-link" (click)="showTeamPlayers(p.black)">{{ p.black }}</span>
                      } @else {
                        {{ p.black }}
                      }
                    </td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="pairingColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: pairingColumns;"></tr>
                </table>
              </div>

              <!-- Mobile -->
              <div class="mobile-only pairing-cards">
                @for (p of displayedPairings; track p.board) {
                  <div class="pairing-card">
                    <span class="pairing-board">{{ p.board }}</span>
                    <div class="pairing-teams">
                      <div class="pairing-team">
                        @if (hasTeamPairings) {
                          <span class="team-link" (click)="showTeamPlayers(p.white)">{{ p.white }}</span>
                        } @else {
                          {{ p.white }}
                        }
                      </div>
                      <span class="pairing-result">{{ p.result }}</span>
                      <div class="pairing-team">
                        @if (hasTeamPairings) {
                          <span class="team-link" (click)="showTeamPlayers(p.black)">{{ p.black }}</span>
                        } @else {
                          {{ p.black }}
                        }
                      </div>
                    </div>
                  </div>
                }
              </div>
            }
          </mat-tab>
        </mat-tab-group>
      </div>
    } @else {
      <div class="detail-container">
        <mat-card>
          <mat-card-content>
            <p>{{ 'tournaments.public.notFound' | translate }}</p>
          </mat-card-content>
        </mat-card>
      </div>
    }
  `,
  styles: [`
    .detail-container { padding: 2rem; max-width: 1100px; margin: 0 auto; }
    .full-width { width: 100%; }
    .table-scroll { overflow-x: auto; }
    .round-selector { padding: 1rem 0; display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; }
    .filter-bar { padding: 1rem 0 0; display: flex; align-items: center; gap: 1rem; }
    mat-card { margin-bottom: 1rem; }
    .action-bar { display: flex; flex-wrap: wrap; gap: 0.5rem; }

    .fav-icon, .fav-icon-sm { cursor: pointer; color: #ccc; font-size: 20px; }
    .fav-icon.fav-active, .fav-icon-sm.fav-active { color: #ffc107; }
    .fav-icon:hover { color: #ffc107; }

    .team-link { cursor: pointer; color: #1565c0; text-decoration: underline; }
    .team-link:hover { color: #0d47a1; }

    .mobile-only { display: none; }
    .player-cards { padding: 0.5rem 0; }
    .player-card {
      padding: 0.75rem 1rem;
      border-bottom: 1px solid rgba(0,0,0,0.08);
      cursor: pointer;
    }
    .player-card:hover { background: rgba(0,0,0,0.02); }
    .player-main {
      display: flex; align-items: baseline; gap: 0.4rem; font-size: 0.95rem;
    }
    .player-snr { color: #888; min-width: 2rem; }
    .player-title { font-weight: 600; color: #1565c0; }
    .player-name { font-weight: 500; }
    .player-details {
      display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.25rem;
      padding-left: 2.4rem; font-size: 0.82rem; color: #666;
    }
    .player-details span:not(:last-child)::after { content: "\\00b7"; margin-left: 0.5rem; }

    .result-cell { white-space: nowrap; }

    .pairing-cards { padding: 0.5rem 0; }
    .pairing-card {
      display: flex; gap: 0.75rem; padding: 0.75rem 1rem;
      border-bottom: 1px solid rgba(0,0,0,0.08); align-items: center;
    }
    .pairing-board { color: #888; min-width: 1.5rem; font-size: 0.85rem; }
    .pairing-teams { flex: 1; min-width: 0; }
    .pairing-team {
      font-size: 0.93rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    }
    .pairing-result { font-size: 0.82rem; color: #666; padding: 0.15rem 0; white-space: nowrap; }

    @media (max-width: 768px) {
      .detail-container { padding: 0.75rem; }
      .desktop-only { display: none; }
      .mobile-only { display: block; }
      .btn-label { display: none; }
      .action-bar button, .action-bar a {
        min-width: 0 !important; padding: 0 !important; width: 44px; height: 44px; border-radius: 50%;
        display: inline-flex; align-items: center; justify-content: center;
      }
      :host ::ng-deep .action-bar .mat-icon { margin: 0 !important; font-size: 24px; width: 24px; height: 24px; }
      :host ::ng-deep .mat-mdc-tab { min-width: 0 !important; padding: 0 8px !important; }
      :host ::ng-deep .mat-mdc-tab .mdc-tab__text-label { font-size: 0.75rem; }
      .player-details { padding-left: 1.5rem; }
      .pairing-team { white-space: normal; }
    }
  `]
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
