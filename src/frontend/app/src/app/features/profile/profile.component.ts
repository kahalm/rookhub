import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { SnackbarService } from '../../core/snackbar.service';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { DiscordLinkService } from '../../core/discord-link.service';
import { OfflineService } from '../../core/offline.service';
import { OfflineQueueService } from '../../core/offline-queue.service';
import { AuthService } from '../../core/auth.service';
import { RouterModule } from '@angular/router';
import { ApiTokensComponent } from './api-tokens.component';

interface Profile {
  userId: number;
  username: string;
  firstName: string | null;
  lastName: string | null;
  displayName: string | null;
  fideId: string | null;
  chessResultsId: string | null;
  chessComUsername: string | null;
  lichessUsername: string | null;
  discordId: string | null;
  discordUsername: string | null;
  boardTheme: string | null;
  pieceSet: string | null;
  stockfishDepth: number | null;
  puzzleDifficulty: string | null;
  bookStockfishDepth: number | null;
}

interface PlayerSearchResult {
  chessResultsResults: PlayerSearchItem[];
  fideResults: PlayerSearchItem[];
}

interface PlayerSearchItem {
  name: string;
  fideId: string | null;
  chessResultsId: string | null;
  elo: number | null;
  country: string | null;
  title: string | null;
}

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatProgressSpinnerModule, MatListModule,
    MatIconModule, MatDividerModule, TranslateModule, RouterModule, LoadingSpinnerComponent,
    ApiTokensComponent],
  template: `
    @if (loading) {
      <app-loading-spinner />
    } @else if (profile) {
      <div class="profile-container">
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'profile.title' | translate: { username: profile.username } }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <form (ngSubmit)="save()" class="profile-form">
              <div class="name-row">
                <mat-form-field appearance="outline">
                  <mat-label>{{ 'profile.firstName' | translate }}</mat-label>
                  <input matInput [(ngModel)]="profile.firstName" name="firstName">
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>{{ 'profile.lastName' | translate }}</mat-label>
                  <input matInput [(ngModel)]="profile.lastName" name="lastName">
                </mat-form-field>
                <button mat-stroked-button type="button" (click)="searchPlayer()"
                  [disabled]="!profile.lastName || profile.lastName.trim().length < 2 || searching"
                  class="search-btn">
                  @if (searching) {
                    <mat-spinner diameter="20"></mat-spinner>
                  } @else {
                    <mat-icon>search</mat-icon> {{ 'profile.searchPlayer' | translate }}
                  }
                </button>
              </div>

              @if (searchResults) {
                <div class="search-results">
                  @if (searchResults.chessResultsResults.length === 0 && searchResults.fideResults.length === 0) {
                    <p class="no-results">{{ 'profile.noResults' | translate }}</p>
                  }

                  @if (searchResults.chessResultsResults.length > 0) {
                    <h4>ChessResults</h4>
                    <mat-list>
                      @for (p of searchResults.chessResultsResults; track p.name + p.chessResultsId) {
                        <mat-list-item class="search-item" (click)="selectChessResultsPlayer(p)">
                          <span class="player-info">
                            @if (p.title) { <strong class="title">{{ p.title }}</strong> }
                            {{ p.name }}
                            @if (p.elo) { <span class="elo">({{ p.elo }})</span> }
                            @if (p.country) { <span class="country">{{ p.country }}</span> }
                            @if (p.chessResultsId) { <span class="id">CR: {{ p.chessResultsId }}</span> }
                            @if (p.fideId) { <span class="id">FIDE: {{ p.fideId }}</span> }
                          </span>
                          <mat-icon class="select-icon">arrow_forward</mat-icon>
                        </mat-list-item>
                      }
                    </mat-list>
                  }

                  @if (searchResults.fideResults.length > 0) {
                    <h4>FIDE</h4>
                    <mat-list>
                      @for (p of searchResults.fideResults; track p.name + p.fideId) {
                        <mat-list-item class="search-item" (click)="selectFidePlayer(p)">
                          <span class="player-info">
                            @if (p.title) { <strong class="title">{{ p.title }}</strong> }
                            {{ p.name }}
                            @if (p.elo) { <span class="elo">({{ p.elo }})</span> }
                            @if (p.country) { <span class="country">{{ p.country }}</span> }
                            @if (p.fideId) { <span class="id">FIDE: {{ p.fideId }}</span> }
                          </span>
                          <mat-icon class="select-icon">arrow_forward</mat-icon>
                        </mat-list-item>
                      }
                    </mat-list>
                  }
                </div>
              }

              <mat-form-field appearance="outline">
                <mat-label>{{ 'profile.displayName' | translate }}</mat-label>
                <input matInput [(ngModel)]="profile.displayName" name="displayName">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'profile.fideId' | translate }}</mat-label>
                <input matInput [(ngModel)]="profile.fideId" name="fideId">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'profile.chessResultsId' | translate }}</mat-label>
                <input matInput [(ngModel)]="profile.chessResultsId" name="chessResultsId">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'profile.chessComUsername' | translate }}</mat-label>
                <input matInput [(ngModel)]="profile.chessComUsername" name="chessComUsername">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ 'profile.lichessUsername' | translate }}</mat-label>
                <input matInput [(ngModel)]="profile.lichessUsername" name="lichessUsername">
              </mat-form-field>
              <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                {{ saving ? ('profile.saving' | translate) : ('common.save' | translate) }}
              </button>
            </form>

            <mat-divider class="discord-divider"></mat-divider>
            <div class="discord-section">
              <h4>{{ 'profile.discord.title' | translate }}</h4>
              @if (profile.discordId) {
                <div class="discord-linked">
                  <mat-icon class="discord-icon">link</mat-icon>
                  <span class="discord-name">{{ profile.discordUsername || profile.discordId }}</span>
                  <button mat-stroked-button color="warn" type="button" (click)="unlinkDiscord()" [disabled]="unlinking">
                    {{ 'profile.discord.unlink' | translate }}
                  </button>
                </div>
              } @else {
                <p class="discord-hint">{{ 'profile.discord.hint' | translate }}</p>
              }
            </div>

            <mat-divider class="discord-divider"></mat-divider>
            <app-api-tokens></app-api-tokens>

            <mat-divider class="discord-divider"></mat-divider>
            <div class="offline-section">
              <h4>{{ 'profile.offline.title' | translate }}</h4>
              <p class="offline-hint">{{ 'profile.offline.hint' | translate }}</p>
              <div class="offline-fields">
                <mat-form-field appearance="outline">
                  <mat-label>{{ 'profile.offline.puzzleCount' | translate }}</mat-label>
                  <input matInput type="number" min="0" max="200" [(ngModel)]="offlinePuzzleCount" name="offPuzzles" (change)="saveOffline()">
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>{{ 'profile.offline.endlessRuns' | translate }}</mat-label>
                  <input matInput type="number" min="0" max="50" [(ngModel)]="offlineEndlessRuns" name="offRuns" (change)="saveOffline()">
                </mat-form-field>
              </div>
              <div class="offline-cache">
                <span class="offline-size">{{ 'profile.offline.cacheSize' | translate }}: <strong>{{ offlineSize }}</strong>{{ offlineBooks > 0 ? ' (' + ('profile.offline.books' | translate: { count: offlineBooks }) + ')' : '' }}</span>
                <button mat-stroked-button color="warn" type="button" (click)="clearOfflineCache()">
                  <mat-icon>delete_sweep</mat-icon> {{ 'profile.offline.clear' | translate }}
                </button>
              </div>
              @if (offlinePending > 0) {
                <p class="offline-pending">
                  <mat-icon>sync</mat-icon> {{ 'profile.offline.pending' | translate: { count: offlinePending } }}
                </p>
              }
            </div>

            <mat-divider class="discord-divider"></mat-divider>
            <div class="danger-section">
              <h4>{{ 'profile.delete.title' | translate }}</h4>
              <p class="danger-hint">{{ 'profile.delete.hint' | translate }}</p>
              @if (!showDelete) {
                <button mat-stroked-button color="warn" type="button" (click)="showDelete = true">
                  <mat-icon>delete_forever</mat-icon> {{ 'profile.delete.button' | translate }}
                </button>
              } @else {
                <div class="danger-confirm">
                  <p class="danger-warn">{{ 'profile.delete.warn' | translate }}</p>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'profile.delete.password' | translate }}</mat-label>
                    <input matInput type="password" [(ngModel)]="deletePassword" name="delPwd" autocomplete="current-password">
                  </mat-form-field>
                  <div class="danger-actions">
                    <button mat-button type="button" (click)="cancelDelete()">{{ 'common.cancel' | translate }}</button>
                    <button mat-raised-button color="warn" type="button" (click)="deleteAccount()" [disabled]="!deletePassword || deleting">
                      {{ deleting ? ('profile.delete.deleting' | translate) : ('profile.delete.confirm' | translate) }}
                    </button>
                  </div>
                </div>
              }
              <p class="danger-link"><a routerLink="/account-deletion">{{ 'profile.delete.moreInfo' | translate }}</a></p>
            </div>
          </mat-card-content>
        </mat-card>
      </div>
    }
  `,
  styles: [`
    .profile-container { padding: 2rem; display: flex; justify-content: center; }
    mat-card { width: 600px; max-width: 95vw; }
    .profile-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    mat-form-field { width: 100%; }
    .name-row { display: flex; gap: 0.5rem; align-items: flex-start; flex-wrap: wrap; }
    .name-row mat-form-field { flex: 1; min-width: 140px; }
    .search-btn { height: 56px; white-space: nowrap; display: flex; align-items: center; gap: 4px; }
    .search-results {
      border: 1px solid rgba(255,255,255,0.12);
      border-radius: 8px;
      padding: 0.75rem;
      margin-bottom: 0.5rem;
      max-height: 400px;
      overflow-y: auto;
    }
    .search-results h4 { margin: 0.5rem 0 0.25rem; color: #90caf9; }
    .search-item { cursor: pointer; border-radius: 4px; }
    .search-item:hover { background: rgba(255,255,255,0.08); }
    .player-info { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; font-size: 14px; }
    .title { color: #ffd54f; }
    .elo { color: #a5d6a7; }
    .country { color: #90caf9; font-size: 12px; }
    .id { color: #bdbdbd; font-size: 12px; }
    .select-icon { color: #90caf9; margin-left: auto; }
    .no-results { color: #bdbdbd; font-style: italic; text-align: center; padding: 1rem 0; }
    .discord-divider { margin: 1.25rem 0 1rem; }
    .discord-section h4 { margin: 0 0 0.5rem; color: #90caf9; }
    .offline-section h4 { margin: 0 0 0.25rem; color: #90caf9; }
    .offline-hint { color: #bdbdbd; font-size: 0.85rem; margin: 0 0 0.5rem; }
    .offline-fields { display: flex; gap: 0.75rem; flex-wrap: wrap; }
    .offline-fields mat-form-field { width: 200px; max-width: 100%; }
    .offline-cache { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .offline-size { color: #ccc; font-size: 0.9rem; }
    .offline-pending { display: flex; align-items: center; gap: 6px; color: #ffb74d; font-size: 0.85rem; margin: 6px 0 0; }
    .offline-pending mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .discord-linked { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .discord-icon { color: #5865F2; }
    .discord-name { font-weight: 500; }
    .discord-linked button { margin-left: auto; }
    .discord-hint { color: #bdbdbd; font-size: 0.85rem; margin: 0; }
    .danger-section h4 { margin: 0 0 0.25rem; color: #ef9a9a; }
    .danger-hint { color: #bdbdbd; font-size: 0.85rem; margin: 0 0 0.5rem; }
    .danger-warn { color: #ef9a9a; font-size: 0.9rem; }
    .danger-confirm { display: flex; flex-direction: column; gap: 0.25rem; max-width: 360px; }
    .danger-actions { display: flex; gap: 8px; justify-content: flex-end; }
    .danger-link { margin: 0.75rem 0 0; font-size: 0.85rem; }
    .danger-link a { color: #90caf9; }
    @media (max-width: 768px) {
      .profile-container { padding: 0.75rem; }
      .name-row mat-form-field { min-width: 0; flex-basis: 100%; }
      .search-btn { width: 100%; justify-content: center; }
      .search-results { max-height: 300px; }
    }
  `]
})
export class ProfileComponent implements OnInit {
  profile: Profile | null = null;
  loading = true;
  saving = false;
  searching = false;
  unlinking = false;
  searchResults: PlayerSearchResult | null = null;

