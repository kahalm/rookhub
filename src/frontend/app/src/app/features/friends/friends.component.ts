import { Component, OnInit, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, of } from 'rxjs';
import { switchMap, catchError, tap } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterModule, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { SnackbarService } from '../../core/snackbar.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { Friend, FriendRequest, UserSearchResult } from '../../core/models';
import { ChallengeService, IncomingChallenge, OutgoingChallenge } from '../../core/challenge.service';
import { RevengeService, RevengeNotification } from '../../core/revenge.service';

@Component({
  selector: 'app-friends',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatListModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatTabsModule, MatTooltipModule, TranslateModule, LoadingSpinnerComponent],
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
                <button mat-icon-button (click)="sendRequest(user.userId)" matListItemMeta
                        [attr.aria-label]="'friends.aria.sendRequest' | translate" [matTooltip]="'friends.aria.sendRequest' | translate">
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
                  <div matListItemMeta>
                    <button mat-icon-button color="primary" [routerLink]="['/friends', friend.userId, 'stats']"
                            [attr.aria-label]="'friends.stats.compare' | translate" [matTooltip]="'friends.stats.compare' | translate">
                      <mat-icon>bar_chart</mat-icon>
                    </button>
                    <button mat-icon-button color="primary" [routerLink]="['/friends', friend.userId, 'revenge']"
                            [attr.aria-label]="'friends.revenge.menu' | translate" [matTooltip]="'friends.revenge.menu' | translate">
                      <mat-icon>sports_martial_arts</mat-icon>
                    </button>
                    <button mat-icon-button color="warn" (click)="removeFriend(friend.friendshipId)"
                            [attr.aria-label]="'friends.aria.removeFriend' | translate" [matTooltip]="'friends.aria.removeFriend' | translate">
                      <mat-icon>person_remove</mat-icon>
                    </button>
                  </div>
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
                  <button mat-icon-button color="primary" (click)="acceptRequest(req.friendshipId)"
                          [attr.aria-label]="'friends.aria.accept' | translate" [matTooltip]="'friends.aria.accept' | translate">
                    <mat-icon>check</mat-icon>
                  </button>
                  <button mat-icon-button color="warn" (click)="declineRequest(req.friendshipId)"
                          [attr.aria-label]="'friends.aria.decline' | translate" [matTooltip]="'friends.aria.decline' | translate">
                    <mat-icon>close</mat-icon>
                  </button>
                </div>
              </mat-list-item>
            } @empty {
              <p class="empty-text">{{ 'friends.empty.requests' | translate }}</p>
            }
          </mat-list>
        </mat-tab>
        <mat-tab [label]="'friends.challenges.tab' | translate:{ count: incoming.length }">
          <h3 class="section-title">{{ 'friends.challenges.inbox' | translate }}</h3>
          <mat-list>
            @for (c of incoming; track c.id) {
              <mat-list-item>
                <span matListItemTitle>{{ 'friends.challenges.fromLine' | translate:{ name: c.fromDisplayName || c.fromUsername, rating: c.rating } }}</span>
                <span matListItemLine class="themes">{{ c.title || formatThemes(c.themes) }}</span>
                <button mat-raised-button color="primary" matListItemMeta (click)="solveChallenge(c)">
                  <mat-icon>sports_esports</mat-icon> {{ 'friends.challenges.solve' | translate }}
                </button>
              </mat-list-item>
            } @empty {
              <p class="empty-text">{{ 'friends.challenges.emptyInbox' | translate }}</p>
            }
          </mat-list>

          <h3 class="section-title">{{ 'friends.challenges.sent' | translate }}</h3>
          <mat-list>
            @for (c of outgoing; track c.id) {
              <mat-list-item>
                <span matListItemTitle>{{ 'friends.challenges.toLine' | translate:{ name: c.toDisplayName || c.toUsername, rating: c.rating } }}</span>
                <span matListItemLine class="status" [class.solved]="c.status === 'Solved'" [class.failed]="c.status === 'Failed'">
                  @switch (c.status) {
                    @case ('Solved') { ✓ {{ 'friends.challenges.statusSolved' | translate:{ time: c.timeSpentSeconds } }} }
                    @case ('Failed') { ✗ {{ 'friends.challenges.statusFailed' | translate }} }
                    @default { ⏳ {{ 'friends.challenges.statusPending' | translate }} }
                  }
                </span>
              </mat-list-item>
            } @empty {
              <p class="empty-text">{{ 'friends.challenges.emptySent' | translate }}</p>
            }
          </mat-list>

          <h3 class="section-title">{{ 'friends.revengeNotifications.title' | translate }}</h3>
          <mat-list>
            @for (n of revengeNotifications; track n.id) {
              <mat-list-item [class.unseen]="!n.seen">
                <span matListItemTitle>
                  @if (n.solved) {
                    {{ 'friends.revengeNotifications.solved' | translate:{ name: n.avengerDisplayName || n.avengerUsername, rating: n.rating } }}
                  } @else {
                    {{ 'friends.revengeNotifications.failed' | translate:{ name: n.avengerDisplayName || n.avengerUsername, rating: n.rating } }}
                  }
                </span>
                <span matListItemLine class="when">{{ n.createdAt | date:'short' }}</span>
                <button mat-icon-button matListItemMeta [routerLink]="['/puzzles', n.puzzleId]"
                        [matTooltip]="'friends.revengeNotifications.openPuzzle' | translate">
                  <mat-icon>open_in_new</mat-icon>
                </button>
              </mat-list-item>
            } @empty {
              <p class="empty-text">{{ 'friends.revengeNotifications.empty' | translate }}</p>
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
    .empty-text { padding: 1rem; color: color-mix(in srgb, currentColor 47%, transparent); }
    .chess-identities { font-size: 0.75rem; color: color-mix(in srgb, currentColor 40%, transparent); }
    .section-title { margin: 1rem 1rem 0; font-size: 0.95rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .themes { font-size: 0.8rem; color: color-mix(in srgb, currentColor 50%, transparent); }
    .status { font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    .status.solved { color: #2e7d32; }
    .status.failed { color: #c62828; }
    .when { font-size: 0.72rem; color: color-mix(in srgb, currentColor 40%, transparent); }
    .unseen { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 10%, transparent); border-radius: 6px; }
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
  incoming: IncomingChallenge[] = [];
  outgoing: OutgoingChallenge[] = [];
  revengeNotifications: RevengeNotification[] = [];
  searchQuery = '';
  loading = true;

  private destroyRef = inject(DestroyRef);
  // Such-Trigger über switchMap: ein neuer Suchlauf bricht den vorigen ab, damit eine
  // langsamere ältere Antwort nicht ein neueres Ergebnis überschreibt (Out-of-order-Race).
  private searchTrigger = new Subject<string>();

  constructor(
    private http: HttpClient,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private router: Router,
    private challenge: ChallengeService,
    private revenge: RevengeService
  ) {}

  ngOnInit(): void {
    this.searchTrigger.pipe(
      switchMap(q => this.http.get<UserSearchResult[]>(`/api/friends/search?q=${encodeURIComponent(q)}`).pipe(
        catchError(() => { this.snackbar.info(this.translate.instant('friends.errors.search')); return of(null); })
      )),
      tap(r => { if (r) this.searchResults = r; }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.http.get<Friend[]>('/api/friends').subscribe({
      next: f => { this.friends = f; this.loading = false; },
      error: () => { this.loading = false; this.snackbar.info(this.translate.instant('friends.errors.loadFriends')); }
    });
    this.http.get<FriendRequest[]>('/api/friends/requests').subscribe({
      next: r => this.requests = r,
      error: () => this.snackbar.info(this.translate.instant('friends.errors.loadRequests'))
    });
    this.loadChallenges();
  }

  loadChallenges(): void {
    // getIncoming() aktualisiert dabei den Navbar-Badge-Zähler.
    this.challenge.getIncoming().subscribe({ next: c => this.incoming = c, error: () => {} });
    this.challenge.getOutgoing().subscribe({ next: c => this.outgoing = c, error: () => {} });
    // Revanche-Benachrichtigungen laden und (da der User sie jetzt sieht) als gelesen markieren → Badge leert sich.
    // Flach via switchMap statt verschachteltem subscribe (kein subscribe-in-subscribe).
    this.revenge.getNotifications().pipe(
      tap(n => this.revengeNotifications = n),
      switchMap(n => n.some(x => !x.seen) ? this.revenge.markSeen().pipe(catchError(() => of(null))) : of(null)),
      catchError(() => of(null)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe();
  }

  /** Eingehende Challenge lösen: zum passenden Solver navigieren (Standard- vs. Buch-Puzzle),
   *  challengeId mitgeben → Resolve nach dem Versuch. */
  solveChallenge(c: IncomingChallenge): void {
    const path = c.source === 'Book' ? ['/puzzles/book', c.puzzleId] : ['/puzzles', c.puzzleId];
    this.router.navigate(path, { queryParams: { challengeId: c.id } });
  }

  formatThemes(themes: string | null): string {
    return themes ? themes.split(' ').filter(t => t).slice(0, 4).join(', ') : '';
  }

  search(): void {
    if (this.searchQuery.length < 2) return;
    this.searchTrigger.next(this.searchQuery);
  }

  sendRequest(userId: number): void {
    this.http.post(`/api/friends/request/${userId}`, {}).subscribe({
      next: () => this.snackbar.success(this.translate.instant('friends.requestSent')),
      error: (err) => this.snackbar.info(err.error?.message || this.translate.instant('friends.errors.sendRequest'))
    });
  }

  acceptRequest(id: number): void {
    this.http.post(`/api/friends/accept/${id}`, {}).subscribe({
      next: () => this.loadData(),
      error: () => this.snackbar.info(this.translate.instant('friends.errors.acceptRequest'))
    });
  }

  declineRequest(id: number): void {
    this.http.post(`/api/friends/decline/${id}`, {}).subscribe({
      next: () => this.loadData(),
      error: () => this.snackbar.info(this.translate.instant('friends.errors.declineRequest'))
    });
  }

  removeFriend(id: number): void {
    this.http.delete(`/api/friends/${id}`).subscribe({
      next: () => this.loadData(),
      error: () => this.snackbar.info(this.translate.instant('friends.errors.removeFriend'))
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
