import { Component, ChangeDetectionStrategy, ChangeDetectorRef, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subject, debounceTime, takeUntil } from 'rxjs';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';
import { LibraryGame, LibraryService } from './library.service';

/**
 * „Partie anfordern": der Rohbestand kommentierter Meisterpartien als Nachschlagewerk.
 *
 * <p>Bis hierher konnte nur wählen, wer Datenbankzugang hatte — bei 130 000 Partien war die
 * Auswahl damit der Flaschenhals. Wer die Partie sucht, die er spielen will, findet sie hier
 * selbst und stellt sie in die Warteschlange.</p>
 *
 * <p><b>Gesucht wird am SERVER</b>, anders als im kuratierten Bestand daneben: der ist ein paar
 * Dutzend Zeilen und wird ganz ausgeliefert, dieser hier hat 338 MB Partietext. Getippt wird
 * gedrosselt (`SearchDebounceMs`) — ein Server-Umlauf je Buchstabe wäre bei dieser Menge das
 * Gegenteil von schnell.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-library-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatTooltipModule, TranslatePipe,
    LoadingSpinnerComponent],
  template: `
    <h2 mat-dialog-title>{{ 'guess.library.title' | translate }}</h2>

    <mat-dialog-content class="lib">
      <p class="muted small">{{ 'guess.library.intro' | translate }}</p>

      <div class="filters">
        <mat-form-field appearance="outline" class="q" subscriptSizing="dynamic">
          <mat-label>{{ 'guess.library.search' | translate }}</mat-label>
          <mat-icon matPrefix>search</mat-icon>
          <input matInput [(ngModel)]="query" (ngModelChange)="typed.next()"
                 [placeholder]="'guess.library.searchHint' | translate">
        </mat-form-field>

        <mat-form-field appearance="outline" class="small-field" subscriptSizing="dynamic">
          <mat-label>{{ 'guess.library.language' | translate }}</mat-label>
          <mat-select [(ngModel)]="language" (selectionChange)="reload(1)">
            <mat-option [value]="''">{{ 'guess.library.anyLanguage' | translate }}</mat-option>
            @for (l of languages; track l) {
              <mat-option [value]="l">{{ 'guess.library.lang.' + l | translate }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline" class="small-field" subscriptSizing="dynamic">
          <mat-label>{{ 'guess.library.density' | translate }}</mat-label>
          <mat-select [(ngModel)]="minCommentedPlies" (selectionChange)="reload(1)">
            @for (d of densities; track d) {
              <mat-option [value]="d">
                {{ d === 0 ? ('guess.library.anyDensity' | translate)
                           : ('guess.library.fromPlies' | translate:{ plies: d }) }}
              </mat-option>
            }
          </mat-select>
        </mat-form-field>
      </div>

      @if (loading) {
        <app-loading-spinner />
      } @else if (items.length === 0) {
        <p class="muted">{{ (total === 0 && !hasFilter ? 'guess.library.empty' : 'guess.library.none') | translate }}</p>
      } @else {
        <p class="muted small count">{{ 'guess.library.found' | translate:{ total } }}</p>
        @for (g of items; track g.id) {
          <div class="row">
            <div class="who">
              <span class="names">{{ names(g) }}</span>
              <span class="muted small meta">{{ meta(g) }}</span>
            </div>
            <span class="spacer"></span>
            @if (g.inPool || g.requested) {
              <button mat-stroked-button (click)="play(g)">
                <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
              </button>
            } @else {
              <button mat-flat-button color="primary" [disabled]="busy === g.id" (click)="request(g)">
                <mat-icon>hourglass_top</mat-icon> {{ 'guess.library.request' | translate }}
              </button>
            }
          </div>
        }

        @if (pages > 1) {
          <div class="pager">
            <button mat-icon-button [disabled]="page <= 1" (click)="reload(page - 1)"
                    [attr.aria-label]="'common.previous' | translate">
              <mat-icon>chevron_left</mat-icon>
            </button>
            <span class="muted small">{{ 'guess.library.page' | translate:{ page, pages } }}</span>
            <button mat-icon-button [disabled]="page >= pages" (click)="reload(page + 1)"
                    [attr.aria-label]="'common.next' | translate">
              <mat-icon>chevron_right</mat-icon>
            </button>
          </div>
        }
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .lib { min-width: min(720px, 80vw); }
    .filters { display: flex; gap: 10px; flex-wrap: wrap; margin-bottom: 12px; }
    .q { flex: 1 1 260px; }
    .q mat-icon[matPrefix] { margin-right: 8px; opacity: .6; }
    .small-field { flex: 0 1 170px; }
    .count { margin: 0 0 6px; }
    .row { display: flex; align-items: center; gap: 10px; padding: 8px 0; flex-wrap: wrap; }
    .row + .row { border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .who { display: flex; flex-direction: column; min-width: 0; }
    .names { font-weight: 600; }
    .meta { white-space: normal; }
    .spacer { flex: 1 1 auto; }
    .pager { display: flex; align-items: center; justify-content: center; gap: 12px; margin-top: 10px; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class LibraryDialogComponent implements OnInit, OnDestroy {
  private service = inject(LibraryService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  private ref = inject(MatDialogRef<LibraryDialogComponent>);

  /** Getippt wird gedrosselt — ein Umlauf je Buchstabe wäre bei 130 000 Zeilen das Gegenteil
   *  von schnell. */
  private static readonly SearchDebounceMs = 350;
  /** Fuenfzig je Seite: die Trefferliste soll etwas hergeben, ohne dass eine Seite zur Wand wird. */
  private static readonly PageSize = 50;

  readonly typed = new Subject<void>();
  private readonly destroyed = new Subject<void>();

  /** Die vollständig gepflegten Sprachen des Bestands; alles Übrige steht unter „egal". */
  readonly languages = ['en', 'de', 'fr', 'es'];
  readonly densities = [0, 10, 20, 30];

  query = '';
  language = '';
  minCommentedPlies = 0;

  items: LibraryGame[] = [];
  total = 0;
  page = 1;
  loading = true;
  busy: number | null = null;

  get pages(): number { return Math.max(1, Math.ceil(this.total / LibraryDialogComponent.PageSize)); }
  get hasFilter(): boolean { return !!this.query.trim() || !!this.language || this.minCommentedPlies > 0; }

  ngOnInit(): void {
    this.typed.pipe(debounceTime(LibraryDialogComponent.SearchDebounceMs), takeUntil(this.destroyed))
      .subscribe(() => this.reload(1));
    this.reload(1);
  }

  ngOnDestroy(): void {
    this.destroyed.next();
    this.destroyed.complete();
  }

  reload(page: number): void {
    this.loading = true;
    this.page = page;
    this.service.search({
      q: this.query, language: this.language || undefined,
      minCommentedPlies: this.minCommentedPlies || undefined,
      page, pageSize: LibraryDialogComponent.PageSize,
    }).subscribe({
      next: p => {
        this.items = p.items;
        this.total = p.total;
        this.page = p.page;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.loading = false;
        this.snackbar.warn(this.translate.instant('guess.library.loadFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  /** „Anderssen – Kieseritzky"; fehlt ein Name, bleibt der andere. */
  names(g: LibraryGame): string {
    const both = [g.white, g.black].filter(Boolean).join(' – ');
    return both || g.event || this.translate.instant('guess.untitled');
  }

  /** Die Zeile unter den Namen: Turnier, Jahr, Länge, Kommentardichte, Kommentator. */
  meta(g: LibraryGame): string {
    const parts: string[] = [];
    if (g.event) parts.push(g.event);
    if (g.playedOn) parts.push(g.playedOn.slice(0, 4));
    if (g.plyCount) parts.push(this.translate.instant('guess.moves', { moves: Math.ceil(g.plyCount / 2) }));
    if (g.commentedPlies) {
      parts.push(this.translate.instant('guess.library.commented', { plies: g.commentedPlies }));
    }
    if (g.annotator) parts.push(g.annotator);
    return parts.join(' · ');
  }

  request(g: LibraryGame): void {
    if (this.busy) return;
    this.busy = g.id;
    this.service.request(g.id).subscribe({
      next: r => {
        this.busy = null;
        g.requested = true;
        g.gameAnalysisId = r.analysis.id;
        this.snackbar.success(this.translate.instant(
          r.alreadyPlayable ? 'guess.library.alreadyThere' : 'guess.library.queued'));
        this.cdr.markForCheck();
      },
      error: err => {
        this.busy = null;
        const reason = err?.error?.reason;
        this.snackbar.warn(reason
          ? this.translate.instant('guess.upload.reason.' + reason)
          : this.translate.instant('guess.library.requestFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  /** Der Aufrufer bekommt die Analyse-Id und startet die Punktepartie — der Dialog selbst kennt
   *  weder Route noch Sitzung. */
  play(g: LibraryGame): void {
    if (g.gameAnalysisId) this.ref.close(g.gameAnalysisId);
  }
}
