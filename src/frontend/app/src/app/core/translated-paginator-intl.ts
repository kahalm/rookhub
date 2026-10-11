import { Injectable, OnDestroy, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';

/** Schlüssel der Blätterleiste in `common.paginator.*` (alle 25 Sprachdateien). */
const KEYS = {
  itemsPerPage: 'common.paginator.itemsPerPage',
  firstPage: 'common.paginator.firstPage',
  previousPage: 'common.paginator.previousPage',
  nextPage: 'common.paginator.nextPage',
  lastPage: 'common.paginator.lastPage',
} as const;

/**
 * UI-Sweep 2026-10-11 (r2-rk-paginator-en): Material liefert die Blätterleiste nur englisch
 * („Items per page:", „1 – 11 of 11"). Diese Fassung holt Beschriftung, Tooltips der Pfeile und den
 * Bereich („1–11 von 11") über ngx-translate und meldet jeden Sprachwechsel bzw. das Nachladen der
 * Sprachdatei über `changes`, damit jede offene `mat-paginator` neu beschriftet.
 * App-weit bereitgestellt in `app.config.ts`.
 */
@Injectable()
export class TranslatedPaginatorIntl extends MatPaginatorIntl implements OnDestroy {
  private readonly translate = inject(TranslateService);
  private readonly sub: Subscription;

  constructor() {
    super();
    // stream() liefert sofort (sofern geladen) und bei jedem Sprachwechsel / nachgeladener Datei.
    this.sub = this.translate.stream(Object.values(KEYS)).subscribe((t: Record<string, string>) => {
      this.itemsPerPageLabel = t[KEYS.itemsPerPage];
      this.firstPageLabel = t[KEYS.firstPage];
      this.previousPageLabel = t[KEYS.previousPage];
      this.nextPageLabel = t[KEYS.nextPage];
      this.lastPageLabel = t[KEYS.lastPage];
      this.changes.next();
    });
  }

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0 || pageSize === 0) {
      return this.translate.instant('common.paginator.rangeEmpty', { length });
    }
    const start = page * pageSize;
    // Wie Material: liegt der Seitenanfang hinter dem Ende, gilt die volle Seitengröße.
    const end = start < length ? Math.min(start + pageSize, length) : start + pageSize;
    return this.translate.instant('common.paginator.range', { start: start + 1, end, length });
  };

  ngOnDestroy(): void {
    this.sub.unsubscribe();
  }
}
