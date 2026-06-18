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
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SelectionModel } from '@angular/cdk/collections';
import { LocaleService } from '../../core/locale.service';

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
    MatCheckboxModule, MatButtonToggleModule, TranslateModule
  ],
  template: `
    <div class="history-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>
            <mat-icon>history</mat-icon>
            {{ 'endless.history.title' | translate }}
          </mat-card-title>
        </mat-card-header>
        <mat-card-content>
          @if (loading) {
            <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
          } @else if (totalCount === 0 && archiveFilter === null) {
            <div class="empty-state">
              <mat-icon class="empty-icon">sports_esports</mat-icon>
              <p>{{ 'endless.history.noSessions' | translate }}</p>
              <button mat-raised-button color="primary" (click)="router.navigate(['/puzzles/endless'])">
                <mat-icon>play_arrow</mat-icon> {{ 'endless.history.startPlaying' | translate }}
              </button>
            </div>
          } @else {
            <div class="toolbar">
              <mat-button-toggle-group [value]="archiveFilterValue" (change)="onFilterChange($event.value)">
                <mat-button-toggle value="all">{{ 'endless.history.filterAll' | translate }}</mat-button-toggle>
                <mat-button-toggle value="active">{{ 'endless.history.filterActive' | translate }}</mat-button-toggle>
                <mat-button-toggle value="archived">{{ 'endless.history.filterArchived' | translate }}</mat-button-toggle>
              </mat-button-toggle-group>

              @if (selection.hasValue()) {
                <div class="bulk-actions">
                  <span class="selection-count">{{ 'endless.history.selected' | translate:{ count: selection.selected.length } }}</span>
                  @if (hasUnarchived()) {
                    <button mat-raised-button (click)="archiveSelected(true)">
                      <mat-icon>archive</mat-icon> {{ 'endless.history.archive' | translate }}
                    </button>
                  }
                  @if (hasArchived()) {
                    <button mat-raised-button (click)="archiveSelected(false)">
                      <mat-icon>unarchive</mat-icon> {{ 'endless.history.unarchive' | translate }}
                    </button>
                  }
                </div>
              }
            </div>

            @if (totalCount === 0) {
              <div class="empty-state">
                <p>{{ (archiveFilter === true ? 'endless.history.noArchivedSessions' : 'endless.history.noActiveSessions') | translate }}</p>
              </div>
            } @else {
              <!-- Desktop table -->
              <div class="desktop-only">
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
                    <th mat-header-cell *matHeaderCellDef>{{ 'endless.history.colDate' | translate }}</th>
                    <td mat-cell *matCellDef="let s">{{ formatDate(s.timestamp) }}</td>
                  </ng-container>
                  <ng-container matColumnDef="maxRating">
                    <th mat-header-cell *matHeaderCellDef>{{ 'endless.history.colMaxRating' | translate }}</th>
                    <td mat-cell *matCellDef="let s">{{ s.maxRating }}</td>
                  </ng-container>
                  <ng-container matColumnDef="elo">
                    <th mat-header-cell *matHeaderCellDef>{{ 'endless.history.colEloDelta' | translate }}</th>
                    <td mat-cell *matCellDef="let s" [class]="eloDeltaClass(s)">{{ formatEloDelta(s) }}</td>
                  </ng-container>
                  <ng-container matColumnDef="solved">
                    <th mat-header-cell *matHeaderCellDef>{{ 'endless.history.colSolved' | translate }}</th>
                    <td mat-cell *matCellDef="let s">{{ s.totalSolved }}</td>
                  </ng-container>
                  <ng-container matColumnDef="duration">
                    <th mat-header-cell *matHeaderCellDef>{{ 'endless.history.colDuration' | translate }}</th>
                    <td mat-cell *matCellDef="let s">{{ formatDuration(s.durationSeconds) }}</td>
                  </ng-container>
                  <ng-container matColumnDef="config">
                    <th mat-header-cell *matHeaderCellDef>{{ 'endless.history.colConfig' | translate }}</th>
                    <td mat-cell *matCellDef="let s">{{ formatConfig(s.configJson) }}</td>
                  </ng-container>
                  <ng-container matColumnDef="mistakes">
                    <th mat-header-cell *matHeaderCellDef>{{ 'endless.history.colMistakes' | translate }}</th>
                    <td mat-cell *matCellDef="let s">{{ formatMistakes(s.mistakeAtRatings) }}</td>
                  </ng-container>
                  <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: displayedColumns;"
                      class="clickable-row"
                      [class.archived-row]="row.isArchived"
                      (click)="openSession(row)"></tr>
                </table>
              </div>

              <!-- Mobile cards -->
              <div class="mobile-only">
                @for (s of sessions; track s.id) {
                  <div class="session-card clickable-row" [class.archived-row]="s.isArchived" (click)="openSession(s)">
                    <div class="session-card-header">
                      <mat-checkbox
                        (click)="$event.stopPropagation()"
                        (change)="$event ? selection.toggle(s) : null"
                        [checked]="selection.isSelected(s)">
                      </mat-checkbox>
                      <span class="session-date">{{ formatDate(s.timestamp) }}</span>
                      @if (s.isArchived) {
                        <mat-icon class="archived-icon">archive</mat-icon>
                      }
                    </div>
                    <div class="session-card-stats">
                      <div class="session-stat">
                        <span class="session-stat-value">{{ s.maxRating }}</span>
                        <span class="session-stat-label">{{ 'endless.history.colMaxRating' | translate }}</span>
                      </div>
                      <div class="session-stat">
                        <span class="session-stat-value" [class]="eloDeltaClass(s)">{{ formatEloDelta(s) }}</span>
                        <span class="session-stat-label">{{ 'endless.history.colEloDelta' | translate }}</span>
                      </div>
                      <div class="session-stat">
                        <span class="session-stat-value">{{ s.totalSolved }}</span>
                        <span class="session-stat-label">{{ 'endless.history.colSolved' | translate }}</span>
                      </div>
                      <div class="session-stat">
                        <span class="session-stat-value">{{ formatDuration(s.durationSeconds) }}</span>
                        <span class="session-stat-label">{{ 'endless.history.colDuration' | translate }}</span>
                      </div>
                      <div class="session-stat">
                        <span class="session-stat-value">{{ formatConfig(s.configJson) }}</span>
                        <span class="session-stat-label">{{ 'endless.history.colConfig' | translate }}</span>
                      </div>
                    </div>
                    @if (formatMistakes(s.mistakeAtRatings) !== '-') {
                      <div class="session-mistakes">
                        <mat-icon>heart_broken</mat-icon>
                        <span>{{ formatMistakes(s.mistakeAtRatings) }}</span>
                      </div>
                    }
                  </div>
                }
              </div>

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
          <mat-icon>arrow_back</mat-icon> {{ 'endless.history.backToEndless' | translate }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .history-container { max-width: 900px; margin: 2rem auto; padding: 0 1rem; }
    mat-card-title { display: flex; align-items: center; gap: 0.5rem; }
    .loading { display: flex; justify-content: center; padding: 3rem 0; }
    .empty-state { text-align: center; padding: 3rem 1rem; }
    .empty-icon { font-size: 48px; width: 48px; height: 48px; color: color-mix(in srgb, currentColor 30%, transparent); }
    .empty-state p { color: color-mix(in srgb, currentColor 50%, transparent); margin: 1rem 0; }
    .history-table { width: 100%; }
    .back-link { margin-top: 1rem; }
    th.mat-mdc-header-cell { font-weight: 600; }
    .elo-pos { color: #4caf50; font-weight: 600; }
    .elo-neg { color: #f44336; font-weight: 600; }
    .toolbar { display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .bulk-actions { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
    .selection-count { font-size: 0.875rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .archived-row { opacity: 0.5; }
    .clickable-row { cursor: pointer; }
    tr.clickable-row:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .session-card.clickable-row:hover { background: color-mix(in srgb, currentColor 6%, transparent); }

    .mobile-only { display: none; }
    .session-card {
      padding: 0.75rem; border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent);
    }
    .session-card-header {
      display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem;
    }
    .session-date { font-weight: 500; font-size: 0.9rem; }
    .archived-icon { font-size: 16px; width: 16px; height: 16px; color: color-mix(in srgb, currentColor 40%, transparent); }
    .session-card-stats {
      display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.25rem; text-align: center;
    }
    .session-stat-value { font-weight: 600; font-size: 0.95rem; display: block; }
    .session-stat-label { font-size: 0.7rem; color: color-mix(in srgb, currentColor 50%, transparent); }
    .session-mistakes {
      display: flex; align-items: center; gap: 0.25rem; margin-top: 0.4rem;
      font-size: 0.8rem; color: #f44336;
    }
    .session-mistakes mat-icon { font-size: 14px; width: 14px; height: 14px; }

    @media (max-width: 768px) {
      .history-container { margin: 0.75rem auto; }
      .desktop-only { display: none; }
      .mobile-only { display: block; }
      .session-card-stats { grid-template-columns: repeat(2, 1fr); gap: 0.5rem; }
    }
  `]
})
export class EndlessHistoryComponent implements OnInit {
  sessions: EndlessSessionDto[] = [];
  totalCount = 0;
  page = 1;
  pageSize = 20;
  loading = true;
  archiveFilter: boolean | null = null;
  displayedColumns = ['select', 'date', 'maxRating', 'elo', 'solved', 'duration', 'config', 'mistakes'];
  selection = new SelectionModel<EndlessSessionDto>(true, []);

