import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../courses/course.service';
import {
  ChessableService,
  ChessableCredential,
  ChessableCourse,
  ChessableTestResult,
  ChessableImport,
  ChessableAdminImport,
  ChessableImportTarget,
} from './chessable.service';

/**
 * Hol-Durchsatz (Prod-Messung 2026-06-15, inkl. VPN-Rotationspausen): grob ~15–20 Zeilen/min.
 * Für Schätzungen konservativ die Faustregel 500 Zeilen ≈ 30 Min (≈ 16,7/min) verwenden.
 */
export const CHESSABLE_LINES_PER_MIN = 500 / 30;

/** Kompakte Dauer aus Millisekunden: "1 h 5 min", "12 min", "45 s"; "—" bei ungültig/negativ. */
export function formatDuration(ms: number): string {
  if (!isFinite(ms) || ms < 0) return '—';
  const s = Math.floor(ms / 1000);
  if (s >= 3600) return `${Math.floor(s / 3600)} h ${Math.floor((s % 3600) / 60)} min`;
  if (s >= 60) return `${Math.floor(s / 60)} min`;
  return `${s} s`;
}

/** Lineare Hochrechnung der Gesamt-Zeilenzahl aus dem Kapitel-Fortschritt. 0 = (noch) nicht schätzbar. */
export function estimateTotalLines(linesDone: number, chaptersDone: number, chaptersTotal: number): number {
  if (linesDone <= 0 || chaptersDone <= 0 || chaptersTotal <= 0) return 0;
  return Math.round((linesDone * chaptersTotal) / chaptersDone);
}

/**
 * Geschätzte Rest-Holzeit in Minuten: Gesamt-Zeilen aus den bisher geholten Kapiteln hochrechnen,
 * Rest durch den Durchsatz teilen. 0 = nicht schätzbar (zu wenig Fortschritt). Aufgerundet.
 */
export function estimateRemainingMinutes(linesDone: number, chaptersDone: number, chaptersTotal: number): number {
  const total = estimateTotalLines(linesDone, chaptersDone, chaptersTotal);
  if (total <= 0) return 0;
  const remaining = Math.max(0, total - linesDone);
  return Math.ceil(remaining / CHESSABLE_LINES_PER_MIN);
}

/** Aktiver Import + EINMAL je Update vorberechnetes Statuslabel (statt `translate.instant`
 *  je CD-Zyklus pro Zeile während des 2,5-s-Pollings — analog Dashboard/Admin-Importliste). */
