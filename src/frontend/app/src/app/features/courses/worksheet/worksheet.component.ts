import { ChangeDetectionStrategy, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { CourseService } from '../course.service';
import { BookPuzzleDto } from '../../puzzles/puzzle.service';
import { buildFlashcard } from '../flashcards/flashcard.util';
import { FlashcardBoardComponent } from '../flashcards/flashcard-board.component';

/** Eine Aufgabe des Blatts: die gefragte Stellung, mehr nicht (keine Lösung, kein Linientitel). */
export interface WorksheetTask {
  /** Fortlaufende Nummer über das ganze Dokument (1-basiert). */
  no: number;
  fen: string;
  orientation: 'white' | 'black';
  whiteToMove: boolean;
}

/** Diagramme je A4-Seite (2 Spalten × 3 Zeilen). */
export const TASKS_PER_SHEET = 6;

/**
 * Nur ABGEFRAGTE Linien gehören auf ein Aufgabenblatt: Info-/Erklärseiten (Kurseinleitung,
 * Muster-Diagramme) haben keine Lösung, und eine Linie ohne Züge stellt keine Frage.
 */
export function isQuizLine(p: BookPuzzleDto): boolean {
  return !p.isInfoOnly && (p.moves ?? '').trim().length > 0;
}

/** Zerlegt die Aufgaben in Seiten; die letzte Seite bleibt bewusst unaufgefüllt. */
export function toSheets(tasks: WorksheetTask[], perSheet = TASKS_PER_SHEET): WorksheetTask[][] {
  const sheets: WorksheetTask[][] = [];
  for (let i = 0; i < tasks.length; i += perSheet) sheets.push(tasks.slice(i, i + perSheet));
  return sheets;
}

/**
 * Druckbares AUFGABENBLATT eines Kurses: je Diagramm die Stellung, in der die Linie gefragt wird
 * (Vorspielzüge sind eingespielt), sechs Stück je A4-Seite, darunter Platz zum Eintragen der Lösung.
 *
 * <p>Bewusst OHNE Lösungen und ohne Linientitel — das Blatt soll man am Brett lösen können, und ein
 * Titel wie „Mate in 3" oder „Widerlegung von 10…Sd4" verrät sie. Nachsehen kann man im Kurs.</p>
 *
 * <p>Die PDF macht der Browser („Drucken → Als PDF speichern"): dieselben Bretter wie in der App,
 * kein zweiter Renderweg, der auseinanderlaufen kann. Gezeichnet wird deshalb mit
 * <see cref="FlashcardBoardComponent"/> als Inline-SVG — chessground nutzt CSS-Hintergrundbilder für
 * die Figuren, und die druckt kein Browser standardmäßig (das Blatt wäre leer).</p>
 *
 * Auswahl über Query-Parameter wie bei den Flashcards: `chapter=<Name>` ('' = „ohne Kapitel"),
 * `marked=1` (die markierten Linien), `lines=id,id,…`; ohne alles der ganze Kurs. `print=1` öffnet
 * den Druckdialog von selbst — das ist der Weg, den die Knöpfe im Kurs nehmen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-course-worksheet',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    TranslatePipe, FlashcardBoardComponent,
  ],
  templateUrl: './worksheet.component.html',
  styleUrls: ['./worksheet.component.scss'],
})
export class WorksheetComponent implements OnInit {
  bookId!: number;
  loading = true;
  error = false;
  /** Kopfzeile: Kursname (aus der Kursliste) + Auswahl (Kapitel / markierte Linien / ganzer Kurs). */
  courseName = '';
  scopeLabelKey = 'courses.worksheet.scopeCourse';
  chapterName: string | null = null;
  sheets: WorksheetTask[][] = [];
  taskCount = 0;
  readonly today = new Date();

  /**
   * Figurensatz des Ausdrucks: `merida` — kräftige Umrisse, satt gefüllte schwarze Figuren, also
   * der Satz, der dem klassischen Diagramm-Ausdruck (ChessBase & Co.) am nächsten kommt. Bewusst
   * UNABHÄNGIG vom Bildschirm-Satz des Profils: am Schirm entscheidet Geschmack, auf Papier
   * entscheidet, was bei 58 mm Kantenlänge in Graustufen noch lesbar bleibt.
   */
  readonly pieceSet = 'merida';

  constructor(
    private route: ActivatedRoute,
    private courses: CourseService,
  ) {}

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    this.bookId = Number(this.route.snapshot.paramMap.get('bookId'));
    const chapter = q.has('chapter') ? (q.get('chapter') || '') : null;
    const markedOnly = q.get('marked') === '1';
    const ids = (q.get('lines') || '').split(',').map(s => Number(s.trim())).filter(n => Number.isFinite(n) && n > 0);
    const autoPrint = q.get('print') === '1';

    if (markedOnly) this.scopeLabelKey = 'courses.worksheet.scopeMarked';
    else if (chapter !== null) {
      // `chapter=''` ist die Sammel-Gruppe „ohne Kapitel" — ein leerer Name in der Kopfzeile wäre stumm.
      this.scopeLabelKey = chapter === '' ? 'courses.worksheet.scopeNoChapter' : 'courses.worksheet.scopeChapter';
      this.chapterName = chapter;
    } else if (ids.length) this.scopeLabelKey = 'courses.worksheet.scopeSelection';

    const load = (markedIds: Set<number> | null) => this.courses.getBookPuzzles(this.bookId).subscribe({
      next: puzzles => {
        let picked = puzzles;
        if (markedIds) picked = puzzles.filter(p => markedIds.has(p.id));
        else if (ids.length) { const wanted = new Set(ids); picked = puzzles.filter(p => wanted.has(p.id)); }
        else if (chapter !== null) picked = puzzles.filter(p => (p.chapter?.trim() || '') === chapter);
        this.finish(picked.filter(isQuizLine), autoPrint);
      },
      error: () => { this.loading = false; this.error = true; },
    });

    if (markedOnly) {
      this.courses.getFlashcardMarks(this.bookId).subscribe({
        next: m => load(new Set(m.lineIds)),
        error: () => { this.loading = false; this.error = true; },
      });
    } else {
      load(null);
    }

    // Kursname nur für die Kopfzeile — scheitert er, bleibt sie ohne Namen (kein Fehlerfall).
    this.courses.getCourses().subscribe({
      next: list => { this.courseName = list.find(c => c.bookId === this.bookId)?.displayName || ''; },
      error: () => { /* Kopfzeile ohne Namen ist brauchbar */ },
    });
  }

  private finish(picked: BookPuzzleDto[], autoPrint: boolean): void {
    const tasks: WorksheetTask[] = [];
    for (const p of picked) {
      const card = buildFlashcard(p);
      if (!card?.frontFen) continue;   // nicht aufbaubar (illegale Muster-FEN) → überspringen
      tasks.push({
        no: tasks.length + 1,
        fen: card.frontFen,
        orientation: card.orientation,
        whiteToMove: card.orientation === 'white',
      });
    }
    this.sheets = toSheets(tasks);
    this.taskCount = tasks.length;
    this.loading = false;
    // Erst nach dem Rendern drucken; die Figuren sind SVG-Verweise und brauchen einen Moment.
    if (autoPrint && tasks.length > 0) setTimeout(() => this.print(), 500);
  }

  print(): void {
    window.print();
  }
}
