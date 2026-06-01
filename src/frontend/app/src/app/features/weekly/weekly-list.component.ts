import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RouterModule } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { WeeklyService, WeeklyPost, nextWeeklySlot, weeklyDatePart, weeklyTimePart } from './weekly.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

interface WeeklyPostRow extends WeeklyPost {
  editDate: string;   // YYYY-MM-DD (Admin-Edit)
  editTime: string;   // HH:mm
}

@Component({
  selector: 'app-weekly-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatTableModule, MatFormFieldModule, MatInputModule, MatSnackBarModule,
    LoadingSpinnerComponent
  ],
  template: `
    <div class="weekly-container">
      <h1>Wochenpost</h1>
      <p class="intro">Die wöchentlichen Schach-Posts zum Durchklicken. Datum &amp; Uhrzeit geben an, wann der Post geplant ist.</p>

      @if (auth.isAdmin) {
        <mat-card class="upload-card">
          <mat-card-header><mat-card-title>Neuen Wochenpost hochladen</mat-card-title></mat-card-header>
          <mat-card-content>
            <div class="upload-row">
              <input #pgnInput type="file" accept=".pgn" hidden (change)="onFileSelected($event)">
              <button mat-stroked-button (click)="pgnInput.click()">
                <mat-icon>upload_file</mat-icon> {{ uploadFileName || 'PGN wählen' }}
              </button>
              <mat-form-field appearance="outline" class="f-date">
                <mat-label>Datum</mat-label>
                <input matInput type="date" [(ngModel)]="uploadDate">
              </mat-form-field>
              <mat-form-field appearance="outline" class="f-time">
                <mat-label>Uhrzeit</mat-label>
                <input matInput type="time" [(ngModel)]="uploadTime">
              </mat-form-field>
              <mat-form-field appearance="outline" class="f-title">
                <mat-label>Titel (optional)</mat-label>
                <input matInput [(ngModel)]="uploadTitle" placeholder="aus Dateiname">
              </mat-form-field>
              <button mat-raised-button color="primary"
                      [disabled]="!uploadFile || !uploadDate || !uploadTime || uploading" (click)="upload()">
                <mat-icon>add</mat-icon> Anlegen
              </button>
            </div>
            <p class="upload-hint">Standard: letzter Termin + 7 Tage, gleiche Uhrzeit (sonst 19:00).</p>
          </mat-card-content>
        </mat-card>
      }

      @if (loading) {
        <app-loading-spinner />
      } @else if (rows.length === 0) {
        <p class="empty-hint">Noch keine Wochenposts vorhanden.</p>
      } @else {
        <table mat-table [dataSource]="rows" class="full-width">
          <ng-container matColumnDef="scheduled">
            <th mat-header-cell *matHeaderCellDef>Termin</th>
            <td mat-cell *matCellDef="let r">
              @if (auth.isAdmin) {
                <input type="date" class="inline-date" [(ngModel)]="r.editDate" (change)="savePost(r)">
                <input type="time" class="inline-time" [(ngModel)]="r.editTime" (change)="savePost(r)">
              } @else {
                {{ r.scheduledAt | date:'EEEE, dd.MM.yyyy' }} · {{ r.scheduledAt | date:'HH:mm' }} Uhr
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="title">
            <th mat-header-cell *matHeaderCellDef>Titel</th>
            <td mat-cell *matCellDef="let r">
              @if (auth.isAdmin) {
                <input class="inline-title" [(ngModel)]="r.title" (change)="savePost(r)">
              } @else {
                {{ r.title }}
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>Aktionen</th>
            <td mat-cell *matCellDef="let r">
              <button mat-stroked-button color="primary" [routerLink]="['/weekly', r.id]">
                <mat-icon>play_arrow</mat-icon> Durchspielen
              </button>
              @if (auth.isAdmin) {
                <button mat-icon-button color="warn" (click)="remove(r)" title="Löschen">
                  <mat-icon>delete</mat-icon>
                </button>
              }
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns;"></tr>
        </table>
      }
    </div>
  `,
  styles: [`
    .weekly-container { max-width: 1000px; margin: 24px auto; padding: 0 16px; }
    .intro { color: #666; margin-bottom: 16px; }
    .empty-hint { color: #666; font-style: italic; padding: 16px 0; }
    .full-width { width: 100%; }
    .upload-card { margin-bottom: 20px; }
    .upload-row { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
    .f-date, .f-time { width: 150px; }
    .f-title { flex: 1; min-width: 180px; }
    .upload-hint { color: #888; font-size: 0.8rem; margin: 4px 0 0; }
    .inline-date, .inline-time { font: inherit; padding: 2px 4px; border: 1px solid #ccc; border-radius: 4px; }
    .inline-time { margin-left: 6px; }
    .inline-title { font: inherit; padding: 2px 4px; border: 1px solid #ccc; border-radius: 4px; width: 100%; max-width: 320px; }
  `]
})
export class WeeklyListComponent implements OnInit {
  rows: WeeklyPostRow[] = [];
  loading = false;
  columns = ['scheduled', 'title', 'actions'];

