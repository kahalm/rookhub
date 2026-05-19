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
import { AdminService, AdminUser, RequestLog } from '../../core/admin.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule, MatTabsModule, MatFormFieldModule, MatInputModule,
    MatSnackBarModule, MatChipsModule, LoadingSpinnerComponent
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
            @if (logsLoading) {
              <app-loading-spinner />
            } @else {
              <table mat-table [dataSource]="logs" class="full-width">
                <ng-container matColumnDef="timestamp">
                  <th mat-header-cell *matHeaderCellDef>Time</th>
                  <td mat-cell *matCellDef="let l">{{ l.timestamp | date:'short' }}</td>
                </ng-container>
                <ng-container matColumnDef="method">
                  <th mat-header-cell *matHeaderCellDef>Method</th>
                  <td mat-cell *matCellDef="let l">{{ l.method }}</td>
                </ng-container>
                <ng-container matColumnDef="path">
                  <th mat-header-cell *matHeaderCellDef>Path</th>
                  <td mat-cell *matCellDef="let l">{{ l.path }}</td>
                </ng-container>
                <ng-container matColumnDef="statusCode">
                  <th mat-header-cell *matHeaderCellDef>Status</th>
                  <td mat-cell *matCellDef="let l">{{ l.statusCode }}</td>
                </ng-container>
                <ng-container matColumnDef="durationMs">
                  <th mat-header-cell *matHeaderCellDef>Duration</th>
                  <td mat-cell *matCellDef="let l">{{ l.durationMs }}ms</td>
                </ng-container>
                <ng-container matColumnDef="userName">
                  <th mat-header-cell *matHeaderCellDef>User</th>
                  <td mat-cell *matCellDef="let l">{{ l.userName || '-' }}</td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="logColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: logColumns;"></tr>
              </table>

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
    .admin-container { max-width: 1200px; margin: 24px auto; padding: 0 16px; }
    .tab-content { padding: 16px 0; }
    .search-field { width: 100%; max-width: 400px; }
    .full-width { width: 100%; }
    .admin-badge { color: #ff9800; font-size: 20px; }
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
  logColumns = ['timestamp', 'method', 'path', 'statusCode', 'durationMs', 'userName'];

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
    this.adminService.getRequestLogs({
      page: this.logsPage.toString(),
      pageSize: this.logsPageSize.toString()
    }).subscribe({
      next: res => {
        this.logs = res.items;
        this.logsTotalCount = res.totalCount;
        this.logsLoading = false;
      },
      error: () => {
        this.snackBar.open('Failed to load logs', 'OK', { duration: 3000 });
        this.logsLoading = false;
      }
    });
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
