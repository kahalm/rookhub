import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FlashcardBoardComponent } from '../courses/flashcards/flashcard-board.component';
import { QrCodeComponent } from '../../shared/qr-code/qr-code.component';
import { Worksheet, WorksheetItem, WorksheetService } from './worksheet.service';

/** Eine Aufgabe auf dem Papier: Stellung, Nummer und die Worte des Erstellers — nie die Lösung. */
export interface PrintTask {
  /** Fortlaufende Nummer über das ganze Dokument (1-basiert). */
  no: number;
  fen: string;
  orientation: 'white' | 'black';
  whiteToMove: boolean;
  heading: string;
  text: string;
}

/** Zerlegt die Aufgaben in Seiten; die letzte Seite bleibt bewusst unaufgefüllt. */
export function toSheets(tasks: PrintTask[], perSheet: number): PrintTask[][] {
  const size = perSheet > 0 ? perSheet : 6;
  const sheets: PrintTask[][] = [];
  for (let i = 0; i < tasks.length; i += size) sheets.push(tasks.slice(i, i + size));
  return sheets;
}

/** Schreiblinien je Aufgabe — je weniger Diagramme auf der Seite, desto mehr Platz für die Lösung. */
export function solutionLines(perPage: number): number[] {
  const count = perPage <= 2 ? 5 : perPage <= 4 ? 3 : 2;
  return Array.from({ length: count }, (_, i) => i);
}

/**
 * Die DRUCKANSICHT eines Aufgabenblatts (`/worksheets/:id/print`): die zusammengestellten
 * Stellungen als A4-Seiten, je nach Dichte 6, 4 oder 2 Diagramme pro Seite, unter jeder Aufgabe
 * Schreiblinien für die Lösung.
 *
 * <p>Gedruckt wird ohne Lösungen — das Blatt soll man am Brett lösen können. Überschrift und
 * Begleittext stehen nur dort, wo der Ersteller sie selbst hingeschrieben hat.</p>
 *
 * <p>Die PDF macht der Browser („Drucken → Als PDF speichern"): dieselben Bretter wie in der App,
 * kein zweiter Renderweg, der auseinanderlaufen kann. Gezeichnet wird deshalb mit
 * <see cref="FlashcardBoardComponent"/> als Inline-SVG — chessground nutzt CSS-Hintergrundbilder für
 * die Figuren, und die druckt kein Browser standardmäßig (das Blatt wäre leer).</p>
 *
 * <p>`print=1` öffnet den Druckdialog von selbst — den Weg nehmen die Druck-Knöpfe in Übersicht
 * und Blatt.</p>
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-worksheet-print',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    TranslatePipe, FlashcardBoardComponent, QrCodeComponent,
  ],
  templateUrl: './worksheet-print.component.html',
  styleUrls: ['./worksheet-print.component.scss'],
})
export class WorksheetPrintComponent implements OnInit {
  id!: number;
  loading = true;
  error = false;
  sheetName = '';
  perPage = 6;
  sheets: PrintTask[][] = [];
  taskCount = 0;
  readonly today = new Date();

  /** Adresse hinter dem QR-Code in der Fußzeile; leer, solange das Blatt nicht geteilt ist. */
  shareUrl = '';
  /** Läuft gerade „Teilen einschalten" (nur am Schirm, der Knopf wird nicht gedruckt). */
  sharing = false;

  /**
   * Figurensatz des Ausdrucks: `merida` — kräftige Umrisse, satt gefüllte schwarze Figuren, also
   * der Satz, der dem klassischen Diagramm-Ausdruck (ChessBase & Co.) am nächsten kommt. Bewusst
   * UNABHÄNGIG vom Bildschirm-Satz des Profils: am Schirm entscheidet Geschmack, auf Papier
   * entscheidet, was bei 58 mm Kantenlänge in Graustufen noch lesbar bleibt.
   */
  readonly pieceSet = 'merida';

  /** Die Zwischenablage lässt sich nicht teilen — dort gibt es keinen QR-Code, nur den Hinweis. */
  isClipboard = false;

  private route = inject(ActivatedRoute);
  private worksheets = inject(WorksheetService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  get lines(): number[] { return solutionLines(this.perPage); }

  ngOnInit(): void {
    this.id = Number(this.route.snapshot.paramMap.get('id'));
    const autoPrint = this.route.snapshot.queryParamMap.get('print') === '1';

    this.worksheets.get(this.id).subscribe({
      next: sheet => this.finish(sheet, autoPrint),
      error: () => { this.loading = false; this.error = true; this.cdr.markForCheck(); },
    });
  }

  private finish(sheet: Worksheet, autoPrint: boolean): void {
    this.sheetName = sheet.isClipboard ? this.translate.instant('worksheets.clipboard') : sheet.name;
    this.perPage = sheet.perPage;
    this.isClipboard = sheet.isClipboard;
    this.shareUrl = sheet.shareToken ? this.worksheets.shareUrl(sheet.shareToken) : '';
    const tasks = sheet.items.map((item, index) => this.toTask(item, index + 1));
    this.sheets = toSheets(tasks, this.perPage);
    this.taskCount = tasks.length;
    this.loading = false;
    this.cdr.markForCheck();
    // Erst nach dem Rendern drucken; die Figuren sind SVG-Verweise und brauchen einen Moment.
    if (autoPrint && tasks.length > 0) setTimeout(() => this.print(), 500);
  }

  private toTask(item: WorksheetItem, no: number): PrintTask {
    return {
      no,
      fen: item.fen,
      orientation: item.orientation,
      whiteToMove: item.fen.split(' ')[1] !== 'b',
      heading: item.heading,
      text: item.text,
    };
  }

  /**
   * „Teilen einschalten" direkt aus der Druckansicht: Wer hier steht, will drucken — und ein Blatt
   * ohne Link trägt keinen QR-Code. Der Knopf steht nur am Schirm (im Druck ausgeblendet).
   */
  enableShare(): void {
    if (this.sharing || this.isClipboard) return;
    this.sharing = true;
    this.worksheets.share(this.id).subscribe({
      next: token => {
        this.sharing = false;
        this.shareUrl = token ? this.worksheets.shareUrl(token) : '';
        this.cdr.markForCheck();
      },
      error: () => { this.sharing = false; this.cdr.markForCheck(); },
    });
  }

  print(): void {
    window.print();
  }
}
