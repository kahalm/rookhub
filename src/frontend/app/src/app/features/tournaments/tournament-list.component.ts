import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { SnackbarService } from '../../core/snackbar.service';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { FormsModule } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { forkJoin } from 'rxjs';
import { Tournament, Subscription } from '../../core/models';

@Component({
  selector: 'app-tournament-list',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, TranslateModule, LoadingSpinnerComponent],
  template: `
    <div class="tournament-container">
      <h1>{{ 'tournaments.list.title' | translate }}</h1>

      <mat-card class="crawl-card">
        <mat-card-header>
          <mat-card-title>{{ 'tournaments.list.importTitle' | translate }}</mat-card-title>
          <mat-card-subtitle>{{ 'tournaments.list.importSubtitle' | translate }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div class="crawl-form">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'tournaments.list.chessResultsId' | translate }}</mat-label>
              <input matInput [(ngModel)]="crawlId" [placeholder]="'tournaments.list.idPlaceholder' | translate" (keyup.enter)="startCrawl()">
              <mat-hint>{{ 'tournaments.list.idHintPrefix' | translate }}<strong>1234567</strong>{{ 'tournaments.list.idHintSuffix' | translate }}</mat-hint>
            </mat-form-field>
            <button mat-raised-button color="primary" (click)="startCrawl()" [disabled]="crawling || !crawlId.trim()">
              <mat-icon>download</mat-icon> {{ 'tournaments.list.crawl' | translate }}
            </button>
          </div>
          @if (crawling) {
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
            <p class="crawl-status">{{ 'tournaments.list.crawling' | translate:{ id: crawlId, jobId: crawlJobId } }}</p>
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
            <p>{{ 'tournaments.list.crawlerUnavailable' | translate }}</p>
            <button mat-raised-button color="primary" (click)="loadTournaments()">{{ 'common.retry' | translate }}</button>
          </mat-card-content>
        </mat-card>
      } @else {
        <div class="tournament-grid">
          @for (t of visibleTournaments; track t.id) {
            <mat-card>
              <mat-card-header>
                <mat-card-title>{{ t.name }}</mat-card-title>
                <mat-card-subtitle>{{ formatSubtitle(t) }}</mat-card-subtitle>
              </mat-card-header>
              <mat-card-actions>
                <button mat-button [routerLink]="['/tournaments', t.id]">{{ 'tournaments.list.details' | translate }}</button>
                @if (isSubscribed(t)) {
                  <button mat-button color="warn" (click)="unsubscribe(t)" [disabled]="togglingId === t.id">
                    <mat-icon>notifications_off</mat-icon> {{ 'tournaments.actions.unsubscribe' | translate }}
                  </button>
                } @else {
                  <button mat-button color="primary" (click)="subscribe(t)" [disabled]="togglingId === t.id">
                    <mat-icon>notifications</mat-icon> {{ 'tournaments.actions.subscribe' | translate }}
                  </button>
                }
                <button mat-button (click)="hide(t)">
                  <mat-icon>visibility_off</mat-icon> {{ 'tournaments.list.hide' | translate }}
                </button>
              </mat-card-actions>
            </mat-card>
          } @empty {
            <p>{{ 'tournaments.list.empty' | translate }}</p>
          }
          @if (hiddenCount > 0) {
            <button mat-button (click)="showAll()">
              <mat-icon>visibility</mat-icon> {{ 'tournaments.list.showHidden' | translate:{ count: hiddenCount } }}
            </button>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .tournament-container { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .crawl-card { margin-bottom: 2rem; }
    .crawl-form { display: flex; align-items: flex-start; gap: 1rem; flex-wrap: wrap; }
    .crawl-form mat-form-field { flex: 1; min-width: 0; max-width: 400px; }
    .crawl-form button { margin-top: 4px; }
    .crawl-status { margin-top: 0.5rem; color: color-mix(in srgb, currentColor 60%, transparent); font-style: italic; }
    .crawl-error { margin-top: 0.5rem; color: #f44336; }
    .tournament-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(350px, 100%), 1fr)); gap: 1rem; }
    @media (max-width: 768px) {
      .tournament-container { padding: 0.75rem; }
      h1 { font-size: 1.4rem; }
      .crawl-card { margin-bottom: 1rem; }
      .crawl-form mat-form-field { max-width: none; }
    }
  `]
})
export class TournamentListComponent implements OnInit, OnDestroy {
  tournaments: Tournament[] = [];
  subscriptions: Subscription[] = [];
  loading = true;
  error = false;
  togglingId: number | null = null;
  crawlId = '';
  crawling = false;
  crawlJobId: number | null = null;
  crawlError = '';
  private hiddenIds: Set<string> = new Set();
  private pollInterval: ReturnType<typeof setInterval> | null = null;

