import { Component, EventEmitter, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { TrainingGoalService, ChessableCourseSummary, ChessableTheme } from './training-goals.service';
import { formatDuration } from './duration.util';

/**
 * Karte „Chessable-Kurse: History + manuelle Themen-Zuordnung": listet die Chessable-Trainingszeit je
 * Kurs und erlaubt, jedem Kurs ein Thema (Eröffnung/Mittelspiel/Endspiel/Taktik) zuzuordnen. Aus
 * <c>TrainingGoalsComponent</c> ausgegliedert; lädt die Kursliste selbst und meldet Themen-Änderungen
 * über <c>(changed)</c>, weil sie die Themen-Aufschlüsselung des Eltern-Containers beeinflussen.
 */
@Component({
  selector: 'app-chessable-themes-card',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatFormFieldModule, MatSelectModule, MatTooltipModule,
    TranslatePipe, LoadingSpinnerComponent,
  ],
  templateUrl: './chessable-themes-card.component.html',
  styles: [`
    .intro { color: color-mix(in srgb, currentColor 60%, transparent); margin-top: -8px; }
    .small { font-size: .8rem; }
    .muted { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; }
    .filter-toggle { display: inline-flex; align-items: center; gap: 6px; margin: 8px 0; font-size: .9rem; cursor: pointer; }
    .course-list { list-style: none; padding: 0; margin: 8px 0 0; }
    .course-list li { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; padding: 8px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    .course-list .c-main { flex: 1; min-width: 160px; display: flex; flex-direction: column; }
    .course-list .c-name { font-weight: 600; overflow-wrap: anywhere; }
    .course-list .c-id { font-size: .72rem; color: color-mix(in srgb, currentColor 50%, transparent); }
    .course-list .c-time { white-space: nowrap; font-variant-numeric: tabular-nums; }
    .course-list .c-time .unit { color: color-mix(in srgb, currentColor 50%, transparent); font-size: .8em; }
    .course-list .c-auto { font-size: .75rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .course-list .c-unassigned { font-size: .72rem; padding: 2px 8px; border-radius: 999px; background: color-mix(in srgb, #d4820a 18%, transparent); color: #d4820a; }
    .course-list .c-theme { width: 190px; }
    .course-list .c-theme mat-form-field { margin-bottom: -1.25em; }
  `],
})
export class ChessableThemesCardComponent implements OnInit {
  /** Feuert nach einer Themen-Zuordnung → Eltern lädt Themen-Aufschlüsselung/History neu. */
  @Output() changed = new EventEmitter<void>();

  readonly chessableThemes: ChessableTheme[] = ['Opening', 'Middlegame', 'Endgame', 'Tactics'];
  chessableCourses: ChessableCourseSummary[] = [];
  chessableUnassignedOnly = false;
  loadingCourses = false;
  savingCourseId: string | null = null;

  constructor(
    private service: TrainingGoalService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.loadChessableCourses();
  }

  durValue(seconds: number): string { return formatDuration(seconds, this.translate.currentLang()).value; }
  durUnit(seconds: number): string { return formatDuration(seconds, this.translate.currentLang()).unitKey; }

  loadChessableCourses(): void {
    this.loadingCourses = true;
    this.service.listChessableCourses(this.chessableUnassignedOnly).subscribe({
      next: list => { this.chessableCourses = list; this.loadingCourses = false; },
      error: () => { this.loadingCourses = false; },
    });
  }

  toggleUnassignedFilter(on: boolean): void {
    this.chessableUnassignedOnly = on;
    this.loadChessableCourses();
  }

  /** mat-select-Wert je Kurs: das manuell zugeordnete Thema (Großschreibung) oder null. */
  selectedTheme(c: ChessableCourseSummary): ChessableTheme | null {
    if (!c.assignedTheme) return null;
    return (c.assignedTheme.charAt(0).toUpperCase() + c.assignedTheme.slice(1)) as ChessableTheme;
  }

  /** Thema eines Kurses setzen (null = manuelle Zuordnung entfernen); lädt die Liste + meldet Änderung. */
  assignTheme(c: ChessableCourseSummary, theme: ChessableTheme | null): void {
    this.savingCourseId = c.courseId;
    const req = theme
      ? this.service.setChessableCourseTheme(c.courseId, theme)
      : this.service.clearChessableCourseTheme(c.courseId);
    req.subscribe({
      next: () => {
        this.savingCourseId = null;
        this.snackbar.success(this.translate.instant('trainingGoals.chessable.saved'));
        this.loadChessableCourses();   // eigene Liste aktualisieren
        this.changed.emit();           // Eltern: Themen-Aufschlüsselung/History neu laden
      },
      error: () => { this.savingCourseId = null; this.snackbar.warn(this.translate.instant('trainingGoals.error')); },
    });
  }
}
