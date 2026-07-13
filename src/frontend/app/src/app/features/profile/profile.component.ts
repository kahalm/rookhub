import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProfileService } from '../../core/profile.service';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { SnackbarService } from '../../core/snackbar.service';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { DiscordLinkService } from '../../core/discord-link.service';
import { ApiTokensComponent } from './api-tokens.component';
import { OfflineSettingsCardComponent } from './offline-settings-card.component';
import { ThemeCardComponent } from './theme-card.component';
import { ChangePasswordCardComponent } from './change-password-card.component';
import { DeleteAccountCardComponent } from './delete-account-card.component';

interface Profile {
  userId: number;
  username: string;
  email: string | null;
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
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatProgressSpinnerModule, MatListModule,
    MatIconModule, MatDividerModule, TranslatePipe, LoadingSpinnerComponent,
    ApiTokensComponent, OfflineSettingsCardComponent, ThemeCardComponent,
    ChangePasswordCardComponent, DeleteAccountCardComponent],
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
                <mat-label>{{ 'profile.email' | translate }}</mat-label>
                <input matInput type="email" [(ngModel)]="profile.email" name="email"
                       autocomplete="email" inputmode="email">
                <mat-hint>{{ 'profile.emailHint' | translate }}</mat-hint>
              </mat-form-field>
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
            <app-offline-settings-card></app-offline-settings-card>

            <mat-divider class="discord-divider"></mat-divider>
            <app-theme-card></app-theme-card>

            <mat-divider class="discord-divider"></mat-divider>
            <app-change-password-card></app-change-password-card>

            <mat-divider class="discord-divider"></mat-divider>
            <app-delete-account-card></app-delete-account-card>
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
    .discord-linked { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .discord-icon { color: #5865F2; }
    .discord-name { font-weight: 500; }
    .discord-linked button { margin-left: auto; }
    .discord-hint { color: #bdbdbd; font-size: 0.85rem; margin: 0; }
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

  constructor(
    private profileService: ProfileService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private discordLink: DiscordLinkService,
  ) {}

  ngOnInit(): void {
    this.profileService.getProfile<Profile>().subscribe({
      next: (p) => { this.profile = p; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  searchPlayer(): void {
    if (!this.profile?.lastName || this.profile.lastName.trim().length < 2) return;
    this.searching = true;
    this.searchResults = null;

    this.profileService.searchPlayer<PlayerSearchResult>(
      this.profile.lastName.trim(), this.profile.firstName?.trim() || undefined).subscribe({
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
    this.profileService.updateProfile<Profile>({
      email: this.profile.email ?? '',
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
      error: (err) => {
        this.saving = false;
        const key = err?.status === 409 ? 'profile.emailTaken'
          : err?.status === 400 ? 'profile.emailInvalid'
          : 'profile.saveFailed';
        this.snackbar.info(this.translate.instant(key));
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
}