  get visibleTournaments(): Tournament[] {
    return this.tournaments.filter(t => !this.hiddenIds.has(t.id?.toString()));
  }

  get hiddenCount(): number {
    return this.tournaments.length - this.visibleTournaments.length;
  }

  constructor(private http: HttpClient, private snackbar: SnackbarService, private translate: TranslateService) {}

  formatSubtitle(t: Tournament): string {
    return [t.location, t.date].filter((v) => !!v).join(' | ');
  }

  ngOnInit(): void {
    const stored = localStorage.getItem('hiddenTournaments');
    if (stored) this.hiddenIds = new Set(JSON.parse(stored));
    this.loadTournaments();
  }

  ngOnDestroy(): void {
    if (this.pollInterval) clearInterval(this.pollInterval);
  }

  hide(tournament: Tournament): void {
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
      tournamentResponse: this.http.get<{ items: Tournament[]; totalCount: number }>('/api/tournaments?pageSize=200'),
      subscriptions: this.http.get<Subscription[]>('/api/subscriptions')
    }).subscribe({
      next: ({ tournamentResponse, subscriptions }) => {
        this.tournaments = tournamentResponse.items ?? [];
        this.subscriptions = subscriptions;
        this.loading = false;
      },
      error: () => { this.loading = false; this.error = true; }
    });
  }

  isSubscribed(tournament: Tournament): boolean {
    const tid = tournament.id?.toString();
    return this.subscriptions.some(s => s.crawlerTournamentId === tid);
  }

  getSubscription(tournament: Tournament): Subscription | undefined {
    const tid = tournament.id?.toString();
    return this.subscriptions.find(s => s.crawlerTournamentId === tid);
  }

  subscribe(tournament: Tournament): void {
    this.togglingId = tournament.id;
    this.http.post<Subscription>('/api/subscriptions', {
      crawlerTournamentId: tournament.id?.toString() ?? '',
      tournamentName: tournament.name ?? ''
    }).subscribe({
      next: (sub) => {
        this.subscriptions.push(sub);
        this.togglingId = null;
        this.snackbar.success(this.translate.instant('tournaments.actions.subscribed'));
      },
      error: (err) => {
        this.togglingId = null;
        this.snackbar.info(err.error?.message || this.translate.instant('tournaments.actions.failed'));
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
        this.crawlError = err.error?.message || err.error?.detail || this.translate.instant('tournaments.list.crawlStartFailed');
      }
    });
  }

  private pollCrawlJob(jobId: number): void {
    this.pollInterval = setInterval(() => {
      this.http.get<any>(`/api/tournaments/crawl/${jobId}`).subscribe({
        next: (job) => {
          if (job.status === 'Completed') {
            if (this.pollInterval) clearInterval(this.pollInterval);
            this.pollInterval = null;
            this.crawling = false;
            this.crawlId = '';
            this.snackbar.info(this.translate.instant('tournaments.list.imported'));
            this.loadTournaments();
          } else if (job.status === 'Failed') {
            if (this.pollInterval) clearInterval(this.pollInterval);
            this.pollInterval = null;
            this.crawling = false;
            this.crawlError = job.errorMessage || this.translate.instant('tournaments.list.crawlFailed');
          }
        },
        error: () => {
          if (this.pollInterval) clearInterval(this.pollInterval);
          this.pollInterval = null;
          this.crawling = false;
          this.crawlError = this.translate.instant('tournaments.list.crawlConnectionLost');
        }
      });
    }, 2000);
  }

  unsubscribe(tournament: Tournament): void {
    const sub = this.getSubscription(tournament);
    if (!sub) return;
    this.togglingId = tournament.id;
    this.http.delete(`/api/subscriptions/${sub.id}`).subscribe({
      next: () => {
        this.subscriptions = this.subscriptions.filter(s => s.id !== sub.id);
        this.togglingId = null;
        this.snackbar.success(this.translate.instant('tournaments.actions.unsubscribed'));
      },
      error: () => {
        this.togglingId = null;
        this.snackbar.info(this.translate.instant('tournaments.actions.unsubscribeFailed'));
      }
    });
  }
}
