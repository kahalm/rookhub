import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subject, catchError, map, of, switchMap, timer } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { LibraryGame, LibraryService, MasterGamePage, MasterGameQuery } from '../guess/library.service';
import {
  OPENING_GROUPS, cleanName, gameYear, openingGroupOf, paramsToQuery, queryToParams, resultLabel,
} from './master-games.util';

/** Getippt wird gedrosselt — ein Server-Umlauf je Buchstabe wäre bei 130 000 Partien das Gegenteil von schnell. */
export const MASTER_SEARCH_DEBOUNCE_MS = 350;
export const MASTER_PAGE_SIZE = 25;
/** Feldlänge wie der Server (`LibraryGameSearchInput.MaxFieldLength`). */
export const MASTER_FIELD_MAX = 100;
const CUSTOM_ECO = '__custom';

type LoadState = { kind: 'loading' } | { kind: 'error'; error: unknown } | { kind: 'done'; page: MasterGamePage };

/**
 * „Meisterpartien“ (`/master-games`, 1.038.0): Suche über den ganzen Rohbestand kommentierter Meisterpartien aus Büchern
 * und Sammlungen — Spieler (egal welche Farbe), Turnier, Jahr(esbereich), Ergebnis, Eröffnung/ECO und Freitext.
 * Gesucht wird am Server (`GET /api/library-games/search`), geblättert zu je {@link MASTER_PAGE_SIZE}; die Filter stehen
 * in der Adresse. Ein Klick auf eine Zeile öffnet die Partie mit ihren Anmerkungen im Nachspiel-Dialog (`/view`, wie bei
 * den ähnlichen Meisterpartien) — die Züge gibt es nur angemeldet, ohne Konto führt der Klick zur Anmeldung.
 */
