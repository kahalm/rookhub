import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AuthService } from '../../core/auth.service';
import { Subscription, Repertoire, Friend, PuzzleStatsDto } from '../../core/models';
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatListModule],
  template: `
    <div class="dashboard">
      <h1>Welcome, {{ auth.currentUser?.username }}!</h1>
      <div class="dashboard-grid">
        <mat-card>
          <mat-card-header>
            <mat-icon mat-card-avatar>library_books</mat-icon>
            <mat-card-title>Repertoires</mat-card-title>
            <mat-card-subtitle>{{ repertoireCount }} repertoires</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <button mat-button routerLink="/repertoires">View All</button>
          </mat-card-actions>
        </mat-card>

        <mat-card>
          <mat-card-header>
            <mat-icon mat-card-avatar>emoji_events</mat-icon>
            <mat-card-title>Tournament Subscriptions</mat-card-title>
            <mat-card-subtitle>{{ subscriptionCount }} subscriptions</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <button mat-button routerLink="/tournaments">Browse Tournaments</button>
          </mat-card-actions>
        </mat-card>

        <mat-card>
          <mat-card-header>
            <mat-icon mat-card-avatar>people</mat-icon>
            <mat-card-title>Friends</mat-card-title>
            <mat-card-subtitle>{{ friendCount }} friends</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <button mat-button routerLink="/friends">Manage Friends</button>
          </mat-card-actions>
        </mat-card>

        <mat-card>
          <mat-card-header>
            <mat-icon mat-card-avatar>extension</mat-icon>
            <mat-card-title>Puzzles</mat-card-title>
            <mat-card-subtitle>{{ puzzleSolved }} solved ({{ puzzleAccuracy }}%)</mat-card-subtitle>
          </mat-card-header>
          <mat-card-actions>
            <button mat-button routerLink="/puzzles">Solve Puzzles</button>
          </mat-card-actions>
        </mat-card>
      </div>

      @if (subscriptions.length > 0) {
        <h2>Subscribed Tournaments</h2>
        <mat-list>
          @for (sub of subscriptions; track sub.id) {
            <a mat-list-item [routerLink]="['/tournaments', sub.crawlerTournamentId]" class="tournament-link">
              <mat-icon matListItemIcon>emoji_events</mat-icon>
              <span matListItemTitle>{{ sub.tournamentName }}</span>
              <span matListItemLine>Subscribed {{ sub.subscribedAt | date }}</span>
            </a>
          }
        </mat-list>
      }
    </div>
  `,
  styles: [`
    .dashboard { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .dashboard-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 1rem; margin: 1rem 0; }
    mat-icon[mat-card-avatar] { font-size: 40px; width: 40px; height: 40px; }
    .tournament-link { cursor: pointer; text-decoration: none; color: inherit; }
    .tournament-link:hover { background: rgba(0,0,0,0.04); }
  `]
})
export class DashboardComponent implements OnInit {
  repertoireCount = 0;
  subscriptionCount = 0;
  friendCount = 0;
  puzzleSolved = 0;
  puzzleAccuracy = 0;
  subscriptions: Subscription[] = [];

  constructor(public auth: AuthService, private http: HttpClient) {}

  ngOnInit(): void {
    forkJoin({
      repertoires: this.http.get<Repertoire[]>('/api/repertoires').pipe(catchError(() => of([]))),
      subscriptions: this.http.get<Subscription[]>('/api/subscriptions').pipe(catchError(() => of([]))),
      friends: this.http.get<Friend[]>('/api/friends').pipe(catchError(() => of([]))),
      puzzleStats: this.http.get<PuzzleStatsDto>('/api/puzzles/stats').pipe(
        catchError(() => of({ totalAttempts: 0, solved: 0, accuracy: 0, currentStreak: 0, bestStreak: 0 }))
      )
    }).subscribe(({ repertoires, subscriptions, friends, puzzleStats }) => {
      this.repertoireCount = repertoires.length;
      this.subscriptions = subscriptions;
      this.subscriptionCount = subscriptions.length;
      this.friendCount = friends.length;
      this.puzzleSolved = puzzleStats.solved || 0;
      this.puzzleAccuracy = puzzleStats.accuracy || 0;
    });
  }
}
