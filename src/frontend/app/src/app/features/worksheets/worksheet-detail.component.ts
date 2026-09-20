import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { FlashcardBoardComponent } from '../courses/flashcards/flashcard-board.component';
import { QrCodeComponent } from '../../shared/qr-code/qr-code.component';
import { Worksheet, WorksheetItem, WorksheetService } from './worksheet.service';

/**
 * Das AUFGABENBLATT in Arbeit (`/worksheets/:id`) — Zwischenablage wie benanntes Blatt.
 *
 * <p>Hier steht zwischen „Stellung geschickt" und „PDF": alle Aufgaben in einer Liste, einzeln
 * wegwerfbar, per Ziehen umsortierbar, jede mit Überschrift und Begleittext. Dazu die Dichte des
 * Ausdrucks (6/4/2 Diagramme je Seite) und, bei der Zwischenablage, der Weg zum benannten Blatt.</p>
 *
 * <p>Geschrieben wird kleinteilig und sofort (ein Feld verlässt den Fokus → PUT): ein Blatt ist
 * Bastelarbeit, und ein vergessener „Speichern"-Knopf kostet die halbe Stunde Tipparbeit.</p>
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-worksheet-detail',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, DragDropModule, MatButtonModule, MatButtonToggleModule,
    MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule,
    MatProgressSpinnerModule, MatSlideToggleModule, MatTooltipModule, TranslatePipe,
    FlashcardBoardComponent, QrCodeComponent,
  ],
  templateUrl: './worksheet-detail.component.html',
  styleUrls: ['./worksheet-detail.component.scss'],
})
export class WorksheetDetailComponent implements OnInit {
  id!: number;
  sheet: Worksheet | null = null;
  loading = true;
  error = false;
  busy = false;

  /** Entwürfe der Bedienleiste: neuer Name (Zwischenablage sichern / umbenennen) und FEN-Eingabe. */
  saveName = '';
  renaming = false;
  renameDraft = '';
  newFen = '';

  /** Figurensatz der Vorschau = der des Ausdrucks, damit die Liste zeigt, was auf dem Papier steht. */
  readonly pieceSet = 'merida';
  readonly perPageChoices = [6, 4, 2];

  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private worksheets = inject(WorksheetService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  ngOnInit(): void {
    this.id = Number(this.route.snapshot.paramMap.get('id'));
    this.load();
  }

  private load(): void {
    this.loading = true;
    this.worksheets.get(this.id).subscribe({
      next: sheet => { this.sheet = sheet; this.loading = false; this.error = false; this.cdr.markForCheck(); },
      error: () => { this.loading = false; this.error = true; this.cdr.markForCheck(); },
    });
  }

  /** Überschrift der Seite: die Zwischenablage heißt übersetzt, ein Blatt nach seinem Namen. */
  get title(): string {
    if (!this.sheet) return '';
    return this.sheet.isClipboard ? this.translate.instant('worksheets.clipboard') : this.sheet.name;
  }

  /** Wer ist am Zug? Steht in der FEN — auf dem Blatt (und hier in der Vorschau) die halbe Aufgabe. */
  whiteToMove(item: WorksheetItem): boolean {
    return item.fen.split(' ')[1] !== 'b';
  }

  // ===== Reihenfolge =====

  /** Umsortieren per Ziehen: erst die Liste, dann der Server — bei Fehler zurück auf den Serverstand. */
  drop(event: CdkDragDrop<WorksheetItem[]>): void {
    if (!this.sheet || event.previousIndex === event.currentIndex) return;
    moveItemInArray(this.sheet.items, event.previousIndex, event.currentIndex);
    this.persistOrder();
  }

  /** Dieselbe Bewegung ohne Maus (Handy, Tastatur): eine Stufe hoch/runter. */
  move(index: number, delta: number): void {
    if (!this.sheet) return;
    const target = index + delta;
    if (target < 0 || target >= this.sheet.items.length) return;
    moveItemInArray(this.sheet.items, index, target);
    this.persistOrder();
  }

  private persistOrder(): void {
    if (!this.sheet) return;
    this.worksheets.reorder(this.sheet.id, this.sheet.items.map(i => i.id)).subscribe({
      next: sheet => { this.sheet = sheet; this.cdr.markForCheck(); },
      error: () => { this.failed(); this.load(); },
    });
  }

  // ===== Einzelne Aufgabe =====

  /** Überschrift/Begleittext sichern, sobald das Feld den Fokus verlässt (kein „Speichern"-Knopf). */
  saveItem(item: WorksheetItem): void {
    if (!this.sheet) return;
    this.worksheets.updateItem(this.sheet.id, item.id, { heading: item.heading, text: item.text })
      .subscribe({ error: () => this.failed() });
  }

  /** Brett drehen — die Aufgabe steht oft für die andere Seite („Schwarz am Zug, was nun?"). */
  flip(item: WorksheetItem): void {
    if (!this.sheet) return;
    const orientation = item.orientation === 'white' ? 'black' : 'white';
    const before = item.orientation;
    item.orientation = orientation;   // optimistisch
    this.worksheets.updateItem(this.sheet.id, item.id, { orientation }).subscribe({
      error: () => { item.orientation = before; this.failed(); },
    });
  }

  removeItem(item: WorksheetItem): void {
    if (!this.sheet) return;
    const sheetId = this.sheet.id;
    this.worksheets.removeItem(sheetId, item.id).subscribe({
      next: () => {
        if (!this.sheet) return;
        this.sheet.items = this.sheet.items.filter(i => i.id !== item.id);
        this.sheet.itemCount = this.sheet.items.length;
        this.cdr.markForCheck();
      },
      error: () => this.failed(),
    });
  }

  /** Stellung von Hand ergänzen (FEN aus Analyse, Datenbank, Buch — was man gerade in der Hand hat). */
  addFen(): void {
    const fen = this.newFen.trim();
    if (!fen || !this.sheet || this.busy) return;
    this.busy = true;
    const orientation = fen.split(' ')[1] === 'b' ? 'black' : 'white';
    this.worksheets.addItems(this.sheet.id, [{ fen, orientation }]).subscribe({
      next: res => {
        this.busy = false;
        this.newFen = '';
        if (res.added === 0) this.snackbar.info(this.translate.instant('worksheets.detail.addRejected'));
        this.load();
      },
      error: () => { this.busy = false; this.failed(); },
    });
  }

  // ===== Blatt =====

  setPerPage(perPage: number): void {
    if (!this.sheet || this.sheet.perPage === perPage) return;
    const before = this.sheet.perPage;
    this.sheet.perPage = perPage;   // optimistisch
    this.worksheets.update(this.sheet.id, { perPage }).subscribe({
      error: () => { if (this.sheet) this.sheet.perPage = before; this.failed(); },
    });
  }

  startRename(): void {
    if (!this.sheet || this.sheet.isClipboard) return;
    this.renameDraft = this.sheet.name;
    this.renaming = true;
  }

  applyRename(): void {
    const name = this.renameDraft.trim();
    if (!this.sheet || !name) { this.renaming = false; return; }
    this.worksheets.update(this.sheet.id, { name }).subscribe({
      next: sheet => { this.sheet = { ...sheet, items: this.sheet?.items ?? [] }; this.renaming = false; this.cdr.markForCheck(); },
      error: () => { this.renaming = false; this.failed(); },
    });
  }

  /** Zwischenablage → benanntes Blatt: die Stellungen wandern mit, die Ablage bleibt leer zurück. */
  saveAsSheet(): void {
    const name = this.saveName.trim();
    if (!name || this.busy || !this.sheet?.isClipboard || this.sheet.items.length === 0) return;
    this.busy = true;
    this.worksheets.saveClipboardAs(name, this.sheet.perPage).subscribe({
      next: sheet => { this.busy = false; this.saveName = ''; this.router.navigate(['/worksheets', sheet.id]); },
      error: () => { this.busy = false; this.failed(); },
    });
  }

  clearSheet(): void {
    if (!this.sheet || this.sheet.items.length === 0) return;
    if (!confirm(this.translate.instant('worksheets.list.clearConfirm', { n: this.sheet.items.length }))) return;
    this.worksheets.clear(this.sheet.id).subscribe({
      next: sheet => { this.sheet = sheet; this.cdr.markForCheck(); },
      error: () => this.failed(),
    });
  }

  deleteSheet(): void {
    if (!this.sheet || this.sheet.isClipboard) return;
    if (!confirm(this.translate.instant('worksheets.list.deleteConfirm', { name: this.sheet.name }))) return;
    this.worksheets.remove(this.sheet.id).subscribe({
      next: () => this.router.navigate(['/worksheets']),
      error: () => this.failed(),
    });
  }

  /** Zurück in den Kurs, aus dem die Stellung stammt (Herkunftsvermerk der Aufgabe). */
  openSource(item: WorksheetItem): void {
    if (item.bookId) this.router.navigate(['/courses', item.bookId]);
  }

  // ===== Teilen =====

  /** Die Adresse hinter dem QR-Code; leer, solange das Blatt nicht geteilt ist. */
  get shareUrl(): string {
    return this.sheet?.shareToken ? this.worksheets.shareUrl(this.sheet.shareToken) : '';
  }

  /**
   * Teilen ein-/ausschalten. Ein einmal erzeugter Link bleibt beim erneuten Einschalten NICHT
   * erhalten — Abschalten ist der Widerruf, und ein gedruckter QR-Code soll danach ins Leere
   * laufen. Deshalb fragt das Abschalten nach.
   */
  toggleShare(share: boolean): void {
    if (!this.sheet || this.busy) return;
    if (share) {
      this.busy = true;
      this.worksheets.share(this.sheet.id).subscribe({
        next: token => {
          this.busy = false;
          if (this.sheet) this.sheet.shareToken = token;
          this.cdr.markForCheck();
        },
        error: () => { this.busy = false; this.failed(); },
      });
      return;
    }

    if (!confirm(this.translate.instant('worksheets.share.stopConfirm'))) { this.cdr.markForCheck(); return; }
    this.busy = true;
    this.worksheets.unshare(this.sheet.id).subscribe({
      next: () => {
        this.busy = false;
        if (this.sheet) this.sheet.shareToken = null;
        this.cdr.markForCheck();
      },
      error: () => { this.busy = false; this.failed(); },
    });
  }

  copyShareUrl(): void {
    const url = this.shareUrl;
    if (!url) return;
    navigator.clipboard?.writeText(url)
      .then(() => this.snackbar.copy(this.translate.instant('worksheets.share.copied')))
      .catch(() => { /* ohne Zwischenablage-Recht bleibt der Link zum Markieren im Feld stehen */ });
  }

  private failed(): void {
    this.snackbar.warn(this.translate.instant('worksheets.detail.saveError'));
    this.cdr.markForCheck();
  }
}
