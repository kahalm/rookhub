import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { MachineNoteComponent } from '../courses/course-lang-picker.component';
import { CommentSegment } from './comment-variation.util';

/**
 * Info-/Zug-Kommentar des Buch-Lösers (Zerlegung Z18): gestapelte Absätze mit klickbaren Zug-Chips (Variante als
 * Brett-Vorschau), „Zurück“ aus der Vorschau, „Ganz lesen“ für lange Kommentare, Maschinen-Hinweis und der Hinweis
 * auf eine Offline-Kopie in anderer Sprache. Steht zwischen Kontext-Karte und Status-Card.
 */
@Component({
  selector: 'app-book-comment-block',
  imports: [MatButtonModule, MatIconModule, TranslatePipe, MachineNoteComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './book-comment-block.component.html',
  styleUrl: './book-comment-block.component.scss',
})
export class BookCommentBlockComponent {
  /** Gibt es Kommentarzeilen? Sonst steht nur der Offline-Hinweis. */
  readonly hasLines = input(false);
  /** Die Kommentarzeilen, in klickbare Segmente zerlegt (Text + spielbare Zug-Chips). */
  readonly blocks = input<CommentSegment[][]>([]);
  /** Langer Kommentar: gekürzt, „Ganz lesen“ + Klick in den Text öffnen das Fenster. */
  readonly long = input(false);
  /** Eine Kommentar-Variante steht gerade als Brett-Vorschau. */
  readonly previewing = input(false);
  /** Maschinell übersetzt → Hinweis; `machineClickable` = es gibt ein Original, auf das man wechseln kann. */
  readonly machineNote = input(false);
  readonly machineClickable = input(false);
  /** Die Offline-Kopie hat eine andere Sprache als die gewählte. */
  readonly offlineLangStale = input(false);

  readonly bodyClicked = output<MouseEvent>();
  readonly movePicked = output<CommentSegment>();
  readonly previewExited = output<void>();
  readonly dialogOpened = output<void>();
  readonly originalPicked = output<void>();
}
