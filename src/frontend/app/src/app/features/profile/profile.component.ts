import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

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
    MatButtonModule, MatSnackBarModule, MatProgressSpinnerModule, MatListModule,
    MatIconModule, MatDividerModule, LoadingSpinnerComponent],
  template: `
    @if (loading) {
      <app-loading-spinner />
    } @else if (profile) {
      <div class="profile-container">
        <mat-card>
          <mat-card-header>
            <mat-card-title>Profile: {{ profile.username }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <form (ngSubmit)="save()" class="profile-form">
              <div class="name-row">
                <mat-form-field appearance="outline">
                  <mat-label>Vorname</mat-label>
                  <input matInput [(ngModel)]="profile.firstName" name="firstName">
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Nachname</mat-label>
                  <input matInput [(ngModel)]="profile.lastName" name="lastName">
                </mat-form-field>
                <button mat-stroked-button type="button" (click)="searchPlayer()"
                  [disabled]="!profile.lastName || profile.lastName.trim().length < 2 || searching"
                  class="search-btn">
                  @if (searching) {
                    <mat-spinner diameter="20"></mat-spinner>
                  } @else {
                    <mat-icon>search</mat-icon> Spieler suchen
                  }
                </button>
              </div>

              @if (searchResults) {
                <div class="search-results">
                  @if (searchResults.chessResultsResults.length === 0 && searchResults.fideResults.length === 0) {
                    <p class="no-results">Keine Ergebnisse gefunden.</p>
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
                <mat-label>Display Name</mat-label>
                <input matInput [(ngModel)]="profile.displayName" name="displayName">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>FIDE ID</mat-label>
                <input matInput [(ngModel)]="profile.fideId" name="fideId">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>ChessResults ID</mat-label>
                <input matInput [(ngModel)]="profile.chessResultsId" name="chessResultsId">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Chess.com Username</mat-label>
                <input matInput [(ngModel)]="profile.chessComUsername" name="chessComUsername">
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Lichess Username</mat-label>
                <input matInput [(ngModel)]="profile.lichessUsername" name="lichessUsername">
              </mat-form-field>
              <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                {{ saving ? 'Saving...' : 'Save' }}
              </button>
            </form>
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
  searchResults: PlayerSearchResult | null = null;

  constructor(private http: HttpClient, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.http.get<Profile>('/api/profile').subscribe({
      next: (p) => { this.profile = p; this.loading = false; },
      error: () => { this.loading = false; }
    });
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

        // Auto-fill if exactly one result per source
        if (results.chessResultsResults.length === 1) {
          this.selectChessResultsPlayer(results.chessResultsResults[0]);
        }
        if (results.fideResults.length === 1) {
          this.selectFidePlayer(results.fideResults[0]);
        }
      },
      error: () => {
        this.searching = false;
        this.snackBar.open('Suche fehlgeschlagen', 'Close', { duration: 3000 });
      }
    });
  }

  selectChessResultsPlayer(p: PlayerSearchItem): void {
    if (!this.profile) return;
    if (p.chessResultsId) this.profile.chessResultsId = p.chessResultsId;
    if (p.fideId) this.profile.fideId = p.fideId;
    this.snackBar.open('ChessResults-Daten uebernommen', 'Close', { duration: 2000 });
  }

  selectFidePlayer(p: PlayerSearchItem): void {
    if (!this.profile) return;
    if (p.fideId) this.profile.fideId = p.fideId;
    this.snackBar.open('FIDE-Daten uebernommen', 'Close', { duration: 2000 });
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
        this.snackBar.open('Profile saved', 'Close', { duration: 2000 });
      },
      error: () => {
        this.saving = false;
        this.snackBar.open('Failed to save profile', 'Close', { duration: 3000 });
      }
    });
  }
}
