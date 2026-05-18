import { Component, OnInit, OnDestroy, Inject } from '@angular/core';
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
import { MatDialogModule, MatDialog, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { FormsModule } from '@angular/forms';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-team-players-dialog',
  standalone: true,
  imports: [CommonModule, MatTableModule, MatButtonModule, MatIconModule],
  template: `
    <h2 class="dialog-title">{{ data.teamName }}</h2>
    <div class="dialog-table-scroll">
      <table mat-table [dataSource]="data.players" class="full-width">
        <ng-container matColumnDef="boardNumber">
          <th mat-header-cell *matHeaderCellDef>Br.</th>
          <td mat-cell *matCellDef="let p">{{ p.boardNumber }}</td>
        </ng-container>
        <ng-container matColumnDef="title">
          <th mat-header-cell *matHeaderCellDef>Title</th>
          <td mat-cell *matCellDef="let p">{{ p.title }}</td>
        </ng-container>
        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>Name</th>
          <td mat-cell *matCellDef="let p">{{ p.name }}</td>
        </ng-container>
        <ng-container matColumnDef="elo">
          <th mat-header-cell *matHeaderCellDef>Elo</th>
          <td mat-cell *matCellDef="let p">{{ p.elo }}</td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns;"></tr>
      </table>
    </div>
    <div class="dialog-actions">
      <button mat-button mat-dialog-close>Close</button>
    </div>
  `,
  styles: [`
    .dialog-title { margin: 0 0 1rem; font-size: 1.2rem; }
    .dialog-table-scroll { overflow-x: auto; max-height: 60vh; }
    .full-width { width: 100%; }
    .dialog-actions { display: flex; justify-content: flex-end; margin-top: 1rem; }
  `]
})
export class TeamPlayersDialogComponent {
  columns = ['boardNumber', 'title', 'name', 'elo'];
  constructor(@Inject(MAT_DIALOG_DATA) public data: { teamName: string; players: any[] }) {}
}

