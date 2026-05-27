import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CreateRepertoireDialogComponent } from './create-repertoire-dialog.component';
import { Repertoire } from '../../core/models';

@Component({
  selector: 'app-repertoire-list',
  standalone: true,
  imports: [CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule, MatDialogModule, MatSnackBarModule, LoadingSpinnerComponent],
  template: `
    <div class="repertoire-container">
      <div class="header">
        <h1>My Repertoires</h1>
        <button mat-raised-button color="primary" (click)="openCreateDialog()">
          <mat-icon>add</mat-icon> New Repertoire
        </button>
      </div>

      @if (loading) {
        <app-loading-spinner />
      } @else {
        <div class="repertoire-grid">
          @for (rep of repertoires; track rep.id) {
            <mat-card>
              <mat-card-header>
                <mat-card-title>{{ rep.name }}</mat-card-title>
                <mat-card-subtitle>{{ rep.fileCount }} files | {{ rep.isPublic ? 'Public' : 'Private' }}</mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <p>{{ rep.description || 'No description' }}</p>
              </mat-card-content>
              <mat-card-actions>
                <button mat-button [routerLink]="['/repertoires', rep.id]">Open</button>
                <button mat-button color="warn" (click)="deleteRepertoire(rep.id)">Delete</button>
              </mat-card-actions>
            </mat-card>
          } @empty {
            <p>No repertoires yet. Create one to get started!</p>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .repertoire-container { padding: 2rem; max-width: 1200px; margin: 0 auto; }
    .header { display: flex; justify-content: space-between; align-items: center; }
    .repertoire-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 1rem; }
  `]
})
export class RepertoireListComponent implements OnInit {
  repertoires: Repertoire[] = [];
  loading = true;

  constructor(private http: HttpClient, private dialog: MatDialog, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.loadRepertoires();
  }

  loadRepertoires(): void {
    this.loading = true;
    this.http.get<Repertoire[]>('/api/repertoires').subscribe({
      next: (r) => { this.repertoires = r; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(CreateRepertoireDialogComponent, { width: '400px' });
    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.http.post('/api/repertoires', result).subscribe(() => this.loadRepertoires());
      }
    });
  }

  deleteRepertoire(id: number): void {
    if (confirm('Delete this repertoire?')) {
      this.http.delete(`/api/repertoires/${id}`).subscribe(() => this.loadRepertoires());
    }
  }
}
