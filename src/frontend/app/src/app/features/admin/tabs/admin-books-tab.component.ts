import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AdminService, Book } from '../../../core/admin.service';
import { AuthService } from '../../../core/auth.service';
import { SnackbarService } from '../../../core/snackbar.service';
import { apiErrorText } from '../../../core/api-error';
import { ConfirmService } from '../../../shared/confirm-dialog/confirm-dialog.component';
import { PromptService } from '../../../shared/prompt-dialog/prompt-dialog.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { AdminGroupsStore } from '../admin-groups.store';
import { emptyBookFilters, filterBooks, hasActiveBookFilters } from './admin-book-filter';

/**
 * Admin-Tab „Bücher" (books.manage): PGN-Import, Suche + Spaltenfilter (`admin-book-filter.ts`), je Buch Art, Elo-Spanne,
 * Pools, Öffentlich/Kinder (mit Rückfrage, F5-013/N9-010), Kurz-Alias, Gruppen-Freigabe und Löschen. Aus
 * `AdminComponent` ausgelagert (Zerlegung Z30); die Gruppen-Auswahl kommt aus dem {@link AdminGroupsStore} der Seite.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-admin-books-tab',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatTableModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatSlideToggleModule, TranslatePipe, LoadingSpinnerComponent,
  ],
  templateUrl: './admin-books-tab.component.html',
  styleUrl: './admin-books-tab.component.scss',
})
export class AdminBooksTabComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly confirm = inject(ConfirmService);
  private readonly prompts = inject(PromptService);
  private readonly cdr = inject(ChangeDetectorRef);
  /** Gruppen-Auswahl für Filter und Freigaben (mit groups.manage die volle Liste, sonst die Optionen). */
  readonly groupOptions = inject(AdminGroupsStore).groupOptions;

  readonly books = signal<Book[]>([]);
  readonly filteredBooks = signal<Book[]>([]);
  readonly bookSearch = signal('');
  readonly booksLoading = signal(false);
  readonly booksUploading = signal(false);
  readonly bookColumns = ['displayName', 'puzzleCount', 'kind', 'difficulty', 'elo', 'forDaily', 'forRandom', 'forBlind', 'isPublic', 'forKids', 'groups', 'actions'];

  /** Per-Spalten-Filter über dem Bücher-Grid (UND-verknüpft, zusätzlich zur globalen Suche); Felder per ngModel. */
  bookFilters = emptyBookFilters();

  ngOnInit(): void {
    this.loadBooks();
  }

  loadBooks(): void {
    this.booksLoading.set(true);
    this.adminService.getBooks().subscribe({
      next: books => {
        this.books.set(books);
        this.applyBookFilter();
        this.booksLoading.set(false);
      },
      error: () => {
        this.snackbar.info(this.translate.instant('admin.books.errors.load'));
        this.booksLoading.set(false);
      }
    });
  }

  /** Clientseitiger Filter über die bereits geladenen Bücher (Name/Dateiname/Tags, case-insensitive). */
  applyBookFilter(): void {
    this.filteredBooks.set(filterBooks(this.books(), this.bookSearch(), this.bookFilters));
  }

  hasActiveBookFilters(): boolean {
    return hasActiveBookFilters(this.bookFilters);
  }

  resetBookFilters(): void {
    this.bookFilters = emptyBookFilters();
    this.applyBookFilter();
  }

  clearBookSearch(): void {
    this.bookSearch.set('');
    this.applyBookFilter();
  }

  /** Buch umbenennen (nur DisplayName; Backend akzeptiert das im Update-DTO). */
  renameBook(book: Book): void {
    this.prompts.ask({
      title: this.translate.instant('admin.books.rename'),
      fields: [{ key: 'name', label: this.translate.instant('admin.books.renamePrompt'), value: book.displayName }],
    }).subscribe(res => {
      if (!res) return;
      const name = (res['name'] ?? '').trim();
      if (!name || name === book.displayName) return;
      this.adminService.updateBook(book.id, { displayName: name }).subscribe({
        next: () => {
          book.displayName = name;
          this.applyBookFilter();
          this.cdr.markForCheck();   // die Zeile zeigt das geänderte Buch-Objekt
        },
        error: err => this.snackbar.info(apiErrorText(err, this.translate, 'admin.books.errors.save'))
      });
    });
  }

  onBookFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    this.booksUploading.set(true);
    this.adminService.importBooks(input.files).subscribe({
      next: res => {
        const parts = [this.translate.instant(res.totalImported === 1 ? 'admin.books.importedCountOne' : 'admin.books.importedCount', { count: res.totalImported })];
        if (res.totalSkipped > 0) parts.push(this.translate.instant(res.totalSkipped === 1 ? 'admin.books.duplicatesCountOne' : 'admin.books.duplicatesCount', { count: res.totalSkipped }));
        if (res.totalInvalid > 0) parts.push(this.translate.instant('admin.books.invalidCount', { count: res.totalInvalid }));
        this.snackbar.info(parts.join(', '), { duration: 6000 });
        this.booksUploading.set(false);
        input.value = '';
        this.loadBooks();
      },
      error: err => {
        this.snackbar.info(apiErrorText(err, this.translate, 'admin.books.errors.import'), { duration: 4000 });
        this.booksUploading.set(false);
        input.value = '';
      }
    });
  }

  /**
   * Titel auf KidHub, alle Sprachen der Kinderseite in EINEM Dialog (F5-016 — vorher vier Abfragen
   * hintereinander, Abbrechen bei der letzten verwarf alle). Abbrechen speichert nichts; leer lassen =
   * kein eigener Titel in dieser Sprache. Die Sprachnamen stehen in der eigenen Sprache (wie in der Sprachwahl).
   */
  editKidsTitles(book: Book): void {
    const languages = [['de', 'Deutsch'], ['en', 'English'], ['hr', 'Hrvatski'], ['hu', 'Magyar']];
    this.prompts.ask({
      title: this.translate.instant('admin.books.kidsTitles'),
      hint: this.translate.instant('admin.books.kidsTitlesHint'),
      fields: languages.map(([code, name]) => ({ key: code, label: name, value: book.kidsTitles?.[code] ?? '' })),
    }).subscribe(res => {
      if (!res) return;
      const titles: Record<string, string> = {};
      for (const [code] of languages) titles[code] = (res[code] ?? '').trim();
      this.adminService.updateBook(book.id, { kidsTitles: titles }).subscribe({
        next: saved => { book.kidsTitles = saved.kidsTitles; this.cdr.markForCheck(); },
        error: err => this.snackbar.info(apiErrorText(err, this.translate, 'admin.books.errors.save')),
      });
    });
  }

  /** Privates Buch eines ANDEREN Kontos (Nutzer-Import/-Upload) — in der Liste markiert (N9-010). */
  isForeignBook(book: Book): boolean {
    return book.ownerUserId != null && book.ownerUserId !== this.auth.currentUser?.userId;
  }

  /** „Öffentlich" und „Kinder" schalten ein Buch ohne Anmeldung frei — beim EINSCHALTEN erst nachfragen (F5-013).
   *  Bei einem privaten Buch eines anderen Kontos fragen auch die Pools (Täglich/Zufall/Blind, ebenfalls anonym
   *  lesbar) nach und nennen den Besitzer (N9-010). Abbrechen stellt den Schalter zurück, Ausschalten speichert sofort. */
  toggleExposure(book: Book, field: 'isPublic' | 'forKids' | 'forDaily' | 'forRandom' | 'forBlind'): void {
    const foreign = this.isForeignBook(book);
    if (!book[field] || (!foreign && field !== 'isPublic' && field !== 'forKids')) { this.saveBook(book); return; }
    const key = foreign ? 'admin.books.confirmForeign'
      : field === 'isPublic' ? 'admin.books.confirmPublic' : 'admin.books.confirmKids';
    const params = foreign ? { name: book.displayName, owner: book.ownerName || `#${book.ownerUserId}` } : { name: book.displayName };
    this.confirm.ask(key, params).subscribe(ok => {
      if (ok) this.saveBook(book);
      else { book[field] = false; this.cdr.markForCheck(); }
    });
  }

  saveBook(book: Book): void {
    this.adminService.updateBook(book.id, {
      forDaily: book.forDaily,
      forRandom: book.forRandom,
      forBlind: book.forBlind,
      isPublic: book.isPublic,
      forKids: book.forKids,
      publicSlug: book.publicSlug ?? '',
      kind: book.kind,
      // Geleertes Elo-Feld = null; der Endpunkt liest null als „unverändert", 0 entfernt die Grenze (N9-009).
      minElo: book.minElo ?? 0,
      maxElo: book.maxElo ?? 0
    }).subscribe({
      error: err => {
        this.snackbar.info(apiErrorText(err, this.translate, 'admin.books.errors.save'));
        this.loadBooks(); // Stand zurücksetzen
      }
    });
  }

  saveBookGroups(book: Book): void {
    this.adminService.updateBookGroups(book.id, book.accessGroupIds ?? []).subscribe({
      error: err => {
        this.snackbar.info(apiErrorText(err, this.translate, 'admin.books.errors.saveGroups'));
        this.loadBooks(); // Stand zurücksetzen
      }
    });
  }

  deleteBook(book: Book): void {
    this.confirm.ask(book.puzzleCount === 1 ? 'admin.books.deleteConfirmOne' : 'admin.books.deleteConfirm', { name: book.displayName, count: book.puzzleCount }).subscribe(ok => {
      if (!ok) return;

      this.adminService.deleteBook(book.id).subscribe({
        next: () => {
          this.snackbar.info(this.translate.instant('admin.books.deleted', { name: book.displayName }));
          this.loadBooks();
        },
        error: err => {
          this.snackbar.info(apiErrorText(err, this.translate, 'admin.books.errors.delete'));
        }
      });
    });
  }
}