  constructor(private http: HttpClient, public router: Router, private locale: LocaleService) {}

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

  /** Detail-Ansicht eines Laufs öffnen (gleiches Game-Over-Layout wie direkt nach Spielende). */
  openSession(s: EndlessSessionDto): void {
    this.router.navigate(['/puzzles/endless'], { queryParams: { session: s.id } });
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
    const loc = this.locale.current;
    return d.toLocaleDateString(loc, { day: '2-digit', month: '2-digit', year: 'numeric' })
      + ' ' + d.toLocaleTimeString(loc, { hour: '2-digit', minute: '2-digit' });
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
      const parts = [`${c.startElo ?? '?'}`];
      if (c.fasttrackThreshold1) parts.push(`T1 ${c.fasttrackThreshold1}`);
      if (c.fasttrackThreshold2) parts.push(`T2 ${c.fasttrackThreshold2}`);
      return parts.join(' · ');
    } catch {
      return '-';
    }
  }

  /** Elo-Aufstieg im Lauf = erreichtes Max-Rating minus Start-Elo (aus der Lauf-Config). null wenn unbekannt. */
  eloDelta(s: EndlessSessionDto): number | null {
    try {
      const start = JSON.parse(s.configJson)?.startElo;
      if (typeof start !== 'number') return null;
      return s.maxRating - start;
    } catch {
      return null;
    }
  }

  formatEloDelta(s: EndlessSessionDto): string {
    const d = this.eloDelta(s);
    if (d === null) return '-';
    if (d > 0) return `+${d}`;
    if (d < 0) return `−${-d}`; // echtes Minus-Zeichen (−)
    return '±0'; // ±0
  }

  eloDeltaClass(s: EndlessSessionDto): string {
    const d = this.eloDelta(s);
    if (d === null || d === 0) return '';
    return d > 0 ? 'elo-pos' : 'elo-neg';
  }

  formatMistakes(mistakeAtRatings: string): string {
    if (!mistakeAtRatings) return '-';
    const ratings = mistakeAtRatings.split(',').filter(r => r.trim());
    return ratings.length > 0 ? ratings.join(', ') : '-';
  }
}
