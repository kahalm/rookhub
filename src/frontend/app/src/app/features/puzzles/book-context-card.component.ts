import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { CourseLangPickerComponent } from '../courses/course-lang-picker.component';
import { CourseLineEbookRef, CourseMode, CourseScopeStats } from '../courses/course.service';
import { WeeklyMode } from '../weekly/weekly.service';
import { BookPuzzleDto } from './puzzle.service';
import { PuzzleTagsComponent } from './puzzle-tags.component';
import { CourseStatsPanelComponent } from './course-stats-panel.component';
import { dailyDateValue } from './book-daily-mode';

/**
 * Kontext-Karte des Buch-Lösers (Zerlegung Z18): Puzzle-Info (Wochenpost-/Tagespuzzle-Chip, Titel, Kapitel, Buch,
 * Abzeichen), Sprachwahl, Kurs-/Wochenpost-Fortschritt und die Navigation je Modus — dazu die Karte „kein Puzzle“
 * (Wochenpost ohne Puzzles, Kurs abgeschlossen). Reine Darstellung: alle Werte kommen als Eingaben, jede Handlung
 * geht als Ausgabe an den Container.
 */
@Component({
  selector: 'app-book-context-card',
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, MatTooltipModule,
    TranslatePipe, IconLabelDirective, CourseLangPickerComponent, PuzzleTagsComponent, CourseStatsPanelComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './book-context-card.component.html',
  styleUrl: './book-context-card.component.scss',
})
export class BookContextCardComponent {
  // ---- Rahmen
  /** Steht ein Puzzle? Sonst ggf. die Karte „kein Puzzle“. */
  readonly hasPuzzle = input(false);
  readonly state = input('LOADING');
  readonly inCourse = input(false);
  readonly inWeekly = input(false);
  readonly isDaily = input(false);
  readonly browseInBook = input(false);
  readonly isLoggedIn = input(false);

  // ---- Puzzle-Info
  readonly titleDisplay = input<string | null>(null);
  readonly chapterDisplay = input<string | null>(null);
  readonly bookName = input('');
  readonly sourceGame = input<BookPuzzleDto['sourceGame']>(null);
  readonly difficulty = input<string | null | undefined>(null);
  readonly bookRating = input<number | null | undefined>(null);
  readonly tags = input<string | null | undefined>(null);
  readonly dailyDate = input<string | null>(null);

  // ---- Sprachwahl
  readonly langEnabled = input(false);
  readonly languages = input<readonly string[]>([]);
  readonly langValue = input<string | null>(null);

  // ---- Wochenpost
  readonly weeklyChipLabel = input('');
  readonly weeklyTotal = input(0);
  readonly weeklyDisplayIndex = input(0);
  readonly weeklyPercent = input(0);
  readonly weeklyPlayed = input(0);
  readonly weeklySolved = input(0);
  readonly weeklySeconds = input(0);
  readonly weeklyTimeDisplay = input('');
  readonly weeklyTrainingCount = input(0);
  readonly weeklyEasyCount = input(0);
  readonly weeklyMode = input<WeeklyMode>('training');
  readonly weeklyReturnUrl = input('/weekly');
  readonly weeklyCompleted = input(false);
  readonly canOpenWeeklyOverview = input(false);

  // ---- Kurs
  readonly courseTotal = input(0);
  readonly courseSolved = input(0);
  readonly coursePercent = input(0);
  readonly courseCompleted = input(false);
  readonly courseFullyDone = input(false);
  readonly courseRemaining = input(0);
  readonly courseModeKind = input<CourseMode>('sequential');
  readonly courseBookStats = input<CourseScopeStats | null>(null);
  readonly courseChapterStats = input<CourseScopeStats | null>(null);
  readonly courseChapterDisplay = input<string | null>(null);
  readonly statsOpen = input(false);
  readonly ebookRef = input<CourseLineEbookRef | null>(null);
  readonly linkedCourseBookId = input<number | null>(null);
  readonly linkedCourseName = input<string | null>(null);
  /** Beschriftung des Rückpfeils bzw. der „Zur Übersicht“-Knöpfe (für Gäste „Zur Startseite“). */
  readonly courseBackLabel = input('book.course.backToCourses');
  readonly courseOverviewLabel = input('book.course.toOverview');

  // ---- Buch / Tagespuzzle
  readonly bookNavLoading = input(false);
  readonly canGoForwardDaily = input(false);

  // ---- Handlungen
  readonly back = output<void>();
  readonly languagePicked = output<string>();
  readonly statsToggled = output<void>();
  readonly switchLinked = output<void>();
  readonly weeklyModeToggled = output<void>();
  readonly weeklyOverview = output<void>();
  readonly courseOverview = output<void>();
  readonly retryFailed = output<void>();
  readonly restartCourse = output<void>();
  readonly courseNext = output<void>();
  readonly nextInBook = output<void>();
  readonly randomInBook = output<void>();
  readonly prevDaily = output<void>();
  readonly nextDaily = output<void>();

  /** Tag des Tagespuzzles als Datum für die DatePipe (nur mit Tagespuzzle). */
  readonly day = computed(() => (this.isDaily() ? dailyDateValue(this.dailyDate()) : null));
  /** Router-Ziel der Partie, aus der die Aufgabe geerntet wurde (Taktik-Ernte); `null` = keine. */
  readonly sourceGameLink = computed(() => {
    const g = this.sourceGame();
    return g ? [g.kind === 'club' ? '/club-games' : '/games', g.id] : null;
  });
}
