import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatSnackBarModule],
  template: `
    <div class="auth-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>Register</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form (ngSubmit)="onSubmit()" class="auth-form">
            <mat-form-field appearance="outline">
              <mat-label>Username</mat-label>
              <input matInput [(ngModel)]="username" name="username" required minlength="3">
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Email</mat-label>
              <input matInput type="email" [(ngModel)]="email" name="email" required>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <input matInput type="password" [(ngModel)]="password" name="password" required minlength="8">
            </mat-form-field>
            <button mat-raised-button color="primary" type="submit" [disabled]="loading">
              {{ loading ? 'Registering...' : 'Register' }}
            </button>
          </form>
        </mat-card-content>
        <mat-card-actions>
          <a mat-button routerLink="/login">Already have an account? Login</a>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styles: [`
    .auth-container { display: flex; justify-content: center; align-items: center; min-height: 80vh; }
    mat-card { width: 400px; max-width: 90vw; }
    .auth-form { display: flex; flex-direction: column; gap: 0.5rem; padding-top: 1rem; }
    mat-form-field { width: 100%; }
  `]
})
export class RegisterComponent {
  username = '';
  email = '';
  password = '';
  loading = false;

  constructor(private auth: AuthService, private router: Router, private snackBar: MatSnackBar) {}

  onSubmit(): void {
    this.loading = true;
    this.auth.register(this.username, this.email, this.password).subscribe({
      next: () => {
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.loading = false;
        this.snackBar.open(err.error?.message || 'Registration failed', 'Close', { duration: 3000 });
      }
    });
  }
}
