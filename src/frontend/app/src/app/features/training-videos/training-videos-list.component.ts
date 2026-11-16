import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Observable, Subject, catchError, map, of, switchMap } from 'rxjs';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { ClampMoreDirective } from '../../shared/clamp-more/clamp-more.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { SanPipe } from '../../shared/chess/san.pipe';
import { whiteToMove } from '../../shared/chess/fen-side';
import { apiErrorText } from '../../core/api-error';
import { TrainingPositionDialogComponent, TrainingPositionDialogData } from './training-position-dialog.component';
import {
  TrainingGame, TrainingLessonListItem, TrainingSearchHit, TrainingVideosService, trainingGameLabel,
} from './training-videos.service';

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
/** Figurenfeld einer FEN (8 Reihen) am Anfang der Eingabe — dann ist es eine Stellungssuche. */
const FEN_BOARD = /^\s*([1-8pnbrqkPNBRQK]{1,8}\/){7}[1-8pnbrqkPNBRQK]{1,8}(\s|$)/;

/** Ist die Eingabe eine FEN (auch nur das Figurenfeld)? */
export function looksLikeFen(q: string): boolean {
  return FEN_BOARD.test(q);
}

/** Für die Anzeige des Bretts eine volle FEN (nur Figurenfeld → Weiß am Zug ergänzt). */
export function displayFen(fen: string): string {
  const t = fen.trim().split(/\s+/);
  return t.length >= 2 ? t.join(' ') : `${t[0]} w - - 0 1`;
}

/** Stellungssuche: Treffer mit gleichem Zugrecht zuerst, sonst die Reihenfolge des Servers. */
export function sortPositionHits(hits: readonly TrainingSearchHit[]): TrainingSearchHit[] {
  return [...hits].sort((a, b) => Number(b.sameSide === true) - Number(a.sameSide === true));
}

interface SearchState {
  kind: 'text' | 'position';
  /** Nur Stellungssuche: die gesuchte Stellung (zum Anzeigen als Brett). */
  fen: string | null;
  hits: TrainingSearchHit[];
  truncated: boolean;
  error: string | null;
}

