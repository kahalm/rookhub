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
import { AdminService, AdminUser, RequestLog } from '../../core/admin.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule, MatTabsModule, MatFormFieldModule, MatInputModule,
    MatSnackBarModule, MatChipsModule, MatSelectModule, MatTooltipModule, LoadingSpinnerComponent
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

  constructor(private adminService: AdminService, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.loadUsers();
    this.loadLogs();
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
}