  offlinePuzzleCount = 10;
  offlineEndlessRuns = 2;
  offlineSize = '0 B';
  offlineBooks = 0;
  offlinePending = 0;

  showDelete = false;
  deletePassword = '';
  deleting = false;

  constructor(
    private http: HttpClient,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private discordLink: DiscordLinkService,
    private offline: OfflineService,
    private offlineQueue: OfflineQueueService,
    private auth: AuthService
  ) {}

  ngOnInit(): void {
    this.offlinePuzzleCount = this.offline.puzzleCount;
    this.offlineEndlessRuns = this.offline.endlessRuns;
    this.refreshOfflineSize();
    this.http.get<Profile>('/api/profile').subscribe({
      next: (p) => { this.profile = p; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  saveOffline(): void {
    this.offline.setPuzzleCount(this.offlinePuzzleCount);
    this.offline.setEndlessRuns(this.offlineEndlessRuns);
    this.offlinePuzzleCount = this.offline.puzzleCount;   // geklemmte Werte zurückspiegeln
    this.offlineEndlessRuns = this.offline.endlessRuns;
  }

  private refreshOfflineSize(): void {
    this.offlineSize = this.offline.formatSize(this.offline.cacheSizeBytes());
    this.offlineBooks = this.offline.cachedBookCount();
    this.offlinePending = this.offlineQueue.pendingCount();
  }

  clearOfflineCache(): void {
    this.offline.clearAll();
    this.refreshOfflineSize();
    this.snackbar.success(this.translate.instant('profile.offline.cleared'));
  }

  searchPlayer(): void {
    if (!this.profile?.lastName || this.profile.lastName.trim().length < 2) return;
    this.searching = true;
    this.searchResults = null;

    let params = new HttpParams().set('lastName', this.profile.lastName.trim());
    if (this.profile.firstName?.trim()) {
      params = params.set('firstName', this.profile.firstName.trim());
    }

    this.http.get<PlayerSearchResult>('/api/profile/player-search', { params }).subscribe({
      next: (results) => {
        this.searching = false;
        this.searchResults = results;

        // Auto-fill if exactly one result per source.
        const crSingle = results.chessResultsResults.length === 1 ? results.chessResultsResults[0] : null;
        const fideSingle = results.fideResults.length === 1 ? results.fideResults[0] : null;
        if (crSingle) {
          this.selectChessResultsPlayer(crSingle);
        }
        // FIDE-Treffer nur auto-uebernehmen, wenn der CR-Treffer nicht bereits eine
        // (zum CR-Spieler gehoerende) FIDE-Id geliefert hat — sonst wuerde ein evtl.
        // fremder Einzel-FIDE-Treffer diese ueberschreiben.
        if (fideSingle && !(crSingle && crSingle.fideId)) {
          this.selectFidePlayer(fideSingle);
        }
      },
      error: () => {
        this.searching = false;
        this.snackbar.info(this.translate.instant('profile.searchFailed'));
      }
    });
  }

  selectChessResultsPlayer(p: PlayerSearchItem): void {
    if (!this.profile) return;
    if (p.chessResultsId) this.profile.chessResultsId = p.chessResultsId;
    if (p.fideId) this.profile.fideId = p.fideId;
    this.snackbar.success(this.translate.instant('profile.chessResultsApplied'));
  }

  selectFidePlayer(p: PlayerSearchItem): void {
    if (!this.profile) return;
    if (p.fideId) this.profile.fideId = p.fideId;
    this.snackbar.success(this.translate.instant('profile.fideApplied'));
  }

  save(): void {
    if (!this.profile) return;
    this.saving = true;
    this.http.put<Profile>('/api/profile', {
      firstName: this.profile.firstName,
      lastName: this.profile.lastName,
      displayName: this.profile.displayName,
      fideId: this.profile.fideId,
      chessResultsId: this.profile.chessResultsId,
      chessComUsername: this.profile.chessComUsername,
      lichessUsername: this.profile.lichessUsername
    }).subscribe({
      next: (p) => {
        this.profile = p;
        this.saving = false;
        this.snackbar.success(this.translate.instant('profile.saved'));
      },
      error: () => {
        this.saving = false;
        this.snackbar.info(this.translate.instant('profile.saveFailed'));
      }
    });
  }

  unlinkDiscord(): void {
    if (!this.profile) return;
    this.unlinking = true;
    this.discordLink.unlink().subscribe({
      next: () => {
        if (this.profile) { this.profile.discordId = null; this.profile.discordUsername = null; }
        this.unlinking = false;
        this.snackbar.success(this.translate.instant('profile.discord.unlinked'));
      },
      error: () => {
        this.unlinking = false;
        this.snackbar.info(this.translate.instant('profile.discord.linkFailed'));
      }
    });
  }

  cancelDelete(): void {
    this.showDelete = false;
    this.deletePassword = '';
  }

  deleteAccount(): void {
    if (!this.deletePassword || this.deleting) return;
    this.deleting = true;
    this.auth.deleteAccount(this.deletePassword).subscribe({
      next: () => {
        // logout() in deleteAccount navigiert bereits zu /login
        this.snackbar.success(this.translate.instant('profile.delete.done'));
      },
      error: (err) => {
        this.deleting = false;
        this.snackbar.info(this.translate.instant(
          err?.status === 401 ? 'profile.delete.wrongPassword' : 'profile.delete.failed'));
      }
    });
  }
}
