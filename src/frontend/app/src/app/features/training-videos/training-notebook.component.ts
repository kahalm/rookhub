import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subject, catchError, map, of, switchMap } from 'rxjs';
import { apiErrorText } from '../../core/api-error';
import { RepertoireService } from '../../core/repertoire.service';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { SanPipe } from '../../shared/chess/san.pipe';
import { whiteToMove } from '../../shared/chess/fen-side';
import { LineKeyResolver } from '../repertoire/repertoire-line-link';
import { TrainingClipPlayerComponent } from './training-clip-player.component';
import {
  NotebookKindFilter, categoryTabs, clipRange, filterNotes, groupByLesson, noteGameLink, notebookGameKey, normalizeCategory,
} from './training-notebook.util';
import {
  TrainingNotebook, TrainingNotebookNote, TrainingNotebookOpening, TrainingNotebookRepertoireHit, TrainingVideosService,
  trainingGameLabel,
} from './training-videos.service';

/** So viele Eröffnungsstellungen stehen zuerst da (jede mit eigenem Brett); der Rest auf Zuruf. */
const OPENINGS_FIRST = 6;

interface SearchState { q: string; ids: ReadonlySet<number> | null; truncated: boolean; error: string | null; }

/**
 * `/training-videos/notebook` „Trainer-Notizbuch“: alle Notizen der Trainerstunden stundenübergreifend — oben „Sagt er
 * immer wieder“ (serverseitig gruppiert), dann die Eröffnungsempfehlungen mit Stellung und Repertoire-Abgleich, dann
 * alle Notizen als Reiter je Kategorie (Anzahl), darin je Stunde. Filter Art/Partie, Volltext über die vorhandene
 * Suche (`search/text`, Notiz-Ids). Jede Notiz: „▶ anhören“ (Ausschnitt-Player) + Rohzitat. Doku:
 * `docs/frontend/trainerstunden.md`.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-training-notebook',
  standalone: true,
  imports: [DatePipe, NgTemplateOutlet, RouterLink, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatProgressBarModule, MatSelectModule, TranslatePipe, IconLabelDirective, LoadErrorComponent,
    LoadingSpinnerComponent, ChessBoardComponent, SanPipe, TrainingClipPlayerComponent],
  template: `
    <div class="page">
      <div class="header">
        <div class="title">
          <a mat-icon-button routerLink="/training-videos" [appIconLabel]="'trainingVideos.toList' | translate"><mat-icon>arrow_back</mat-icon></a>
          <h1>{{ 'trainingVideos.notebook.title' | translate }}</h1>
        </div>
      </div>
      <p class="intro">{{ 'trainingVideos.notebook.intro' | translate }}</p>

      @if (loading()) {
        <app-loading-spinner />
      } @else if (loadFailed()) {
        <app-load-error (retry)="load()" />
      } @else if (!data()?.notes?.length) {
        <p class="empty-state">{{ 'trainingVideos.notebook.empty' | translate }}</p>
      } @else {
        @if (themes().length) {
          <section class="block themes" aria-labelledby="nb-themes">
            <h2 id="nb-themes">{{ 'trainingVideos.notebook.themes' | translate }}</h2>
            <ul class="theme-list">
              @for (t of themes(); track t.noteId) {
                <li class="theme">
                  <div class="rh-dots theme-meta">
                    <span class="count">{{ 'trainingVideos.notebook.themeLessons' | translate: { count: t.lessons } }}</span>
                    <span class="chip">{{ catKey(t.category) | translate }}</span>
                  </div>
                  <p class="theme-text">{{ t.note.text }}</p>
                  <button mat-button type="button" class="nb-theme-toggle" [attr.aria-expanded]="openTheme() === t.noteId"
                          (click)="toggleTheme(t.noteId)">
                    <mat-icon>{{ openTheme() === t.noteId ? 'expand_less' : 'expand_more' }}</mat-icon>
                    {{ (openTheme() === t.noteId ? 'common.showLess' : 'trainingVideos.notebook.themeShow') | translate: { count: t.notes.length } }}
                  </button>
                  @if (openTheme() === t.noteId) {
                    <ul class="notes nested">
                      @for (n of t.notes; track n.id) {
                        <li class="note"><ng-container [ngTemplateOutlet]="noteTpl" [ngTemplateOutletContext]="{ $implicit: n, withDate: true }" /></li>
                      }
                    </ul>
                  }
                </li>
              }
            </ul>
          </section>
        }

        @if (openings().length) {
          <section class="block openings" aria-labelledby="nb-openings">
            <h2 id="nb-openings">{{ 'trainingVideos.notebook.openings' | translate }}</h2>
            @if (data()!.repertoireTruncated) { <p class="muted">{{ 'trainingVideos.notebook.repertoireTruncated' | translate }}</p> }
            <ul class="opening-list">
              @for (o of visibleOpenings(); track o.opening.noteId) {
                <li class="opening">
                  @if (o.opening.fen) {
                    <div class="mini-board"><app-chess-board [fen]="o.opening.fen" [flipped]="!whiteMoves(o.opening.fen)" /></div>
                  }
                  <div class="opening-body">
                    <ng-container [ngTemplateOutlet]="noteTpl" [ngTemplateOutletContext]="{ $implicit: o.note, withDate: true }" />
                    @if (o.opening.fen) {
                      <div class="rep">
                        @if (data()!.hasRepertoire) {
                          @for (h of o.opening.repertoire; track h.repertoireId + h.chapter) {
                            <p class="rep-line">
                              <mat-icon class="rep-icon" aria-hidden="true">menu_book</mat-icon>
                              @if (h.moves.length) {
                                {{ 'trainingVideos.notebook.inRepertoire' | translate: { name: h.repertoireName } }}
                                <strong class="rep-moves">@for (m of h.moves; track m; let last = $last) { <span>{{ m | san }}</span>@if (!last) {<span>, </span>} }</strong>
                              } @else {
                                {{ 'trainingVideos.notebook.repertoireEnd' | translate: { name: h.repertoireName } }}
                              }
                              @if (!h.sameSide) { <span class="chip">{{ 'trainingVideos.search.otherSide' | translate }}</span> }
                            </p>
                          } @empty {
                            <p class="rep-line muted">{{ 'trainingVideos.notebook.notInRepertoire' | translate }}</p>
                          }
                        }
                        <div class="actions">
                          @if (o.opening.repertoire[0]; as first) {
                            <button mat-button type="button" class="nb-open-rep" [disabled]="busy() === o.opening.noteId"
                                    (click)="openRepertoire(o.opening.noteId, first)">
                              <mat-icon>menu_book</mat-icon>{{ 'trainingVideos.notebook.openRepertoire' | translate }}
                            </button>
                          }
                          <a mat-button class="nb-open-board" routerLink="/analysis" [queryParams]="{ fen: o.opening.fen }">
                            <mat-icon>grid_on</mat-icon>{{ 'trainingVideos.lesson.openBoard' | translate }}
                          </a>
                        </div>
                      </div>
                    }
                  </div>
                </li>
              }
            </ul>
            @if (openings().length > visibleOpenings().length) {
              <button mat-stroked-button type="button" class="nb-more-openings" (click)="allOpenings.set(true)">
                {{ 'trainingVideos.notebook.moreOpenings' | translate: { count: openings().length } }}
              </button>
            }
          </section>
        }

        <section class="block all" aria-labelledby="nb-all">
          <h2 id="nb-all">{{ 'trainingVideos.notebook.allNotes' | translate }}</h2>
          <form class="filters" (submit)="$event.preventDefault(); submitSearch()">
            <mat-form-field appearance="outline" class="q-field" subscriptSizing="dynamic">
              <mat-label>{{ 'trainingVideos.notebook.searchLabel' | translate }}</mat-label>
              <input matInput name="q" [value]="query()" (input)="query.set($any($event.target).value)" autocomplete="off" />
              @if (hint(); as h) { <mat-hint>{{ h | translate }}</mat-hint> }
            </mat-form-field>
            <button mat-stroked-button type="submit" class="nb-search"><mat-icon>search</mat-icon>{{ 'common.search' | translate }}</button>
            <mat-form-field appearance="outline" class="sel" subscriptSizing="dynamic">
              <mat-label>{{ 'trainingVideos.notebook.kindLabel' | translate }}</mat-label>
              <mat-select [value]="kind()" (selectionChange)="kind.set($event.value)">
                <mat-option value="all">{{ 'trainingVideos.notebook.kindAll' | translate }}</mat-option>
                <mat-option value="own">{{ 'trainingVideos.kind.own' | translate }}</mat-option>
                <mat-option value="lesson">{{ 'trainingVideos.kind.lesson' | translate }}</mat-option>
                <mat-option value="exercise">{{ 'trainingVideos.kind.exercise' | translate }}</mat-option>
              </mat-select>
            </mat-form-field>
            <mat-form-field appearance="outline" class="sel game-sel" subscriptSizing="dynamic">
              <mat-label>{{ 'trainingVideos.notebook.gameLabel' | translate }}</mat-label>
              <mat-select [value]="game()" (selectionChange)="game.set($event.value)">
                <mat-option [value]="null">{{ 'trainingVideos.notebook.gameAll' | translate }}</mat-option>
                @for (g of games(); track g.key) { <mat-option [value]="g.key">{{ g.label }}</mat-option> }
              </mat-select>
            </mat-form-field>
          </form>
          @if (searching()) { <mat-progress-bar mode="indeterminate" /> }
          @if (search(); as s) {
            <div class="search-state">
              @if (s.error) {
                <p class="error" role="alert">{{ s.error }}</p>
              } @else {
                <span>{{ 'trainingVideos.notebook.searchActive' | translate: { q: s.q, count: filtered().length } }}</span>
                @if (s.truncated) { <span class="muted">{{ 'trainingVideos.search.truncated' | translate: { count: s.ids?.size ?? 0 } }}</span> }
              }
              <button mat-button type="button" class="nb-clear" (click)="clearSearch()"><mat-icon>close</mat-icon>{{ 'common.clear' | translate }}</button>
            </div>
          }

          @if (tabs().length === 0) {
            <p class="empty-state">{{ 'trainingVideos.notebook.none' | translate }}</p>
          } @else {
            <mat-chip-listbox class="tabs" [attr.aria-label]="'trainingVideos.notebook.allNotes' | translate" hideSingleSelectionIndicator
                              [value]="activeCategory()" (change)="$event.value && category.set($event.value)">
              @for (tab of tabs(); track tab.category) {
                <mat-chip-option [value]="tab.category" [selected]="tab.category === activeCategory()">
                  {{ catKey(tab.category) | translate }} ({{ tab.count }})
                </mat-chip-option>
              }
            </mat-chip-listbox>
            @for (g of groups(); track g.lessonId) {
              <div class="lesson-group">
                <h3><a [routerLink]="['/training-videos', g.lessonId]">{{ g.date | date: 'mediumDate' }}</a></h3>
                <ul class="notes">
                  @for (n of g.notes; track n.id) {
                    <li class="note"><ng-container [ngTemplateOutlet]="noteTpl" [ngTemplateOutletContext]="{ $implicit: n, withDate: false }" /></li>
                  }
                </ul>
              </div>
            }
          }
        </section>
      }
    </div>

    <ng-template #noteTpl let-n let-withDate="withDate">
      <p class="note-text">{{ n.text }}</p>
      <div class="rh-dots note-meta">
        @if (withDate) { <span>{{ n.date | date: 'mediumDate' }}</span> }
        @if (n.gameKey) {
          <a class="nb-game" [routerLink]="gameLink(n).link" [queryParams]="gameLink(n).query">{{ gameName(n) }}</a>
          @if (n.gameKind) { <span class="chip">{{ kindKey(n.gameKind) | translate }}</span> }
        }
      </div>
      @if ((n.hasVideo && n.startSec != null) || n.quote) {
        <div class="note-actions">
          @if (n.hasVideo && n.startSec != null) {
            <button mat-button type="button" class="nb-listen" [attr.aria-expanded]="openNote() === n.id" (click)="toggleNote(n.id)">
              <mat-icon>{{ openNote() === n.id ? 'expand_less' : 'play_arrow' }}</mat-icon>
              {{ (openNote() === n.id ? 'common.close' : 'trainingVideos.notebook.listen') | translate }}
            </button>
          }
          @if (n.quote) {
            <button mat-button type="button" class="nb-quote" [attr.aria-expanded]="openQuote() === n.id" (click)="toggleQuote(n.id)">
              <mat-icon>{{ openQuote() === n.id ? 'expand_less' : 'format_quote' }}</mat-icon>{{ 'trainingVideos.notebook.quote' | translate }}
            </button>
          }
        </div>
        @if (openNote() === n.id && clip(n); as c) {
          <app-training-clip-player [lessonId]="n.lessonId" [start]="c.start" [end]="c.end" [autoplay]="true" />
        }
        @if (openQuote() === n.id) { <blockquote class="quote"><q>{{ n.quote }}</q></blockquote> }
      }
    </ng-template>
  `,
  styles: [`
    .page { max-width: var(--page-max-width); margin: 0 auto; padding: 16px; box-sizing: border-box; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .title { display: flex; align-items: center; gap: 4px; min-width: 0; }
    .header h1 { margin: 0; }
    .intro { margin: 8px 0 16px; }
    .block { margin-bottom: 28px; }
    .block h2 { font-size: 1.15rem; margin: 0 0 8px; }
    .muted { color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--rh-error); margin: 0; }
    .empty-state { text-align: center; padding: 2rem 1rem; }
    .chip { font-size: .72rem; padding: 1px 7px; border-radius: 999px; background: color-mix(in srgb, currentColor 10%, transparent); white-space: nowrap; }
    ul { list-style: none; margin: 0; padding: 0; }
    .theme-list, .opening-list { display: flex; flex-direction: column; gap: 8px; }
    .theme, .opening { border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; padding: 8px 12px 0; min-width: 0; }
    .theme-meta, .note-meta { font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .theme-meta .count { font-weight: 600; color: var(--mat-sys-on-surface); }
    .theme-text { margin: 4px 0 0; font-weight: 500; overflow-wrap: anywhere; }
    .theme > button, .note-actions button, .actions a, .actions button { min-height: 44px; }
    .notes { display: flex; flex-direction: column; }
    .notes.nested { border-top: 1px solid var(--mat-sys-outline-variant); margin-bottom: 8px; }
    .note { padding: 8px 0; border-bottom: 1px solid var(--mat-sys-outline-variant); min-width: 0; }
    .note:last-child { border-bottom: 0; }
    .note-text { margin: 0 0 2px; overflow-wrap: anywhere; }
    .note-meta a { color: var(--rh-accent); }
    .note-actions, .actions { display: flex; flex-wrap: wrap; gap: 4px; }
    .quote { margin: 4px 0 8px; padding: 6px 10px; border-left: 3px solid var(--mat-sys-outline); color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    app-training-clip-player { display: block; margin: 4px 0 8px; }
    .opening { display: flex; gap: 12px; flex-wrap: wrap; padding-bottom: 0; }
    .mini-board { width: 160px; flex: none; padding-bottom: 8px; }
    .opening-body { flex: 1 1 260px; min-width: 0; }
    .rep-line { margin: 6px 0 0; display: flex; flex-wrap: wrap; align-items: center; gap: 4px 6px; }
    .rep-icon { font-size: 18px; width: 18px; height: 18px; }
    .nb-more-openings { margin-top: 8px; min-height: 44px; }
    .filters { display: flex; flex-wrap: wrap; gap: 8px; align-items: flex-start; margin-bottom: 8px; }
    .q-field { flex: 1 1 240px; min-width: 0; }
    .filters .nb-search { min-height: 44px; margin-top: 6px; }
    .sel { flex: 0 1 180px; min-width: 140px; }
    .game-sel { flex-basis: 240px; }
    .search-state { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 12px; margin-bottom: 8px; }
    .tabs { display: block; margin: 4px 0 12px; }
    .lesson-group h3 { font-size: 1rem; margin: 12px 0 0; }
    .lesson-group h3 a { color: inherit; }
  `],
})
export class TrainingNotebookComponent implements OnInit {
  private readonly svc = inject(TrainingVideosService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly repertoires = inject(RepertoireService);
  private readonly lineKeys = new LineKeyResolver(() => this.repertoires);

  readonly data = signal<TrainingNotebook | null>(null);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);

  readonly kind = signal<NotebookKindFilter>('all');
  readonly game = signal<string | null>(null);
  readonly category = signal<string | null>(null);
  readonly query = signal('');
  readonly hint = signal<string | null>(null);
  readonly searching = signal(false);
  readonly search = signal<SearchState | null>(null);

  readonly openNote = signal<number | null>(null);
  readonly openQuote = signal<number | null>(null);
  readonly openTheme = signal<number | null>(null);
  readonly allOpenings = signal(false);
  readonly busy = signal<number | null>(null);

  private readonly searches = new Subject<string>();

  private readonly byId = computed(() => new Map((this.data()?.notes ?? []).map(n => [n.id, n] as const)));

  readonly themes = computed(() => {
    const byId = this.byId();
    return (this.data()?.themes ?? [])
      .map(t => ({ ...t, note: byId.get(t.noteId), notes: t.noteIds.map(id => byId.get(id)).filter((n): n is TrainingNotebookNote => !!n) }))
      .filter((t): t is typeof t & { note: TrainingNotebookNote } => !!t.note);
  });

  readonly openings = computed(() => {
    const byId = this.byId();
    return (this.data()?.openings ?? [])
      .map(o => ({ opening: o, note: byId.get(o.noteId) }))
      .filter((o): o is { opening: TrainingNotebookOpening; note: TrainingNotebookNote } => !!o.note);
  });
  readonly visibleOpenings = computed(() => this.allOpenings() ? this.openings() : this.openings().slice(0, OPENINGS_FIRST));

  /** Partien für den Filter: je Partie einmal, nach Namen. */
  readonly games = computed(() => {
    const seen = new Map<string, string>();
    for (const n of this.data()?.notes ?? []) {
      const key = notebookGameKey(n);
      if (key && !seen.has(key)) seen.set(key, this.gameName(n));
    }
    return [...seen].map(([key, label]) => ({ key, label })).sort((a, b) => a.label.localeCompare(b.label));
  });

  readonly filtered = computed(() => filterNotes(this.data()?.notes ?? [],
    { kind: this.kind(), game: this.game(), ids: this.search()?.error ? null : this.search()?.ids ?? null }));
  readonly tabs = computed(() => categoryTabs(this.filtered()));
  /** Gewählter Reiter, sonst der erste mit Notizen (ein Filter kann den gewählten leeren). */
  readonly activeCategory = computed(() => {
    const tabs = this.tabs();
    const chosen = this.category();
    return tabs.some(t => t.category === chosen) ? chosen : tabs[0]?.category ?? null;
  });
  readonly groups = computed(() => {
    const c = this.activeCategory();
    return c ? groupByLesson(this.filtered(), c) : [];
  });

  ngOnInit(): void {
    this.load();
    this.searches.pipe(
      switchMap(q => {
        this.searching.set(true);
        return this.svc.searchText(q).pipe(
          map(r => ({
            q, truncated: !!r.truncated, error: null,
            ids: new Set((r.hits ?? []).filter(h => h.kind === 'note' && h.noteId != null).map(h => h.noteId!)),
          }) as SearchState),
          // catchError INNEN: sonst endet der Strom beim ersten Fehler (F5-004)
          catchError(err => of({ q, ids: null, truncated: false, error: apiErrorText(err, this.translate, 'trainingVideos.search.failed') } as SearchState)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(s => { this.searching.set(false); this.search.set(s); });
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.svc.notebook().subscribe({
      next: d => { this.data.set(d); this.loading.set(false); },
      error: () => { this.loadFailed.set(true); this.loading.set(false); },
    });
  }

  submitSearch(): void {
    const q = this.query().trim();
    if (!q) { this.clearSearch(); return; }
    if (q.replace(/[^\p{L}\p{N}]/gu, '').length < 3) { this.hint.set('trainingVideos.search.tooShort'); return; }
    this.hint.set(null);
    this.searches.next(q);
  }

  clearSearch(): void {
    this.query.set('');
    this.hint.set(null);
    this.search.set(null);
  }

  toggleNote(id: number): void { this.openNote.set(this.openNote() === id ? null : id); }
  toggleQuote(id: number): void { this.openQuote.set(this.openQuote() === id ? null : id); }
  toggleTheme(id: number): void { this.openTheme.set(this.openTheme() === id ? null : id); }

  /** „Im Repertoire öffnen“: Linienschlüssel aus dem Repertoire-PGN wie in „In welchen Repertoires?“. */
  openRepertoire(noteId: number, hit: TrainingNotebookRepertoireHit): void {
    this.busy.set(noteId);
    this.lineKeys.resolve(hit.repertoireId, { chapter: hit.chapter, lineName: hit.lineName, gameIndex: hit.gameIndex, ply: hit.ply })
      .subscribe({
        next: line => {
          this.busy.set(null);
          void this.router.navigate(['/repertoires', hit.repertoireId], { queryParams: { line, ply: hit.ply >= 0 ? hit.ply : null } });
        },
        error: () => {
          this.busy.set(null);
          void this.router.navigate(['/repertoires', hit.repertoireId]);
        },
      });
  }

  clip(n: TrainingNotebookNote) { return clipRange(n); }
  gameLink(n: TrainingNotebookNote) { return noteGameLink(n); }
  gameName(n: TrainingNotebookNote): string {
    return trainingGameLabel({ white: n.gameWhite, black: n.gameBlack }, this.translate.instant('trainingVideos.unknownPlayer'));
  }
  catKey(c: string): string { return 'trainingVideos.notebook.cat.' + normalizeCategory(c); }
  kindKey(k: string): string {
    return ['own', 'lesson', 'exercise'].includes(k) ? 'trainingVideos.kind.' + k : 'trainingVideos.kind.lesson';
  }
  whiteMoves(fen: string): boolean { return whiteToMove(fen); }
}
