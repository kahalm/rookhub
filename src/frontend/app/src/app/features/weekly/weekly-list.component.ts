import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SnackbarService } from '../../core/snackbar.service';
import { RouterModule } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { WeeklyService, WeeklyPost, WeeklyProgress, nextWeeklySlot, weeklyDatePart, weeklyTimePart } from './weekly.service';
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
    MatTableModule, MatFormFieldModule, MatInputModule,
    TranslateModule, LoadingSpinnerComponent
  ],
  template: `
    <div class="weekly-container">
      <h1>{{ 'weekly.title' | translate }}</h1>
      <p class="intro">{{ 'weekly.intro' | translate }}</p>

      @if (auth.isAdmin) {
        <mat-card class="upload-card">
          <mat-card-header><mat-card-title>{{ 'weekly.upload.title' | translate }}</mat-card-title></mat-card-header>
          <mat-card-content>
            <div class="upload-row">
              <input #pgnInput type="file" accept=".pgn" hidden (change)="onFileSelected($event)">
              <button mat-stroked-button (click)="pgnInput.click()">
                <mat-icon>upload_file</mat-icon> {{ uploadFileName || ('weekly.upload.choosePgn' | translate) }}
              </button>
              <mat-form-field appearance="outline" class="f-date">
                <mat-label>{{ 'weekly.fields.date' | translate }}</mat-label>
                <input matInput type="date" [(ngModel)]="uploadDate">
              </mat-form-field>
              <mat-form-field appearance="outline" class="f-time">
                <mat-label>{{ 'weekly.fields.time' | translate }}</mat-label>
                <input matInput type="time" [(ngModel)]="uploadTime">
              </mat-form-field>
              <mat-form-field appearance="outline" class="f-title">
                <mat-label>{{ 'weekly.fields.titleOptional' | translate }}</mat-label>
                <input matInput [(ngModel)]="uploadTitle" [placeholder]="'weekly.upload.titlePlaceholder' | translate">
              </mat-form-field>
              <button mat-raised-button color="primary"
                      [disabled]="!uploadFile || !uploadDate || !uploadTime || uploading" (click)="upload()">
                <mat-icon>add</mat-icon> {{ 'weekly.upload.create' | translate }}
              </button>
            </div>
            <p class="upload-hint">{{ 'weekly.upload.hint' | translate }}</p>
          </mat-card-content>
        </mat-card>
      }

      @if (loading) {
        <app-loading-spinner />
      } @else if (rows.length === 0) {
        <p class="empty-hint">{{ 'weekly.empty' | translate }}</p>
      } @else {
        <table mat-table [dataSource]="rows" class="full-width">
          <ng-container matColumnDef="scheduled">
            <th mat-header-cell *matHeaderCellDef>{{ 'weekly.columns.scheduled' | translate }}</th>
            <td mat-cell *matCellDef="let r">
              @if (auth.isAdmin) {
                <input type="date" class="inline-date" [(ngModel)]="r.editDate" (change)="savePost(r)">
                <input type="time" class="inline-time" [(ngModel)]="r.editTime" (change)="savePost(r)">
              } @else {
                {{ r.scheduledAt | date:'EEEE, dd.MM.yyyy' }} · {{ r.scheduledAt | date:'HH:mm' }} {{ 'weekly.oClock' | translate }}
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="title">
            <th mat-header-cell *matHeaderCellDef>{{ 'weekly.columns.title' | translate }}</th>
            <td mat-cell *matCellDef="let r">
              @if (auth.isAdmin) {
                <input class="inline-title" [(ngModel)]="r.title" (change)="savePost(r)">
              } @else {
                {{ r.title }}
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="progress">
            <th mat-header-cell *matHeaderCellDef>{{ 'weekly.columns.progress' | translate }}</th>
            <td mat-cell *matCellDef="let r">
              @if (prog[r.id]; as p) {
                <span class="wp-prog">
                  <span class="wp-solved" [attr.title]="'weekly.progress.solvedLabel' | translate">✓ {{ p.solvedCount }}</span>
                  <span class="wp-slash">/</span>
                  <span class="wp-failed" [attr.title]="'weekly.progress.failedLabel' | translate">✗ {{ p.playedCount - p.solvedCount }}</span>
                  <span class="wp-pct" [attr.title]="'weekly.progress.doneLabel' | translate">· {{ pct(p) }}%</span>
                </span>
              } @else {
                <span class="wp-none">—</span>
              }
            </td>
          </ng-container>
          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>{{ 'weekly.columns.actions' | translate }}</th>
            <td mat-cell *matCellDef="let r">
              <button mat-stroked-button color="primary" [routerLink]="['/weekly', r.id]">
                <mat-icon>play_arrow</mat-icon> {{ 'weekly.play' | translate }}
              </button>
              @if (auth.isAdmin) {
                <button mat-icon-button color="warn" (click)="remove(r)" [attr.title]="'common.delete' | translate">
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
    .wp-prog { white-space: nowrap; font-variant-numeric: tabular-nums; }
    .wp-solved { color: #2e7d32; font-weight: 600; }
    .wp-failed { color: #c62828; font-weight: 600; }
    .wp-slash { color: #999; margin: 0 4px; }
    .wp-pct { color: #555; margin-left: 6px; }
    .wp-none { color: #bbb; }
  `]
})
export class WeeklyListComponent implements OnInit {
  rows: WeeklyPostRow[] = [];
  loading = false;
  columns = ['scheduled', 'title', 'progress', 'actions'];
  /** Per-User-Fortschritt je WeeklyPost-Id (nur Posts mit Versuchen). */
  prog: Record<number, WeeklyProgress> = {};

