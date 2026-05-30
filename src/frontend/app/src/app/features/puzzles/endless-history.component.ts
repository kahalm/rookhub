import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

interface EndlessSessionDto {
  id: number;
  timestamp: number;
  totalSolved: number;
  maxRating: number;
  durationSeconds: number;
  configJson: string;
  mistakeAtRatings: string;
}

interface EndlessHistoryResponse {
  items: EndlessSessionDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

@Component({
  selector: 'app-endless-history',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule,
    MatTableModule, MatPaginatorModule, MatProgressSpinnerModule
  ],
  template: `
    <div class="history-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>
            <mat-icon>history</mat-icon>
            Endless Puzzle History
          </mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (loading) {
            <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
          } @else if (totalCount === 0) {
            <div class="empty-state">
              <mat-icon class="empty-icon">sports_esports</mat-icon>
              <p>No sessions played yet.</p>
              <button mat-raised-button color="primary" (click)="router.navigate(['/puzzles/endless'])">
                <mat-icon>play_arrow</mat-icon> Start Playing
              </button>
            </div>
          } @else {
            <table mat-table [dataSource]="sessions" class="history-table">
              <ng-container matColumnDef="date">
                <th mat-header-cell *matHeaderCellDef>Date</th>
                <td mat-cell *matCellDef="let s">{{ formatDate(s.timestamp) }}</td>
              </ng-container>
              <ng-container matColumnDef="maxRating">
                <th mat-header-cell *matHeaderCellDef>Max Rating</th>
                <td mat-cell *matCellDef="let s">{{ s.maxRating }}</td>
              </ng-container>
              <ng-container matColumnDef="solved">
                <th mat-header-cell *matHeaderCellDef>Solved</th>
                <td mat-cell *matCellDef="let s">{{ s.totalSolved }}</td>
              </ng-container>
              <ng-container matColumnDef="duration">
                <th mat-header-cell *matHeaderCellDef>Duration</th>
                <td mat-cell *matCellDef="let s">{{ formatDuration(s.durationSeconds) }}</td>
              </ng-container>
              <ng-container matColumnDef="config">
                <th mat-header-cell *matHeaderCellDef>Config</th>
                <td mat-cell *matCellDef="let s">{{ formatConfig(s.configJson) }}</td>
              </ng-container>
              <ng-container matColumnDef="mistakes">
                <th mat-header-cell *matHeaderCellDef>Mistakes</th>
                <td mat-cell *matCellDef="let s">{{ formatMistakes(s.mistakeAtRatings) }}</td>
              </ng-container>
              <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
            </table>
            <mat-paginator
              [length]="totalCount"
              [pageSize]="pageSize"
              [pageIndex]="page - 1"
              [pageSizeOptions]="[10, 20, 50]"
              (page)="onPageChange($event)"
              showFirstLastButtons>
            </mat-paginator>
          }
        </mat-card-content>
      </mat-card>

      <div class="back-link">
        <button mat-button (click)="router.navigate(['/puzzles/endless'])">
          <mat-icon>arrow_back</mat-icon> Back to Endless Mode
        </button>
      </div>
    </div>
  `,
  styles: [`
    .history-container { max-width: 900px; margin: 2rem auto; padding: 0 1rem; }
    mat-card-title { display: flex; align-items: center; gap: 0.5rem; }
    .loading { display: flex; justify-content: center; padding: 3rem 0; }
    .empty-state { text-align: center; padding: 3rem 1rem; }
    .empty-icon { font-size: 48px; width: 48px; height: 48px; color: rgba(0,0,0,0.3); }
    .empty-state p { color: rgba(0,0,0,0.5); margin: 1rem 0; }
    .history-table { width: 100%; }
    .back-link { margin-top: 1rem; }
    th.mat-mdc-header-cell { font-weight: 600; }
  `]
})
export class EndlessHistoryComponent implements OnInit {
  sessions: EndlessSessionDto[] = [];
  totalCount = 0;
  page = 1;
  pageSize = 20;
  loading = true;
  displayedColumns = ['date', 'maxRating', 'solved', 'duration', 'config', 'mistakes'];

  constructor(private http: HttpClient, public router: Router) {}

  ngOnInit(): void {
    this.loadPage();
  }

  loadPage(): void {
    this.loading = true;
    this.http.get<EndlessHistoryResponse>(`/api/endless/history`, {
      params: { page: this.page.toString(), pageSize: this.pageSize.toString() }
    }).subscribe({
      next: (res) => {
        this.sessions = res.items;
        this.totalCount = res.totalCount;
        this.page = res.page;
        this.pageSize = res.pageSize;
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  onPageChange(event: PageEvent): void {
    this.page = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  formatDate(timestamp: number): string {
    const d = new Date(timestamp);
    if (isNaN(d.getTime())) return '-';
    return d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })
      + ' ' + d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
  }

  formatDuration(seconds: number): string {
    if (!seconds) return '-';
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return m > 0 ? `${m}m ${s}s` : `${s}s`;
  }

  formatConfig(configJson: string): string {
    try {
      const c = JSON.parse(configJson);
      return `${c.startElo ?? '?'} / +${c.step ?? '?'}`;
    } catch {
      return '-';
    }
  }

  formatMistakes(mistakeAtRatings: string): string {
    if (!mistakeAtRatings) return '-';
    const ratings = mistakeAtRatings.split(',').filter(r => r.trim());
    return ratings.length > 0 ? ratings.join(', ') : '-';
  }
}
