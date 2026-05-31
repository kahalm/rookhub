import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { AdminService, AdminUser, RequestLog, Book } from '../../core/admin.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule, MatTabsModule, MatFormFieldModule, MatInputModule,
    MatSnackBarModule, MatChipsModule, MatSelectModule, MatTooltipModule, MatSlideToggleModule, LoadingSpinnerComponent
  ],
  template: `
    <div class="admin-container">
      <h1>Admin</h1>

      <mat-tab-group>
        <mat-tab label="Users">
          <div class="tab-content">
            <mat-form-field appearance="outline" class="search-field">
              <mat-label>Search users</mat-label>
              <input matInput [(ngModel)]="userSearch" (keyup.enter)="loadUsers()">
              <button matSuffix mat-icon-button (click)="loadUsers()">
                <mat-icon>search</mat-icon>
              </button>
            </mat-form-field>

            @if (usersLoading) {
              <app-loading-spinner />
            } @else {
              <table mat-table [dataSource]="users" class="full-width">
                <ng-container matColumnDef="id">
                  <th mat-header-cell *matHeaderCellDef>ID</th>
                  <td mat-cell *matCellDef="let u">{{ u.id }}</td>
                </ng-container>
                <ng-container matColumnDef="username">
                  <th mat-header-cell *matHeaderCellDef>Username</th>
                  <td mat-cell *matCellDef="let u">{{ u.username }}</td>
                </ng-container>
                <ng-container matColumnDef="email">
                  <th mat-header-cell *matHeaderCellDef>Email</th>
                  <td mat-cell *matCellDef="let u">{{ u.email }}</td>
                </ng-container>
                <ng-container matColumnDef="isAdmin">
                  <th mat-header-cell *matHeaderCellDef>Admin</th>
                  <td mat-cell *matCellDef="let u">
                    @if (u.isAdmin) {
                      <mat-icon class="admin-badge">shield</mat-icon>
                    }
                  </td>
                </ng-container>
                <ng-container matColumnDef="createdAt">
                  <th mat-header-cell *matHeaderCellDef>Created</th>
                  <td mat-cell *matCellDef="let u">{{ u.createdAt | date:'short' }}</td>
                </ng-container>
                <ng-container matColumnDef="actions">
                  <th mat-header-cell *matHeaderCellDef>Actions</th>
                  <td mat-cell *matCellDef="let u">
                    <button mat-icon-button (click)="toggleAdmin(u)" title="Toggle admin">
                      <mat-icon>{{ u.isAdmin ? 'remove_moderator' : 'add_moderator' }}</mat-icon>
                    </button>
                    <button mat-icon-button color="warn" (click)="deleteUser(u)" title="Delete user">
                      <mat-icon>delete</mat-icon>
                    </button>
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="userColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: userColumns;"></tr>
              </table>

              <mat-paginator
                [length]="usersTotalCount"
                [pageSize]="usersPageSize"
                [pageIndex]="usersPage - 1"
                [pageSizeOptions]="[10, 20, 50]"
                (page)="onUsersPageChange($event)"
                showFirstLastButtons>
              </mat-paginator>
            }
          </div>
        </mat-tab>

        <mat-tab label="Bücher">
          <div class="tab-content">
            <div class="book-upload">
              <input #pgnInput type="file" accept=".pgn" multiple hidden (change)="onBookFilesSelected($event)">
              <button mat-raised-button color="primary" (click)="pgnInput.click()" [disabled]="booksUploading">
                <mat-icon>upload_file</mat-icon> PGN(s) hochladen
              </button>
              @if (booksUploading) { <span class="upload-hint">Import läuft…</span> }
              <span class="book-hint">Pro Buch festlegen, in welchen Pools die Puzzles erscheinen.</span>
            </div>

            @if (booksLoading) {
              <app-loading-spinner />
            } @else if (books.length === 0) {
              <p class="empty-hint">Noch keine Bücher importiert.</p>
            } @else {
              <table mat-table [dataSource]="books" class="full-width">
                <ng-container matColumnDef="displayName">
                  <th mat-header-cell *matHeaderCellDef>Buch</th>
                  <td mat-cell *matCellDef="let b">{{ b.displayName }}</td>
                </ng-container>
                <ng-container matColumnDef="puzzleCount">
                  <th mat-header-cell *matHeaderCellDef>Puzzles</th>
                  <td mat-cell *matCellDef="let b">{{ b.puzzleCount }}</td>
                </ng-container>
                <ng-container matColumnDef="difficulty">
                  <th mat-header-cell *matHeaderCellDef>Schwierigkeit</th>
                  <td mat-cell *matCellDef="let b">
                    {{ b.difficulty || '–' }}@if (b.rating) { <span> · {{ b.rating }}/10</span> }
                  </td>
                </ng-container>
                <ng-container matColumnDef="forDaily">
                  <th mat-header-cell *matHeaderCellDef>Daily</th>
                  <td mat-cell *matCellDef="let b">
                    <mat-slide-toggle [(ngModel)]="b.forDaily" (change)="saveBook(b)"></mat-slide-toggle>
                  </td>
                </ng-container>
                <ng-container matColumnDef="forRandom">
                  <th mat-header-cell *matHeaderCellDef>Random</th>
                  <td mat-cell *matCellDef="let b">
                    <mat-slide-toggle [(ngModel)]="b.forRandom" (change)="saveBook(b)"></mat-slide-toggle>
                  </td>
                </ng-container>
                <ng-container matColumnDef="forBlind">
                  <th mat-header-cell *matHeaderCellDef>Blind</th>
                  <td mat-cell *matCellDef="let b">
                    <mat-slide-toggle [(ngModel)]="b.forBlind" (change)="saveBook(b)"></mat-slide-toggle>
                  </td>
                </ng-container>
                <ng-container matColumnDef="actions">
                  <th mat-header-cell *matHeaderCellDef>Aktionen</th>
                  <td mat-cell *matCellDef="let b">
                    <button mat-icon-button color="warn" (click)="deleteBook(b)" title="Buch löschen">
                      <mat-icon>delete</mat-icon>
                    </button>
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="bookColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: bookColumns;"></tr>
              </table>
            }
          </div>
        </mat-tab>

        <mat-tab label="Request Logs">
          <div class="tab-content">
            <div class="log-filters">
              <mat-form-field appearance="outline" class="filter-field">
                <mat-label>Pfad</mat-label>
                <input matInput [(ngModel)]="logFilterPath" placeholder="/api/...">
              </mat-form-field>

              <mat-form-field appearance="outline" class="filter-field filter-small">
                <mat-label>Methode</mat-label>
                <mat-select [(ngModel)]="logFilterMethod">
                  <mat-option value="">Alle</mat-option>
                  <mat-option value="GET">GET</mat-option>
                  <mat-option value="POST">POST</mat-option>
                  <mat-option value="PUT">PUT</mat-option>
                  <mat-option value="DELETE">DELETE</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" class="filter-field filter-small">
                <mat-label>Status</mat-label>
                <mat-select [(ngModel)]="logFilterStatus">
                  <mat-option value="">Alle</mat-option>
                  <mat-option value="400">4xx+ (Fehler)</mat-option>
                  <mat-option value="500">5xx (Server)</mat-option>
                  <mat-option value="200">2xx+ (OK)</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" class="filter-field">
                <mat-label>Benutzer</mat-label>
                <input matInput [(ngModel)]="logFilterUser">
              </mat-form-field>

              <div class="filter-actions">
                <button mat-raised-button color="primary" (click)="applyLogFilters()">
                  <mat-icon>search</mat-icon> Filtern
                </button>
                <button mat-button (click)="resetLogFilters()">
                  <mat-icon>clear</mat-icon> Zuruecksetzen
                </button>
              </div>
            </div>

            <div class="log-summary">
              {{ logsTotalCount }} Eintraege
              @if (hasActiveLogFilters()) {
                <span class="filter-hint">(gefiltert)</span>
              }
            </div>

            @if (logsLoading) {
              <app-loading-spinner />
            } @else {
              <div class="table-responsive">
                <table mat-table [dataSource]="logs" class="full-width log-table">
                  <ng-container matColumnDef="timestamp">
                    <th mat-header-cell *matHeaderCellDef>Zeit</th>
                    <td mat-cell *matCellDef="let l">{{ l.timestamp | date:'dd.MM. HH:mm:ss' }}</td>
                  </ng-container>
                  <ng-container matColumnDef="method">
                    <th mat-header-cell *matHeaderCellDef>Methode</th>
                    <td mat-cell *matCellDef="let l">
                      <span class="method-badge method-{{ l.method | lowercase }}">{{ l.method }}</span>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="path">
                    <th mat-header-cell *matHeaderCellDef>Pfad</th>
                    <td mat-cell *matCellDef="let l" [matTooltip]="l.queryString || ''">
                      {{ l.path }}@if (l.queryString) { <span class="qs-indicator">?</span> }
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="statusCode">
                    <th mat-header-cell *matHeaderCellDef>Status</th>
                    <td mat-cell *matCellDef="let l">
                      <span [class]="'status-badge status-' + getStatusClass(l.statusCode)">{{ l.statusCode }}</span>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="durationMs">
                    <th mat-header-cell *matHeaderCellDef>Dauer</th>
                    <td mat-cell *matCellDef="let l" [class.slow-request]="l.durationMs > 1000">
                      {{ l.durationMs }}ms
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="userName">
                    <th mat-header-cell *matHeaderCellDef>Benutzer</th>
                    <td mat-cell *matCellDef="let l">{{ l.userName || '-' }}</td>
                  </ng-container>
                  <ng-container matColumnDef="ipAddress">
                    <th mat-header-cell *matHeaderCellDef>IP</th>
                    <td mat-cell *matCellDef="let l">{{ l.ipAddress || '-' }}</td>
                  </ng-container>

                  <tr mat-header-row *matHeaderRowDef="logColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: logColumns;"
                      [class.error-row]="row.statusCode >= 400"></tr>
                </table>
              </div>

              <mat-paginator
                [length]="logsTotalCount"
                [pageSize]="logsPageSize"
                [pageIndex]="logsPage - 1"
                [pageSizeOptions]="[20, 50, 100]"
                (page)="onLogsPageChange($event)"
                showFirstLastButtons>
              </mat-paginator>
            }
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .admin-container { max-width: 1400px; margin: 24px auto; padding: 0 16px; }
    .tab-content { padding: 16px 0; }
    .search-field { width: 100%; max-width: 400px; }
    .full-width { width: 100%; }
    .admin-badge { color: #ff9800; font-size: 20px; }
    .table-responsive { overflow-x: auto; }

    .book-upload {
      display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-bottom: 16px;
    }
    .upload-hint { color: #1976d2; font-weight: 500; }
    .book-hint { color: #666; font-size: 0.85rem; }
    .empty-hint { color: #666; font-style: italic; padding: 16px 0; }

    .log-filters {
      display: flex; flex-wrap: wrap; gap: 12px; align-items: flex-start;
      margin-bottom: 8px; padding: 12px; background: #fafafa; border-radius: 8px;
    }
    .filter-field { flex: 1; min-width: 150px; }
    .filter-small { max-width: 160px; }
    .filter-actions { display: flex; gap: 8px; align-items: center; padding-top: 4px; }
    .log-summary { font-size: 0.85rem; color: #666; margin-bottom: 8px; }
    .filter-hint { color: #1976d2; font-weight: 500; }

    .method-badge {
      font-size: 0.75rem; font-weight: 600; padding: 2px 6px; border-radius: 4px;
      font-family: monospace;
    }
    .method-get { background: #e3f2fd; color: #1565c0; }
    .method-post { background: #e8f5e9; color: #2e7d32; }
    .method-put { background: #fff3e0; color: #e65100; }
    .method-delete { background: #fce4ec; color: #c62828; }

    .status-badge { font-weight: 600; font-family: monospace; padding: 2px 6px; border-radius: 4px; }
    .status-ok { background: #e8f5e9; color: #2e7d32; }
    .status-redirect { background: #fff3e0; color: #e65100; }
    .status-client-error { background: #fce4ec; color: #c62828; }
    .status-server-error { background: #f3e5f5; color: #6a1b9a; }

    .qs-indicator { color: #999; font-size: 0.8rem; }
    .slow-request { color: #e65100; font-weight: 500; }
    .error-row { background: #fff8f8; }

    .log-table td { font-size: 0.85rem; }
    .log-table th { font-size: 0.8rem; font-weight: 600; }
  `]
})
export class AdminComponent implements OnInit {
  users: AdminUser[] = [];
  userSearch = '';
  usersPage = 1;
  usersPageSize = 20;
  usersTotalCount = 0;
  usersLoading = false;
  userColumns = ['id', 'username', 'email', 'isAdmin', 'createdAt', 'actions'];

