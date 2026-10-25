import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SendToWorksheetComponent } from '../worksheets/send-to-worksheet.component';
import { CourseLine, CourseManageChapter } from './course.types';
import { CalcEdition } from './calc-editions.service';

/**
 * Eine Zeile der Kapitelliste auf der Kurs-Detailseite: Kopf (Aufklapper, Name bzw. Umbenennen-Feld, Zähler,
 * Kalkulationspunkte, Calc-Serie-Marke, Balken, ▶ und ⋮-Menü) und die aufgeklappten Linien. Reine Darstellung —
 * jede Aktion geht als Ereignis an {@link CourseDetailComponent}, die lädt, fragt nach und speichert (Zerlegung Z29).
 */
@Component({
  selector: 'app-chapter-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatProgressBarModule,
    MatProgressSpinnerModule, MatTooltipModule, IconLabelDirective, TranslatePipe, SendToWorksheetComponent,
  ],
  templateUrl: './chapter-row.component.html',
  styleUrls: ['./chapter-row.component.scss'],
})
export class ChapterRowComponent {
  readonly chapter = input.required<CourseManageChapter>();
  readonly bookId = input.required<number>();
  /** Angezeigter Kapitelname (Übersetzung bzw. „ohne Kapitel"). */
  readonly label = input('');
  readonly isCalculation = input(false);
  readonly canManage = input(false);
  /** Kurs ohne lösbare Aufgaben — dann keine Kapitelbalken (siehe `CourseDetailComponent.isEmptyQuiz`). */
  readonly isEmptyQuiz = input(false);
  readonly canCreateWeekly = input(false);
  readonly busy = input(false);
  readonly expanded = input(false);
  readonly loadingLines = input(false);
  /** Geladene Linien (`undefined` = noch nicht geladen). */
  readonly lines = input<CourseLine[] | undefined>(undefined);
  /** Wird dieses Kapitel gerade umbenannt? Der Entwurf gehört der Seite (`renameDraft`, zweiseitig). */
  readonly renaming = input(false);
  readonly renameDraft = model('');
  /** Punkte der Selbstbewertung („14 / 24"); leer = nicht bewertet. */
  readonly calcScore = input('');
  readonly edition = input<CalcEdition | null>(null);
  readonly startLink = input<unknown[] | null>(null);
  readonly startParams = input<Record<string, unknown> | null>(null);

  readonly expandToggle = output<void>();
  readonly addLines = output<void>();
  readonly startRename = output<void>();
  readonly commitRename = output<void>();
  readonly cancelRename = output<void>();
  readonly openEdition = output<void>();
  /** Kapitel aufs Aufgabenblatt (`null` = Zwischenablage). */
  readonly sendToWorksheet = output<number | null>();
  readonly createWeekly = output<void>();
  readonly downloadChapterPgn = output<void>();
  readonly resetChapter = output<void>();
  readonly deleteChapter = output<void>();
  readonly downloadLinePgn = output<CourseLine>();
  readonly deleteLine = output<CourseLine>();
}
