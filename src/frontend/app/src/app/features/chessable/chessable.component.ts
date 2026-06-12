import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { CourseService } from '../courses/course.service';
import {
  ChessableService,
  ChessableCredential,
  ChessableCourse,
  ChessableTestResult,
  ChessableImport,
  ChessableImportTarget,
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
    MatProgressSpinnerModule,
    TranslateModule,
  ],
  template: `
    <div class="container">
      <h1>{{ 'chessable.title' | translate }}</h1>
      <p class="intro">{{ 'chessable.intro' | translate }}</p>

      @if (disclaimerAccepted === null) {
        <mat-progress-spinner mode="indeterminate" diameter="36"></mat-progress-spinner>
      } @else if (!disclaimerAccepted) {
        <mat-card class="disclaimer-card">
          <mat-card-content>
            <h2 class="disclaimer-title"><mat-icon>warning</mat-icon> {{ 'chessable.disclaimerTitle' | translate }}</h2>
            <p class="disclaimer-text">{{ 'chessable.disclaimerText' | translate }}</p>
            <div class="actions">
              <button mat-raised-button color="primary" (click)="acceptDisclaimer()">
                {{ 'chessable.disclaimerAccept' | translate }}
              </button>
              <button mat-stroked-button (click)="declineDisclaimer()">
                {{ 'chessable.disclaimerDecline' | translate }}
              </button>
            </div>
          </mat-card-content>
        </mat-card>
      } @else {
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
              <mat-hint>
                <a href="#" (click)="$event.preventDefault(); showHelp = !showHelp">{{ 'chessable.helpLink' | translate }}</a>
              </mat-hint>
            </mat-form-field>

            @if (showHelp) {
              <div class="help-panel">
                <p>{{ 'chessable.helpIntro' | translate }}</p>
                <ol>
                  <li>{{ 'chessable.helpStep1' | translate }}</li>
                  <li>{{ 'chessable.helpStep2' | translate }}</li>
                  <li>{{ 'chessable.helpStep3' | translate }}</li>
                </ol>
                <p class="help-note">{{ 'chessable.helpNote' | translate }}</p>
              </div>
            }

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
                      (click)="loadCourses(true)">
                <mat-icon>refresh</mat-icon>
                {{ (loadingCourses ? 'chessable.loadingCourses' : 'chessable.refreshCourses') | translate }}
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
            @if (coursesCachedAt) {
              <mat-card-subtitle>{{ 'chessable.coursesCachedAt' | translate: { date: (coursesCachedAt | date:'short') } }}</mat-card-subtitle>
            }
          </mat-card-header>
          <mat-card-content>
            @if (courses.length === 0) {
              <p class="empty">{{ 'chessable.noCourses' | translate }}</p>
            } @else {
              <div class="course-list">
                @for (c of courses; track c.bid) {
                  <div class="course-row">
                    <div class="course-info">
                      <mat-icon>menu_book</mat-icon>
                      <div class="course-text">
                        <div class="course-name">{{ c.name }}</div>
                        <div class="bid">bid {{ c.bid }}</div>
                      </div>
                    </div>
                    <div class="course-actions">
                      @if (activeImports[c.bid]; as imp) {
                        <mat-progress-spinner mode="indeterminate" diameter="20"></mat-progress-spinner>
                        <span class="phase">{{ statusLabel(imp) }}</span>
                      } @else {
                        <ng-container>
                          @if (c.importedRepertoire) {
                            <span class="done-badge"><mat-icon>check_circle</mat-icon> {{ 'chessable.doneRepertoire' | translate }}</span>
                          } @else {
                            <button mat-stroked-button (click)="importCourse(c, 'repertoire')">
                              <mat-icon>library_books</mat-icon> {{ 'chessable.importRepertoire' | translate }}
                            </button>
                          }
                          @if (c.importedBook) {
                            <span class="done-badge"><mat-icon>check_circle</mat-icon> {{ 'chessable.doneBook' | translate }}</span>
                          } @else {
                            <button mat-stroked-button (click)="importCourse(c, 'book')">
                              <mat-icon>school</mat-icon> {{ 'chessable.importBook' | translate }}
                            </button>
                          }
                        </ng-container>
                      }
                    </div>
                  </div>
                }
              </div>
            }
          </mat-card-content>
        </mat-card>
      }
      }
    </div>
  `,
  styles: [`
    .container { max-width: 760px; margin: 0 auto; padding: 1rem; }
    h1 { margin-bottom: 0.25rem; }
    .intro { color: var(--mat-sys-on-surface-variant, #666); margin-bottom: 1.25rem; }
    .disclaimer-card { border-left: 4px solid #c62828; }
    .disclaimer-title { display: flex; align-items: center; gap: 0.5rem; color: #c62828; margin-top: 0; }
    .disclaimer-text { font-size: 1.02rem; line-height: 1.5; }
    .help-panel { margin: 0.25rem 0 0.75rem; padding: 0.6rem 0.9rem; border-radius: 8px;
      background: var(--mat-sys-surface-container-high, #eef); font-size: 0.9rem; }
    .help-panel ol { margin: 0.4rem 0; padding-left: 1.2rem; }
    .help-panel li { margin: 0.2rem 0; }
    .help-note { color: var(--mat-sys-on-surface-variant, #777); font-style: italic; margin-bottom: 0; }
    .status { display: flex; align-items: center; gap: 0.5rem; margin: 0 0 0.75rem; }
    .status mat-icon.ok { color: #2e7d32; }
    .status mat-icon.neutral { color: var(--mat-sys-on-surface-variant, #888); }
    .bearer-field { width: 100%; }
    .bearer-field textarea { font-family: monospace; font-size: 0.85rem; word-break: break-all; }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.5rem; }
    .actions button mat-icon { margin-right: 0.25rem; }
    .courses-card { margin-top: 1rem; }
    .empty { color: var(--mat-sys-on-surface-variant, #888); }

    .course-list { display: flex; flex-direction: column; }
    .course-row { display: flex; align-items: center; justify-content: space-between; gap: 1rem;
      padding: 0.6rem 0; border-bottom: 1px solid var(--mat-sys-outline-variant, #e3e3e3); flex-wrap: wrap; }
    .course-row:last-child { border-bottom: none; }
    .course-info { display: flex; align-items: center; gap: 0.6rem; min-width: 0; flex: 1 1 240px; }
    .course-text { min-width: 0; }
    .course-name { font-weight: 500; overflow-wrap: anywhere; }
    .bid { font-family: monospace; font-size: 0.78rem; color: var(--mat-sys-on-surface-variant, #888); }
    .course-actions { display: flex; align-items: center; gap: 0.4rem; flex-shrink: 0; }
    .course-actions button mat-icon { margin-right: 0.2rem; }
    .course-actions .phase { font-size: 0.82rem; color: var(--mat-sys-on-surface-variant, #888); white-space: nowrap; }
    .done-badge { display: inline-flex; align-items: center; gap: 0.2rem; font-size: 0.82rem; color: #2e7d32; }
    .done-badge mat-icon { font-size: 1.05rem; width: 1.05rem; height: 1.05rem; }
  `]
})
export class ChessableComponent implements OnInit, OnDestroy {
  credentials: ChessableCredential | null = null;
  bearerInput = '';
  courses: ChessableCourse[] | null = null;
  coursesCachedAt: string | null = null;