  logs: RequestLog[] = [];
  logsPage = 1;
  logsPageSize = 50;
  logsTotalCount = 0;
  logsLoading = false;
  logColumns = ['timestamp', 'method', 'path', 'statusCode', 'durationMs', 'userName', 'ipAddress'];

  logFilterPath = '';
  logFilterMethod = '';
  logFilterStatus = '';
  logFilterUser = '';

  books: Book[] = [];
  booksLoading = false;
  booksUploading = false;
  bookColumns = ['displayName', 'puzzleCount', 'difficulty', 'forDaily', 'forRandom', 'forBlind', 'actions'];

  constructor(private adminService: AdminService, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.loadUsers();
    this.loadLogs();
    this.loadBooks();
  }

  loadUsers(): void {
    this.usersLoading = true;
    this.adminService.getUsers(this.userSearch, this.usersPage, this.usersPageSize).subscribe({
      next: res => {
        this.users = res.items;
        this.usersTotalCount = res.totalCount;
        this.usersLoading = false;
      },
      error: () => {
        this.snackBar.open('Failed to load users', 'OK', { duration: 3000 });
        this.usersLoading = false;
      }
    });
  }

  loadLogs(): void {
    this.logsLoading = true;
    const params: Record<string, string> = {
      page: this.logsPage.toString(),
      pageSize: this.logsPageSize.toString()
    };
    if (this.logFilterPath) params['path'] = this.logFilterPath;
    if (this.logFilterMethod) params['method'] = this.logFilterMethod;
    if (this.logFilterStatus) params['minStatus'] = this.logFilterStatus;
    if (this.logFilterUser) params['userName'] = this.logFilterUser;

    this.adminService.getRequestLogs(params).subscribe({
      next: res => {
        this.logs = res.items;
        this.logsTotalCount = res.totalCount;
        this.logsLoading = false;
      },
      error: () => {
        this.snackBar.open('Logs konnten nicht geladen werden', 'OK', { duration: 3000 });
        this.logsLoading = false;
      }
    });
  }

