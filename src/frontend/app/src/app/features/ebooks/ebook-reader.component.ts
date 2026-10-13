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
import { Ebook, EbookService, READABLE_FORMATS, formatBytes } from './ebook.service';
import { PdfViewerComponent } from './pdf-viewer.component';
import { nextZoom, parsePageInput } from './reader-progress.util';

/**
 * Leser-Seite (`/ebooks/:id`): Werkzeugleiste (zurück, Titel, Seite x / N, Zoom, Download) und darunter der
 * PDF-Leser mit der ganzen Resthöhe. Der Lesestand wird 1,5 s nach dem letzten Seitenwechsel gespeichert und beim
 * nächsten Öffnen angesprungen. Formate, die der Browser nicht zeigt (EPUB, DJVU …), bekommen eine Download-Karte.
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-ebook-reader',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslatePipe,
    IconLabelDirective, LoadErrorComponent, LoadingSpinnerComponent, PdfViewerComponent],
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
