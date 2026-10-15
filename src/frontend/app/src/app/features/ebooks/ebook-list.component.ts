import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { readJson, sessionStore, writeJson } from '../../core/local-json-store';
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
import { AuthService } from '../../core/auth.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { mediaSignal } from '../../shared/layout/wide-media';
import { Ebook, EbookGroupOption, EbookList, EbookTagFacet, EbookService, EbookSort, READABLE_FORMATS, ebookAuthorNames, formatBytes } from './ebook.service';
import { EbookEditDialogComponent, EbookEditDialogData } from './ebook-edit-dialog.component';
import { EbookVisibilityDialogComponent, EbookVisibilityDialogData } from './ebook-visibility-dialog.component';
import { EbookCollectionDialogComponent, EbookCollectionDialogData } from './ebook-collection-dialog.component';
import { PromptService } from '../../shared/prompt-dialog/prompt-dialog.component';

export const EBOOK_PAGE_SIZE = 60;

/** Zugeklappt zeigt die Schlagwort-Leiste nur die häufigsten (der Server liefert sie nach Anzahl), c1-eb-tag-filter-lead. */
export const EBOOK_TAGS_COLLAPSED = 8;

/** Sitzungs-Schlüssel der Listen-Ansicht (Suche, Filter, Seite) — „zurück" aus dem Leser landet wieder dort. */
export const EBOOK_LIST_STATE_KEY = 'rh.ebooks.listState';