  applyLogFilters(): void {
    this.logsPage = 1;
    this.loadLogs();
  }

  resetLogFilters(): void {
    this.logFilterPath = '';
    this.logFilterMethod = '';
    this.logFilterStatus = '';
    this.logFilterUser = '';
    this.logsPage = 1;
    this.loadLogs();
  }

  hasActiveLogFilters(): boolean {
    return !!(this.logFilterPath || this.logFilterMethod || this.logFilterStatus || this.logFilterUser);
  }

  getStatusClass(code: number): string {
    if (code >= 500) return 'server-error';
    if (code >= 400) return 'client-error';
    if (code >= 300) return 'redirect';
    return 'ok';
  }

  onUsersPageChange(event: PageEvent): void {
    this.usersPage = event.pageIndex + 1;
    this.usersPageSize = event.pageSize;
    this.loadUsers();
  }

  onLogsPageChange(event: PageEvent): void {
    this.logsPage = event.pageIndex + 1;
    this.logsPageSize = event.pageSize;
    this.loadLogs();
  }

  toggleAdmin(user: AdminUser): void {
    this.adminService.toggleAdmin(user.id).subscribe({
      next: updated => {
        user.isAdmin = updated.isAdmin;
        this.snackBar.open(`${user.username} is ${updated.isAdmin ? 'now' : 'no longer'} admin`, 'OK', { duration: 3000 });
      },
      error: err => {
        this.snackBar.open(err.error?.message || 'Failed to toggle admin', 'OK', { duration: 3000 });
      }
    });
  }

