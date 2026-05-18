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
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { FormsModule } from '@angular/forms';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-tournament-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatTabsModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatIconModule, MatSnackBarModule, MatProgressBarModule, LoadingSpinnerComponent],
  template: `
    @if (loading) {
      <app-loading-spinner />
    } @else if (tournament) {
      <div class="detail-container">
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ tournament.name }}</mat-card-title>
            <mat-card-subtitle>{{ tournament.location }} | {{ tournament.date }}</mat-card-subtitle>
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

        <mat-tab-group (selectedTabChange)="onTabChange($event)">
          <mat-tab label="Players ({{ players.length }})">
            @if (playersLoading) {
              <app-loading-spinner />
            } @else {
              <!-- Desktop: full table -->
              <div class="table-scroll desktop-only">
                <table mat-table [dataSource]="players" class="full-width">
                  <ng-container matColumnDef="snr">
                    <th mat-header-cell *matHeaderCellDef>Nr.</th>
                    <td mat-cell *matCellDef="let p">{{ p.snr }}</td>
                  </ng-container>
                  <ng-container matColumnDef="title">
                    <th mat-header-cell *matHeaderCellDef>Title</th>
                    <td mat-cell *matCellDef="let p">{{ p.title }}</td>
                  </ng-container>
                  <ng-container matColumnDef="name">
                    <th mat-header-cell *matHeaderCellDef>Name</th>
                    <td mat-cell *matCellDef="let p">{{ p.name }}</td>
                  </ng-container>
                  <ng-container matColumnDef="fideId">
                    <th mat-header-cell *matHeaderCellDef>FIDE ID</th>
                    <td mat-cell *matCellDef="let p">{{ p.fideId }}</td>
                  </ng-container>
                  <ng-container matColumnDef="elo">
                    <th mat-header-cell *matHeaderCellDef>Elo</th>
                    <td mat-cell *matCellDef="let p">{{ p.elo }}</td>
                  </ng-container>
                  <ng-container matColumnDef="country">
                    <th mat-header-cell *matHeaderCellDef>Country</th>
                    <td mat-cell *matCellDef="let p">{{ p.country }}</td>
                  </ng-container>
                  <ng-container matColumnDef="team">
                    <th mat-header-cell *matHeaderCellDef>Team</th>
                    <td mat-cell *matCellDef="let p">{{ p.teamName }}</td>
                  </ng-container>
                  <ng-container matColumnDef="board">
                    <th mat-header-cell *matHeaderCellDef>Br.</th>
                    <td mat-cell *matCellDef="let p">{{ p.boardNumber }}</td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="playerColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: playerColumns;"></tr>
                </table>
              </div>

              <!-- Mobile: card list -->
              <div class="mobile-only player-cards">
                @for (p of players; track p.snr) {
                  <div class="player-card">
                    <div class="player-main">
                      <span class="player-snr">{{ p.snr }}</span>
                      <span class="player-title" *ngIf="p.title">{{ p.title }}</span>
                      <span class="player-name">{{ p.name }}</span>
                    </div>
                    <div class="player-details">
                      @if (p.elo) { <span>{{ p.elo }}</span> }
                      @if (p.country) { <span>{{ p.country }}</span> }
                      @if (p.teamName) { <span>{{ p.teamName }}</span> }
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
              <div class="table-scroll">
                <table mat-table [dataSource]="teams" class="full-width">
                  <ng-container matColumnDef="rank">
                    <th mat-header-cell *matHeaderCellDef>Rank</th>
                    <td mat-cell *matCellDef="let t; let i = index">{{ i + 1 }}</td>
                  </ng-container>
                  <ng-container matColumnDef="name">
                    <th mat-header-cell *matHeaderCellDef>Team</th>
                    <td mat-cell *matCellDef="let t">{{ t.name }}</td>
                  </ng-container>
                  <ng-container matColumnDef="points">
                    <th mat-header-cell *matHeaderCellDef>Points</th>
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
            </div>
            @if (pairingsLoading) {
              <app-loading-spinner />
            } @else {
              <div class="table-scroll">
                <table mat-table [dataSource]="pairings" class="full-width">
                  <ng-container matColumnDef="board">
                    <th mat-header-cell *matHeaderCellDef>Board</th>
                    <td mat-cell *matCellDef="let p; let i = index">{{ i + 1 }}</td>
                  </ng-container>
                  <ng-container matColumnDef="white">
                    <th mat-header-cell *matHeaderCellDef>White</th>
                    <td mat-cell *matCellDef="let p">{{ p.white }}</td>
                  </ng-container>
                  <ng-container matColumnDef="result">
                    <th mat-header-cell *matHeaderCellDef>Result</th>
                    <td mat-cell *matCellDef="let p">{{ p.result }}</td>
                  </ng-container>
                  <ng-container matColumnDef="black">
                    <th mat-header-cell *matHeaderCellDef>Black</th>
                    <td mat-cell *matCellDef="let p">{{ p.black }}</td>
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
    .round-selector { padding: 1rem 0; }
    mat-card { margin-bottom: 1rem; }
    .action-bar { display: flex; flex-wrap: wrap; gap: 0.5rem; }

    /* Mobile card list for players */
    .mobile-only { display: none; }
    .player-cards { padding: 0.5rem 0; }
    .player-card {
      padding: 0.75rem 1rem;
      border-bottom: 1px solid rgba(0,0,0,0.08);
    }
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
    }
  `]
})
export class TournamentDetailComponent implements OnInit {
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

  playerColumns = ['snr', 'title', 'name', 'fideId', 'elo', 'country', 'team', 'board'];
  teamColumns = ['rank', 'name', 'points'];
  pairingColumns = ['board', 'white', 'result', 'black'];

  subscription: any = null;
  toggling = false;
  refreshing = false;

  private id!: string;

  constructor(private route: ActivatedRoute, private http: HttpClient, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id')!;
    this.http.get(`/api/tournaments/${this.id}`).subscribe({
      next: (t: any) => {
        this.tournament = t;
        this.loading = false;
        if (t.roundCount) {
          this.rounds = Array.from({ length: t.roundCount }, (_, i) => i + 1);
        }
        this.loadPlayers();
        this.loadTeams();
      },
      error: () => { this.loading = false; }
    });
    this.loadSubscription();
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
    const interval = setInterval(() => {
      this.http.get<any>(`/api/tournaments/crawl/${jobId}`).subscribe({
        next: (job) => {
          if (job.status === 'Completed') {
            clearInterval(interval);
            this.refreshing = false;
            this.snackBar.open('Data refreshed!', 'Close', { duration: 2000 });
            this.reloadAll();
          } else if (job.status === 'Failed') {
            clearInterval(interval);
            this.refreshing = false;
            this.snackBar.open(job.errorMessage || 'Refresh failed', 'Close', { duration: 3000 });
          }
        },
        error: () => {
          clearInterval(interval);
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
        if (t.roundCount) {
          this.rounds = Array.from({ length: t.roundCount }, (_, i) => i + 1);
        }
      }
    });
    this.loadPlayers();
    this.teams = [];
    this.pairings = [];
  }

  onTabChange(event: any): void {
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
      next: (p) => { this.pairings = p; this.pairingsLoading = false; },
      error: () => { this.pairingsLoading = false; }
    });
  }
}
