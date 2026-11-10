import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadErrorComponent } from '../../../shared/load-error/load-error.component';

/** Gemeldete falsche Stellung (`GET /api/admin/ebooks/diagram-reports`). */
export interface EbookDiagramReportRow {
  id: number; ebookId: number; ebookTitle: string; diagramId: number; diagramExists: boolean; page: number; fen: string;
  note: string | null; username: string | null; fontFamily: string | null; createdAt: string;
}

/** Fehlendes Diagramm (`GET /api/admin/ebooks/missed-diagrams`, 0.945.0): Leser-Rückfrage oder importierter Fund. */
export interface EbookMissedDiagramRow {
  id: number; ebookId: number; ebookTitle: string; page: number; x: number; y: number;
  epubRef: string | null; epubChapter: string | null; isDiagram: boolean; note: string | null; source: string | null;
  userId: number; username: string | null; createdAt: string;
}

/** Lage eines Klickpunkts (Anteile 0–1, links oben) als Drittel-Raster: „tl“ = oben links … „br“ = unten rechts. */
export function positionKey(x: number, y: number): string {
  const third = (v: number) => (v < 1 / 3 ? 0 : v < 2 / 3 ? 1 : 2);
  return 'tmb'[third(y)] + 'lcr'[third(x)];
}

/**
 * Admin-Tab „Diagramm-Meldungen“ (0.945.0): falsche Stellungen und fehlende Diagramme der E-Book-Bibliothek in zwei Listen,
 * je Zeile ein Link in den Leser auf die Seite. Filter nach Buch (über beide Listen).
 */
@Component({
  selector: 'app-admin-diagram-reports',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatFormFieldModule, MatSelectModule, TranslatePipe, LoadErrorComponent],
  template: `
    <div class="diag">
      <p class="intro">{{ 'admin.diagrams.intro' | translate }}</p>
      @if (error()) {
        <app-load-error (retry)="load()" />
      } @else if (reports() && missed()) {
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="book-filter">
          <mat-label>{{ 'admin.diagrams.book' | translate }}</mat-label>
          <mat-select [value]="bookId()" (selectionChange)="bookId.set($event.value)">
            <mat-option [value]="null">{{ 'admin.diagrams.allBooks' | translate }}</mat-option>
            @for (b of books(); track b.id) {
              <mat-option [value]="b.id">{{ b.title }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <h3>{{ 'admin.diagrams.wrongTitle' | translate }} ({{ shownReports().length }})</h3>
        @if (!shownReports().length) {
          <p class="empty-state">{{ 'admin.diagrams.empty' | translate }}</p>
        } @else {
          <div class="scroll"><table class="tbl reports">
            <thead><tr>
              <th>{{ 'admin.diagrams.book' | translate }}</th><th class="num">{{ 'admin.diagrams.page' | translate }}</th>
              <th>FEN</th><th>{{ 'admin.diagrams.note' | translate }}</th><th>{{ 'admin.diagrams.who' | translate }}</th>
              <th>{{ 'admin.diagrams.when' | translate }}</th>
            </tr></thead>
            <tbody>
              @for (r of shownReports(); track r.id) {
                <tr [class.gone]="!r.diagramExists">
                  <td><a [routerLink]="['/ebooks', r.ebookId]" [queryParams]="{ page: r.page }">{{ r.ebookTitle }}</a></td>
                  <td class="num">{{ r.page }}</td>
                  <td class="fen">{{ r.fen }}@if (r.fontFamily) { <span class="muted"> · {{ r.fontFamily }}</span> }
                    @if (!r.diagramExists) { <div class="muted">{{ 'admin.diagrams.gone' | translate }}</div> }</td>
                  <td class="note">{{ r.note }}</td>
                  <td>{{ r.username }}</td>
                  <td class="when">{{ r.createdAt | date:'short' }}</td>
                </tr>
              }
            </tbody>
          </table></div>
        }

        <h3>{{ 'admin.diagrams.missedTitle' | translate }} ({{ shownMissed().length }})</h3>
        @if (!shownMissed().length) {
          <p class="empty-state">{{ 'admin.diagrams.empty' | translate }}</p>
        } @else {
          <div class="scroll"><table class="tbl missed">
            <thead><tr>
              <th>{{ 'admin.diagrams.book' | translate }}</th><th class="num">{{ 'admin.diagrams.page' | translate }}</th>
              <th>{{ 'admin.diagrams.place' | translate }}</th><th>{{ 'admin.diagrams.answer' | translate }}</th>
              <th>{{ 'admin.diagrams.note' | translate }}</th><th>{{ 'admin.diagrams.source' | translate }}</th>
              <th>{{ 'admin.diagrams.when' | translate }}</th>
            </tr></thead>
            <tbody>
              @for (m of shownMissed(); track m.id) {
                <tr [class.gone]="!m.isDiagram">
                  <td>@if (m.page > 0) {
                      <a [routerLink]="['/ebooks', m.ebookId]" [queryParams]="{ page: m.page }">{{ m.ebookTitle }}</a>
                    } @else {
                      <a [routerLink]="['/ebooks', m.ebookId]">{{ m.ebookTitle }}</a>
                    }</td>
                  <td class="num">{{ m.page > 0 ? m.page : '–' }}</td>
                  <td class="place">@if (m.page > 0) {
                      {{ ('admin.diagrams.pos.' + pos(m)) | translate }}
                      <span class="muted">{{ 'admin.diagrams.pct' | translate:{ x: pct(m.x), y: pct(m.y) } }}</span>
                    } @else {
                      <span class="muted">{{ m.epubRef ?? m.epubChapter }}</span>
                    }</td>
                  <td>{{ (m.isDiagram ? 'admin.diagrams.yes' : 'admin.diagrams.no') | translate }}</td>
                  <td class="note">{{ m.note }}</td>
                  <td>{{ m.source ?? ('admin.diagrams.reader' | translate) }}@if (m.username) { <span class="muted"> · {{ m.username }}</span> }</td>
                  <td class="when">{{ m.createdAt | date:'short' }}</td>
                </tr>
              }
            </tbody>
          </table></div>
        }
      }
    </div>
  `,
  styles: [`
    .diag { padding: 16px 0; }
    .intro { color: var(--mat-sys-on-surface-variant); }
    .book-filter { min-width: 260px; max-width: 100%; margin-bottom: 8px; }
    h3 { margin: 16px 0 8px; }
    .scroll { overflow-x: auto; }
    .tbl { width: 100%; border-collapse: collapse; }
    .tbl th, .tbl td { text-align: left; padding: 6px 8px; border-bottom: 1px solid rgba(128,128,128,.25); vertical-align: top; }
    .tbl .num { text-align: right; white-space: nowrap; }
    .note { max-width: 40ch; overflow-wrap: anywhere; }
    .fen { font-family: monospace; font-size: .85em; overflow-wrap: anywhere; max-width: 36ch; }
    .when, .place { white-space: nowrap; }
    .muted { opacity: .7; }
    /* Verwaiste Meldung bzw. Antwort „nein“: gedämpft, aber lesbar */
    tr.gone td { opacity: .6; }
  `],
})
export class AdminDiagramReportsComponent implements OnInit {
  private readonly http = inject(HttpClient);

