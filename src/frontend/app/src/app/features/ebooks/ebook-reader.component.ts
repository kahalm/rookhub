import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { Ebook, EbookDiagram, EbookService, READABLE_FORMATS, formatBytes } from './ebook.service';
import { PdfViewerComponent, PdfZone } from './pdf-viewer.component';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { nextZoom, parsePageInput } from './reader-progress.util';

/**
 * Leser-Seite (`/ebooks/:id`): Werkzeugleiste (zurück, Titel, Seite x / N, Zoom, Download) und darunter der
 * PDF-Leser mit der ganzen Resthöhe. Hat das Buch erkannte Diagramme (0.766.0), steht rechts ein Brett: Doppelklick auf
 * ein Diagramm im Buch holt die Stellung herüber, ‹/› springen zum vorigen/nächsten Diagramm, „Analysieren" öffnet sie
 * im Analysebrett. Unter 900 px liegt das Brett als Leiste unten und erscheint erst mit dem ersten Doppelklick. Der Lesestand wird 1,5 s nach dem letzten Seitenwechsel gespeichert und beim
 * nächsten Öffnen angesprungen. Formate, die der Browser nicht zeigt (EPUB, DJVU …), bekommen eine Download-Karte.
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-reader',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslatePipe,
    IconLabelDirective, LoadErrorComponent, LoadingSpinnerComponent, PdfViewerComponent, ChessBoardComponent],
  templateUrl: './ebook-reader.component.html',
  styleUrls: ['./ebook-reader.component.scss'],
})
export class EbookReaderComponent implements OnInit, OnDestroy {
  @ViewChild(PdfViewerComponent) private viewer?: PdfViewerComponent;

  ebook: Ebook | null = null;
  token = '';
  loading = true;
  error = false;
  /** Adresse der Hauptdatei (mit Ausweis), wenn sie der Browser zeigen kann. */
  fileUrl: string | null = null;
  readonly page = signal(1);
  readonly pageCount = signal(0);
  /** `null` = Breite füllen. */
  readonly zoom = signal<number | null>(null);
  readonly appliedZoom = signal(1);
  readonly viewerFailed = signal(false);
  initialPage = 1;

  /** Erkannte Diagramme in Lesereihenfolge (leer, solange die Erkennung läuft oder das Buch ein Scan ist). */
  readonly diagrams = signal<EbookDiagram[]>([]);
  readonly zones = signal<PdfZone[]>([]);
  /** Das Diagramm auf dem Brett. */
  readonly picked = signal<EbookDiagram | null>(null);
  readonly flipped = signal(false);
  /** Schmale Ansicht: Brettleiste ein-/ausgeklappt. */
  readonly panelOpen = signal(false);

  private route = inject(ActivatedRoute);
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  private progress$ = new Subject<number>();
  private destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.progress$.pipe(debounceTime(1500), distinctUntilChanged(), takeUntil(this.destroy$)).subscribe(p => this.saveProgress(p));
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.loading = true;
    this.error = false;
    this.ebooks.get(id).subscribe({
      next: book => {
        this.ebook = book;
        this.initialPage = book.progress?.page ?? 1;
        this.page.set(this.initialPage);
        if (book.pageCount) this.pageCount.set(book.pageCount);
        if (book.diagramState === 'done' && book.diagramCount > 0) this.loadDiagrams(book.id);
        this.ebooks.accessToken().subscribe({
          next: token => {
            this.token = token;
            this.fileUrl = book.primaryFormat && READABLE_FORMATS.has(book.primaryFormat) ? this.ebooks.fileUrl(book.id, token) : null;
            this.loading = false;
            this.cdr.markForCheck();
          },
          error: () => { this.loading = false; this.error = true; this.cdr.markForCheck(); },
        });
      },
      error: () => { this.loading = false; this.error = true; this.cdr.markForCheck(); },
    });
  }

  get readable(): boolean { return this.fileUrl != null; }

  get hasDiagrams(): boolean { return this.diagrams().length > 0; }

  private loadDiagrams(id: number): void {
    this.ebooks.diagrams(id).subscribe({
      next: list => {
        this.diagrams.set(list.items);
        const title = this.translate.instant('ebooks.reader.diagramZone');
        this.zones.set(list.items.map(d => ({
          id: d.id, page: d.page, x: d.x, y: d.y, w: d.w, h: d.h,
          title: d.label ? `${d.label} · ${title}` : title,
        })));
        this.cdr.markForCheck();
      },
      // Ohne Diagramme bleibt der Leser einspaltig — das Buch ist trotzdem lesbar, kein Fehlerhinweis nötig.
      error: () => {},
    });
  }

  /** Doppelklick im Buch: Stellung aufs Brett. */
  pick(id: number): void {
    const d = this.diagrams().find(x => x.id === id);
    if (!d) return;
    this.picked.set(d);
    this.flipped.set(d.fen.split(' ')[1] === 'b');
    this.panelOpen.set(true);
  }

  /** ‹ / › — voriges/nächstes Diagramm, das Buch scrollt mit. */
  step(delta: 1 | -1): void {
    const list = this.diagrams();
    if (list.length === 0) return;
    const cur = this.picked();
    let idx = cur ? list.findIndex(d => d.id === cur.id) + delta : (delta > 0 ? list.findIndex(d => d.page >= this.page()) : -1);
    if (idx < 0) idx = delta > 0 ? 0 : list.length - 1;
    if (idx >= list.length) idx = list.length - 1;
    const d = list[idx];
    this.pick(d.id);
    this.viewer?.revealZone(d);
  }

  /** Position in der Liste (für „3 / 1 144"). */
  get pickedIndex(): number {
    const cur = this.picked();
    return cur ? this.diagrams().findIndex(d => d.id === cur.id) + 1 : 0;
  }

  sideKey(d: EbookDiagram): string {
    if (!d.sideKnown) return 'ebooks.reader.sideUnknown';
    return d.fen.split(' ')[1] === 'b' ? 'ebooks.reader.blackToMove' : 'ebooks.reader.whiteToMove';
  }

  analysisParams(d: EbookDiagram): Record<string, string> {
    return { fen: d.fen, orientation: this.flipped() ? 'black' : 'white', from: `/ebooks/${this.ebook?.id ?? ''}` };
  }

  /** „Diagramm falsch": melden bzw. zweiter Klick nimmt die Meldung zurück. */
  toggleReport(d: EbookDiagram): void {
    if (!this.ebook) return;
    const next = !d.reportedByMe;
    this.ebooks.reportDiagram(this.ebook.id, d.id, next).subscribe({
      next: r => {
        const updated = { ...d, reportedByMe: r.reported, reportCount: Math.max(0, d.reportCount + (r.reported ? 1 : -1)) };
        this.diagrams.update(list => list.map(x => x.id === d.id ? updated : x));
        if (this.picked()?.id === d.id) this.picked.set(updated);
        this.snackbar.quick(this.translate.instant(r.reported ? 'ebooks.reader.reported' : 'ebooks.reader.reportWithdrawn'));
      },
      error: () => this.snackbar.warn(this.translate.instant('ebooks.reader.reportFailed')),
    });
  }

  copyFen(d: EbookDiagram): void {
    navigator.clipboard?.writeText(d.fen).then(
      () => this.snackbar.quick(this.translate.instant('ebooks.reader.fenCopied')),
      () => this.snackbar.warn(this.translate.instant('ebooks.reader.copyFailed')),
    );
  }

  onLoaded(e: { pageCount: number; fitZoom: number }): void {
    this.pageCount.set(e.pageCount);
    this.viewerFailed.set(false);
  }

  onViewerFailed(): void {
    this.viewerFailed.set(true);
    // Ein abgelaufener Ausweis ist der häufigste Grund — beim nächsten Öffnen kommt ein frischer.
    this.ebooks.forgetToken();
  }

  onPageChange(p: number): void {
    this.page.set(p);
    this.progress$.next(p);
  }

  private saveProgress(page: number): void {
    if (!this.ebook) return;
    this.ebooks.setProgress(this.ebook.id, page, this.pageCount() || null).subscribe({
      error: () => this.snackbar.warn(this.translate.instant('ebooks.reader.saveProgressFailed')),
    });
  }

  jump(raw: string): void {
    const p = parsePageInput(raw, this.pageCount());
    if (p == null) return;
    this.viewer?.goTo(p);
  }

  zoomBy(direction: 1 | -1): void {
    this.zoom.set(nextZoom(this.appliedZoom(), direction));
  }

  fitWidth(): void { this.zoom.set(null); }

  downloadUrl(fileId?: number): string {
    return this.ebook ? this.ebooks.fileUrl(this.ebook.id, this.token, { fileId, download: true }) : '';
  }

  size(bytes: number): string { return formatBytes(bytes, this.translate.currentLang() ?? undefined); }
}
