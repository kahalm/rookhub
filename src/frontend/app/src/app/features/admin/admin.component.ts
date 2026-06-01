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
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AdminService, AdminUser, RequestLog, Book, Group, GroupMember } from '../../core/admin.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatTableModule, MatPaginatorModule,
    MatButtonModule, MatIconModule, MatTabsModule, MatFormFieldModule, MatInputModule,
    MatSnackBarModule, MatChipsModule, MatSelectModule, MatTooltipModule, MatSlideToggleModule, TranslateModule, LoadingSpinnerComponent
  ],
  template: `
    <div class="admin-container">
      <h1>{{ 'admin.title' | translate }}</h1>

      <mat-tab-group>
        <mat-tab [label]="'admin.tabs.users' | translate">
          <div class="tab-content">
            <mat-form-field appearance="outline" class="search-field">
              <mat-label>{{ 'admin.users.searchLabel' | translate }}</mat-label>
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
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columns.id' | translate }}</th>
                  <td mat-cell *matCellDef="let u">{{ u.id }}</td>
                </ng-container>
                <ng-container matColumnDef="username">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columns.username' | translate }}</th>
                  <td mat-cell *matCellDef="let u">{{ u.username }}</td>
                </ng-container>
                <ng-container matColumnDef="email">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columns.email' | translate }}</th>
                  <td mat-cell *matCellDef="let u">{{ u.email }}</td>
                </ng-container>
                <ng-container matColumnDef="isAdmin">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columns.admin' | translate }}</th>
                  <td mat-cell *matCellDef="let u">
                    @if (u.isAdmin) {
                      <mat-icon class="admin-badge">shield</mat-icon>
                    }
                  </td>
                </ng-container>
                <ng-container matColumnDef="createdAt">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columns.created' | translate }}</th>
                  <td mat-cell *matCellDef="let u">{{ u.createdAt | date:'short' }}</td>
                </ng-container>
                <ng-container matColumnDef="actions">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.users.columns.actions' | translate }}</th>
                  <td mat-cell *matCellDef="let u">
                    <button mat-icon-button (click)="toggleAdmin(u)" [attr.title]="'admin.users.toggleAdmin' | translate">
                      <mat-icon>{{ u.isAdmin ? 'remove_moderator' : 'add_moderator' }}</mat-icon>
                    </button>
                    <button mat-icon-button color="warn" (click)="deleteUser(u)" [attr.title]="'admin.users.deleteUser' | translate">
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

        <mat-tab [label]="'admin.tabs.books' | translate">
          <div class="tab-content">
            <div class="book-upload">
              <input #pgnInput type="file" accept=".pgn" multiple hidden (change)="onBookFilesSelected($event)">
              <button mat-raised-button color="primary" (click)="pgnInput.click()" [disabled]="booksUploading">
                <mat-icon>upload_file</mat-icon> {{ 'admin.books.uploadPgn' | translate }}
              </button>
              @if (booksUploading) { <span class="upload-hint">{{ 'admin.books.importing' | translate }}</span> }
              <span class="book-hint">{{ 'admin.books.poolHint' | translate }}</span>
            </div>

            @if (booksLoading) {
              <app-loading-spinner />
            } @else if (books.length === 0) {
              <p class="empty-hint">{{ 'admin.books.empty' | translate }}</p>
            } @else {
              <table mat-table [dataSource]="books" class="full-width">
                <ng-container matColumnDef="displayName">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.book' | translate }}</th>
                  <td mat-cell *matCellDef="let b">{{ b.displayName }}</td>
                </ng-container>
                <ng-container matColumnDef="puzzleCount">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.puzzles' | translate }}</th>
                  <td mat-cell *matCellDef="let b">{{ b.puzzleCount }}</td>
                </ng-container>
                <ng-container matColumnDef="difficulty">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.difficulty' | translate }}</th>
                  <td mat-cell *matCellDef="let b">
                    {{ b.difficulty || '–' }}@if (b.rating) { <span> · {{ b.rating }}/10</span> }
                  </td>
                </ng-container>
                <ng-container matColumnDef="elo">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.elo' | translate }}</th>
                  <td mat-cell *matCellDef="let b">
                    <input type="number" class="elo-input" [(ngModel)]="b.minElo" (change)="saveBook(b)" [placeholder]="'admin.books.eloFrom' | translate">
                    <span class="elo-sep">–</span>
                    <input type="number" class="elo-input" [(ngModel)]="b.maxElo" (change)="saveBook(b)" [placeholder]="'admin.books.eloTo' | translate">
                  </td>
                </ng-container>
                <ng-container matColumnDef="forDaily">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.daily' | translate }}</th>
                  <td mat-cell *matCellDef="let b">
                    <mat-slide-toggle [(ngModel)]="b.forDaily" (change)="saveBook(b)"></mat-slide-toggle>
                  </td>
                </ng-container>
                <ng-container matColumnDef="forRandom">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.random' | translate }}</th>
                  <td mat-cell *matCellDef="let b">
                    <mat-slide-toggle [(ngModel)]="b.forRandom" (change)="saveBook(b)"></mat-slide-toggle>
                  </td>
                </ng-container>
                <ng-container matColumnDef="forBlind">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.blind' | translate }}</th>
                  <td mat-cell *matCellDef="let b">
                    <mat-slide-toggle [(ngModel)]="b.forBlind" (change)="saveBook(b)"></mat-slide-toggle>
                  </td>
                </ng-container>
                <ng-container matColumnDef="groups">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.visibleFor' | translate }}</th>
                  <td mat-cell *matCellDef="let b">
                    <mat-form-field appearance="outline" class="groups-select" subscriptSizing="dynamic">
                      <mat-select multiple [(ngModel)]="b.accessGroupIds" (selectionChange)="saveBookGroups(b)"
                                  [placeholder]="'admin.books.adminOnly' | translate">
                        @for (g of groups; track g.id) {
                          <mat-option [value]="g.id">{{ g.name }}</mat-option>
                        }
                      </mat-select>
                    </mat-form-field>
                  </td>
                </ng-container>
                <ng-container matColumnDef="actions">
                  <th mat-header-cell *matHeaderCellDef>{{ 'admin.books.columns.actions' | translate }}</th>
                  <td mat-cell *matCellDef="let b">
                    <button mat-icon-button color="warn" (click)="deleteBook(b)" [attr.title]="'admin.books.deleteBook' | translate">
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

        <mat-tab [label]="'admin.tabs.groups' | translate">
          <div class="tab-content">
            <div class="group-create">
              <mat-form-field appearance="outline" class="group-name-field">
                <mat-label>{{ 'admin.groups.newGroup' | translate }}</mat-label>
                <input matInput [(ngModel)]="newGroupName" (keyup.enter)="createGroup()" [placeholder]="'admin.groups.namePlaceholder' | translate">
              </mat-form-field>
              <mat-form-field appearance="outline" class="group-desc-field">
                <mat-label>{{ 'admin.groups.descriptionOptional' | translate }}</mat-label>
                <input matInput [(ngModel)]="newGroupDescription" (keyup.enter)="createGroup()">
              </mat-form-field>
              <button mat-raised-button color="primary" (click)="createGroup()" [disabled]="!newGroupName.trim()">
                <mat-icon>add</mat-icon> {{ 'admin.groups.create' | translate }}
              </button>
            </div>

            @if (groupsLoading) {
              <app-loading-spinner />
            } @else if (groups.length === 0) {
              <p class="empty-hint">{{ 'admin.groups.empty' | translate }}</p>
            } @else {
              <div class="group-layout">
                <table mat-table [dataSource]="groups" class="full-width group-table">
                  <ng-container matColumnDef="name">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.groups.columns.group' | translate }}</th>
                    <td mat-cell *matCellDef="let g">
                      <strong>{{ g.name }}</strong>
                      @if (g.description) { <div class="group-desc">{{ g.description }}</div> }
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="memberCount">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.groups.columns.members' | translate }}</th>
                    <td mat-cell *matCellDef="let g">{{ g.memberCount }}</td>
                  </ng-container>
                  <ng-container matColumnDef="actions">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.groups.columns.actions' | translate }}</th>
                    <td mat-cell *matCellDef="let g">
                      <button mat-icon-button (click)="selectGroup(g)" [attr.title]="'admin.groups.manageMembers' | translate">
                        <mat-icon>group</mat-icon>
                      </button>
                      <button mat-icon-button color="warn" (click)="deleteGroup(g)" [attr.title]="'admin.groups.deleteGroup' | translate">
                        <mat-icon>delete</mat-icon>
                      </button>
                    </td>
                  </ng-container>

                  <tr mat-header-row *matHeaderRowDef="groupColumns"></tr>
                  <tr mat-row *matRowDef="let row; columns: groupColumns;"
                      [class.selected-group]="selectedGroup?.id === row.id"></tr>
                </table>

                @if (selectedGroup) {
                  <mat-card class="member-panel">
                    <mat-card-header>
                      <mat-card-title>{{ 'admin.groups.membersOf' | translate: { name: selectedGroup.name } }}</mat-card-title>
                    </mat-card-header>
                    <mat-card-content>
                      <div class="member-add">
                        <mat-form-field appearance="outline" class="member-select">
                          <mat-label>{{ 'admin.groups.addUser' | translate }}</mat-label>
                          <mat-select [(ngModel)]="addMemberUserId">
                            @for (u of availableUsers(); track u.id) {
                              <mat-option [value]="u.id">{{ u.username }}</mat-option>
                            }
                          </mat-select>
                        </mat-form-field>
                        <button mat-raised-button color="primary" (click)="addMember()" [disabled]="!addMemberUserId">
                          <mat-icon>person_add</mat-icon> {{ 'common.add' | translate }}
                        </button>
                      </div>

                      @if (membersLoading) {
                        <app-loading-spinner />
                      } @else if (groupMembers.length === 0) {
                        <p class="empty-hint">{{ 'admin.groups.noMembers' | translate }}</p>
                      } @else {
                        <mat-chip-set>
                          @for (m of groupMembers; track m.userId) {
                            <mat-chip (removed)="removeMember(m)">
                              {{ m.username }}
                              <button matChipRemove [attr.aria-label]="'common.remove' | translate"><mat-icon>cancel</mat-icon></button>
                            </mat-chip>
                          }
                        </mat-chip-set>
                      }
                    </mat-card-content>
                  </mat-card>
                }
              </div>
            }
          </div>
        </mat-tab>

        <mat-tab [label]="'admin.tabs.logs' | translate">
          <div class="tab-content">
            <div class="log-filters">
              <mat-form-field appearance="outline" class="filter-field">
                <mat-label>{{ 'admin.logs.columns.path' | translate }}</mat-label>
                <input matInput [(ngModel)]="logFilterPath" placeholder="/api/...">
              </mat-form-field>

              <mat-form-field appearance="outline" class="filter-field filter-small">
                <mat-label>{{ 'admin.logs.columns.method' | translate }}</mat-label>
                <mat-select [(ngModel)]="logFilterMethod">
                  <mat-option value="">{{ 'admin.logs.all' | translate }}</mat-option>
                  <mat-option value="GET">GET</mat-option>
                  <mat-option value="POST">POST</mat-option>
                  <mat-option value="PUT">PUT</mat-option>
                  <mat-option value="DELETE">DELETE</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" class="filter-field filter-small">
                <mat-label>{{ 'admin.logs.columns.status' | translate }}</mat-label>
                <mat-select [(ngModel)]="logFilterStatus">
                  <mat-option value="">{{ 'admin.logs.all' | translate }}</mat-option>
                  <mat-option value="400">{{ 'admin.logs.status4xx' | translate }}</mat-option>
                  <mat-option value="500">{{ 'admin.logs.status5xx' | translate }}</mat-option>
                  <mat-option value="200">{{ 'admin.logs.status2xx' | translate }}</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" class="filter-field">
                <mat-label>{{ 'admin.logs.columns.user' | translate }}</mat-label>
                <input matInput [(ngModel)]="logFilterUser">
              </mat-form-field>

              <div class="filter-actions">
                <button mat-raised-button color="primary" (click)="applyLogFilters()">
                  <mat-icon>search</mat-icon> {{ 'admin.logs.filter' | translate }}
                </button>
                <button mat-button (click)="resetLogFilters()">
                  <mat-icon>clear</mat-icon> {{ 'admin.logs.reset' | translate }}
                </button>
              </div>
            </div>

            <div class="log-summary">
              {{ 'admin.logs.entryCount' | translate: { count: logsTotalCount } }}
              @if (hasActiveLogFilters()) {
                <span class="filter-hint">{{ 'admin.logs.filtered' | translate }}</span>
              }
            </div>

            @if (logsLoading) {
              <app-loading-spinner />
            } @else {
              <div class="table-responsive">
                <table mat-table [dataSource]="logs" class="full-width log-table">
                  <ng-container matColumnDef="timestamp">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.columns.time' | translate }}</th>
                    <td mat-cell *matCellDef="let l">{{ l.timestamp | date:'dd.MM. HH:mm:ss' }}</td>
                  </ng-container>
                  <ng-container matColumnDef="method">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.columns.method' | translate }}</th>
                    <td mat-cell *matCellDef="let l">
                      <span class="method-badge method-{{ l.method | lowercase }}">{{ l.method }}</span>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="path">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.columns.path' | translate }}</th>
                    <td mat-cell *matCellDef="let l" [matTooltip]="l.queryString || ''">
                      {{ l.path }}@if (l.queryString) { <span class="qs-indicator">?</span> }
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="statusCode">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.columns.status' | translate }}</th>
                    <td mat-cell *matCellDef="let l">
                      <span [class]="'status-badge status-' + getStatusClass(l.statusCode)">{{ l.statusCode }}</span>
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="durationMs">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.columns.duration' | translate }}</th>
                    <td mat-cell *matCellDef="let l" [class.slow-request]="l.durationMs > 1000">
                      {{ l.durationMs }}ms
                    </td>
                  </ng-container>
                  <ng-container matColumnDef="userName">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.columns.user' | translate }}</th>
                    <td mat-cell *matCellDef="let l">{{ l.userName || '-' }}</td>
                  </ng-container>
                  <ng-container matColumnDef="ipAddress">
                    <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.columns.ip' | translate }}</th>
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
    .elo-input { width: 56px; }
    .elo-sep { margin: 0 2px; color: #999; }
    .groups-select { min-width: 150px; max-width: 220px; font-size: 0.85rem; }

    .group-create { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-bottom: 16px; }
    .group-name-field { min-width: 220px; }
    .group-desc-field { flex: 1; min-width: 220px; }
    .group-layout { display: flex; flex-wrap: wrap; gap: 24px; align-items: flex-start; }
    .group-table { flex: 1; min-width: 320px; }
    .group-desc { color: #666; font-size: 0.8rem; }
    .selected-group { background: #e3f2fd; }
    .member-panel { flex: 1; min-width: 300px; }
    .member-add { display: flex; gap: 12px; align-items: center; margin-bottom: 12px; }
    .member-select { min-width: 200px; }

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
  bookColumns = ['displayName', 'puzzleCount', 'difficulty', 'elo', 'forDaily', 'forRandom', 'forBlind', 'groups', 'actions'];

  groups: Group[] = [];
  groupsLoading = false;
  groupColumns = ['name', 'memberCount', 'actions'];
  newGroupName = '';
  newGroupDescription = '';
  selectedGroup: Group | null = null;
  groupMembers: GroupMember[] = [];
  membersLoading = false;
  allUsers: AdminUser[] = [];
  addMemberUserId: number | null = null;

  constructor(private adminService: AdminService, private snackBar: MatSnackBar, private translate: TranslateService) {}

  ngOnInit(): void {
    this.loadUsers();
    this.loadLogs();
    this.loadBooks();
    this.loadGroups();
    this.loadAllUsers();
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
        this.snackBar.open(this.translate.instant('admin.users.errors.load'), this.translate.instant('common.close'), { duration: 3000 });
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
        this.snackBar.open(this.translate.instant('admin.logs.errors.load'), this.translate.instant('common.close'), { duration: 3000 });
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
        const key = updated.isAdmin ? 'admin.users.nowAdmin' : 'admin.users.noLongerAdmin';
        this.snackBar.open(this.translate.instant(key, { username: user.username }), this.translate.instant('common.close'), { duration: 3000 });
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.users.errors.toggleAdmin'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  deleteUser(user: AdminUser): void {
    if (!confirm(this.translate.instant('admin.users.deleteConfirm', { username: user.username }))) return;

    this.adminService.deleteUser(user.id).subscribe({
      next: () => {
        this.snackBar.open(this.translate.instant('admin.users.deleted', { username: user.username }), this.translate.instant('common.close'), { duration: 3000 });
        this.loadUsers();
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.users.errors.delete'), this.translate.instant('common.close'), { duration: 3000 });
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
        this.snackBar.open(this.translate.instant('admin.books.errors.load'), this.translate.instant('common.close'), { duration: 3000 });
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
        this.snackBar.open(this.translate.instant('admin.books.imported', { imported: res.totalImported, skipped: res.totalSkipped }), this.translate.instant('common.close'), { duration: 4000 });
        this.booksUploading = false;
        input.value = '';
        this.loadBooks();
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.books.errors.import'), this.translate.instant('common.close'), { duration: 4000 });
        this.booksUploading = false;
        input.value = '';
      }
    });
  }

  saveBook(book: Book): void {
    this.adminService.updateBook(book.id, {
      forDaily: book.forDaily,
      forRandom: book.forRandom,
      forBlind: book.forBlind,
      minElo: book.minElo,
      maxElo: book.maxElo
    }).subscribe({
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.books.errors.save'), this.translate.instant('common.close'), { duration: 3000 });
        this.loadBooks(); // Stand zurücksetzen
      }
    });
  }

  saveBookGroups(book: Book): void {
    this.adminService.updateBookGroups(book.id, book.accessGroupIds ?? []).subscribe({
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.books.errors.saveGroups'), this.translate.instant('common.close'), { duration: 3000 });
        this.loadBooks(); // Stand zurücksetzen
      }
    });
  }

  deleteBook(book: Book): void {
    if (!confirm(this.translate.instant('admin.books.deleteConfirm', { name: book.displayName, count: book.puzzleCount }))) return;

    this.adminService.deleteBook(book.id).subscribe({
      next: () => {
        this.snackBar.open(this.translate.instant('admin.books.deleted', { name: book.displayName }), this.translate.instant('common.close'), { duration: 3000 });
        this.loadBooks();
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.books.errors.delete'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  // --- Gruppen ----------------------------------------------------------
  loadGroups(): void {
    this.groupsLoading = true;
    this.adminService.getGroups().subscribe({
      next: groups => {
        this.groups = groups;
        this.groupsLoading = false;
        // Auswahl aktualisieren, falls die gewählte Gruppe noch existiert
        if (this.selectedGroup) {
          this.selectedGroup = groups.find(g => g.id === this.selectedGroup!.id) ?? null;
        }
      },
      error: () => {
        this.snackBar.open(this.translate.instant('admin.groups.errors.load'), this.translate.instant('common.close'), { duration: 3000 });
        this.groupsLoading = false;
      }
    });
  }

  loadAllUsers(): void {
    // User-Liste fuer das Mitglieder-Dropdown (kleiner Nutzerkreis).
    this.adminService.getUsers('', 1, 500).subscribe({
      next: res => this.allUsers = res.items
    });
  }

  createGroup(): void {
    const name = this.newGroupName.trim();
    if (!name) return;
    this.adminService.createGroup(name, this.newGroupDescription.trim() || null).subscribe({
      next: () => {
        this.snackBar.open(this.translate.instant('admin.groups.created', { name }), this.translate.instant('common.close'), { duration: 3000 });
        this.newGroupName = '';
        this.newGroupDescription = '';
        this.loadGroups();
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.groups.errors.create'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  deleteGroup(group: Group): void {
    if (!confirm(this.translate.instant('admin.groups.deleteConfirm', { name: group.name }))) return;
    this.adminService.deleteGroup(group.id).subscribe({
      next: () => {
        this.snackBar.open(this.translate.instant('admin.groups.deleted', { name: group.name }), this.translate.instant('common.close'), { duration: 3000 });
        if (this.selectedGroup?.id === group.id) {
          this.selectedGroup = null;
          this.groupMembers = [];
        }
        this.loadGroups();
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.groups.errors.delete'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  selectGroup(group: Group): void {
    this.selectedGroup = group;
    this.addMemberUserId = null;
    this.loadMembers(group.id);
  }

  loadMembers(groupId: number): void {
    this.membersLoading = true;
    this.adminService.getGroupMembers(groupId).subscribe({
      next: members => {
        this.groupMembers = members;
        this.membersLoading = false;
      },
      error: () => {
        this.snackBar.open(this.translate.instant('admin.groups.errors.loadMembers'), this.translate.instant('common.close'), { duration: 3000 });
        this.membersLoading = false;
      }
    });
  }

  availableUsers(): AdminUser[] {
    const memberIds = new Set(this.groupMembers.map(m => m.userId));
    return this.allUsers.filter(u => !memberIds.has(u.id));
  }

  addMember(): void {
    if (!this.selectedGroup || !this.addMemberUserId) return;
    const groupId = this.selectedGroup.id;
    this.adminService.addGroupMember(groupId, this.addMemberUserId).subscribe({
      next: () => {
        this.addMemberUserId = null;
        this.loadMembers(groupId);
        this.loadGroups(); // Mitgliederzahl aktualisieren
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.groups.errors.addMember'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }

  removeMember(member: GroupMember): void {
    if (!this.selectedGroup) return;
    const groupId = this.selectedGroup.id;
    this.adminService.removeGroupMember(groupId, member.userId).subscribe({
      next: () => {
        this.loadMembers(groupId);
        this.loadGroups(); // Mitgliederzahl aktualisieren
      },
      error: err => {
        this.snackBar.open(err.error?.message || this.translate.instant('admin.groups.errors.removeMember'), this.translate.instant('common.close'), { duration: 3000 });
      }
    });
  }
}