  readonly reports = signal<EbookDiagramReportRow[] | null>(null);
  readonly missed = signal<EbookMissedDiagramRow[] | null>(null);
  readonly error = signal(false);
  /** Gewähltes Buch, `null` = alle. */
  readonly bookId = signal<number | null>(null);

  /** Alle Bücher, die in einer der Listen vorkommen, nach Titel. */
  readonly books = computed(() => {
    const map = new Map<number, string>();
    for (const r of this.reports() ?? []) map.set(r.ebookId, r.ebookTitle);
    for (const m of this.missed() ?? []) map.set(m.ebookId, m.ebookTitle);
    return [...map].map(([id, title]) => ({ id, title })).sort((a, b) => a.title.localeCompare(b.title));
  });
  readonly shownReports = computed(() => this.only(this.reports() ?? []));
  readonly shownMissed = computed(() => this.only(this.missed() ?? []));

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(false);
    try {
      const [reports, missed] = await Promise.all([
        firstValueFrom(this.http.get<EbookDiagramReportRow[]>('/api/admin/ebooks/diagram-reports')),
        firstValueFrom(this.http.get<EbookMissedDiagramRow[]>('/api/admin/ebooks/missed-diagrams')),
      ]);
      this.reports.set(reports);
      this.missed.set(missed);
    } catch {
      this.error.set(true);
    }
  }

  pos(m: EbookMissedDiagramRow): string {
    return positionKey(m.x, m.y);
  }

  pct(v: number): number {
    return Math.round(v * 100);
  }

  private only<T extends { ebookId: number }>(rows: T[]): T[] {
    const id = this.bookId();
    return id == null ? rows : rows.filter(r => r.ebookId === id);
  }
}
