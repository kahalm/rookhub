import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

interface Profile {
  userId: number;
  username: string;
  displayName: string | null;
  fideId: string | null;
  chessResultsId: string | null;
  chessComUsername: string | null;
  lichessUsername: string | null;
}

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatSnackBarModule, LoadingSpinnerComponent],
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
    mat-card { width: 500px; max-width: 90vw; }
    .profile-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    mat-form-field { width: 100%; }
  `]
})
export class ProfileComponent implements OnInit {
  profile: Profile | null = null;
  loading = true;
  saving = false;

  constructor(private http: HttpClient, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.http.get<Profile>('/api/profile').subscribe({
      next: (p) => { this.profile = p; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  save(): void {
    if (!this.profile) return;
    this.saving = true;
    this.http.put<Profile>('/api/profile', {
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
