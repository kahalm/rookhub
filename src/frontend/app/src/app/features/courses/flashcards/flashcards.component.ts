import { ChangeDetectionStrategy, Component, HostListener, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { CourseService } from '../course.service';
import { PreferencesService } from '../../../core/preferences.service';
import { RepertoireTrainingService } from '../../repertoire/repertoire-training.service';
import { lineKeyFromSans } from '../../repertoire/repertoire-line-key.util';
import { parsePgnText } from '../../../shared/pgn-viewer/pgn-parser';
import { forkJoin } from 'rxjs';
import { Flashcard, buildFlashcard, buildRepertoireFlashcards } from './flashcard.util';
import { FlashcardBoardComponent } from './flashcard-board.component';
import { BoardFullscreenButtonComponent } from '../../../shared/fullscreen/board-fullscreen-button.component';

/**
 * Druckansicht „Flashcards": je Kurs-Linie eine Karteikarte — VORN die Endstellung mit den
 * Chessable-Pfeilen, HINTEN die Linie in Notation + Abschlussbeschreibung. Vier Karten je
 * A4-Blatt; auf jedes Vorderseiten-Blatt folgt das zugehörige Rückseiten-Blatt mit GESPIEGELTEN
 * Spalten, damit beidseitiger Druck (Wenden an der langen Kante) Vorder- und Rückseite
 * deckungsgleich übereinanderlegt.
 *
 * Auswahl über Query-Parameter: `lines=id,id,…` (einzelne Linien, z. B. aus dem Durchsehen),
 * `chapter=<Name>` ('' = „ohne Kapitel") oder `marked=1` (nur die PERSISTENT als Flashcard
 * markierten Linien des Users — der „eigene Bereich" je Kurs/Repertoire); ohne alles der ganze Kurs.
 *
 * ZWEI Quellen über dieselbe Komponente: `/courses/:bookId/flashcards` (Aufgabe vorn, Lösung
 * hinten) und `/repertoires/:id/flashcards` (UMGEKEHRT: Endstellung+Pfeile vorn, Linie hinten;
 * `lines=` trägt dort Linien-Schlüssel statt Ids).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-flashcards',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatTooltipModule, TranslatePipe, FlashcardBoardComponent, BoardFullscreenButtonComponent,
  ],
  templateUrl: './flashcards.component.html',
  styleUrls: ['./flashcards.component.scss'],
})
export class FlashcardsComponent implements OnInit {
  bookId!: number;
  /** Quelle: Kurs (Buch) oder Repertoire — steuert Laden, Auswahl-Filter und Zurück-Link. */
  source: 'course' | 'repertoire' = 'course';
  backLink: (string | number)[] = ['/courses'];
  loading = true;
  error = false;
  cards: Flashcard[] = [];
  /** `?marked=1` — nur die serverseitig markierten Linien (eigener Flashcard-Bereich). */
  markedOnly = false;

  // ===== Digitale Lern-Ansicht =====
  /** 'digital' = Karten am Schirm durchblättern (Standard), 'print' = Druckvorschau. */
  view: 'digital' | 'print' = 'digital';
  /** Reihenfolge als Index-Liste (Mischen tauscht nur diese, nie `cards`). */
  order: number[] = [];
  pos = 0;
  flipped = false;
  /** true = Flip-Transition kurz aus (Blätterwechsel soll nicht rückwärts drehen). */
  flipSnap = false;
  shuffled = false;
  /** Blätter à 4 Karten: [Vorderseiten (Leseordnung), Rückseiten (Spalten gespiegelt)]. */
  sheets: { fronts: (Flashcard | null)[]; backs: (Flashcard | null)[] }[] = [];
  pieceSet = 'cburnett';

  constructor(
    private route: ActivatedRoute,
    private courses: CourseService,
    private training: RepertoireTrainingService,
    prefs: PreferencesService,
  ) {
    this.pieceSet = prefs.pieceSet || 'cburnett';
  }

  ngOnInit(): void {
    const pm = this.route.snapshot.paramMap;
    const q = this.route.snapshot.queryParamMap;
    const rawLines = (q.get('lines') || '').split(',').map(s => s.trim()).filter(Boolean);
    const chapter = q.has('chapter') ? (q.get('chapter') || '') : null;
    this.markedOnly = q.get('marked') === '1';

    if (pm.has('bookId')) {
      this.source = 'course';
      this.bookId = Number(pm.get('bookId'));
      this.backLink = ['/courses', this.bookId];
      const ids = rawLines.map(Number).filter(n => Number.isFinite(n) && n > 0);
      const load = (markedIds: Set<number> | null) => this.courses.getBookPuzzles(this.bookId).subscribe({
        next: puzzles => {
          let picked = puzzles;
          if (markedIds) {
            picked = puzzles.filter(p => markedIds.has(p.id));
          } else if (ids.length) {
            const wanted = new Set(ids);
            picked = puzzles.filter(p => wanted.has(p.id));
          } else if (chapter !== null) {
            picked = puzzles.filter(p => (p.chapter?.trim() || '') === chapter);
          }
          this.finish(picked.map(buildFlashcard).filter((c): c is Flashcard => c !== null));
        },
        error: () => { this.loading = false; this.error = true; },
      });
      if (this.markedOnly) {
        this.courses.getFlashcardMarks(this.bookId).subscribe({
          next: m => load(new Set(m.lineIds)),
          error: () => { this.loading = false; this.error = true; },
        });
      } else {
        load(null);
      }
      return;
    }

    // Repertoire-Quelle: kombiniertes PGN laden; je Spiel bleibt der Roh-Abschnitt für die
    // [%cal]/[%csl]-Marker gepaart. `lines=` = Linien-Schlüssel der Linienliste.
    this.source = 'repertoire';
    this.bookId = Number(pm.get('id'));
    this.backLink = ['/repertoires', this.bookId];
    const loadRep = (markedKeys: Set<string> | null) => this.training.getPgn(this.bookId).subscribe({
      next: pgn => {
        const raws = pgn.split(/\n\n(?=\[Event )/);
        const games: Parameters<typeof buildRepertoireFlashcards>[0] = [];
        const alignedRaws: string[] = [];
        for (const raw of raws) {
          const parsed = parsePgnText(raw)[0];
          if (!parsed || !parsed.moves.length) continue;
          games.push(parsed);
          alignedRaws.push(raw);
        }
        let built = buildRepertoireFlashcards(games, alignedRaws, lineKeyFromSans);
        if (markedKeys) {
          built = built.filter(b => markedKeys.has(b.lineKey));
        } else if (rawLines.length) {
          const wanted = new Set(rawLines);
          built = built.filter(b => wanted.has(b.lineKey));
        } else if (chapter !== null) {
          built = built.filter(b => (b.card.chapter?.trim() || '') === chapter);
        }
        this.finish(built.map(b => b.card));
      },
      error: () => { this.loading = false; this.error = true; },
    });
    if (this.markedOnly) {
      this.training.getFlashcardMarks(this.bookId).subscribe({
        next: m => loadRep(new Set(m.lineKeys)),
        error: () => { this.loading = false; this.error = true; },
      });
    } else {
      loadRep(null);
    }
  }

  private finish(cards: Flashcard[]): void {
    this.cards = cards;
    this.sheets = this.buildSheets(cards);
    this.order = cards.map((_, i) => i);
    this.loading = false;
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

  // ===== Digitale Lern-Ansicht ==============================================

  get current(): Flashcard | null {
    return this.cards.length ? this.cards[this.order[this.pos]] : null;
  }

  flip(): void {
    this.flipped = !this.flipped;
  }

  next(): void {
    if (!this.cards.length) return;
    this.pos = (this.pos + 1) % this.cards.length;
    this.snapUnflip();
  }

  prev(): void {
    if (!this.cards.length) return;
    this.pos = (this.pos - 1 + this.cards.length) % this.cards.length;
    this.snapUnflip();
  }

  /** Beim Blättern sofort (ohne Rückwärts-Drehung) die Vorderseite der neuen Karte zeigen. */
  private snapUnflip(): void {
    this.flipSnap = true;
    this.flipped = false;
    setTimeout(() => { this.flipSnap = false; }, 60);
  }

  /** Mischen an/aus — aus stellt die Kurs-Reihenfolge wieder her; Position beginnt vorn. */
  toggleShuffle(): void {
    this.shuffled = !this.shuffled;
    this.order = this.cards.map((_, i) => i);
    if (this.shuffled) {
      for (let i = this.order.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [this.order[i], this.order[j]] = [this.order[j], this.order[i]];
      }
    }
    this.pos = 0;
    this.snapUnflip();
  }

  /** ←/→ blättern, Leertaste/Enter dreht die Karte um (nur in der digitalen Ansicht). */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    if (this.view !== 'digital' || this.loading || !this.cards.length) return;
    const t = event.target as HTMLElement | null;
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
    if (event.key === 'ArrowRight') { this.next(); event.preventDefault(); }
    else if (event.key === 'ArrowLeft') { this.prev(); event.preventDefault(); }
    else if (event.key === ' ' || event.key === 'Enter') { this.flip(); event.preventDefault(); }
  }
}