  deleteUser(user: AdminUser): void {
    if (!confirm(`Delete user "${user.username}"? This cannot be undone.`)) return;

    this.adminService.deleteUser(user.id).subscribe({
      next: () => {
        this.snackBar.open(`User "${user.username}" deleted`, 'OK', { duration: 3000 });
        this.loadUsers();
      },
      error: err => {
        this.snackBar.open(err.error?.message || 'Failed to delete user', 'OK', { duration: 3000 });
      }
    });
  }

  // --- Bücher -----------------------------------------------------------
  loadBooks(): void {
    this.booksLoading = true;
    this.adminService.getBooks().subscribe({
      next: books => {
        this.books = books;
        this.booksLoading = false;
      },
      error: () => {
        this.snackBar.open('Bücher konnten nicht geladen werden', 'OK', { duration: 3000 });
        this.booksLoading = false;
      }
    });
  }

  onBookFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    this.booksUploading = true;
    this.adminService.importBooks(input.files).subscribe({
      next: res => {
        this.snackBar.open(`${res.totalImported} Puzzle(s) importiert, ${res.totalSkipped} übersprungen`, 'OK', { duration: 4000 });
        this.booksUploading = false;
        input.value = '';
        this.loadBooks();
      },
      error: err => {
        this.snackBar.open(err.error?.message || 'Import fehlgeschlagen', 'OK', { duration: 4000 });
        this.booksUploading = false;
        input.value = '';
      }
    });
  }

  saveBook(book: Book): void {
    this.adminService.updateBook(book.id, {
      forDaily: book.forDaily,
      forRandom: book.forRandom,
      forBlind: book.forBlind
    }).subscribe({
      error: err => {
        this.snackBar.open(err.error?.message || 'Speichern fehlgeschlagen', 'OK', { duration: 3000 });
        this.loadBooks(); // Stand zurücksetzen
      }
    });
  }

  deleteBook(book: Book): void {
    if (!confirm(`Buch "${book.displayName}" mit ${book.puzzleCount} Puzzle(s) löschen?`)) return;

    this.adminService.deleteBook(book.id).subscribe({
      next: () => {
        this.snackBar.open(`Buch "${book.displayName}" gelöscht`, 'OK', { duration: 3000 });
        this.loadBooks();
      },
      error: err => {
        this.snackBar.open(err.error?.message || 'Löschen fehlgeschlagen', 'OK', { duration: 3000 });
      }
    });
  }
}