  /** null = noch nicht geprüft, false = Disclaimer offen, true = bestätigt. */
  disclaimerAccepted: boolean | null = null;
  showHelp = false;

  loadingStatus = true;
  saving = false;
  testing = false;
  loadingCourses = false;

  /** Laufende/wartende Importe, je Kurs-bid. Mehrere gleichzeitig möglich — der Server
   *  arbeitet sie sequenziell ab; wartende stehen auf Phase "queued". */
  activeImports: Record<string, ChessableImport> = {};
  private pollSub?: Subscription;

  constructor(
    private chessable: ChessableService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private router: Router,
    private courseService: CourseService,
  ) {}

  ngOnInit(): void {
    // Erst den (in der DB gespeicherten) Disclaimer-Status prüfen — ohne Bestätigung kein Zugriff.
    this.chessable.getDisclaimer().subscribe({
      next: r => {
        this.disclaimerAccepted = r.accepted;
        if (r.accepted) this.proceed();
      },
      error: () => { this.disclaimerAccepted = false; }
    });
  }

  private proceed(): void {
    this.refresh();
    this.loadActiveImports();
  }

  acceptDisclaimer(): void {
    this.chessable.acceptDisclaimer().subscribe({
      next: () => { this.disclaimerAccepted = true; this.proceed(); },
      error: e => this.showError(e)
    });
  }