@Component({
  selector: 'app-master-games',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule,
    MatProgressSpinnerModule, MatSelectModule, TranslatePipe, HelpHintComponent, LoadErrorComponent, LoadingSpinnerComponent],
  template: `
    <div class="mg-page">
      <div class="header">
        <h1>{{ 'masterGames.title' | translate }}</h1>
        <div class="header-actions">
          @if (hasFilter()) {
            <button mat-button type="button" class="reset" (click)="reset()">
              <mat-icon>filter_alt_off</mat-icon> {{ 'masterGames.reset' | translate }}
            </button>
          }
        </div>
      </div>
      <p class="intro">{{ 'masterGames.intro' | translate }} <app-help-hint [text]="'masterGames.help' | translate" /></p>

      <div class="filters" role="search">
        <mat-form-field appearance="outline" class="wide" subscriptSizing="dynamic">
          <mat-label>{{ 'masterGames.q' | translate }}</mat-label>
          <mat-icon matPrefix>search</mat-icon>
          <input matInput name="q" [maxlength]="fieldMax" [ngModel]="query().q" (ngModelChange)="set({ q: $event }, true)"
                 [placeholder]="'masterGames.qHint' | translate">
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'masterGames.player' | translate }}</mat-label>
          <input matInput name="player" [maxlength]="fieldMax" [ngModel]="query().player" (ngModelChange)="set({ player: $event }, true)"
                 [placeholder]="'masterGames.playerHint' | translate">
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'masterGames.event' | translate }}</mat-label>
          <input matInput name="event" [maxlength]="fieldMax" [ngModel]="query().event" (ngModelChange)="set({ event: $event }, true)">
        </mat-form-field>
        <div class="years">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ 'masterGames.yearFrom' | translate }}</mat-label>
            <input matInput name="yearFrom" type="number" inputmode="numeric" min="1000" max="2100"
                   [ngModel]="query().yearFrom" (ngModelChange)="set({ yearFrom: year($event) }, true)">
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ 'masterGames.yearTo' | translate }}</mat-label>
            <input matInput name="yearTo" type="number" inputmode="numeric" min="1000" max="2100"
                   [ngModel]="query().yearTo" (ngModelChange)="set({ yearTo: year($event) }, true)">
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'masterGames.result' | translate }}</mat-label>
          <mat-select name="result" [ngModel]="query().result ?? ''" (ngModelChange)="set({ result: $event })">
            <mat-option value="">{{ 'masterGames.resultAny' | translate }}</mat-option>
            <mat-option value="white">{{ 'masterGames.resultWhite' | translate }}</mat-option>
            <mat-option value="black">{{ 'masterGames.resultBlack' | translate }}</mat-option>
            <mat-option value="draw">{{ 'masterGames.resultDraw' | translate }}</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'masterGames.opening' | translate }}</mat-label>
          <mat-select name="opening" [ngModel]="openingKey()" (ngModelChange)="pickOpening($event)">
            <mat-option value="">{{ 'masterGames.openingAny' | translate }}</mat-option>
            @if (openingKey() === custom) {
              <mat-option [value]="custom">{{ 'masterGames.openingCustom' | translate: { eco: query().eco } }}</mat-option>
            }
            @for (o of openings; track o.key) {
              <mat-option [value]="o.key">{{ 'masterGames.openings.' + o.key | translate }} ({{ o.eco }})</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'masterGames.eco' | translate }}</mat-label>
          <input matInput name="eco" maxlength="15" [ngModel]="query().eco" (ngModelChange)="set({ eco: $event }, true)"
                 [placeholder]="'masterGames.ecoHint' | translate">
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'masterGames.sort' | translate }}</mat-label>
          <mat-select name="sort" [ngModel]="query().sort ?? 'best'" (ngModelChange)="set({ sort: $event })">
            <mat-option value="best">{{ 'masterGames.sortBest' | translate }}</mat-option>
            <mat-option value="newest">{{ 'masterGames.sortNewest' | translate }}</mat-option>
            <mat-option value="oldest">{{ 'masterGames.sortOldest' | translate }}</mat-option>
          </mat-select>
        </mat-form-field>
      </div>

      @if (!loggedIn) {
        <p class="login-note">
          <mat-icon inline>lock</mat-icon> {{ 'masterGames.loginToView' | translate }}
          <button mat-button type="button" class="login" (click)="login()">{{ 'nav.login' | translate }}</button>
        </p>
      }

      @switch (state().kind) {
        @case ('loading') { <app-loading-spinner /> }
        @case ('error') { <app-load-error [error]="errorOf()" (retry)="reload()" /> }
        @default {
          @if (page(); as p) {
            @if (p.ignored.length) {
              <p class="ignored">{{ 'masterGames.ignored' | translate: { words: p.ignored.join(', ') } }}</p>
            }
            @if (p.items.length === 0) {
              <p class="empty-state">{{ 'masterGames.empty' | translate }}</p>
            } @else {
              <p class="count">{{ 'masterGames.found' | translate: { count: p.total } }}</p>
              <ul class="games">
                @for (g of p.items; track g.id) {
                  <li class="game rh-tile">
                    <button type="button" class="game-main rh-tile-target" (click)="open(g)" [disabled]="busy() === g.id"
                            [attr.aria-label]="'masterGames.openLabel' | translate: { game: names(g) }">
                      <span class="names">
                        <span class="who">{{ names(g) }}</span>
                        @if (result(g); as r) { <span class="result">{{ r }}</span> }
                      </span>
                      <span class="meta">{{ meta(g) }}</span>
                    </button>
                    @if (busy() === g.id) { <mat-spinner diameter="18" class="busy" /> }
                  </li>
                }
              </ul>
              @if (capped()) {
                <p class="capped">{{ 'masterGames.capped' | translate: { count: shownTotal() } }}</p>
              }
              @if (p.total > p.pageSize) {
                <mat-paginator [length]="shownTotal()" [pageSize]="p.pageSize" [pageIndex]="p.page - 1" [hidePageSize]="true"
                               (page)="onPage($event)" />
              }
            }
          }
        }
      }
    </div>
  `,
  styles: [`
    .mg-page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 16px; box-sizing: border-box; }
    .header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
    .header h1 { margin: 0; }
    .header-actions { display: flex; gap: .5rem; flex-wrap: wrap; }
    .intro { margin: 4px 0 16px; color: var(--mat-sys-on-surface-variant); }
    .filters { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin-bottom: 12px; }
    .filters .wide { grid-column: 1 / -1; }
    .years { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; min-width: 0; }
    .years mat-form-field { min-width: 0; }
    .login-note { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; margin: 0 0 8px;
      color: var(--mat-sys-on-surface-variant); font-size: .9rem; }
    .count, .ignored, .capped { margin: 8px 0; font-size: .875rem; color: var(--mat-sys-on-surface-variant); }
    .empty-state { text-align: center; padding: 2rem 1rem; color: var(--mat-sys-on-surface-variant); }
    .games { list-style: none; margin: 0; padding: 0; }
    .game { display: flex; align-items: center; gap: 8px; padding: 10px 12px; border-radius: 8px;
      border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .game-main { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; gap: 2px; min-height: 44px;
      justify-content: center; background: none; border: 0; padding: 0; margin: 0; font: inherit; color: inherit;
      text-align: left; cursor: pointer; }
    .game-main:disabled { cursor: progress; }
    .names { display: flex; align-items: baseline; gap: 10px; min-width: 0; }
    .who { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
    .result { flex: none; font-variant-numeric: tabular-nums; font-size: .85rem; padding: 1px 7px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 10%, transparent); }
    .meta { font-size: .85rem; color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
    .busy { flex: none; }
  `],
})
export class MasterGamesComponent implements OnInit {
  private library = inject(LibraryService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private translate = inject(TranslateService);
  private snackbar = inject(SnackbarService);
  private auth = inject(AuthService);
  private destroyRef = inject(DestroyRef);

  readonly openings = OPENING_GROUPS;
  readonly custom = CUSTOM_ECO;
  readonly fieldMax = MASTER_FIELD_MAX;

  readonly query = signal<MasterGameQuery>(paramsToQuery({}));
  readonly state = signal<LoadState>({ kind: 'loading' });
  readonly busy = signal<number | null>(null);

  readonly page = computed(() => { const s = this.state(); return s.kind === 'done' ? s.page : null; });
  readonly errorOf = computed(() => { const s = this.state(); return s.kind === 'error' ? s.error : null; });
  readonly hasFilter = computed(() => Object.keys(queryToParams({ ...this.query(), page: 1 })).length > 0);
  readonly openingKey = computed(() => {
    const eco = (this.query().eco ?? '').trim();
    return eco ? (openingGroupOf(eco) || CUSTOM_ECO) : '';
  });
  /** So weit blättert die Suche (Server-Deckel), höchstens alle Treffer. */
  readonly shownTotal = computed(() => {
    const p = this.page();
    return p ? Math.min(p.total, p.maxPage * p.pageSize) : 0;
  });
  readonly capped = computed(() => { const p = this.page(); return !!p && p.total > this.shownTotal(); });

  private readonly requests = new Subject<{ query: MasterGameQuery; debounce: boolean }>();

  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  ngOnInit(): void {
    this.requests.pipe(
      // Getipptes wartet auf die Pause (jede neue Eingabe verwirft den Wecker), Auswahl und Blättern gehen sofort.
      // Nicht `of(r).pipe(debounceTime())`: das schließt sofort ab und gibt damit ungedrosselt weiter.
      switchMap(r => (r.debounce ? timer(MASTER_SEARCH_DEBOUNCE_MS).pipe(map(() => r)) : of(r))),
      switchMap(r => {
        this.state.set({ kind: 'loading' });
        return this.library.searchMasters({ ...r.query, pageSize: MASTER_PAGE_SIZE }).pipe(
          map(page => ({ kind: 'done', page }) as LoadState),
          // catchError INNEN — sonst endet der Strom beim ersten Fehler (F5-004).
          catchError(error => of({ kind: 'error', error } as LoadState)),
        );
      }),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(s => this.state.set(s));

    const params = this.route.snapshot.queryParamMap;
    const initial: Record<string, string> = {};
    for (const k of params.keys) initial[k] = params.get(k) ?? '';
    this.query.set(paramsToQuery(initial));
    this.requests.next({ query: this.query(), debounce: false });
  }

  /** Ein Filter ändert sich: zurück auf Seite 1, Adresse nachziehen, suchen. */
  set(patch: Partial<MasterGameQuery>, typed = false): void {
    this.query.update(q => ({ ...q, ...patch, page: 1 }));
    this.search(typed);
  }

  pickOpening(key: string): void {
    if (key === CUSTOM_ECO) return;
    this.set({ eco: OPENING_GROUPS.find(o => o.key === key)?.eco ?? '' });
  }

  reset(): void {
    this.query.set(paramsToQuery({}));
    this.search(false);
  }

  reload(): void { this.requests.next({ query: this.query(), debounce: false }); }

  onPage(e: PageEvent): void {
    this.query.update(q => ({ ...q, page: e.pageIndex + 1 }));
    this.search(false);
    window.scrollTo?.({ top: 0 });
  }

  year(v: unknown): number | null {
    const n = typeof v === 'number' ? v : Number.parseInt(String(v ?? ''), 10);
    return Number.isFinite(n) ? n : null;
  }

  private search(typed: boolean): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: queryToParams(this.query()), replaceUrl: true });
    this.requests.next({ query: this.query(), debounce: typed });
  }

  names(g: LibraryGame): string {
    const unknown = this.translate.instant('masterGames.unknownPlayer');
    return `${cleanName(g.white) || unknown} – ${cleanName(g.black) || unknown}`;
  }

  result(g: LibraryGame): string { return resultLabel(g.result); }

  /** „Wijk aan Zee 2001 · B90 · kommentiert von X · Quelle: CBM 104“ — nur gesetzte Angaben. */
  meta(g: LibraryGame): string {
    const place = [cleanName(g.event), gameYear(g)].filter(Boolean).join(' ');
    const by = cleanName(g.annotator) ? this.translate.instant('games.review.masterBy', { name: g.annotator }) : '';
    const source = cleanName(g.sourceTitle) ? this.translate.instant('masterGames.source', { source: g.sourceTitle }) : '';
    return [place, cleanName(g.eco), by, source].filter(Boolean).join(' · ');
  }

  /** Anschauen: Hauptvariante + Anmerkungen in der Sprache der Oberfläche im Nachspiel-Dialog (erst hier nachgeladen). */
  open(g: LibraryGame): void {
    if (!this.loggedIn) { this.login(); return; }
    if (this.busy()) return;
    this.busy.set(g.id);
    this.library.view(g.id, this.translate.currentLang() || undefined).subscribe({
      next: async v => {
        try {
          const { PgnViewerComponent } = await import('../games/pgn-viewer.component');
          this.dialog.open(PgnViewerComponent, { data: { pgn: v.pgn }, maxWidth: '96vw', panelClass: 'pgn-viewer-dialog' });
        } finally {
          this.busy.set(null);
        }
      },
      error: () => { this.busy.set(null); this.snackbar.warn(this.translate.instant('masterGames.viewFailed')); },
    });
  }

  login(): void {
    void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
  }
}