  uploadFile: File | null = null;
  uploadFileName = '';
  uploadDate = '';
  uploadTime = '19:00';
  uploadTitle = '';
  uploading = false;

  constructor(
    public auth: AuthService,
    private weekly: WeeklyService,
    private snackbar: SnackbarService,
    private translate: TranslateService
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
        this.loadProgress();
      },
      error: () => {
        this.snackbar.info(this.translate.instant('weekly.loadFailed'), { action: 'common.ok', duration: 3000 });
        this.loading = false;
      }
    });
  }

  /** Lädt den eigenen Fortschritt je Post (für die Spalte „gelöst/failed · %"). */
  private loadProgress(): void {
    if (!this.auth.isLoggedIn) return;
    this.weekly.getAllProgress().subscribe({
      next: list => {
        const map: Record<number, WeeklyProgress> = {};
        for (const p of list) map[p.weeklyPostId] = p;
        this.prog = map;
      },
      error: () => { /* Fortschritt ist optional — Übersicht funktioniert auch ohne */ }
    });
  }

  /** Prozent gespielt (von allen Puzzles des Posts). */
  pct(p: WeeklyProgress): number {
    return p.total > 0 ? Math.round(100 * p.playedCount / p.total) : 0;
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
        this.snackbar.info(this.translate.instant('weekly.created'), { action: 'common.ok', duration: 3000 });
        this.uploading = false;
        this.uploadFile = null;
        this.uploadFileName = '';
        this.uploadTitle = '';
        this.loadPosts();   // lädt neu + setzt nächsten Termin-Vorschlag
      },
      error: err => {
        this.snackbar.info(err.error?.message || this.translate.instant('weekly.uploadFailed'), { action: 'common.ok', duration: 4000 });
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
        this.snackbar.info(err.error?.message || this.translate.instant('weekly.saveFailed'), { action: 'common.ok', duration: 3000 });
        this.loadPosts();
      }
    });
  }

  remove(row: WeeklyPostRow): void {
    if (!confirm(this.translate.instant('weekly.deleteConfirm', { title: row.title }))) return;
    this.weekly.delete(row.id).subscribe({
      next: () => {
        this.snackbar.info(this.translate.instant('weekly.deleted'), { action: 'common.ok', duration: 3000 });
        this.loadPosts();
      },
      error: () => this.snackbar.info(this.translate.instant('weekly.deleteFailed'), { action: 'common.ok', duration: 3000 })
    });
  }
}
