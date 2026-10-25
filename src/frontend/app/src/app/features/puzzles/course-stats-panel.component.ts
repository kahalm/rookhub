import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { CourseScopeStats } from '../courses/course.service';
import { CourseProgressSplitComponent } from '../courses/course-progress-split.component';
import { courseEta, courseTime } from './book-course-mode';

/**
 * Kurs-Fortschritt im Buch-Löser (Zerlegung Z18): ganzes Buch und aktuelles Kapitel mit dreigeteilter Leiste,
 * Trefferquote, Zeit und Restzeit; ohne Statistik vom Server nur Balken + „x/y (n %)“. Aufgeklappt über das ⓘ der
 * Kontext-Karte.
 */
@Component({
  selector: 'app-course-stats-panel',
  imports: [MatIconModule, MatProgressBarModule, MatTooltipModule, TranslatePipe, CourseProgressSplitComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './course-stats-panel.component.html',
  styleUrl: './course-stats-panel.component.scss',
})
export class CourseStatsPanelComponent {
  readonly bookStats = input<CourseScopeStats | null>(null);
  readonly chapterStats = input<CourseScopeStats | null>(null);
  /** Kapitelname (übersetzt, wo es eine Übersetzung gibt). */
  readonly chapterLabel = input<string | null>(null);
  readonly solved = input(0);
  readonly total = input(0);
  readonly percent = input(0);

  readonly courseTime = courseTime;
  readonly courseEta = courseEta;
}
