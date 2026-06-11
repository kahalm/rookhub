import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import {
  ChessableService,
  ChessableCredential,
  ChessableCourse,
  ChessableTestResult,
} from './chessable.service';

@Component({
  selector: 'app-chessable',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    TranslateModule,
  ],
  template: `
    <div class="container">
      <h1>{{ 'chessable.title' | translate }}</h1>
      <p class="intro">{{ 'chessable.intro' | translate }}</p>

      <mat-card>
        <mat-card-content>
          @if (loadingStatus) {
            <mat-progress-spinner mode="indeterminate" diameter="32"></mat-progress-spinner>
          } @else {
            <p class="status">
              @if (credentials?.hasCredentials) {
                <mat-icon class="ok">check_circle</mat-icon>
                {{ 'chessable.currentToken' | translate: { masked: credentials!.maskedBearer } }}
              } @else {
                <mat-icon class="neutral">key_off</mat-icon>
                {{ 'chessable.noToken' | translate }}
              }
            </p>

            <mat-form-field appearance="outline" class="bearer-field">
              <mat-label>{{ 'chessable.bearerLabel' | translate }}</mat-label>
              <textarea matInput [(ngModel)]="bearerInput" rows="4" autocomplete="off"
                        [placeholder]="'eyJ0eXAiOi...'"></textarea>
              <mat-hint>{{ 'chessable.bearerHint' | translate }}</mat-hint>
            </mat-form-field>

            <div class="actions">
              <button mat-raised-button color="primary"
                      [disabled]="!bearerInput.trim() || saving"
                      (click)="save()">
                <mat-icon>save</mat-icon>
                {{ (saving ? 'chessable.saving' : 'chessable.save') | translate }}
              </button>

              <button mat-stroked-button
                      [disabled]="!credentials?.hasCredentials || testing"
                      (click)="test()">
                <mat-icon>cable</mat-icon>
                {{ (testing ? 'chessable.testing' : 'chessable.test') | translate }}
              </button>

              <button mat-stroked-button
                      [disabled]="!credentials?.hasCredentials || loadingCourses"
                      (click)="loadCourses()">
                <mat-icon>list</mat-icon>
                {{ (loadingCourses ? 'chessable.loadingCourses' : 'chessable.loadCourses') | translate }}
              </button>

              <button mat-stroked-button color="warn"
                      [disabled]="!credentials?.hasCredentials"
                      (click)="remove()">
                <mat-icon>delete</mat-icon>
                {{ 'chessable.delete' | translate }}
              </button>
            </div>
          }
        </mat-card-content>
      </mat-card>

      @if (courses !== null) {
        <mat-card class="courses-card">
          <mat-card-header>
            <mat-card-title>{{ 'chessable.coursesTitle' | translate }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            @if (courses.length === 0) {
              <p class="empty">{{ 'chessable.noCourses' | translate }}</p>
            } @else {
              <mat-list>
                @for (c of courses; track c.bid) {
                  <mat-list-item>
                    <mat-icon matListItemIcon>menu_book</mat-icon>
                    <span matListItemTitle>{{ c.name }}</span>
                    <span matListItemLine class="bid">bid {{ c.bid }}</span>
                  </mat-list-item>
                }
              </mat-list>
            }
          </mat-card-content>
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .container { max-width: 760px; margin: 0 auto; padding: 1rem; }
    h1 { margin-bottom: 0.25rem; }
    .intro { color: var(--mat-sys-on-surface-variant, #666); margin-bottom: 1.25rem; }
    .status { display: flex; align-items: center; gap: 0.5rem; margin: 0 0 0.75rem; }
    .status mat-icon.ok { color: #2e7d32; }
    .status mat-icon.neutral { color: var(--mat-sys-on-surface-variant, #888); }
    .bearer-field { width: 100%; }
    .bearer-field textarea { font-family: monospace; font-size: 0.85rem; word-break: break-all; }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.5rem; }
    .actions button mat-icon { margin-right: 0.25rem; }
    .courses-card { margin-top: 1rem; }
    .empty { color: var(--mat-sys-on-surface-variant, #888); }
    .bid { font-family: monospace; font-size: 0.8rem; color: var(--mat-sys-on-surface-variant, #888); }
  `]
})
export class ChessableComponent implements OnInit {
  credentials: ChessableCredential | null = null;
  bearerInput = '';
  courses: ChessableCourse[] | null = null;

  loadingStatus = true;
  saving = false;
  testing = false;
  loadingCourses = false;

  constructor(
    private chessable: ChessableService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.refresh();
  }

  private refresh(): void {
    this.loadingStatus = true;
    this.chessable.getCredentials().subscribe({
      next: c => { this.credentials = c; this.loadingStatus = false; },
      error: e => { this.loadingStatus = false; this.showError(e); }
    });
  }

  save(): void {
    const value = this.bearerInput.trim();
    if (!value) return;
    this.saving = true;
    this.chessable.saveCredentials(value).subscribe({
      next: c => {
        this.credentials = c;
        this.bearerInput = '';
        this.saving = false;
        this.snackbar.success(this.translate.instant('chessable.saved'));
      },
      error: e => { this.saving = false; this.showError(e); }
    });
  }

  remove(): void {
    this.chessable.deleteCredentials().subscribe({
      next: () => {
        this.credentials = { hasCredentials: false, maskedBearer: null };
        this.courses = null;
        this.snackbar.success(this.translate.instant('chessable.deleted'));
      },
      error: e => this.showError(e)
    });
  }

  test(): void {
    this.testing = true;
    this.chessable.test().subscribe({
      next: (r: ChessableTestResult) => {
        this.testing = false;
        this.snackbar.success(this.translate.instant('chessable.testOk', { uid: r.uid, count: r.courseCount }));
      },
      error: e => { this.testing = false; this.showError(e); }
    });
  }

  loadCourses(): void {
    this.loadingCourses = true;
    this.chessable.getCourses().subscribe({
      next: list => { this.courses = list; this.loadingCourses = false; },
      error: e => { this.loadingCourses = false; this.showError(e); }
    });
  }

  private showError(err: any): void {
    const message = err?.error?.message ?? err?.message ?? String(err);
    this.snackbar.info(this.translate.instant('chessable.error', { message }));
  }
}