export interface EbookListState { search: string; tag: string | null; author?: string | null; sort: EbookSort; mine: boolean; withDiagrams: boolean; page: number; collection?: number | null; }

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
  /** Autor-Filter (0.796.0): Klick auf einen Namen unter einem Buch zeigt alle Bücher dieses Autors. */
  author: string | null = null;
  /** Vorgabe „Neueste zuerst“ (0.775.4, Nutzerwunsch) — frisch hochgeladene/eingelesene Bücher stehen oben. */
  sort: EbookSort = 'newest';
  mine = false;
  withDiagrams = false;
  page = 1;
  readonly pageSize = EBOOK_PAGE_SIZE;
  readonly sorts: EbookSort[] = ['title', 'newest', 'recent'];

  /** Handy (c2-eb-search-placeholder-truncated): kurzer Platzhalter „Bücher suchen“ neben „Filter ▾“; aria-label bleibt lang. */
  readonly narrow = mediaSignal('(max-width: 599.98px)');
  /** Auswahlmodus für „Sichtbarkeit setzen" (nur verwaltbare Bücher lassen sich ankreuzen). */
  readonly selecting = signal(false);
  readonly selected = signal<Set<number>>(new Set());
  /** Geöffnete Sammlung (0.787.0); `null` = Hauptliste, Sammlungen dort als Ordner. */
  collection: number | null = null;
  readonly importing = signal(false);
  /** Schlagwort-Leiste ganz aufgeklappt (sonst nur die häufigsten + das aktive). */
  readonly tagsOpen = signal(false);
  /** Handy: Sortierung, Schalter und Schlagwörter aufgeklappt (ab 600 px stehen sie immer da). */
  readonly filtersOpen = signal(false);
  readonly tagsCollapsedCount = EBOOK_TAGS_COLLAPSED;

  private groups: EbookGroupOption[] | null = null;
  private search$ = new Subject<string>();
  private destroy$ = new Subject<void>();
  private ebooks = inject(EbookService);
  private auth = inject(AuthService);
  private dialog = inject(MatDialog);
  private confirm = inject(ConfirmService);
  private prompt = inject(PromptService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  private route = inject(ActivatedRoute, { optional: true });
  private router = inject(Router, { optional: true });

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntil(this.destroy$)).subscribe(() => { this.page = 1; this.load(); });
    this.ebooks.accessToken().subscribe({ next: t => { this.token = t; this.cdr.markForCheck(); }, error: () => {} });
    this.restoreState();
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * Ansicht wiederherstellen: zuerst aus der Adresse (Lesezeichen, Zurück-Taste des Browsers), sonst aus der Sitzung
   * (Zurück-Pfeil im Leser führt auf das nackte /ebooks).
   */
  private restoreState(): void {
    const q = this.route?.snapshot.queryParamMap;
    let st: Partial<EbookListState> | null = null;
    if (q && q.keys.length > 0) {
      st = { search: q.get('q') ?? '', tag: q.get('tag'), author: q.get('author'), sort: (q.get('sort') as EbookSort) ?? 'newest',
        mine: q.get('mine') === '1', withDiagrams: q.get('diagrams') === '1', page: Number(q.get('page')) || 1,
        collection: Number(q.get('collection')) || null };
    } else {
      const store = sessionStore();
      st = store ? readJson<Partial<EbookListState>>(store, EBOOK_LIST_STATE_KEY) : null;
    }
    if (!st) return;
    this.search = st.search ?? '';
    this.tag = st.tag ?? null;
    this.author = st.author ?? null;
    this.sort = this.sorts.includes(st.sort as EbookSort) ? st.sort as EbookSort : 'newest';
    this.mine = !!st.mine;
    this.withDiagrams = !!st.withDiagrams;
    this.page = Math.max(1, Math.floor(st.page ?? 1));
    this.collection = st.collection ?? null;
  }

  /** Aktuelle Ansicht in Sitzung + Adresse (replaceUrl: kein neuer Verlaufseintrag je Tastendruck). */
  private saveState(): void {
    const st: EbookListState = { search: this.search.trim(), tag: this.tag, author: this.author, sort: this.sort, mine: this.mine, withDiagrams: this.withDiagrams, page: this.page, collection: this.collection };
    const store = sessionStore();
    if (store) writeJson(store, EBOOK_LIST_STATE_KEY, st);
    const queryParams: Record<string, string | null> = {
      q: st.search || null, tag: st.tag, author: st.author || null, sort: st.sort !== 'newest' ? st.sort : null,
      mine: st.mine ? '1' : null, diagrams: st.withDiagrams ? '1' : null, page: st.page > 1 ? String(st.page) : null,
      collection: st.collection ? String(st.collection) : null,
    };
    void this.router?.navigate([], { relativeTo: this.route ?? undefined, queryParams, replaceUrl: true });
  }

  load(): void {
    this.saveState();
    this.loading = this.list == null;
    this.error = false;
    this.ebooks.list({ search: this.search.trim() || undefined, tag: this.tag, author: this.author, mine: this.mine, withDiagrams: this.withDiagrams, collection: this.collection, sort: this.sort, page: this.page, pageSize: this.pageSize })
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
  get filtered(): boolean { return !!(this.search.trim() || this.tag || this.author || this.mine || this.withDiagrams); }

  /** Schlagwörter, die die Leiste gerade zeigt: aufgeklappt alle, sonst die häufigsten plus das aktive. */
  get visibleTags(): EbookTagFacet[] {
    const all = this.list?.tags ?? [];
    if (this.tagsOpen() || all.length <= EBOOK_TAGS_COLLAPSED) return all;
    return all.filter((t, i) => i < EBOOK_TAGS_COLLAPSED || t.tag === this.tag);
  }

  /** Zahl der aktiven Filter hinter „Filter ▾“ (Handy). Die Suche steht sichtbar daneben und zählt nicht. */
  get activeFilterCount(): number {
    return (this.tag ? 1 : 0) + (this.mine ? 1 : 0) + (this.withDiagrams ? 1 : 0);
  }

  /** Alle Filter weg (Leerzustand „Zu diesem Filter passt nichts.“, c1-eb-filter-empty-no-reset). Sortierung bleibt. */
  resetFilters(): void {
    const hadSearch = this.search.trim().length > 0;
    this.tag = null;
    this.author = null;
    this.mine = false;
    this.withDiagrams = false;
    this.page = 1;
    // Über den Such-Strom, damit distinctUntilChanged den alten Suchbegriff vergisst (sonst lädt dieselbe Suche nie wieder).
    if (hadSearch) this.onSearch(''); else this.load();
  }

  onSearch(value: string): void {
    this.search = value;
    this.search$.next(value.trim());
  }

  readonly authorNames = ebookAuthorNames;

  /** Klick auf einen Autor: nur seine Bücher; derselbe Name noch einmal (oder ✕ am Chip) hebt den Filter auf. */
  setAuthor(name: string | null): void {
    this.author = name && this.author !== name ? name : null;
    this.page = 1;
    this.load();
    if (this.author) globalThis.scrollTo?.({ top: 0, behavior: 'smooth' });
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

  toggleWithDiagrams(): void {
    this.withDiagrams = !this.withDiagrams;
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

  /** Ohne Anmeldung (0.778.0): „Alle“-Bücher + eigene Uploads, keine Freigaben. */
  get anonymous(): boolean { return !this.auth.isLoggedIn; }

  /** Zur Anmeldung, danach zurück in die Bibliothek (die anonymen Uploads wandern dabei ins Konto). */
  get loginQuery(): Record<string, string> { return { returnUrl: '/ebooks' }; }

  private withGroups(fn: (groups: EbookGroupOption[]) => void): void {
    if (this.anonymous) { fn([]); return; }   // Freigabe-Gruppen gibt es nur mit Konto
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
      const data: EbookEditDialogData = { ebook: null, groups, maxFileBytes: this.list?.maxFileBytes ?? 0, anonymous: this.anonymous };
      this.dialog.open(EbookEditDialogComponent, { data, width: '560px', maxWidth: '94vw', disableClose: true })
        .afterClosed().subscribe((saved: Ebook | null | undefined) => {
          if (!saved) return;
          // Verarbeitung dauert (Titelbild, DJVU→PDF, Diagramme, Lösungen, Stockfish) — sagen, dass eine Meldung kommt.
          this.snackbar.success(this.translate.instant(saved.processing ? 'ebooks.uploadedProcessing' : 'ebooks.uploaded', { title: saved.title }), { duration: 9000 });
          this.load();
        });
    });
  }

  edit(book: Ebook): void {
    this.withGroups(groups => {
      const data: EbookEditDialogData = { ebook: book, groups, maxFileBytes: this.list?.maxFileBytes ?? 0, anonymous: this.anonymous };
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

  // ── Sammlungen (0.787.0) ────────────────────────────────────────────────────────────────────

  openCollection(id: number): void {
    this.collection = id;
    this.page = 1;
    this.selected.set(new Set());
    this.load();
    window.scrollTo?.({ top: 0 });
  }

  closeCollection(): void {
    this.collection = null;
    this.page = 1;
    this.selected.set(new Set());
    this.load();
  }

  assignCollectionSelected(): void {
    const ebookIds = [...this.selected()];
    if (ebookIds.length === 0) return;
    const data: EbookCollectionDialogData = { ebookIds };
    this.dialog.open(EbookCollectionDialogComponent, { data, width: '440px', maxWidth: '94vw' })
      .afterClosed().subscribe(c => {
        if (c === undefined) return;
        this.snackbar.quick(this.translate.instant('ebooks.collection.assigned', { name: c?.name ?? '' }));
        this.selected.set(new Set());
        this.selecting.set(false);
        this.load();
      });
  }

  removeFromCollectionSelected(): void {
    const ebookIds = [...this.selected()];
    if (ebookIds.length === 0) return;
    this.ebooks.assignCollection(ebookIds, null, null).subscribe({
      next: () => {
        this.snackbar.quick(this.translate.instant('ebooks.collection.removed', { n: ebookIds.length }));
        this.selected.set(new Set());
        this.selecting.set(false);
        if (this.list?.collection && ebookIds.length >= this.list.collection.bookCount) this.closeCollection(); else this.load();
      },
      error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.saveFailed')),
    });
  }

  renameCollection(): void {
    const c = this.list?.collection;
    if (!c) return;
    this.prompt.ask({ title: this.translate.instant('ebooks.collection.rename'),
      fields: [{ key: 'name', label: this.translate.instant('ebooks.collection.name'), value: c.name }] })
      .subscribe(v => {
        const name = v?.['name']?.trim();
        if (!name || name === c.name) return;
        this.ebooks.renameCollection(c.id, name).subscribe({
          next: () => this.load(),
          error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.saveFailed')),
        });
      });
  }

  dissolveCollection(): void {
    const c = this.list?.collection;
    if (!c) return;
    this.confirm.ask('ebooks.collection.dissolveConfirm', { name: c.name }).subscribe(ok => {
      if (!ok) return;
      this.ebooks.deleteCollection(c.id).subscribe({
        next: () => { this.snackbar.quick(this.translate.instant('ebooks.collection.dissolved')); this.closeCollection(); },
        error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.saveFailed')),
      });
    });
  }
}
