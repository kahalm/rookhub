import { Component, OnInit, OnDestroy, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { timer, Subscription } from 'rxjs';
import { SnackbarService } from '../../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { ChessableService, ChessableCredentialedUser, ChessableCourse, ChessableImport, ChessableImportTarget, ChessableCourseInfo } from '../../chessable/chessable.service';
import { CHESSABLE_LINES_PER_MIN } from '../../chessable/chessable.component';

/**
 * Admin-Tab „Kurse von Usern holen": lädt im Namen eines Users (mit dessen Bearer) dessen
 * Chessable-Kursliste und importiert einzelne Kurse ins eigene Admin-Konto (Repertoire/Buch),
 * inkl. Größen-Schätzung + Live-Fortschritts-Polling. Aus <c>AdminComponent</c> ausgegliedert;
 * self-contained (nur ChessableService), lädt die User-Liste selbst beim Init.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-admin-chessable-download',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatSelectModule,
    MatCheckboxModule, MatTooltipModule, MatSlideToggleModule, MatProgressSpinnerModule, TranslatePipe,
    LoadingSpinnerComponent,
  ],
  templateUrl: './admin-chessable-download.component.html',
  styleUrl: './admin-chessable-download.component.scss',
})
export class AdminChessableDownloadComponent implements OnInit, OnDestroy {
  dlUsers: ChessableCredentialedUser[] = [];
  dlUsersLoading = false;
  dlShowExpired = false;
  dlTesting = false;
  dlSelectedUserId: number | null = null;
  dlCourses: ChessableCourse[] = [];
  dlHideLoaded = false;
  dlCoursesLoading = false;
  dlCoursesError: string | null = null;
  dlImports: Record<string, ChessableImport> = {};
  dlEstimates: Record<string, { info?: ChessableCourseInfo; loading: boolean; error?: string }> = {};
  private dlPollSubs: Record<string, Subscription> = {};

  constructor(
    private chessable: ChessableService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.loadDlUsers();
  }

  ngOnDestroy(): void {
    Object.values(this.dlPollSubs).forEach(s => s.unsubscribe());
  }

  loadDlUsers(): void {
    this.dlUsersLoading = true;
    this.chessable.getCredentialedUsersAdmin().subscribe({
      next: list => { this.dlUsers = list; this.dlUsersLoading = false; },
      error: () => { this.dlUsersLoading = false; },
    });
  }

  /** Der aktuell gewählte Download-User (oder undefined). */
  dlSelectedUser(): ChessableCredentialedUser | undefined {
    return this.dlUsers.find(u => u.userId === this.dlSelectedUserId);
  }

  /** Im Dropdown anzuzeigende User — ohne „auch abgelaufene" nur solche mit gültigem (nicht gesperrtem) Bearer. */
  dlVisibleUsers(): ChessableCredentialedUser[] {
    if (this.dlShowExpired) return this.dlUsers;
    return this.dlUsers.filter(u => !u.blocked);
  }

  /** „Auch abgelaufene anzeigen" umgeschaltet: ist der gewählte User nun ausgeblendet, Auswahl + Kursliste leeren. */
  onDlShowExpiredChange(): void {
    if (this.dlSelectedUserId != null && !this.dlVisibleUsers().some(u => u.userId === this.dlSelectedUserId)) {
      this.dlSelectedUserId = null;
      this.dlCourses = [];
      this.dlCoursesError = null;
    }
  }

  /** Anzuzeigende Kurse — bei aktivem „geladene ausblenden" ohne bereits als Repertoire/Buch importierte. */
  dlVisibleCourses(): ChessableCourse[] {
    if (!this.dlHideLoaded) return this.dlCourses;
    return this.dlCourses.filter(c => !c.importedRepertoire && !c.importedBook);
  }

  /** ADMIN: Bearer des gewählten Users testen — setzt bei Erfolg dessen Circuit-Breaker zurück und
   *  nimmt seine pausierten Importe wieder auf. */
  dlTestUser(): void {
    if (this.dlSelectedUserId == null) return;
    this.dlTesting = true;
    this.chessable.testUser(this.dlSelectedUserId).subscribe({
      next: r => {
        this.dlTesting = false;
        this.snackbar.info(this.translate.instant('chessable.testOk', { uid: r.uid, count: r.courseCount }));
        this.loadDlUsers(); // Blocked-Flag aktualisieren
      },
      error: err => {
        this.dlTesting = false;
        this.snackbar.info(err?.error?.message || this.translate.instant('chessable.testFailed'));
        this.loadDlUsers();
      },
    });
  }

  /** User gewählt → dessen Chessable-Kursliste laden. */
  onDlUserChange(): void {
    this.dlCourses = [];
    this.dlCoursesError = null;
    if (this.dlSelectedUserId != null) this.loadDlCourses(false);
  }

  loadDlCourses(refresh: boolean): void {
    if (this.dlSelectedUserId == null) return;
    this.dlCoursesLoading = true;
    this.dlCoursesError = null;
    this.chessable.getUserCoursesAdmin(this.dlSelectedUserId, refresh).subscribe({
      next: res => { this.dlCourses = res.courses; this.dlCoursesLoading = false; },
      error: err => {
        this.dlCoursesError = err?.error?.message || this.translate.instant('admin.courseDl.loadError');
        this.dlCoursesLoading = false;
      },
    });
  }

  /** Geschätzte Rest-Holzeit (Min) eines laufenden Imports aus der bekannten Gesamt-Linienzahl;
   *  0 = unbekannt/fertig (Anzeige unterdrückt). Durchsatz: CHESSABLE_LINES_PER_MIN. */
  dlEtaMin(imp: ChessableImport): number {
    if (!imp.linesTotal || imp.linesTotal <= imp.linesDone) return 0;
    return Math.ceil((imp.linesTotal - imp.linesDone) / CHESSABLE_LINES_PER_MIN);
  }

  /** Geschätzte Holzeit (Min) aus einer Linienzahl; gecacht ⇒ 0 (quasi sofort). */
  estMinFromLines(totalLines: number): number {
    return Math.ceil(totalLines / CHESSABLE_LINES_PER_MIN);
  }

  /** On-demand: Gesamt-Linienzahl + grobe Zeit eines Kurses schätzen (1 getCourse-Call bzw. Cache). */
  dlEstimate(course: ChessableCourse): void {
    if (this.dlSelectedUserId == null) return;
    const bid = course.bid;
    if (this.dlEstimates[bid]?.loading) return;
    this.dlEstimates[bid] = { loading: true };
    this.chessable.estimateCourseForUser(this.dlSelectedUserId, bid).subscribe({
      next: info => { this.dlEstimates[bid] = { info, loading: false }; },
      error: err => { this.dlEstimates[bid] = { loading: false, error: err?.error?.message || this.translate.instant('admin.courseDl.estimateError') }; },
    });
  }

  dlImport(course: ChessableCourse, target: ChessableImportTarget): void {
    if (this.dlSelectedUserId == null) return;
    const bid = course.bid;
    this.dlImports[bid] = { ...(this.dlImports[bid] ?? {} as ChessableImport), status: 'running', phase: 'queued', bid, target } as ChessableImport;
    this.chessable.importForUserAdmin(this.dlSelectedUserId, bid, target, course.name).subscribe({
      next: imp => { this.dlImports[bid] = imp; this.pollDlImport(bid, imp.id); },
      error: err => {
        delete this.dlImports[bid];
        this.snackbar.show(err?.error?.message || this.translate.instant('admin.courseDl.importError'), { duration: 3500 });
      },
    });
  }

  private pollDlImport(bid: string, id: number): void {
    this.dlPollSubs[bid]?.unsubscribe();
    this.dlPollSubs[bid] = timer(2500, 2500).subscribe(() => {
      this.chessable.getImport(id).subscribe({
        next: imp => {
          this.dlImports[bid] = imp;
          // Nur bei ENDzuständen stoppen — 'paused' weiterpollen, sonst friert der Fortschritt ein,
          // wenn der Import (anderswo) fortgesetzt wird. Angeglichen an die Haupt-Chessable-Komponente.
          const terminal = imp.status === 'completed' || imp.status === 'failed' || imp.status === 'cancelled';
          if (terminal) { this.dlPollSubs[bid]?.unsubscribe(); delete this.dlPollSubs[bid]; }
          // Erfolgreich → das passende „erledigt"-Flag am Kurs setzen (Button verschwindet, Badge
          // erscheint — wie beim normalen Chessable-Feature) und den Live-Status entfernen.
          if (imp.status === 'completed') {
            const course = this.dlCourses.find(c => c.bid === bid);
            if (course) {
              if (imp.target === 'book') course.importedBook = true; else course.importedRepertoire = true;
            }
            delete this.dlImports[bid];
          }
        },
        error: () => { this.dlPollSubs[bid]?.unsubscribe(); delete this.dlPollSubs[bid]; },
      });
    });
  }
}