@Component({
  selector: 'app-tournament-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatTabsModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatIconModule, MatSnackBarModule, MatProgressBarModule, MatSlideToggleModule, MatSortModule, MatDialogModule, LoadingSpinnerComponent],
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
              <mat-icon>open_in_new</mat-icon><span class="btn-label"> Chess-Results</span>
            </a>
            <button mat-raised-button (click)="refresh()" [disabled]="refreshing">
              <mat-icon>refresh</mat-icon><span class="btn-label"> Refresh</span>
            </button>
            @if (subscription) {
              <button mat-raised-button color="warn" (click)="unsubscribe()" [disabled]="toggling">
                <mat-icon>notifications_off</mat-icon><span class="btn-label"> Unsubscribe</span>
              </button>
            } @else {
              <button mat-raised-button color="primary" (click)="subscribe()" [disabled]="toggling">
                <mat-icon>notifications</mat-icon><span class="btn-label"> Subscribe</span>
              </button>
            }
          </mat-card-actions>
          @if (refreshing) {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
          }
        </mat-card>

        <mat-tab-group [selectedIndex]="selectedTabIndex" (selectedTabChange)="onTabChange($event)">
          <mat-tab label="Players ({{ players.length }})">
            @if (playersLoading) {
              <app-loading-spinner />
            } @else {
              @if (hasFavorites) {
                <div class="filter-bar">
                  <mat-slide-toggle [(ngModel)]="showFavoritesOnly">Nur Favoriten</mat-slide-toggle>
                </div>
              }
              <!-- Desktop: full table -->
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
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Nr.</th>
                    <td mat-cell *matCellDef="let p">{{ p.snr }}</td>
                  </ng-container>
                  <ng-container matColumnDef="title">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Title</th>
                    <td mat-cell *matCellDef="let p">{{ p.title }}</td>
                  </ng-container>
                  <ng-container matColumnDef="name">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
                    <td mat-cell *matCellDef="let p">{{ p.name }}</td>
                  </ng-container>
                  <ng-container matColumnDef="fideId">
                    <th mat-header-cell *matHeaderCellDef>FIDE ID</th>
                    <td mat-cell *matCellDef="let p">{{ p.fideId }}</td>
                  </ng-container>
                  <ng-container matColumnDef="elo">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Elo</th>
                    <td mat-cell *matCellDef="let p">{{ p.elo }}</td>
                  </ng-container>
                  <ng-container matColumnDef="country">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Country</th>
                    <td mat-cell *matCellDef="let p">{{ p.country }}</td>
                  </ng-container>
                  <ng-container matColumnDef="team">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ isTeamTournament ? 'Team' : 'Verein' }}</th>
                    <td mat-cell *matCellDef="let p">
                      @if (p.teamName && isTeamTournament) {
                        <span class="team-link" (click)="showTeamPlayers(p.teamName)">{{ p.teamName }}</span>
                      } @else {
                        {{ p.teamName }}
                      }
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="board">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Br.</th>
                    <td mat-cell *matCellDef="let p">{{ p.boardNumber }}</td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="playerColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: playerColumns;"></tr>
                </table>
              </div>

              <!-- Mobile: card list -->
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
                      @if (p.teamName) { <span>{{ isTeamTournament ? '' : '' }}{{ p.teamName }}</span> }
                      @if (p.boardNumber) { <span>Br. {{ p.boardNumber }}</span> }
                    </div>
                  </div>
                }
              </div>
            }
          </mat-tab>

          <mat-tab label="Teams ({{ teams.length }})">
            @if (teamsLoading) {
              <app-loading-spinner />
            } @else {
              @if (hasFavorites) {
                <div class="filter-bar">
                  <mat-slide-toggle [(ngModel)]="showFavoritesOnly">Nur Favoriten</mat-slide-toggle>
                </div>
              }
              <div class="table-scroll">
                <table mat-table [dataSource]="displayedTeams" matSort (matSortChange)="teamSort = $event" class="full-width">
                  <ng-container matColumnDef="rank">
                    <th mat-header-cell *matHeaderCellDef>Rank</th>
                    <td mat-cell *matCellDef="let t; let i = index">{{ i + 1 }}</td>
                  </ng-container>
                  <ng-container matColumnDef="name">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Team</th>
                    <td mat-cell *matCellDef="let t">
                      <span class="team-link" (click)="showTeamPlayers(t.name)">{{ t.name }}</span>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="points">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Points</th>
                    <td mat-cell *matCellDef="let t">{{ t.points }}</td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="teamColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: teamColumns;"></tr>
                </table>
              </div>
            }
          </mat-tab>

          <mat-tab label="Pairings">
            <div class="round-selector">
              <mat-form-field appearance="outline">
                <mat-label>Round</mat-label>
                <mat-select [(ngModel)]="selectedRound" (selectionChange)="loadPairings()">
                  @for (r of rounds; track r) {
                    <mat-option [value]="r">Round {{ r }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              @if (hasFavorites) {
                <mat-slide-toggle [(ngModel)]="showFavoritesOnly">Nur Favoriten</mat-slide-toggle>
              }
            </div>
            @if (pairingsLoading) {
              <app-loading-spinner />
            } @else {
              <div class="table-scroll">
                <table mat-table [dataSource]="displayedPairings" matSort (matSortChange)="pairingSort = $event" class="full-width">
                  <ng-container matColumnDef="board">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>Board</th>
                    <td mat-cell *matCellDef="let p">{{ p.board }}</td>
                  </ng-container>
                  <ng-container matColumnDef="white">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ isTeamTournament ? 'Home' : 'White' }}</th>
                    <td mat-cell *matCellDef="let p">
                      @if (isTeamTournament) {
                        <span class="team-link" (click)="showTeamPlayers(p.white)">{{ p.white }}</span>
                      } @else {
                        {{ p.white }}
                      }
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="result">
                    <th mat-header-cell *matHeaderCellDef>Result</th>
                    <td mat-cell *matCellDef="let p">{{ p.result }}</td>
                  </ng-container>
                  <ng-container matColumnDef="black">
                    <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ isTeamTournament ? 'Away' : 'Black' }}</th>
                    <td mat-cell *matCellDef="let p">
                      @if (isTeamTournament) {
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
            }
          </mat-tab>
        </mat-tab-group>
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
    .empty-hint { padding: 1.5rem; color: #888; }

    .fav-icon, .fav-icon-sm { cursor: pointer; color: #ccc; font-size: 20px; }
    .fav-icon.fav-active, .fav-icon-sm.fav-active { color: #ffc107; }
    .fav-icon:hover { color: #ffc107; }
    .remove-fav:hover { color: #f44336 !important; }

    .team-link { cursor: pointer; color: #1565c0; text-decoration: underline; }
    .team-link:hover { color: #0d47a1; }

    /* Mobile card list for players */
    .mobile-only { display: none; }
    .player-cards { padding: 0.5rem 0; }
    .player-card {
      padding: 0.75rem 1rem;
      border-bottom: 1px solid rgba(0,0,0,0.08);
      cursor: pointer;
    }
    .player-card:hover { background: rgba(0,0,0,0.02); }
    .player-main {
      display: flex;
      align-items: baseline;
      gap: 0.4rem;
      font-size: 0.95rem;
    }
    .player-snr { color: #888; min-width: 2rem; }
    .player-title { font-weight: 600; color: #1565c0; }
    .player-name { font-weight: 500; }
    .player-details {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-top: 0.25rem;
      padding-left: 2.4rem;
      font-size: 0.82rem;
      color: #666;
    }
    .player-details span:not(:last-child)::after { content: "\\00b7"; margin-left: 0.5rem; }

    @media (max-width: 768px) {
      .detail-container { padding: 0.75rem; }
      .desktop-only { display: none; }
      .mobile-only { display: block; }
      .btn-label { display: none; }
      .action-bar button, .action-bar a { min-width: 0; padding: 0 12px; }
      :host ::ng-deep .mat-mdc-tab { min-width: 0 !important; padding: 0 8px !important; }
      :host ::ng-deep .mat-mdc-tab .mdc-tab__text-label { font-size: 0.75rem; }
    }
  `]
})
export class TournamentDetailComponent implements OnInit, OnDestroy {
  tournament: any = null;
  players: any[] = [];
  teams: any[] = [];
  pairings: any[] = [];
  rounds: number[] = [];
  selectedRound = 1;
  loading = true;
  playersLoading = false;
  teamsLoading = false;
  pairingsLoading = false;

  playerColumns = ['fav', 'snr', 'title', 'name', 'fideId', 'elo', 'country', 'team', 'board'];
  teamColumns = ['rank', 'name', 'points'];
  pairingColumns = ['board', 'white', 'result', 'black'];
  showFavoritesOnly = false;
  favoriteSnrs: Set<number> = new Set();
  selectedTabIndex = 0;

  // Sort states
  playerSort: Sort = { active: '', direction: '' };
  teamSort: Sort = { active: '', direction: '' };
  pairingSort: Sort = { active: '', direction: '' };

  subscription: any = null;
  toggling = false;
  refreshing = false;
  private pollInterval: ReturnType<typeof setInterval> | null = null;

  private static readonly TAB_NAMES = ['players', 'teams', 'pairings'];
  private id!: string;

  constructor(private route: ActivatedRoute, private router: Router, private http: HttpClient, private snackBar: MatSnackBar, private dialog: MatDialog) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id')!;
    const tab = this.route.snapshot.queryParams['tab'];
    const tabIndex = TournamentDetailComponent.TAB_NAMES.indexOf(tab);
    if (tabIndex > 0) this.selectedTabIndex = tabIndex;
    this.loadFavorites();
    this.http.get(`/api/tournaments/${this.id}`).subscribe({
      next: (t: any) => {
        this.tournament = t;
        this.loading = false;
        if (t.totalRounds) {
          this.rounds = Array.from({ length: t.totalRounds }, (_, i) => i + 1);
        }
        this.loadPlayers();
        this.loadTeams();
      },
      error: () => { this.loading = false; }
    });
    this.loadSubscription();
  }

  ngOnDestroy(): void {
    if (this.pollInterval) clearInterval(this.pollInterval);
  }

  loadSubscription(): void {
    this.http.get<any[]>('/api/subscriptions').subscribe({
      next: (subs) => {
        this.subscription = subs.find(s => s.crawlerTournamentId === this.id) ?? null;
      }
    });
  }

  subscribe(): void {
    this.toggling = true;
    this.http.post<any>('/api/subscriptions', {
      crawlerTournamentId: this.id,
      tournamentName: this.tournament?.name ?? ''
    }).subscribe({
      next: (sub) => {
        this.subscription = sub;
        this.toggling = false;
        this.snackBar.open('Subscribed!', 'Close', { duration: 2000 });
      },
      error: (err) => {
        this.toggling = false;
        this.snackBar.open(err.error?.message || 'Failed', 'Close', { duration: 3000 });
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
        this.snackBar.open('Unsubscribed', 'Close', { duration: 2000 });
      },
      error: () => {
        this.toggling = false;
        this.snackBar.open('Failed to unsubscribe', 'Close', { duration: 3000 });
      }
    });
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
        this.snackBar.open('Failed to start refresh', 'Close', { duration: 3000 });
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
            this.snackBar.open('Data refreshed!', 'Close', { duration: 2000 });
            this.reloadAll();
          } else if (job.status === 'Failed') {
            if (this.pollInterval) clearInterval(this.pollInterval);
            this.pollInterval = null;
            this.refreshing = false;
            this.snackBar.open(job.errorMessage || 'Refresh failed', 'Close', { duration: 3000 });
          }
        },
        error: () => {
          if (this.pollInterval) clearInterval(this.pollInterval);
          this.pollInterval = null;
          this.refreshing = false;
          this.snackBar.open('Lost connection to crawl job', 'Close', { duration: 3000 });
        }
      });
    }, 2000);
  }

  private reloadAll(): void {
    this.http.get(`/api/tournaments/${this.id}`).subscribe({
      next: (t: any) => {
        this.tournament = t;
        if (t.totalRounds) {
          this.rounds = Array.from({ length: t.totalRounds }, (_, i) => i + 1);
        }
      }
    });
    this.loadPlayers();
    this.teams = [];
    this.pairings = [];
  }

  onTabChange(event: any): void {
    this.selectedTabIndex = event.index;
    const tabName = TournamentDetailComponent.TAB_NAMES[event.index];
    this.router.navigate([], { queryParams: tabName === 'players' ? {} : { tab: tabName }, queryParamsHandling: 'merge', replaceUrl: true });
    if (event.index === 1 && this.teams.length === 0) this.loadTeams();
    if (event.index === 2 && this.pairings.length === 0) this.loadPairings();
  }

  loadPlayers(): void {
    this.playersLoading = true;
    this.http.get<any[]>(`/api/tournaments/${this.id}/players`).subscribe({
      next: (p) => { this.players = p; this.playersLoading = false; },
      error: () => { this.playersLoading = false; }
    });
  }

  loadTeams(): void {
    this.teamsLoading = true;
    this.http.get<any[]>(`/api/tournaments/${this.id}/teams`).subscribe({
      next: (t) => { this.teams = t; this.teamsLoading = false; },
      error: () => { this.teamsLoading = false; }
    });
  }

  loadPairings(): void {
    this.pairingsLoading = true;
    this.http.get<any[]>(`/api/tournaments/${this.id}/pairings?round=${this.selectedRound}`).subscribe({
      next: (p) => {
        // Normalize: team pairings have homeTeam/awayTeam, individual have white/black
        this.pairings = p.map(item => {
          if (item.homeTeam !== undefined) {
            return { board: item.matchNumber, white: item.homeTeam, black: item.awayTeam, result: item.homeScore != null ? `${item.homeScore} : ${item.awayScore}` : '' };
          }
          return { ...item, board: item.boardNumber };
        });
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

  // --- Team/Individual detection ---

  get isTeamTournament(): boolean {
    return this.teams.length > 0;
  }

  // --- Favorites ---

  private get favKey(): string { return `favorites_${this.id}`; }

  private loadFavorites(): void {
    const stored = localStorage.getItem(this.favKey);
    this.favoriteSnrs = stored ? new Set(JSON.parse(stored)) : new Set();
  }

  private saveFavorites(): void {
    this.favoriteSnrs = new Set(this.favoriteSnrs);
    localStorage.setItem(this.favKey, JSON.stringify([...this.favoriteSnrs]));
  }

  get hasFavorites(): boolean {
    return this.favoriteSnrs.size > 0;
  }

  get subtitle(): string {
    return [this.tournament?.location, this.tournament?.date].filter(Boolean).join(' | ');
  }

  get favoriteNames(): Set<string> {
    const names = new Set<string>();
    for (const p of this.players) {
      if (this.favoriteSnrs.has(p.snr)) names.add(p.name);
    }
    return names;
  }

  get favoriteTeamNames(): Set<string> {
    const teamNames = new Set<string>();
    for (const p of this.players) {
      if (this.favoriteSnrs.has(p.snr) && p.teamName) teamNames.add(p.teamName);
    }
    return teamNames;
  }

  get displayedPlayers(): any[] {
    let data = this.showFavoritesOnly ? this.players.filter(p => this.favoriteSnrs.has(p.snr)) : this.players;
    return this.sortData(data, this.playerSort);
  }

  get displayedTeams(): any[] {
    let data = this.teams;
    if (this.showFavoritesOnly) {
      const favTeams = this.favoriteTeamNames;
      data = data.filter(t => favTeams.has(t.name));
    }
    return this.sortData(data, this.teamSort);
  }

  get displayedPairings(): any[] {
    let data = this.pairings;
    if (this.showFavoritesOnly) {
      if (this.isTeamTournament) {
        const favTeams = this.favoriteTeamNames;
        data = data.filter(p => favTeams.has(p.white) || favTeams.has(p.black));
      } else {
        const names = this.favoriteNames;
        data = data.filter(p => names.has(p.white) || names.has(p.black));
      }
    }
    return this.sortData(data, this.pairingSort);
  }

  isFavorite(player: any): boolean {
    return this.favoriteSnrs.has(player.snr);
  }

  toggleFavorite(player: any): void {
    if (this.favoriteSnrs.has(player.snr)) {
      this.favoriteSnrs.delete(player.snr);
      this.snackBar.open(`${player.name} removed from favorites`, 'Close', { duration: 1500 });
    } else {
      this.favoriteSnrs.add(player.snr);
      this.snackBar.open(`${player.name} added to favorites`, 'Close', { duration: 1500 });
    }
    this.saveFavorites();
  }

  // --- Team detail dialog ---

  showTeamPlayers(teamName: string): void {
    const team = this.teams.find(t => t.name === teamName);
    if (!team) return;
    this.http.get<any>(`/api/tournaments/${this.id}/teams/${team.snr}`).subscribe({
      next: (result) => {
        this.dialog.open(TeamPlayersDialogComponent, {
          data: { teamName: result.name, players: result.players || [] },
          width: '500px'
        });
      },
      error: () => {
        this.snackBar.open('Failed to load team details', 'Close', { duration: 3000 });
      }
    });
  }
}
