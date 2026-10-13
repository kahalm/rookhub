import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { Ebook, EbookGroupOption, EbookList, EbookService, EbookSort, READABLE_FORMATS, formatBytes } from './ebook.service';
import { EbookEditDialogComponent, EbookEditDialogData } from './ebook-edit-dialog.component';
import { EbookVisibilityDialogComponent, EbookVisibilityDialogData } from './ebook-visibility-dialog.component';

export const EBOOK_PAGE_SIZE = 24;

/**
 * Übersicht der E-Book-Bibliothek (`/ebooks`): Suche, Schlagwort-Filter, Sortierung, Seiten; je Buch eine Karte mit
 * Titelbild, Lesestand und der Hauptaktion „Lesen" (PDF) bzw. „Herunterladen". Besitzer und Verwalter bearbeiten,
 * geben frei und löschen über das ⋮-Menü; die Auswahl (Kästchen) setzt die Sichtbarkeit vieler Bücher auf einmal.
 * Verwalter lesen zusätzlich die Bibliothek vom Server ein.
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatCheckboxModule, MatDialogModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule,
    MatTooltipModule, TranslatePipe, HelpHintComponent, IconLabelDirective, LoadErrorComponent, LoadingSpinnerComponent],
  templateUrl: './ebook-list.component.html',
  styleUrls: ['./ebook-list.component.scss'],
})
export class EbookListComponent implements OnInit, OnDestroy {
  list: EbookList | null = null;
  loading = true;
  error = false;
  token = '';

  search = '';
  tag: string | null = null;
  sort: EbookSort = 'title';
  mine = false;
  page = 1;
  readonly pageSize = EBOOK_PAGE_SIZE;
  readonly sorts: EbookSort[] = ['title', 'newest', 'recent'];

  /** Auswahlmodus für „Sichtbarkeit setzen" (nur verwaltbare Bücher lassen sich ankreuzen). */
  readonly selecting = signal(false);
  readonly selected = signal<Set<number>>(new Set());
  readonly importing = signal(false);

  private groups: EbookGroupOption[] | null = null;
  private search$ = new Subject<string>();
  private destroy$ = new Subject<void>();
  private ebooks = inject(EbookService);
  private dialog = inject(MatDialog);
  private confirm = inject(ConfirmService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntil(this.destroy$)).subscribe(() => { this.page = 1; this.load(); });
    this.ebooks.accessToken().subscribe({ next: t => { this.token = t; this.cdr.markForCheck(); }, error: () => {} });
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading = this.list == null;
    this.error = false;
    this.ebooks.list({ search: this.search.trim() || undefined, tag: this.tag, mine: this.mine, sort: this.sort, page: this.page, pageSize: this.pageSize })
      .subscribe({
        next: list => {
          this.list = list;
          this.loading = false;
          // Auswahl auf die Bücher dieser Seite eindampfen, die es noch gibt.
          const ids = new Set(list.items.map(i => i.id));
          this.selected.update(s => new Set([...s].filter(id => ids.has(id))));
          this.cdr.markForCheck();
        },
        error: () => { this.loading = false; this.error = true; this.cdr.markForCheck(); },
      });
  }

  get items(): Ebook[] { return this.list?.items ?? []; }
  get filtered(): boolean { return !!(this.search.trim() || this.tag || this.mine); }

  onSearch(value: string): void {
    this.search = value;
    this.search$.next(value.trim());
  }

  setTag(tag: string | null): void {
    this.tag = this.tag === tag ? null : tag;
    this.page = 1;
    this.load();
  }

  setSort(sort: EbookSort): void {
    this.sort = sort;
    this.page = 1;
    this.load();
  }

  toggleMine(): void {
    this.mine = !this.mine;
    this.page = 1;
    this.load();
  }

  onPage(e: PageEvent): void {
    this.page = e.pageIndex + 1;
    this.load();
    window.scrollTo({ top: 0 });
  }

  // ── Karten ──────────────────────────────────────────────────────────────────────────────────

  readable(book: Ebook): boolean { return !!book.primaryFormat && READABLE_FORMATS.has(book.primaryFormat); }

  coverUrl(book: Ebook): string | null {
    return book.hasCover && this.token ? this.ebooks.coverUrl(book.id, this.token, book.updatedAt) : null;
  }

  downloadUrl(book: Ebook, fileId?: number): string {
    return this.token ? this.ebooks.fileUrl(book.id, this.token, { fileId, download: true }) : '';
  }

  /** Lesestand in Prozent (0–100) oder `null`, wenn noch nicht angefangen. */
  progressPercent(book: Ebook): number | null {
    const p = book.progress;
    if (!p) return null;
    const count = p.pageCount ?? book.pageCount;
    if (!count || count < 1) return null;
    return Math.max(1, Math.min(100, Math.round(100 * p.page / count)));
  }

  /** Kurzform der Sichtbarkeit für die Karte eines verwaltbaren Buchs. */
  visibilityKey(book: Ebook): 'onlyMe' | 'everyone' | 'groups' {
    if (!book.groupIds || book.groupIds.length === 0) return 'onlyMe';
    const everyone = this.groups?.find(g => g.isEveryone)?.id;
    return everyone != null && book.groupIds.includes(everyone) ? 'everyone' : 'groups';
  }

  size(bytes: number): string { return formatBytes(bytes, this.translate.currentLang() ?? undefined); }

  // ── Aktionen ────────────────────────────────────────────────────────────────────────────────

  private withGroups(fn: (groups: EbookGroupOption[]) => void): void {
    if (this.groups) { fn(this.groups); return; }
    this.ebooks.groupOptions().subscribe({
      next: g => { this.groups = g; fn(g); },
      error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'common.loadFailed')),
    });
  }

  openUpload(): void {
    if (!this.list?.uploadEnabled) {
      this.snackbar.warn(this.translate.instant('apiErrors.ebooks_not_configured'));
      return;
    }
    this.withGroups(groups => {
      const data: EbookEditDialogData = { ebook: null, groups, maxFileBytes: this.list?.maxFileBytes ?? 0 };
      this.dialog.open(EbookEditDialogComponent, { data, width: '560px', maxWidth: '94vw', disableClose: true })
        .afterClosed().subscribe((saved: Ebook | null | undefined) => {
          if (!saved) return;
          this.snackbar.success(this.translate.instant('ebooks.uploaded', { title: saved.title }));
          this.load();
        });
    });
  }

  edit(book: Ebook): void {
    this.withGroups(groups => {
      const data: EbookEditDialogData = { ebook: book, groups, maxFileBytes: this.list?.maxFileBytes ?? 0 };
      this.dialog.open(EbookEditDialogComponent, { data, width: '560px', maxWidth: '94vw' })
        .afterClosed().subscribe((saved: Ebook | null | undefined) => { if (saved) this.load(); });
    });
  }

  remove(book: Ebook): void {
    this.confirm.ask('ebooks.deleteConfirm', { title: book.title }).subscribe(ok => {
      if (!ok) return;
      this.ebooks.delete(book.id).subscribe({
        next: () => { this.snackbar.quick(this.translate.instant('ebooks.deleted')); this.load(); },
        error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.deleteFailed')),
      });
    });
  }

  // ── Auswahl / Sichtbarkeit ──────────────────────────────────────────────────────────────────

  get manageable(): Ebook[] { return this.items.filter(i => i.canManage); }

  toggleSelecting(): void {
    this.selecting.update(v => !v);
    if (!this.selecting()) this.selected.set(new Set());
  }

  toggleSelected(book: Ebook, checked: boolean): void {
    this.selected.update(s => {
      const next = new Set(s);
      if (checked) next.add(book.id); else next.delete(book.id);
      return next;
    });
  }

  selectAllOnPage(): void {
    this.selected.set(new Set(this.manageable.map(b => b.id)));
  }

  setVisibility(books: Ebook[]): void {
    if (books.length === 0) return;
    this.withGroups(groups => {
      const data: EbookVisibilityDialogData = {
        ebookIds: books.map(b => b.id),
        groups,
        selected: books.length === 1 ? (books[0].groupIds ?? []) : [],
      };
      this.dialog.open(EbookVisibilityDialogComponent, { data, width: '440px', maxWidth: '94vw' })
        .afterClosed().subscribe((updated: number | null | undefined) => {
          if (updated == null) return;
          this.snackbar.quick(this.translate.instant('ebooks.bulk.done', { n: updated }));
          this.selected.set(new Set());
          this.selecting.set(false);
          this.load();
        });
    });
  }

  setVisibilitySelected(): void {
    const ids = this.selected();
    this.setVisibility(this.items.filter(b => ids.has(b.id)));
  }

  importLibrary(): void {
    this.importing.set(true);
    this.ebooks.importLibrary().subscribe({
      next: r => {
        this.importing.set(false);
        this.snackbar.info(this.translate.instant(r.configured ? 'ebooks.library.result' : 'apiErrors.ebooks_not_configured',
          { added: r.added, files: r.filesAdded, skipped: r.skipped }));
        this.load();
      },
      error: err => { this.importing.set(false); this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.library.failed')); },
    });
  }
}
