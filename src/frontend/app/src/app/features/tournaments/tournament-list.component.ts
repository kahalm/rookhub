import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { FormsModule } from '@angular/forms';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { forkJoin } from 'rxjs';

@Component({
  selector: 'app-tournament-list',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatSnackBarModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, LoadingSpinnerComponent],
  template: `
    <div class="tournament-container">
      <h1>Tournaments</h1>

      <mat-card class="crawl-card">
        <mat-card-header>
          <mat-card-title>Import Tournament</mat-card-title>
          <mat-card-subtitle>Enter a chess-results.com tournament ID to crawl it</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div class="crawl-form">
            <mat-form-field appearance="outline">
              <mat-label>Chess-Results ID</mat-label>
              <input matInput [(ngModel)]="crawlId" placeholder="e.g. 1234567" (keyup.enter)="startCrawl()">
              <mat-hint>The number from chess-results.com/tnr<strong>1234567</strong>.aspx</mat-hint>
            </mat-form-field>
            <button mat-raised-button color="primary" (click)="startCrawl()" [disabled]="crawling || !crawlId.trim()">
              <mat-icon>download</mat-icon> Crawl
            </button>
          </div>
          @if (crawling) {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
            <p class="crawl-status">Crawling tournament {{ crawlId }}... Job #{{ crawlJobId }}</p>
          }
          @if (crawlError) {
            <p class="crawl-error">{{ crawlError }}</p>
          }
        </mat-card-content>
      </mat-card>

      @if (loading) {
        <app-loading-spinner />
      } @else if (error) {
        <mat-card>
          <mat-card-content>
            <p>Could not connect to tournament crawler. Make sure the crawler service is running.</p>
            <button mat-raised-button color="primary" (click)="loadTournaments()">Retry</button>
          </mat-card-content>
        </mat-card>
      } @else {
        <div class="tournament-grid">
          @for (t of visibleTournaments; track t.id) {
            <mat-card>
              <mat-card-header>
                <mat-card-title>{{ t.name }}</mat-card-title>
                <mat-card-subtitle>{{ t.location }} | {{ t.date }}</mat-card-subtitle>
              </mat-card-header>
              <mat-card-actions>
                <button mat-button [routerLink]="['/tournaments', t.id]">Details</button>
                @if (isSubscribed(t)) {
                  <button mat-button color="warn" (click)="unsubscribe(t)" [disabled]="togglingId === t.id">
                    <mat-icon>notifications_off</mat-icon> Unsubscribe
                  </button>
                } @else {
                  <button mat-button color="primary" (click)="subscribe(t)" [disabled]="togglingId === t.id">
                    <mat-icon>notifications</mat-icon> Subscribe
                  </button>
                }
                <button mat-button (click)="hide(t)">
                  <mat-icon>visibility_off</mat-icon> Hide
                </button>
              </mat-card-actions>
            </mat-card>
          } @empty {
            <p>No tournaments found.</p>
          }
          @if (hiddenCount > 0) {
            <button mat-button (click)="showAll()">
              <mat-icon>visibility</mat-icon> Show {{ hiddenCount }} hidden
            </button>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .tournament-container { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .crawl-card { margin-bottom: 2rem; }
    .crawl-form { display: flex; align-items: flex-start; gap: 1rem; }
    .crawl-form mat-form-field { flex: 1; max-width: 400px; }
    .crawl-form button { margin-top: 4px; }
    .crawl-status { margin-top: 0.5rem; color: #666; font-style: italic; }
    .crawl-error { margin-top: 0.5rem; color: #f44336; }
    .tournament-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(350px, 1fr)); gap: 1rem; }
  `]
})
export class TournamentListComponent implements OnInit {
  tournaments: any[] = [];
  subscriptions: any[] = [];
  loading = true;
  error = false;
  togglingId: string | null = null;
  crawlId = '';
  crawling = false;
  crawlJobId: number | null = null;
  crawlError = '';
  private hiddenIds: Set<string> = new Set();

  get visibleTournaments(): any[] {
    return this.tournaments.filter(t => !this.hiddenIds.has(t.id?.toString()));
  }

