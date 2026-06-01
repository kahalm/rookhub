import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { Friend, FriendRequest, UserSearchResult } from '../../core/models';

@Component({
  selector: 'app-friends',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatListModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatTabsModule, MatSnackBarModule, TranslateModule, LoadingSpinnerComponent],
  template: `
    <div class="friends-container">
      <h1>{{ 'friends.title' | translate }}</h1>

      <mat-card class="search-card">
        <mat-form-field appearance="outline" class="search-field">
          <mat-label>{{ 'friends.searchUsers' | translate }}</mat-label>
          <input matInput [(ngModel)]="searchQuery" (keyup.enter)="search()">
        </mat-form-field>
        <button mat-raised-button color="primary" (click)="search()">{{ 'common.search' | translate }}</button>
      </mat-card>

      @if (searchResults.length > 0) {
        <mat-card>
          <mat-card-header><mat-card-title>{{ 'friends.searchResults' | translate }}</mat-card-title></mat-card-header>
          <mat-list>
            @for (user of searchResults; track user.userId) {
              <mat-list-item>
                <span matListItemTitle>{{ user.username }}</span>
                <span matListItemLine>{{ user.displayName || '' }}</span>
                @if (getChessIdentities(user)) {
                  <span matListItemLine class="chess-identities">{{ getChessIdentities(user) }}</span>
                }
                <button mat-icon-button (click)="sendRequest(user.userId)" matListItemMeta>
                  <mat-icon>person_add</mat-icon>
                </button>
              </mat-list-item>
            }
          </mat-list>
        </mat-card>
      }

      <mat-tab-group>
        <mat-tab [label]="'friends.tabs.friends' | translate:{ count: friends.length }">
          @if (loading) {
            <app-loading-spinner />
          } @else {
            <mat-list>
              @for (friend of friends; track friend.friendshipId) {
                <mat-list-item>
                  <span matListItemTitle>{{ friend.username }}</span>
                  <span matListItemLine>{{ friend.displayName || '' }}</span>
                  <button mat-icon-button color="warn" (click)="removeFriend(friend.friendshipId)" matListItemMeta>
                    <mat-icon>person_remove</mat-icon>
                  </button>
                </mat-list-item>
              } @empty {
                <p class="empty-text">{{ 'friends.empty.friends' | translate }}</p>
              }
            </mat-list>
          }
        </mat-tab>
        <mat-tab [label]="'friends.tabs.requests' | translate:{ count: requests.length }">
          <mat-list>
            @for (req of requests; track req.friendshipId) {
              <mat-list-item>
                <span matListItemTitle>{{ req.requesterUsername }}</span>
                <span matListItemLine>{{ 'friends.sentOn' | translate:{ date: (req.createdAt | date) } }}</span>
                <div matListItemMeta>
                  <button mat-icon-button color="primary" (click)="acceptRequest(req.friendshipId)">
                    <mat-icon>check</mat-icon>
                  </button>
                  <button mat-icon-button color="warn" (click)="declineRequest(req.friendshipId)">
                    <mat-icon>close</mat-icon>
                  </button>
                </div>
              </mat-list-item>
            } @empty {
              <p class="empty-text">{{ 'friends.empty.requests' | translate }}</p>
            }
          </mat-list>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .friends-container { padding: 2rem; max-width: 800px; margin: 0 auto; }
    .search-card { display: flex; align-items: center; gap: 1rem; padding: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .search-field { flex: 1; min-width: 0; margin-bottom: -1.25em; }
    .empty-text { padding: 1rem; color: #888; }
    .chess-identities { font-size: 0.75rem; color: #999; }
    @media (max-width: 768px) {
      .friends-container { padding: 0.75rem; }
      h1 { font-size: 1.4rem; }
      .search-field { flex-basis: 100%; }
      .search-card button { width: 100%; }
    }
  `]
})
export class FriendsComponent implements OnInit {
  friends: Friend[] = [];
  requests: FriendRequest[] = [];
  searchResults: UserSearchResult[] = [];
  searchQuery = '';
  loading = true;

  constructor(private http: HttpClient, private snackBar: MatSnackBar, private translate: TranslateService) {}

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.http.get<Friend[]>('/api/friends').subscribe({
      next: f => { this.friends = f; this.loading = false; },
      error: () => { this.loading = false; this.snackBar.open(this.translate.instant('friends.errors.loadFriends'), this.translate.instant('common.close'), { duration: 3000 }); }
    });
    this.http.get<FriendRequest[]>('/api/friends/requests').subscribe({
      next: r => this.requests = r,
      error: () => this.snackBar.open(this.translate.instant('friends.errors.loadRequests'), this.translate.instant('common.close'), { duration: 3000 })
    });
  }

  search(): void {
    if (this.searchQuery.length < 2) return;
    this.http.get<UserSearchResult[]>(`/api/friends/search?q=${encodeURIComponent(this.searchQuery)}`)
      .subscribe({
        next: r => this.searchResults = r,
        error: () => this.snackBar.open(this.translate.instant('friends.errors.search'), this.translate.instant('common.close'), { duration: 3000 })
      });
  }

  sendRequest(userId: number): void {
    this.http.post(`/api/friends/request/${userId}`, {}).subscribe({
      next: () => this.snackBar.open(this.translate.instant('friends.requestSent'), this.translate.instant('common.close'), { duration: 2000 }),
      error: (err) => this.snackBar.open(err.error?.message || this.translate.instant('friends.errors.sendRequest'), this.translate.instant('common.close'), { duration: 3000 })
    });
  }

  acceptRequest(id: number): void {
    this.http.post(`/api/friends/accept/${id}`, {}).subscribe({
      next: () => this.loadData(),
      error: () => this.snackBar.open(this.translate.instant('friends.errors.acceptRequest'), this.translate.instant('common.close'), { duration: 3000 })
    });
  }

  declineRequest(id: number): void {
    this.http.post(`/api/friends/decline/${id}`, {}).subscribe({
      next: () => this.loadData(),
      error: () => this.snackBar.open(this.translate.instant('friends.errors.declineRequest'), this.translate.instant('common.close'), { duration: 3000 })
    });
  }

  removeFriend(id: number): void {
    this.http.delete(`/api/friends/${id}`).subscribe({
      next: () => this.loadData(),
      error: () => this.snackBar.open(this.translate.instant('friends.errors.removeFriend'), this.translate.instant('common.close'), { duration: 3000 })
    });
  }

  getChessIdentities(user: UserSearchResult | Friend): string {
    const parts: string[] = [];
    if (user.chessComUsername) parts.push(`chess.com: ${user.chessComUsername}`);
    if (user.lichessUsername) parts.push(`lichess: ${user.lichessUsername}`);
    if (user.fideId) parts.push(`FIDE: ${user.fideId}`);
    if (user.chessResultsId) parts.push(`CR: ${user.chessResultsId}`);
    return parts.join(' | ');
  }
}