  uploadFile: File | null = null;
  uploadFileName = '';
  uploadDate = '';
  uploadTime = '19:00';
  uploadTitle = '';
  uploading = false;

  constructor(
    public auth: AuthService,
    private weekly: WeeklyService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadPosts();
  }

  loadPosts(): void {
    this.loading = true;
    this.weekly.getAll().subscribe({
      next: posts => {
        this.rows = posts.map(p => ({ ...p, editDate: weeklyDatePart(p.scheduledAt), editTime: weeklyTimePart(p.scheduledAt) }));
        this.suggestNextSlot();
        this.loading = false;
      },
      error: () => {
        this.snackBar.open('Wochenposts konnten nicht geladen werden', 'OK', { duration: 3000 });
        this.loading = false;
      }
    });
  }

  /** Prefill für den Upload: letzter Termin + 7 Tage, gleiche Uhrzeit; sonst heute + 19:00. */
  private suggestNextSlot(): void {
    // Liste ist nach Termin absteigend sortiert -> rows[0] = letzter Eintrag.
    const slot = nextWeeklySlot(this.rows.length > 0 ? this.rows[0].scheduledAt : null);
    this.uploadDate = slot.date;
    this.uploadTime = slot.time;
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadFile = input.files && input.files.length ? input.files[0] : null;
    this.uploadFileName = this.uploadFile?.name ?? '';
  }

  upload(): void {
    if (!this.uploadFile || !this.uploadDate || !this.uploadTime) return;
    this.uploading = true;
    const scheduledAt = `${this.uploadDate}T${this.uploadTime}:00`;
    this.weekly.create(this.uploadFile, scheduledAt, this.uploadTitle.trim() || undefined).subscribe({
      next: () => {
        this.snackBar.open('Wochenpost angelegt', 'OK', { duration: 3000 });
        this.uploading = false;
        this.uploadFile = null;
        this.uploadFileName = '';
        this.uploadTitle = '';
        this.loadPosts();   // lädt neu + setzt nächsten Termin-Vorschlag
      },
      error: err => {
        this.snackBar.open(err.error?.message || 'Upload fehlgeschlagen', 'OK', { duration: 4000 });
        this.uploading = false;
      }
    });
  }

  savePost(row: WeeklyPostRow): void {
    if (!row.editDate || !row.editTime) return;
    const scheduledAt = `${row.editDate}T${row.editTime}:00`;
    this.weekly.update(row.id, { title: row.title, scheduledAt }).subscribe({
      next: p => { row.scheduledAt = p.scheduledAt; },
      error: err => {
        this.snackBar.open(err.error?.message || 'Speichern fehlgeschlagen', 'OK', { duration: 3000 });
        this.loadPosts();
      }
    });
  }

  remove(row: WeeklyPostRow): void {
    if (!confirm(`Wochenpost „${row.title}" löschen?`)) return;
    this.weekly.delete(row.id).subscribe({
      next: () => {
        this.snackBar.open('Wochenpost gelöscht', 'OK', { duration: 3000 });
        this.loadPosts();
      },
      error: () => this.snackBar.open('Löschen fehlgeschlagen', 'OK', { duration: 3000 })
    });
  }
}
