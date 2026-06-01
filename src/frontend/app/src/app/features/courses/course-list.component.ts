import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { CourseService, CourseListItem } from './course.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';

@Component({
  selector: 'app-course-list',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressBarModule, MatTooltipModule, MatSnackBarModule, LoadingSpinnerComponent, TranslateModule
  ],
  template: `
    <div class="courses-container">
      <h1>{{ 'courses.title' | translate }}</h1>
      <p class="intro">{{ 'courses.intro' | translate }}</p>

      @if (loading) {
        <app-loading-spinner />
      } @else if (courses.length === 0) {
        <p class="empty-hint">{{ 'courses.emptyHint' | translate }}</p>
      } @else {
        <div class="course-grid">
          @for (c of courses; track c.bookId) {
            <mat-card class="course-card">
              <mat-card-header>
                <mat-card-title>{{ c.displayName }}</mat-card-title>
                <mat-card-subtitle>
                  {{ 'courses.puzzleCount' | translate:{ count: c.puzzleCount } }}
                  @if (c.difficulty) { · {{ c.difficulty }} }
                  @if (c.rating) { · {{ c.rating }}/10 }
                </mat-card-subtitle>
              </mat-card-header>
              <mat-card-content>
                <div class="progress-row">
                  <mat-progress-bar mode="determinate" [value]="c.progressPercent"></mat-progress-bar>
                  <span class="progress-label">{{ c.solvedCount }}/{{ c.puzzleCount }} ({{ c.progressPercent }}%)</span>
                </div>
                @if (c.puzzleCount > 0 && c.solvedCount >= c.puzzleCount) {
                  <p class="done-hint"><mat-icon>emoji_events</mat-icon> {{ 'courses.completed' | translate }}</p>
                }
              </mat-card-content>
              <mat-card-actions class="course-actions">
                <button mat-raised-button color="primary"
                        [routerLink]="['/courses', c.bookId, 'sequential']" [disabled]="c.puzzleCount === 0">
                  <mat-icon>format_list_numbered</mat-icon> {{ 'courses.sequential' | translate }}
                </button>
                <button mat-stroked-button
                        [routerLink]="['/courses', c.bookId, 'random']" [disabled]="c.puzzleCount === 0">
                  <mat-icon>shuffle</mat-icon> {{ 'courses.random' | translate }}
                </button>
                <span class="spacer"></span>
                <button mat-icon-button [matTooltip]="'courses.resetTooltip' | translate"
                        [disabled]="c.solvedCount === 0" (click)="reset(c)">
                  <mat-icon>restart_alt</mat-icon>
                </button>
              </mat-card-actions>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .courses-container { max-width: 1100px; margin: 24px auto; padding: 0 16px; }
    .intro { color: #666; margin-bottom: 16px; }
    .empty-hint { color: #666; font-style: italic; padding: 16px 0; }
    .course-grid {
      display: grid; gap: 16px;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
    }
    .course-card { display: flex; flex-direction: column; }
    .progress-row { display: flex; align-items: center; gap: 10px; margin: 8px 0 4px; }
    .progress-row mat-progress-bar { flex: 1; }
    .progress-label { font-variant-numeric: tabular-nums; font-size: 0.85rem; color: #444; white-space: nowrap; }
    .done-hint { display: flex; align-items: center; gap: 4px; color: #2e7d32; font-weight: 500; margin: 4px 0 0; }
    .done-hint mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .course-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .spacer { flex: 1 1 auto; }
  `]
})
export class CourseListComponent implements OnInit {
  courses: CourseListItem[] = [];
  loading = false;

  constructor(private courseService: CourseService, private snackBar: MatSnackBar, private translate: TranslateService) {}

  ngOnInit(): void {
    this.loadCourses();
  }

  loadCourses(): void {
    this.loading = true;
    this.courseService.getCourses().subscribe({
      next: courses => {
        this.courses = courses;
        this.loading = false;
      },
      error: () => {
        this.snackBar.open(this.translate.instant('courses.loadFailed'), this.translate.instant('common.ok'), { duration: 3000 });
        this.loading = false;
      }
    });
  }

  reset(course: CourseListItem): void {
    if (!confirm(this.translate.instant('courses.resetConfirm', { name: course.displayName }))) return;
    this.courseService.reset(course.bookId).subscribe({
      next: p => {
        course.solvedCount = p.solvedCount;
        course.progressPercent = p.progressPercent;
      },
      error: () => this.snackBar.open(this.translate.instant('courses.resetFailed'), this.translate.instant('common.ok'), { duration: 3000 })
    });
  }
}