type ActiveImport = ChessableImport & { queueLabelText: string };

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
    MatTooltipModule,
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
              <button mat-raised-button color="primary" (click)="acceptDisclaimer()" [disabled]="disclaimerSubmitting">
                {{ 'chessable.disclaimerAccept' | translate }}
              </button>
              <button mat-stroked-button (click)="declineDisclaimer()">
                {{ 'chessable.disclaimerDecline' | translate }}
              </button>
            </div>
          </mat-card-content>
        </mat-card>
      } @else {
      @if (activeList().length > 0) {
        <mat-card class="queue-card">
          <mat-card-content>
            <h3 class="queue-title">{{ 'chessable.queueTitle' | translate }}</h3>
            @for (imp of activeList(); track imp.bid) {
              <div class="queue-row">
                @if (imp.status === 'running') {
                  <mat-progress-spinner mode="indeterminate" diameter="18"></mat-progress-spinner>
                } @else {
                  <mat-icon class="paused-icon">pause_circle</mat-icon>
                }
                <span class="queue-name">{{ imp.courseName || imp.bid }}</span>
                <span class="queue-status">{{ imp.queueLabelText }}</span>
                <span class="queue-actions">
                  @if (imp.status === 'paused') {
                    <button mat-icon-button [matTooltip]="'chessable.resume' | translate" (click)="resumeImport(imp)">
                      <mat-icon>play_arrow</mat-icon>
                    </button>
                  } @else {
                    <button mat-icon-button [matTooltip]="'chessable.pause' | translate" (click)="pauseImport(imp)">
                      <mat-icon>pause</mat-icon>
                    </button>
                  }
                  <button mat-icon-button [matTooltip]="'chessable.cancel' | translate" (click)="cancelImport(imp)">
                    <mat-icon>close</mat-icon>
                  </button>
                </span>
              </div>
            }
          </mat-card-content>
        </mat-card>
      }
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
                <p class="help-repcheck">
                  {{ 'chessable.helpRepcheck' | translate }}
                  RepCheck (<a href="https://chromewebstore.google.com/detail/mhddbldcaancdahlochjanpkkboaccpn" target="_blank" rel="noopener noreferrer">Chrome</a> /
                  <a href="https://addons.mozilla.org/de/firefox/addon/repcheck/" target="_blank" rel="noopener noreferrer">Firefox</a>).
                </p>

                @if (bookmarkletHref) {
                  <div class="bookmarklet-box">
                    <p class="bookmarklet-title"><mat-icon>bookmark_add</mat-icon> {{ 'chessable.bookmarkletTitle' | translate }}</p>
                    <p>{{ 'chessable.bookmarkletIntro' | translate }}</p>
                    <p class="bookmarklet-drag">
                      {{ 'chessable.bookmarkletDrag' | translate }}
                      <a class="bookmarklet-link" [href]="bookmarkletHref"
                         (click)="$event.preventDefault()" draggable="true">{{ 'chessable.bookmarkletLinkLabel' | translate }}</a>
                    </p>
                    <ol>
                      <li>{{ 'chessable.bookmarkletStep1' | translate }}</li>
                      <li>{{ 'chessable.bookmarkletStep2' | translate }}</li>
                      <li>{{ 'chessable.bookmarkletStep3' | translate }}</li>
                    </ol>
                  </div>
                }

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
            <p class="throughput-hint"><mat-icon>schedule</mat-icon> {{ 'chessable.throughputHint' | translate }}</p>
            @if (courses.length === 0) {
              <p class="empty">{{ 'chessable.noCourses' | translate }}</p>
            } @else {
              <div class="course-list">
                @for (c of courses; track c.bid) {
                  <div class="course-row">
                    <div class="course-info">
                      <mat-icon>menu_book</mat-icon>
                      <div class="course-text">
                        <div class="course-name">
                          {{ c.name }}
                          @if (c.cached) {
                            <mat-icon class="cached-badge" [matTooltip]="'chessable.cachedHint' | translate">bolt</mat-icon>
                          }
                        </div>
                        <div class="bid">bid {{ c.bid }}</div>
                      </div>
                    </div>
                    <div class="course-actions">
                      @if (activeImports[c.bid]; as imp) {
                        @if (imp.status === 'running') {
                          <mat-progress-spinner mode="indeterminate" diameter="20"></mat-progress-spinner>
                        }
                        <span class="phase">{{ imp.queueLabelText }}</span>
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

      @if (isAdmin && adminImports !== null) {
        <mat-card class="admin-imports-card">
          <mat-card-header>
            <mat-icon mat-card-avatar>admin_panel_settings</mat-icon>
            <mat-card-title>{{ 'chessable.adminImportsTitle' | translate }}</mat-card-title>
            <mat-card-subtitle>{{ 'chessable.adminImportsSubtitle' | translate }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            @if (adminImports.length === 0) {
              <p class="empty">{{ 'chessable.adminNoImports' | translate }}</p>
            } @else {
              <div class="admin-list">
                @for (imp of adminImports; track imp.id) {
                  <div class="admin-row" [class.active]="imp.status === 'running' || imp.status === 'paused'">
                    <span class="admin-user"><mat-icon>person</mat-icon> {{ imp.username }}</span>
                    <span class="admin-name">{{ imp.courseName || imp.bid }}</span>
                    <span class="admin-target">{{ ('chessable.target_' + imp.target) | translate }}</span>
                    <span class="admin-status" [attr.data-status]="imp.status">{{ imp.statusLabel }}</span>
                    @if (imp.durationLabel; as dur) { <span class="admin-duration">{{ dur }}</span> }
                    <span class="admin-date">{{ imp.createdAt | date:'short' }}</span>
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
    .help-repcheck { margin: 0.4rem 0 0.2rem; }
    .bookmarklet-box { margin: 0.6rem 0; padding: 0.5rem 0.75rem; border-radius: 8px;
      border: 1px dashed var(--mat-sys-outline, #b9b9c9); background: var(--mat-sys-surface, #fff); }
    .bookmarklet-title { display: flex; align-items: center; gap: 0.4rem; font-weight: 600; margin: 0 0 0.3rem; }
    .bookmarklet-title mat-icon { font-size: 1.15rem; width: 1.15rem; height: 1.15rem; }
    .bookmarklet-drag { margin: 0.4rem 0; }
    .bookmarklet-link { display: inline-block; margin-left: 0.3rem; padding: 0.2rem 0.6rem; border-radius: 6px;
      background: var(--mat-sys-primary, #3f51b5); color: var(--mat-sys-on-primary, #fff);
      font-weight: 600; text-decoration: none; cursor: grab; }
    .queue-card { margin-bottom: 1rem; border-left: 4px solid var(--mat-sys-primary, #3f51b5); }
    .queue-title { margin: 0 0 0.5rem; font-size: 1rem; }
    .queue-row { display: flex; align-items: center; gap: 0.6rem; padding: 0.3rem 0; flex-wrap: wrap; }
    .queue-row .queue-name { font-weight: 500; flex: 1 1 200px; min-width: 0; overflow-wrap: anywhere; }
    .queue-row .queue-status { font-size: 0.82rem; color: var(--mat-sys-on-surface-variant, #777); }
    .queue-row .queue-actions { display: flex; align-items: center; margin-left: auto; }
    .queue-row .paused-icon { color: var(--mat-sys-on-surface-variant, #999); }
    .status { display: flex; align-items: center; gap: 0.5rem; margin: 0 0 0.75rem; }
    .status mat-icon.ok { color: #2e7d32; }
    .status mat-icon.neutral { color: var(--mat-sys-on-surface-variant, #888); }
    .bearer-field { width: 100%; }
    .bearer-field textarea { font-family: monospace; font-size: 0.85rem; word-break: break-all; }
    .actions { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 0.5rem; }
    .actions button mat-icon { margin-right: 0.25rem; }
    .courses-card { margin-top: 1rem; }
    .empty { color: var(--mat-sys-on-surface-variant, #888); }
    .throughput-hint { display: flex; align-items: center; gap: 6px; color: var(--mat-sys-on-surface-variant, #888); font-size: 0.85rem; margin: 0 0 0.75rem; }
    .throughput-hint mat-icon { font-size: 18px; width: 18px; height: 18px; }

    .course-list { display: flex; flex-direction: column; }
    .course-row { display: flex; align-items: center; justify-content: space-between; gap: 1rem;
      padding: 0.6rem 0; border-bottom: 1px solid var(--mat-sys-outline-variant, #e3e3e3); flex-wrap: wrap; }
    .course-row:last-child { border-bottom: none; }
    .course-info { display: flex; align-items: center; gap: 0.6rem; min-width: 0; flex: 1 1 240px; }
    .course-text { min-width: 0; }
    .course-name { font-weight: 500; overflow-wrap: anywhere; }
    .cached-badge { color: #f9a825; font-size: 18px; width: 18px; height: 18px; vertical-align: text-bottom; margin-left: 4px; }
    .bid { font-family: monospace; font-size: 0.78rem; color: var(--mat-sys-on-surface-variant, #888); }
    .course-actions { display: flex; align-items: center; gap: 0.4rem; flex-shrink: 0; }
    .course-actions button mat-icon { margin-right: 0.2rem; }
    .course-actions .phase { font-size: 0.82rem; color: var(--mat-sys-on-surface-variant, #888); white-space: nowrap; }
    .done-badge { display: inline-flex; align-items: center; gap: 0.2rem; font-size: 0.82rem; color: #2e7d32; }
    .done-badge mat-icon { font-size: 1.05rem; width: 1.05rem; height: 1.05rem; }

    .admin-imports-card { margin-top: 1rem; border-left: 4px solid var(--mat-sys-tertiary, #7b1fa2); }
    .admin-list { display: flex; flex-direction: column; }
    .admin-row { display: grid; grid-template-columns: minmax(90px, 1fr) minmax(120px, 2fr) auto auto auto;
      align-items: center; gap: 0.5rem 0.75rem; padding: 0.45rem 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant, #e3e3e3); font-size: 0.88rem; }
    .admin-row:last-child { border-bottom: none; }
    .admin-row.active { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 7%, transparent); }
    .admin-row .admin-user { display: inline-flex; align-items: center; gap: 0.2rem; font-weight: 500; overflow-wrap: anywhere; }
    .admin-row .admin-user mat-icon { font-size: 1.05rem; width: 1.05rem; height: 1.05rem; color: var(--mat-sys-on-surface-variant, #888); }
    .admin-row .admin-name { overflow-wrap: anywhere; }
    .admin-row .admin-target { font-size: 0.8rem; color: var(--mat-sys-on-surface-variant, #777); }
    .admin-row .admin-status { font-size: 0.8rem; }
    .admin-row .admin-status[data-status="completed"] { color: #2e7d32; }
    .admin-row .admin-status[data-status="failed"] { color: #c62828; }
    .admin-row .admin-status[data-status="cancelled"] { color: var(--mat-sys-on-surface-variant, #888); }
    .admin-row .admin-duration { font-size: 0.78rem; color: var(--mat-sys-on-surface-variant, #888); white-space: nowrap; }
    .admin-row .admin-date { font-size: 0.78rem; color: var(--mat-sys-on-surface-variant, #888); white-space: nowrap; }
    @media (max-width: 600px) {
      .admin-row { grid-template-columns: 1fr auto; }
      .admin-row .admin-date { grid-column: 2; }
    }
  `]
})
export class ChessableComponent implements OnInit, OnDestroy {
  credentials: ChessableCredential | null = null;
  bearerInput = '';
  courses: ChessableCourse[] | null = null;
  coursesCachedAt: string | null = null;

  /** null = noch nicht geprüft, false = Disclaimer offen, true = bestätigt. */
  disclaimerAccepted: boolean | null = null;
  disclaimerSubmitting = false;
  showHelp = false;

  /** Per Bookmarklet via URL-Fragment (#chessbearer=…) übergebener Bearer, der nach
   *  bestätigtem Disclaimer automatisch gespeichert wird. */
  private pendingBearer: string | null = null;
  /** `javascript:`-Bookmarklet zum Ziehen in die Lesezeichenleiste (auf chessable.com geklickt). */
  bookmarkletHref: SafeUrl | null = null;

  loadingStatus = true;
  saving = false;
  testing = false;
  loadingCourses = false;

  /** Laufende/wartende Importe, je Kurs-bid. Mehrere gleichzeitig möglich — der Server
   *  arbeitet sie sequenziell ab; wartende stehen auf Phase "queued". `queueLabelText` ist das
   *  EINMAL je Update (statt je CD-Zyklus) vorberechnete Statuslabel (analog Dashboard/Admin-Liste). */
  activeImports: Record<string, ActiveImport> = {};
  private pollSub?: Subscription;

  /** Admin-Sicht: alle Importe aller User (Verlauf + aktive). null = (noch) nicht geladen / kein Admin. */
  adminImports: (ChessableAdminImport & { statusLabel: string; durationLabel: string })[] | null = null;
  private adminPollSub?: Subscription;

  constructor(
    private chessable: ChessableService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    private sanitizer: DomSanitizer,
    private router: Router,
    private courseService: CourseService,
    private auth: AuthService,
  ) {}

  get isAdmin(): boolean {
    return this.auth.isAdmin;
  }

  ngOnInit(): void {
    // Bearer-Übergabe vom Bookmarklet: kommt als URL-Fragment (#chessbearer=…), das
    // NIE an den Server geschickt wird. Sofort auslesen und aus URL/History tilgen.
    this.pendingBearer = parseChessbearerFragment(window.location.hash);
    if (this.pendingBearer) {
      history.replaceState(null, '', window.location.pathname + window.location.search);
    }

    // `javascript:`-Bookmarklet als vertrauenswürdige URL markieren. SICHERHEIT: das ist
    // unbedenklich, weil der Code AUSSCHLIESSLICH aus app-eigenen, nicht-nutzergesteuerten Werten
    // gebaut wird — Ziel = die eigene App-Origin (`window.location.origin`), die einzige
    // eingebettete Zeichenkette ist eine übersetzte Meldung, die `buildChessableBookmarklet`
    // einfach-quote-escaped. Es fließt KEIN Nutzer-Input ein. Als Defense-in-Depth wird das Ziel
    // zusätzlich gegen die eigene Origin geprüft, bevor der Sanitizer-Bypass greift.
    const target = `${window.location.origin}/chessable`;
    if (target.startsWith(window.location.origin)) {
      const code = buildChessableBookmarklet(
        target,
        this.translate.instant('chessable.bookmarkletNoLogin'),
      );
      this.bookmarkletHref = this.sanitizer.bypassSecurityTrustUrl(code);
    }

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
    // Admins sehen zusätzlich ALLE Importe aller User (Verlauf + aktive), live aktualisiert.
    if (this.isAdmin) this.startAdminPolling();
    // Per Bookmarklet übergebenen Bearer automatisch übernehmen, sobald der Disclaimer steht.
    if (this.pendingBearer) {
      this.bearerInput = this.pendingBearer;
      this.pendingBearer = null;
      this.snackbar.info(this.translate.instant('chessable.bookmarkletReceived'));
      this.save();
    }
  }


  acceptDisclaimer(): void {
    if (this.disclaimerSubmitting) return;   // Doppelsubmit verhindern
    this.disclaimerSubmitting = true;
    this.chessable.acceptDisclaimer().subscribe({
      next: () => { this.disclaimerAccepted = true; this.disclaimerSubmitting = false; this.proceed(); },
      error: e => { this.disclaimerSubmitting = false; this.showError(e); }
    });
  }

  declineDisclaimer(): void {
    this.router.navigate(['/dashboard']);
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.adminPollSub?.unsubscribe();
  }

  /** Admin: alle Importe laden + alle 5 s aktualisieren (zeigt aktive Queue + Verlauf live). */
  private startAdminPolling(): void {
    this.loadAdminImports();
    this.adminPollSub = timer(5000, 5000).subscribe(() => this.loadAdminImports());
  }

  private loadAdminImports(): void {
    this.chessable.getAllImportsAdmin().subscribe({
      // Labels EINMAL je Poll berechnen + cachen (statt je CD-Zyklus translate.instant pro Zeile).
      next: list => this.adminImports = list.map(imp => ({
        ...imp,
        statusLabel: this.adminStatusLabel(imp),
        durationLabel: this.importDurationLabel(imp),
      })),
      error: () => { /* nicht kritisch */ }
    });
  }

  /** Zeilen-Status in der Admin-Liste: aktive nutzen die Queue-/Phasen-Anzeige, erledigte den Endstatus. */
  adminStatusLabel(imp: ChessableAdminImport): string {
    if (imp.status === 'running' || imp.status === 'paused') return this.queueLabel(imp);
    return this.translate.instant('chessable.adminStatus_' + imp.status);
  }

  /** Beim Laden der Seite noch laufende/wartende Importe übernehmen + Polling aufnehmen. */
  private loadActiveImports(): void {
    this.chessable.getImports().subscribe({
      next: list => {
        for (const imp of list.filter(i => i.status === 'running' || i.status === 'paused')) {
          this.setActiveImport(imp);
        }
        if (this.hasRunning()) this.ensurePolling();
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
    this.setActiveImport({
      id: 0, bid: c.bid, courseName: c.name, target, status: 'running', phase: 'queued',
      error: null, resultId: null, imported: 0, skipped: 0, invalid: 0,
      chaptersDone: 0, chaptersTotal: 0, linesDone: 0, queuedAhead: 0,
      createdAt: new Date().toISOString(), startedAt: null, completedAt: null,
    });
    this.chessable.startImport(c.bid, target, c.name).subscribe({
      next: imp => {
        this.setActiveImport(imp);
        this.ensurePolling();
        const key = imp.queuedAhead > 0 ? 'chessable.queuePopup' : 'chessable.queueStarted';
        this.snackbar.info(this.translate.instant(key, { ahead: imp.queuedAhead, name: c.name }));
      },
      error: e => { delete this.activeImports[c.bid]; this.showError(e); }
    });
  }

  /** Text für den Zeilen-Status (Phase + ggf. Kapitel/Linien-Fortschritt). */
  statusLabel(imp: ChessableImport): string {
    let s = this.translate.instant('chessable.phase_' + (imp.phase || 'queued'));
    if (imp.phase === 'fetching' && imp.chaptersTotal > 0) {
      s += ' ' + this.translate.instant('chessable.fetchProgress',
        { ch: imp.chaptersDone, total: imp.chaptersTotal, lines: imp.linesDone });
      // Restzeit aus dem bisherigen Kapitel-Fortschritt hochrechnen (sobald genug Daten da sind).
      const eta = estimateRemainingMinutes(imp.linesDone, imp.chaptersDone, imp.chaptersTotal);
      if (eta > 0) s += ' · ' + this.translate.instant('chessable.etaRemaining', { min: eta });
    }
    return s;
  }

  /** „Wartezeit X · Holzeit Y" eines abgeschlossenen Imports; '' solange keine Zeiten vorliegen. */
  importDurationLabel(imp: ChessableImport): string {
    if (!imp.startedAt || !imp.completedAt) return '';
    const queue = formatDuration(Date.parse(imp.startedAt) - Date.parse(imp.createdAt));
    const fetch = formatDuration(Date.parse(imp.completedAt) - Date.parse(imp.startedAt));
    return this.translate.instant('chessable.importDuration', { queue, fetch });
  }

  /** Liste der laufenden/wartenden/pausierten Importe (für die Warteschlangen-Anzeige oben). */
  activeList(): ActiveImport[] {
    return Object.values(this.activeImports);
  }

  /** Status für Warteschlange/Zeile: pausiert / globale Position / Hol-Fortschritt. */
  queueLabel(imp: ChessableImport): string {
    if (imp.status === 'paused') return this.translate.instant('chessable.statusPaused');
    if (imp.phase === 'queued') return this.translate.instant('chessable.queuePos', { pos: imp.queuedAhead + 1 });
    return this.statusLabel(imp);
  }

  pauseImport(imp: ChessableImport): void {
    this.chessable.pauseImport(imp.id).subscribe({ next: u => this.applyUpdate(u), error: e => this.showError(e) });
  }

  resumeImport(imp: ChessableImport): void {
    this.chessable.resumeImport(imp.id).subscribe({
      next: u => { this.applyUpdate(u); this.ensurePolling(); },
      error: e => this.showError(e),
    });
  }

  cancelImport(imp: ChessableImport): void {
    this.chessable.cancelImport(imp.id).subscribe({
      next: u => { delete this.activeImports[u.bid]; if (!this.hasRunning()) this.stopPolling(); },
      error: e => this.showError(e),
    });
  }

  private applyUpdate(u: ChessableImport): void {
    this.setActiveImport(u);
  }

  /** Übernimmt einen Import in `activeImports` und berechnet sein Statuslabel EINMAL (hier, beim
   *  Update) statt je Change-Detection-Zyklus im Template. */
  private setActiveImport(u: ChessableImport): void {
    this.activeImports[u.bid] = { ...u, queueLabelText: this.queueLabel(u) };
  }

  private hasRunning(): boolean {
    return this.activeList().some(i => i.status === 'running');
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
          if (upd.status === 'running' || upd.status === 'paused') {
            this.setActiveImport(upd);
          } else {
            delete this.activeImports[bid]; // completed/failed/cancelled
            this.notifyDone(upd);
          }
        }
        if (!this.hasRunning()) this.stopPolling();
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
    } else if (imp.status === 'cancelled') {
      this.snackbar.info(this.translate.instant('chessable.importCancelled', { name: imp.courseName }));
    } else {
      this.snackbar.info(this.translate.instant('chessable.importFailed', { error: imp.error ?? '' }));
    }
  }

  private showError(err: any): void {
    const message = err?.error?.message ?? err?.message ?? String(err);
    this.snackbar.info(this.translate.instant('chessable.error', { message }));
  }
}

/**
 * Liest den vom Bookmarklet übergebenen Bearer aus einem URL-Fragment
 * (`#chessbearer=<urlencoded>`). Gibt `null` zurück, wenn keiner enthalten ist.
 */
export function parseChessbearerFragment(hash: string): string | null {
  const m = /[#&]chessbearer=([^&]+)/.exec(hash || '');
  if (!m) return null;
  try { return decodeURIComponent(m[1]); } catch { return m[1]; }
}

/**
 * Baut das `javascript:`-Bookmarklet: auf chessable.com angeklickt fischt es den
 * eingeloggten Chessable-JWT aus localStorage/sessionStorage/Cookies (robust per
 * JWT-Form + `user.uid`-Payload, unabhängig vom Storage-Key) und öffnet das RookHub-
 * Ziel mit dem Bearer im URL-Fragment. `target` = absolute RookHub-/chessable-URL,
 * `noLoginMsg` = lokalisierter Hinweis, falls kein Login gefunden wird.
 */
export function buildChessableBookmarklet(target: string, noLoginMsg: string): string {
  const msg = (noLoginMsg || '').replace(/\\/g, '\\\\').replace(/'/g, "\\'");
  return (
    "javascript:(function(){" +
    "function d(j){try{return JSON.parse(atob(j.split('.')[1].replace(/-/g,'+').replace(/_/g,'/')))}catch(e){return null}}" +
    "var h=[];function t(v){if(typeof v=='string'&&/^eyJ[\\w-]+\\.[\\w-]+\\.[\\w-]+$/.test(v))h.push(v)}" +
    "function p(o){if(typeof o=='string')t(o);else if(o&&typeof o=='object')for(var k in o)p(o[k])}" +
    "function s(st){try{for(var i=0;i<st.length;i++){var v=st.getItem(st.key(i));t(v);if(v&&(v[0]=='{'||v[0]=='[')){try{p(JSON.parse(v))}catch(e){}}}}catch(e){}}" +
    "s(localStorage);s(sessionStorage);" +
    "document.cookie.split(';').forEach(function(c){t(c.split('=').slice(1).join('=').trim())});" +
    "var j=null;for(var i=0;i<h.length;i++){var x=d(h[i]);if(x&&x.user&&x.user.uid){j=h[i];break}}" +
    "if(!j)j=h[0]||null;" +
    "if(!j){alert('" + msg + "');return}" +
    "var u='" + target + "#chessbearer='+encodeURIComponent(j);" +
    "if(!window.open(u,'_blank'))location.href=u" +
    "})();"
  );
}