/**
 * `/training-videos` „Trainerstunden“: Stunden als Karten (Datum, Dauer, Zusammenfassung, Partien), dazu die Suche —
 * Text (MariaDB-Volltext, ≥ 3 Buchstaben) oder Stellung (FEN einfügen, „Brett aufbauen“, `?fen=` vom Analysebrett).
 * Doku: `docs/frontend/trainerstunden.md`.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-training-videos-list',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressBarModule, TranslatePipe, ClampMoreDirective, LoadErrorComponent, LoadingSpinnerComponent, ChessBoardComponent, SanPipe],
  template: `
    <div class="page">
      <div class="header">
        <h1>{{ 'trainingVideos.title' | translate }}</h1>
        <div class="header-actions">
          <a mat-stroked-button routerLink="/training-videos/questions" class="tv-questions">
            <mat-icon>quiz</mat-icon>{{ 'trainingVideos.questionSet' | translate }}
          </a>
        </div>
      </div>
      <p class="intro">{{ 'trainingVideos.intro' | translate }}</p>

      <section class="search" [attr.aria-label]="'trainingVideos.search.title' | translate">
        <form class="search-row" (submit)="$event.preventDefault(); submit()">
          <mat-form-field appearance="outline" class="q-field" subscriptSizing="dynamic">
            <mat-label>{{ 'trainingVideos.search.label' | translate }}</mat-label>
            <input matInput name="q" [value]="query()" (input)="query.set($any($event.target).value)" autocomplete="off" />
            @if (hint(); as h) { <mat-hint>{{ h | translate }}</mat-hint> }
          </mat-form-field>
          <button mat-flat-button color="primary" type="submit" class="tv-search">
            <mat-icon>search</mat-icon>{{ 'common.search' | translate }}
          </button>
          <button mat-stroked-button type="button" class="tv-setup" (click)="openSetup()">
            <mat-icon>grid_on</mat-icon>{{ 'trainingVideos.search.setup' | translate }}
          </button>
        </form>

        @if (searching()) { <mat-progress-bar mode="indeterminate" /> }
        @if (search(); as s) {
          <div class="results">
            @if (s.kind === 'position' && s.fen) {
              <div class="searched">
                <div class="mini-board"><app-chess-board [fen]="boardFen(s.fen)" /></div>
                <div class="searched-meta">
                  <strong>{{ 'trainingVideos.search.position' | translate }}</strong>
                  <span>{{ (whiteMoves(s.fen) ? 'common.whiteToMove' : 'common.blackToMove') | translate }}</span>
                  <button mat-button type="button" class="tv-clear" (click)="clear()">
                    <mat-icon>close</mat-icon>{{ 'common.clear' | translate }}
                  </button>
                </div>
              </div>
            }
            @if (s.error) {
              <p class="error" role="alert">{{ s.error }}</p>
            } @else if (s.hits.length === 0) {
              <p class="muted no-hits">{{ 'trainingVideos.search.none' | translate }}</p>
            } @else {
              <ul class="hits">
                @for (h of s.hits; track $index) {
                  <li class="hit">
                    <div class="rh-dots hit-meta">
                      <span>{{ h.date | date: 'mediumDate' }}</span>
                      @if (h.startSec != null) { <span class="time">▶ {{ clock(h.startSec) }}</span> }
                      @if (h.gameLabel) { <span class="game">{{ h.gameLabel }}</span> }
                      @if (h.kind === 'note') { <span class="chip">{{ 'trainingVideos.search.note' | translate }}</span> }
                      @if (s.kind === 'position' && h.sameSide === false) {
                        <span class="chip">{{ 'trainingVideos.search.otherSide' | translate }}</span>
                      }
                    </div>
                    @if (h.move || h.text) {
                      <p class="snippet">@if (h.move) {<strong>{{ h.move | san }}</strong> }{{ h.text }}</p>
                    }
                    <div class="hit-actions">
                      <a mat-button class="tv-hit-watch" [routerLink]="['/training-videos', h.lessonId]" [queryParams]="hitParams(h)">
                        <mat-icon>play_circle</mat-icon>{{ 'trainingVideos.watch' | translate }}
                      </a>
                      @if (h.kind === 'segment' && h.segmentId != null) {
                        <a mat-button class="tv-hit-drill" [routerLink]="['/training-videos', h.lessonId, 'drill']" [queryParams]="{ seg: h.segmentId }">
                          <mat-icon>replay</mat-icon>{{ 'trainingVideos.search.drillFromHere' | translate }}
                        </a>
                      }
                    </div>
                  </li>
                }
              </ul>
              @if (s.truncated) { <p class="muted">{{ 'trainingVideos.search.truncated' | translate: { count: s.hits.length } }}</p> }
            }
          </div>
        }
      </section>

      @if (loading()) {
        <app-loading-spinner />
      } @else if (loadFailed()) {
        <app-load-error (retry)="load()" />
      } @else if (lessons().length === 0) {
        <p class="empty-state">{{ 'trainingVideos.empty' | translate }}</p>
      } @else {
        <div class="lessons">
          @for (l of lessons(); track l.id) {
            <mat-card appearance="outlined" class="lesson">
              <div class="lesson-head">
                <h2>{{ l.date | date: 'mediumDate' }}</h2>
                <div class="rh-dots meta">
                  @if (l.durationSec) { <span class="duration" [attr.aria-label]="'trainingVideos.duration' | translate: { time: clock(l.durationSec) }">{{ clock(l.durationSec) }}</span> }
                  <span>{{ (l.segmentCount === 1 ? 'trainingVideos.positionsOne' : 'trainingVideos.positions') | translate: { count: l.segmentCount } }}</span>
                  @if (!l.hasVideo) { <span>{{ 'trainingVideos.noVideoShort' | translate }}</span> }
                  @if (l.ownerName) { <span>{{ 'trainingVideos.owner' | translate: { name: l.ownerName } }}</span> }
                </div>
              </div>
              @if (l.summary) {
                <p class="summary" [id]="'tv-sum-' + l.id" [appClampMore]="l.summary" [clampLines]="3" [clampExpanded]="open().has(l.id)">
                  <span class="clamp-text"></span>
                  <button type="button" class="clamp-more" [attr.aria-controls]="'tv-sum-' + l.id" [attr.aria-expanded]="open().has(l.id)"
                          (click)="toggle(l.id)">{{ (open().has(l.id) ? 'common.showLess' : 'common.showMore') | translate }}</button>
                </p>
              }
              @if (l.games.length) {
                <ul class="games">
                  @for (g of l.games; track g.key) {
                    <li><span class="game-name">{{ label(g) }}</span> <span class="chip kind">{{ kindKey(g) | translate }}</span></li>
                  }
                </ul>
              }
              <div class="actions">
                <a mat-flat-button color="primary" class="tv-watch" [routerLink]="['/training-videos', l.id]">
                  <mat-icon>play_arrow</mat-icon>{{ 'trainingVideos.watch' | translate }}
                </a>
                <a mat-stroked-button class="tv-drill" [routerLink]="['/training-videos', l.id, 'drill']">
                  <mat-icon>replay</mat-icon>{{ 'trainingVideos.replay' | translate }}
                </a>
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .page { max-width: var(--page-max-width); margin: 0 auto; padding: 16px; box-sizing: border-box; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .header h1 { margin: 0; }
    .header-actions { display: flex; gap: .5rem; flex-wrap: wrap; }
    .intro { margin: 8px 0 16px; }
    .search { margin-bottom: 24px; }
    .search-row { display: flex; flex-wrap: wrap; gap: 8px; align-items: flex-start; }
    .q-field { flex: 1 1 260px; min-width: 0; }
    .search-row button { min-height: 44px; margin-top: 6px; }
    .results { margin-top: 12px; }
    .searched { display: flex; gap: 12px; align-items: center; margin-bottom: 8px; flex-wrap: wrap; }
    .mini-board { width: 160px; flex: none; }
    .searched-meta { display: flex; flex-direction: column; gap: 4px; align-items: flex-start; }
    .hits { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .hit { border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; padding: 8px 12px 0; min-width: 0; }
    .hit-meta { font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .hit-meta .time { font-variant-numeric: tabular-nums; }
    .snippet { margin: 4px 0 0; overflow-wrap: anywhere; }
    .hit-actions { display: flex; flex-wrap: wrap; gap: 4px; }
    .hit-actions a { min-height: 44px; }
    .error { color: var(--rh-error); }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .empty-state { text-align: center; padding: 2rem 1rem; }
    .lessons { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 340px), 1fr)); gap: 16px; }
    .lesson { display: flex; flex-direction: column; gap: 8px; padding: 16px; min-width: 0; }
    .lesson-head h2 { margin: 0; font-size: 1.1rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .meta { font-size: .85rem; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
    .summary { margin: 0; overflow-wrap: anywhere; }
    .clamp-more { background: none; border: 0; padding: 0 2px; color: var(--rh-accent); cursor: pointer; font: inherit; text-decoration: underline; }
    .games { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; font-size: .9rem; }
    .games li { min-width: 0; overflow-wrap: anywhere; }
    .chip { font-size: .72rem; padding: 1px 7px; border-radius: 999px; background: color-mix(in srgb, currentColor 10%, transparent); white-space: nowrap; }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: auto; padding-top: 4px; }
  `],
})
export class TrainingVideosListComponent implements OnInit {
  private readonly svc = inject(TrainingVideosService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  readonly lessons = signal<TrainingLessonListItem[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly open = signal<ReadonlySet<number>>(new Set());

  readonly query = signal('');
  readonly searching = signal(false);
  readonly search = signal<SearchState | null>(null);
  /** Hinweis unter dem Suchfeld (zu kurz / keine gültige Stellung) — erst nach einem Absenden. */
  readonly hint = signal<string | null>(null);

  private readonly searches = new Subject<{ kind: 'text' | 'position'; value: string }>();

  ngOnInit(): void {
    this.load();
    this.searches.pipe(
      switchMap(({ kind, value }) => {
        this.searching.set(true);
        const req: Observable<SearchState> = (kind === 'text' ? this.svc.searchText(value) : this.svc.searchPosition(value)).pipe(
          map(r => ({
            kind, fen: kind === 'position' ? value : null,
            hits: kind === 'position' ? sortPositionHits(r.hits ?? []) : (r.hits ?? []),
            truncated: !!r.truncated, error: null,
          })),
          // catchError INNEN: sonst endet der Strom beim ersten Fehler (F5-004)
          catchError(err => of({ kind, fen: kind === 'position' ? value : null, hits: [], truncated: false,
            error: apiErrorText(err, this.translate, 'trainingVideos.search.failed') })),
        );
        return req;
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(s => { this.searching.set(false); this.search.set(s); });

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(p => {
      const fen = p.get('fen');
      if (fen && looksLikeFen(fen)) {
        this.query.set(fen);
        this.hint.set(null);
        this.searches.next({ kind: 'position', value: fen.trim() });
      }
    });
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.svc.list().subscribe({
      next: list => { this.lessons.set(list ?? []); this.loading.set(false); },
      error: () => { this.loadFailed.set(true); this.loading.set(false); },
    });
  }

  submit(): void {
    const q = this.query().trim();
    if (looksLikeFen(q)) { this.runPosition(q); return; }
    const letters = q.replace(/[^\p{L}\p{N}]/gu, '');
    if (letters.length < 3) { this.hint.set('trainingVideos.search.tooShort'); return; }
    this.hint.set(null);
    this.searches.next({ kind: 'text', value: q });
  }

  /** Stellungssuche — über die Adresse (`?fen=`), damit Zurück und Teilen dieselbe Suche zeigen. */
  private runPosition(fen: string): void {
    this.hint.set(null);
    const current = this.route.snapshot.queryParamMap.get('fen');
    if (current === fen) { this.searches.next({ kind: 'position', value: fen }); return; }
    void this.router.navigate([], { relativeTo: this.route, queryParams: { fen }, replaceUrl: false });
  }

  openSetup(): void {
    const q = this.query().trim();
    const data: TrainingPositionDialogData = { fen: looksLikeFen(q) ? displayFen(q) : START_FEN };
    this.dialog.open<TrainingPositionDialogComponent, TrainingPositionDialogData, string>(TrainingPositionDialogComponent,
      { data, width: '560px', maxWidth: '96vw', autoFocus: false })
      .afterClosed().subscribe(fen => {
        if (!fen) return;
        this.query.set(fen);
        this.runPosition(fen);
      });
  }

  clear(): void {
    this.search.set(null);
    this.query.set('');
    if (this.route.snapshot.queryParamMap.has('fen')) {
      void this.router.navigate([], { relativeTo: this.route, queryParams: { fen: null }, replaceUrl: true });
    }
  }

  toggle(id: number): void {
    const next = new Set(this.open());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.open.set(next);
  }

  hitParams(h: TrainingSearchHit): Record<string, number> {
    const p: Record<string, number> = {};
    if (h.startSec != null) p['t'] = h.startSec;
    if (h.segmentId != null) p['seg'] = h.segmentId;
    return p;
  }

  clock(sec: number): string { return formatSecondsClock(sec); }
  boardFen(fen: string): string { return displayFen(fen); }
  whiteMoves(fen: string): boolean { return whiteToMove(displayFen(fen)); }
  label(g: TrainingGame): string { return trainingGameLabel(g, this.translate.instant('trainingVideos.unknownPlayer')); }
  kindKey(g: TrainingGame): string {
    return ['own', 'lesson', 'exercise'].includes(g.kind) ? 'trainingVideos.kind.' + g.kind : 'trainingVideos.kind.lesson';
  }
}