  get hiddenCount(): number {
    return this.tournaments.length - this.visibleTournaments.length;
  }

  constructor(private http: HttpClient, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    const stored = localStorage.getItem('hiddenTournaments');
    if (stored) this.hiddenIds = new Set(JSON.parse(stored));
    this.loadTournaments();
  }

  hide(tournament: any): void {
    this.hiddenIds.add(tournament.id?.toString());
    localStorage.setItem('hiddenTournaments', JSON.stringify([...this.hiddenIds]));
  }

  showAll(): void {
    this.hiddenIds.clear();
    localStorage.removeItem('hiddenTournaments');
  }

  loadTournaments(): void {
    this.loading = true;
    this.error = false;
    forkJoin({
      tournaments: this.http.get<any[]>('/api/tournaments'),
      subscriptions: this.http.get<any[]>('/api/subscriptions')
    }).subscribe({
      next: ({ tournaments, subscriptions }) => {
        this.tournaments = tournaments;
        this.subscriptions = subscriptions;
        this.loading = false;
      },
      error: () => { this.loading = false; this.error = true; }
    });
  }

  isSubscribed(tournament: any): boolean {
    const tid = tournament.id?.toString();
    return this.subscriptions.some(s => s.crawlerTournamentId === tid);
  }

  getSubscription(tournament: any): any {
    const tid = tournament.id?.toString();
    return this.subscriptions.find(s => s.crawlerTournamentId === tid);
  }

  subscribe(tournament: any): void {
    this.togglingId = tournament.id;
    this.http.post<any>('/api/subscriptions', {
      crawlerTournamentId: tournament.id?.toString() ?? '',
      tournamentName: tournament.name ?? ''
    }).subscribe({
      next: (sub) => {
        this.subscriptions.push(sub);
        this.togglingId = null;
        this.snackBar.open('Subscribed!', 'Close', { duration: 2000 });
      },
      error: (err) => {
        this.togglingId = null;
        this.snackBar.open(err.error?.message || 'Failed', 'Close', { duration: 3000 });
      }
    });
  }

  startCrawl(): void {
    let id = this.crawlId.trim();
    if (!id) return;
    // Strip tnr prefix and URL parts - user might paste full URL or "tnr1234567"
    id = id.replace(/.*tnr/i, '').replace(/\..*/g, '');
    this.crawling = true;
    this.crawlError = '';
    this.crawlJobId = null;
    this.http.post<any>('/api/tournaments/crawl', {
      chessResultsId: id,
      jobType: 'Full'
    }).subscribe({
      next: (job) => {
        this.crawlJobId = job.id;
        this.pollCrawlJob(job.id);
      },
      error: (err) => {
        this.crawling = false;
        this.crawlError = err.error?.message || err.error?.detail || 'Failed to start crawl';
      }
    });
  }

  private pollCrawlJob(jobId: number): void {
    const interval = setInterval(() => {
      this.http.get<any>(`/api/tournaments/crawl/${jobId}`).subscribe({
        next: (job) => {
          if (job.status === 'Completed') {
            clearInterval(interval);
            this.crawling = false;
            this.crawlId = '';
            this.snackBar.open('Tournament imported!', 'Close', { duration: 3000 });
            this.loadTournaments();
          } else if (job.status === 'Failed') {
            clearInterval(interval);
            this.crawling = false;
            this.crawlError = job.errorMessage || 'Crawl failed';
          }
        },
        error: () => {
          clearInterval(interval);
          this.crawling = false;
          this.crawlError = 'Lost connection to crawl job';
        }
      });
    }, 2000);
  }

  unsubscribe(tournament: any): void {
    const sub = this.getSubscription(tournament);
    if (!sub) return;
    this.togglingId = tournament.id;
    this.http.delete(`/api/subscriptions/${sub.id}`).subscribe({
      next: () => {
        this.subscriptions = this.subscriptions.filter(s => s.id !== sub.id);
        this.togglingId = null;
        this.snackBar.open('Unsubscribed', 'Close', { duration: 2000 });
      },
      error: () => {
        this.togglingId = null;
        this.snackBar.open('Failed to unsubscribe', 'Close', { duration: 3000 });
      }
    });
  }
}
