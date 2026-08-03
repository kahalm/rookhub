import { ChangeDetectionStrategy, Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { CourseService } from '../course.service';
import { PreferencesService } from '../../../core/preferences.service';
import { Flashcard, buildFlashcard } from './flashcard.util';
import { FlashcardBoardComponent } from './flashcard-board.component';

/**
 * Druckansicht „Flashcards": je Kurs-Linie eine Karteikarte — VORN die Endstellung mit den
 * Chessable-Pfeilen, HINTEN die Linie in Notation + Abschlussbeschreibung. Vier Karten je
 * A4-Blatt; auf jedes Vorderseiten-Blatt folgt das zugehörige Rückseiten-Blatt mit GESPIEGELTEN
 * Spalten, damit beidseitiger Druck (Wenden an der langen Kante) Vorder- und Rückseite
 * deckungsgleich übereinanderlegt.
 *
 * Auswahl über Query-Parameter: `lines=id,id,…` (einzelne Linien, z. B. aus dem Durchsehen)
 * oder `chapter=<Name>` ('' = „ohne Kapitel"); ohne beides der ganze Kurs.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-flashcards',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    TranslatePipe, FlashcardBoardComponent,
  ],
  templateUrl: './flashcards.component.html',
  styleUrls: ['./flashcards.component.scss'],
})
export class FlashcardsComponent implements OnInit {
  bookId!: number;
  loading = true;
  error = false;
  cards: Flashcard[] = [];
  /** Blätter à 4 Karten: [Vorderseiten (Leseordnung), Rückseiten (Spalten gespiegelt)]. */
  sheets: { fronts: (Flashcard | null)[]; backs: (Flashcard | null)[] }[] = [];
  pieceSet = 'cburnett';

  constructor(
    private route: ActivatedRoute,
    private courses: CourseService,
    prefs: PreferencesService,
  ) {
    this.pieceSet = prefs.pieceSet || 'cburnett';
  }

  ngOnInit(): void {
    this.bookId = Number(this.route.snapshot.paramMap.get('bookId'));
    const q = this.route.snapshot.queryParamMap;
    const ids = (q.get('lines') || '').split(',').map(s => Number(s)).filter(n => Number.isFinite(n) && n > 0);
    const chapter = q.has('chapter') ? (q.get('chapter') || '') : null;

    this.courses.getBookPuzzles(this.bookId).subscribe({
      next: puzzles => {
        let picked = puzzles;
        if (ids.length) {
          const wanted = new Set(ids);
          picked = puzzles.filter(p => wanted.has(p.id));
        } else if (chapter !== null) {
          picked = puzzles.filter(p => (p.chapter?.trim() || '') === chapter);
        }
        this.cards = picked.map(buildFlashcard).filter((c): c is Flashcard => c !== null);
        this.sheets = this.buildSheets(this.cards);
        this.loading = false;
      },
      error: () => { this.loading = false; this.error = true; },
    });
  }

  /**
   * 4er-Blätter; die Rückseiten sind je ZEILE spaltenvertauscht (0↔1, 2↔3) — beim Wenden an der
   * langen Kante landet so jede Rückseite exakt hinter ihrer Vorderseite.
   */
  private buildSheets(cards: Flashcard[]): { fronts: (Flashcard | null)[]; backs: (Flashcard | null)[] }[] {
    const sheets: { fronts: (Flashcard | null)[]; backs: (Flashcard | null)[] }[] = [];
    for (let i = 0; i < cards.length; i += 4) {
      const fronts: (Flashcard | null)[] = [
        cards[i] ?? null, cards[i + 1] ?? null, cards[i + 2] ?? null, cards[i + 3] ?? null,
      ];
      const backs: (Flashcard | null)[] = [fronts[1], fronts[0], fronts[3], fronts[2]];
      sheets.push({ fronts, backs });
    }
    return sheets;
  }

  print(): void {
    window.print();
  }
}