  declineDisclaimer(): void {
    this.router.navigate(['/dashboard']);
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  /** Beim Laden der Seite noch laufende/wartende Importe übernehmen + Polling aufnehmen. */
  private loadActiveImports(): void {
    this.chessable.getImports().subscribe({
      next: list => {
        for (const imp of list.filter(i => i.status === 'running')) {
          this.activeImports[imp.bid] = imp;
        }
        if (this.hasActive()) this.ensurePolling();
      },
      error: () => { /* nicht kritisch */ }
    });
  }

  private refresh(): void {
    this.loadingStatus = true;
    this.chessable.getCredentials().subscribe({
      next: c => {
        this.credentials = c;
        this.loadingStatus = false;
        if (c.hasCredentials) this.loadCourses(false); // gecachte Liste automatisch zeigen
      },
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
        this.loadCourses(true); // neuen Account → Kursliste frisch holen + cachen
      },
      error: e => { this.saving = false; this.showError(e); }
    });
  }

  remove(): void {
    this.chessable.deleteCredentials().subscribe({
      next: () => {
        this.credentials = { hasCredentials: false, maskedBearer: null };
        this.courses = null;
        this.coursesCachedAt = null;
        this.activeImports = {};
        this.stopPolling();
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

  loadCourses(refresh = false): void {
    this.loadingCourses = true;
    this.chessable.getCourses(refresh).subscribe({
      next: res => {
        this.courses = res.courses;
        this.coursesCachedAt = res.cachedAt;
        this.loadingCourses = false;
      },
      error: e => { this.loadingCourses = false; this.showError(e); }
    });
  }

  importCourse(c: ChessableCourse, target: ChessableImportTarget): void {
    if (this.activeImports[c.bid]) return; // dieser Kurs ist schon in Arbeit/Warteschlange
    // Optimistischer Platzhalter (id 0) → Zeile zeigt sofort „in Warteschlange".
    this.activeImports[c.bid] = {
      id: 0, bid: c.bid, courseName: c.name, target, status: 'running', phase: 'queued',
      error: null, resultId: null, imported: 0, skipped: 0, invalid: 0,
      chaptersDone: 0, chaptersTotal: 0, linesDone: 0,
    };
    this.chessable.startImport(c.bid, target, c.name).subscribe({
      next: imp => { this.activeImports[c.bid] = imp; this.ensurePolling(); },
      error: e => { delete this.activeImports[c.bid]; this.showError(e); }
    });
  }

  /** Text für den Zeilen-Status (Phase + ggf. Kapitel/Linien-Fortschritt). */
  statusLabel(imp: ChessableImport): string {
    let s = this.translate.instant('chessable.phase_' + (imp.phase || 'queued'));
    if (imp.phase === 'fetching' && imp.chaptersTotal > 0) {
      s += ' ' + this.translate.instant('chessable.fetchProgress',
        { ch: imp.chaptersDone, total: imp.chaptersTotal, lines: imp.linesDone });
    }
    return s;
  }

  private hasActive(): boolean {
    return Object.keys(this.activeImports).length > 0;
  }

  private ensurePolling(): void {
    if (this.pollSub) return;
    this.pollSub = timer(2000, 2500).subscribe(() => this.pollActive());
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = undefined;
  }

  private pollActive(): void {
    this.chessable.getImports().subscribe({
      next: list => {
        const byId = new Map(list.map(i => [i.id, i]));
        for (const bid of Object.keys(this.activeImports)) {
          const cur = this.activeImports[bid];
          if (!cur.id) continue; // Platzhalter — wartet noch auf die Start-Antwort
          const upd = byId.get(cur.id);
          if (!upd) continue;
          if (upd.status === 'running') {
            this.activeImports[bid] = upd;
          } else {
            delete this.activeImports[bid];
            this.notifyDone(upd);
          }
        }
        if (!this.hasActive()) this.stopPolling();
      },
      error: () => { /* nächster Tick versucht es erneut */ }
    });
  }

  private notifyDone(imp: ChessableImport): void {
    if (imp.status === 'completed') {
      // Kurs-Flag setzen → Button wird sofort zum „erledigt"-Badge (ohne Reload).
      const course = this.courses?.find(c => c.bid === imp.bid);
      if (course) {
        if (imp.target === 'book') course.importedBook = true;
        else course.importedRepertoire = true;
      }
      // Buch importiert → „Kurse"-Menü könnte jetzt sichtbar werden: Navbar neu prüfen lassen.
      if (imp.target === 'book') this.courseService.notifyAccessChanged();
      const key = imp.target === 'book' ? 'chessable.importBookDone' : 'chessable.importRepertoireDone';
      this.snackbar.success(this.translate.instant(key, { name: imp.courseName, count: imp.imported }));
    } else {
      this.snackbar.info(this.translate.instant('chessable.importFailed', { error: imp.error ?? '' }));
    }
  }

  private showError(err: any): void {
    const message = err?.error?.message ?? err?.message ?? String(err);
    this.snackbar.info(this.translate.instant('chessable.error', { message }));
  }
}
