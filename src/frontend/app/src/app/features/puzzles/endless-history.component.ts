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
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { SelectionModel } from '@angular/cdk/collections';

interface EndlessSessionDto {
  id: number;
  timestamp: number;
  totalSolved: number;
  maxRating: number;
  durationSeconds: number;
  configJson: string;
  mistakeAtRatings: string;
  isArchived: boolean;
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
    MatTableModule, MatPaginatorModule, MatProgressSpinnerModule,
    MatCheckboxModule, MatButtonToggleModule
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
          } @else if (totalCount === 0 && archiveFilter === null) {
            <div class="empty-state">
              <mat-icon class="empty-icon">sports_esports</mat-icon>
              <p>No sessions played yet.</p>
              <button mat-raised-button color="primary" (click)="router.navigate(['/puzzles/endless'])">
                <mat-icon>play_arrow</mat-icon> Start Playing
              </button>
            </div>
          } @else {
            <div class="toolbar">
              <mat-button-toggle-group [value]="archiveFilterValue" (change)="onFilterChange($event.value)">
                <mat-button-toggle value="all">All</mat-button-toggle>
                <mat-button-toggle value="active">Active</mat-button-toggle>
                <mat-button-toggle value="archived">Archived</mat-button-toggle>
              </mat-button-toggle-group>

              @if (selection.hasValue()) {
                <div class="bulk-actions">
                  <span class="selection-count">{{ selection.selected.length }} selected</span>
                  @if (hasUnarchived()) {
                    <button mat-raised-button (click)="archiveSelected(true)">
                      <mat-icon>archive</mat-icon> Archive
                    </button>
                  }
                  @if (hasArchived()) {
                    <button mat-raised-button (click)="archiveSelected(false)">
                      <mat-icon>unarchive</mat-icon> Unarchive
                    </button>
                  }
                </div>
              }
            </div>

            @if (totalCount === 0) {
              <div class="empty-state">
                <p>No {{ archiveFilter === true ? 'archived' : 'active' }} sessions.</p>
              </div>
            } @else {
              <table mat-table [dataSource]="sessions" class="history-table">
                <ng-container matColumnDef="select">
                  <th mat-header-cell *matHeaderCellDef>
                    <mat-checkbox
                      (change)="$event ? toggleAllRows() : null"
                      [checked]="selection.hasValue() && isAllSelected()"
                      [indeterminate]="selection.hasValue() && !isAllSelected()">
                    </mat-checkbox>
                  </th>
                  <td mat-cell *matCellDef="let s">
                    <mat-checkbox
                      (click)="$event.stopPropagation()"
                      (change)="$event ? selection.toggle(s) : null"
                      [checked]="selection.isSelected(s)">
                    </mat-checkbox>
                  </td>
                </ng-container>
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
                <tr mat-row *matRowDef="let row; columns: displayedColumns;"
                    [class.archived-row]="row.isArchived"></tr>
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
    .toolbar { display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .bulk-actions { display: flex; align-items: center; gap: 0.5rem; }
    .selection-count { font-size: 0.875rem; color: rgba(0,0,0,0.6); }
    .archived-row { opacity: 0.5; }
  `]
})
export class EndlessHistoryComponent implements OnInit {
  sessions: EndlessSessionDto[] = [];
  totalCount = 0;
  page = 1;
  pageSize = 20;
  loading = true;
  archiveFilter: boolean | null = null;
  displayedColumns = ['select', 'date', 'maxRating', 'solved', 'duration', 'config', 'mistakes'];
  selection = new SelectionModel<EndlessSessionDto>(true, []);

  constructor(private http: HttpClient, public router: Router) {}

  get archiveFilterValue(): string {
    if (this.archiveFilter === null) return 'all';
    return this.archiveFilter ? 'archived' : 'active';
  }

  ngOnInit(): void {
    this.loadPage();
  }

  loadPage(): void {
    this.loading = true;
    this.selection.clear();
    const params: Record<string, string> = {
      page: this.page.toString(),
      pageSize: this.pageSize.toString()
    };
    if (this.archiveFilter !== null) {
      params['archived'] = this.archiveFilter.toString();
    }
    this.http.get<EndlessHistoryResponse>('/api/endless/history', { params }).subscribe({
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

  onFilterChange(value: string): void {
    this.archiveFilter = value === 'all' ? null : value === 'archived';
    this.page = 1;
    this.loadPage();
  }

  isAllSelected(): boolean {
    return this.selection.selected.length === this.sessions.length;
  }

  toggleAllRows(): void {
    if (this.isAllSelected()) {
      this.selection.clear();
    } else {
      this.selection.select(...this.sessions);
    }
  }

  hasArchived(): boolean {
    return this.selection.selected.some(s => s.isArchived);
  }

  hasUnarchived(): boolean {
    return this.selection.selected.some(s => !s.isArchived);
  }

  archiveSelected(archive: boolean): void {
    const ids = this.selection.selected
      .filter(s => s.isArchived !== archive)
      .map(s => s.id);
    if (ids.length === 0) return;

    this.http.post('/api/endless/archive', { sessionIds: ids, archive }).subscribe({
      next: () => this.loadPage()
    });
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
