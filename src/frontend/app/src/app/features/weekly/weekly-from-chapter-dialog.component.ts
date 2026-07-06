import { Component, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { CourseService, CourseListItem, CourseChapter } from '../courses/course.service';
import { WeeklyService } from './weekly.service';
import { SnackbarService } from '../../core/snackbar.service';

/** Prefill für den Dialog: vorgeschlagener Termin (wie beim PGN-Upload). */
export interface WeeklyFromChapterDialogData {
  date: string;   // YYYY-MM-DD
  time: string;   // HH:mm
}

/**
 * Admin-Dialog „Wochenpost aus Buch-Kapitel": Buch wählen → dessen Kapitel laden → ein Kapitel wählen,
 * Termin/Titel/Beschreibung setzen, anlegen. Der Wochenpost spiegelt dann live die Puzzles dieses Kapitels.
 */
@Component({
  selector: 'app-weekly-from-chapter-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatSelectModule,
    MatInputModule, MatButtonModule, MatIconModule, TranslateModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'weekly.fromChapter.title' | translate }}</h2>
    <mat-dialog-content>
      <div class="fc-form">
        <mat-form-field appearance="outline">
          <mat-label>{{ 'weekly.fromChapter.book' | translate }}</mat-label>
          <mat-select [(ngModel)]="bookId" (selectionChange)="onBookChange()" [disabled]="loadingBooks">
            @for (b of books; track b.bookId) {
              <mat-option [value]="b.bookId">{{ b.displayName }} ({{ b.puzzleCount }})</mat-option>
            }
          </mat-select>
          @if (loadingBooks) { <mat-hint>{{ 'common.loading' | translate }}</mat-hint> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'weekly.fromChapter.chapter' | translate }}</mat-label>
          <mat-select [(ngModel)]="chapterIndex" [disabled]="!bookId || loadingChapters">
            @for (c of chapters; track c.index) {
              <mat-option [value]="c.index">{{ c.name || ('weekly.fromChapter.noChapter' | translate) }} ({{ c.puzzleCount }})</mat-option>
            }
          </mat-select>
          @if (loadingChapters) { <mat-hint>{{ 'common.loading' | translate }}</mat-hint> }
          @else if (bookId && chapters.length === 0) { <mat-hint>{{ 'weekly.fromChapter.noChapters' | translate }}</mat-hint> }
        </mat-form-field>

        <div class="fc-row">
          <mat-form-field appearance="outline" class="fc-date">
            <mat-label>{{ 'weekly.fields.date' | translate }}</mat-label>
            <input matInput type="date" [(ngModel)]="date">
          </mat-form-field>
          <mat-form-field appearance="outline" class="fc-time">
            <mat-label>{{ 'weekly.fields.time' | translate }}</mat-label>
            <input matInput type="time" [(ngModel)]="time">
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'weekly.fields.titleOptional' | translate }}</mat-label>
          <input matInput [(ngModel)]="title" [placeholder]="'weekly.fromChapter.titlePlaceholder' | translate">
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ 'weekly.fields.descriptionOptional' | translate }}</mat-label>
          <input matInput [(ngModel)]="description" maxlength="500"
                 [placeholder]="'weekly.upload.descriptionPlaceholder' | translate">
        </mat-form-field>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close()">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary"
              [disabled]="!canCreate() || saving" (click)="create()">
        <mat-icon>add</mat-icon> {{ 'weekly.fromChapter.create' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .fc-form { display: flex; flex-direction: column; gap: 8px; min-width: 340px; }
    .fc-row { display: flex; gap: 12px; }
    .fc-date, .fc-time { flex: 1; }
    @media (max-width: 480px) { .fc-form { min-width: 0; } }
  `],
})
export class WeeklyFromChapterDialogComponent implements OnInit {
  books: CourseListItem[] = [];
  chapters: CourseChapter[] = [];
  bookId: number | null = null;
  chapterIndex: number | null = null;
  date = '';
  time = '19:00';
  title = '';
  description = '';
  loadingBooks = false;
  loadingChapters = false;
  saving = false;

  constructor(
    public ref: MatDialogRef<WeeklyFromChapterDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: WeeklyFromChapterDialogData,
    private courses: CourseService,
    private weekly: WeeklyService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {
    this.date = data?.date ?? '';
    this.time = data?.time ?? '19:00';
  }

  ngOnInit(): void {
    this.loadingBooks = true;
    this.courses.getCourses().subscribe({
      next: list => { this.books = list; this.loadingBooks = false; },
      error: () => { this.loadingBooks = false; this.snackbar.info(this.translate.instant('weekly.fromChapter.loadBooksFailed'), { action: 'common.ok', duration: 3000 }); },
    });
  }

  onBookChange(): void {
    this.chapterIndex = null;
    this.chapters = [];
    if (!this.bookId) return;
    this.loadingChapters = true;
    this.courses.getChapters(this.bookId).subscribe({
      next: ch => { this.chapters = ch; this.loadingChapters = false; },
      error: () => { this.loadingChapters = false; this.snackbar.info(this.translate.instant('weekly.fromChapter.loadChaptersFailed'), { action: 'common.ok', duration: 3000 }); },
    });
  }

  canCreate(): boolean {
    return this.bookId != null && this.chapterIndex != null && !!this.date && !!this.time;
  }

  create(): void {
    if (!this.canCreate()) return;
    this.saving = true;
    const scheduledAt = `${this.date}T${this.time}:00`;
    this.weekly.createFromChapter(this.bookId!, this.chapterIndex!, scheduledAt, this.title.trim() || undefined, this.description.trim() || undefined)
      .subscribe({
        next: post => { this.saving = false; this.ref.close(post); },
        error: err => {
          this.saving = false;
          this.snackbar.info(err.error?.message || this.translate.instant('weekly.fromChapter.createFailed'), { action: 'common.ok', duration: 4000 });
        },
      });
  }
}
