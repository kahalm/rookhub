import { Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, computed, signal, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { Observable, Subject, Subscription, debounceTime, of, switchMap, tap, timer, catchError, map } from 'rxjs';
import { Chess } from 'chess.js';
import {
  EXPLORER_RATINGS, EXPLORER_SPEEDS, ExplorerGame, ExplorerGames, ExplorerPosition, ExplorerPositionMove, ExplorerSettings, ExplorerSource,
  ExplorerSources, RepertoireExplorerService, fitToLocal, formatPercent, readExplorerSettings, saveExplorerSettings,
} from '../repertoire/repertoire-explorer.service';

const OPEN_KEY = 'rookhub_analysis_explorer_open';
/** So lange ruht die Abfrage nach einem Zug — wer mit den Pfeiltasten durch eine Partie läuft,
 *  soll nicht jede Zwischenstellung abfragen (online kostet jede Abfrage Kontingent). */
const DEBOUNCE_MS = 250;
const CACHE_MAX = 300;

/** Eine Zeile der Tabelle: Zug + Anteile, fertig für die Vorlage. */
interface Row {
  move: ExplorerPositionMove;
  share: number;
  w: number;
  d: number;
  b: number;
}

function readOpen(): boolean {
  try { return localStorage.getItem(OPEN_KEY) !== '0'; } catch { return true; }
}

/**
 * Eröffnungs-Explorer auf dem Analysebrett: was wird in der aktuellen Stellung gespielt, wie oft und
 * mit welchem Ergebnis? Dieselbe Datenstrecke und dieselbe Auswahl wie der Lochfinder
 * (`rookhub_explorer_settings`: Quelle online/lokal, Datenbank, Elo, Tempo) — wer dort „lokal,
 * Meister" wählt, sieht hier dasselbe.
 *
 * <p>Ein Klick auf einen Zug spielt ihn aufs Brett (`playMove`). Abgefragt wird erst
 * {@link DEBOUNCE_MS} nach dem letzten Stellungswechsel, jede Antwort bleibt je Stellung und
 * Auswahl im Speicher der Seite, und zusammengeklappt fragt die Karte gar nicht.</p>
 */
@Component({
  selector: 'app-opening-explorer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatIconModule, MatProgressBarModule,
    MatTooltipModule, TranslatePipe,
  ],
  template: `
    <div class="head">
      <button type="button" class="title" (click)="toggleOpen()" [attr.aria-expanded]="open()">
        <mat-icon>{{ open() ? 'expand_more' : 'chevron_right' }}</mat-icon>
        {{ 'analysis.explorer.title' | translate }}
      </button>
      @if (open()) {
        @if (sources()?.local) {
          <mat-button-toggle-group class="small" [value]="settings().source" (change)="setSource($event.value)"
                                   hideSingleSelectionIndicator="true" [attr.aria-label]="'repertoire.holes.source' | translate">
            <mat-button-toggle value="online">{{ 'repertoire.holes.sourceOnline' | translate }}</mat-button-toggle>
            <mat-button-toggle value="local">{{ 'repertoire.holes.sourceLocal' | translate }}</mat-button-toggle>
          </mat-button-toggle-group>
        }
        <mat-button-toggle-group class="small" [value]="settings().database" (change)="setDatabase($event.value)"
                                 hideSingleSelectionIndicator="true" [attr.aria-label]="'repertoire.holes.database' | translate">
          <mat-button-toggle value="lichess">Lichess</mat-button-toggle>
          <mat-button-toggle value="masters">{{ 'repertoire.holes.masters' | translate }}</mat-button-toggle>
        </mat-button-toggle-group>
        @if (settings().database === 'lichess') {
          <button mat-icon-button type="button" (click)="showFilters.set(!showFilters())"
                  [matTooltip]="'analysis.explorer.filters' | translate" [attr.aria-label]="'analysis.explorer.filters' | translate">
            <mat-icon>tune</mat-icon>
          </button>
        }
      }
    </div>

    @if (open()) {
      @if (showFilters() && settings().database === 'lichess') {
        <div class="chips">
          @for (r of ratings(); track r) {
            <button type="button" class="chip" [class.on]="settings().ratings.includes(r)" (click)="toggleRating(r)"
                    [attr.aria-pressed]="settings().ratings.includes(r)">{{ ratingLabel(r) }}</button>
          }
        </div>
        <div class="chips">
          @for (s of speeds(); track s) {
            <button type="button" class="chip" [class.on]="settings().speeds.includes(s)" (click)="toggleSpeed(s)"
                    [attr.aria-pressed]="settings().speeds.includes(s)">{{ 'repertoire.holes.speed.' + s | translate }}</button>
          }
        </div>
      }

      <div class="bar-slot">@if (loading()) { <mat-progress-bar mode="indeterminate" /> }</div>

      @if (result(); as r) {
        @switch (r.status) {
          @case ('tokenMissing') {
            <p class="note">{{ 'repertoire.holes.tokenMissing' | translate }}
              <a routerLink="/profile">{{ 'repertoire.holes.toProfile' | translate }}</a>
              @if (sources()?.local) { {{ 'analysis.explorer.orLocal' | translate }} }</p>
          }
          @case ('tokenInvalid') {
            <p class="note">{{ 'repertoire.holes.tokenInvalid' | translate }}
              <a routerLink="/profile">{{ 'repertoire.holes.toProfile' | translate }}</a></p>
          }
          @case ('rateLimited') {
            <p class="note">{{ 'repertoire.holes.rateLimited' | translate: { seconds: r.retryAfterSeconds ?? 60 } }}</p>
          }
          @case ('failed') {
            <p class="note">{{ 'analysis.explorer.failed' | translate }}
              <button mat-button type="button" (click)="reload()">{{ 'analysis.explorer.retry' | translate }}</button></p>
          }
          @default {
            @if (r.total === 0) {
              <p class="note">{{ 'analysis.explorer.noGames' | translate }}</p>
            } @else {
              <table class="moves">
                <thead>
                  <tr>
                    <th>{{ 'analysis.explorer.move' | translate }}</th>
                    <th class="num">{{ 'analysis.explorer.games' | translate }}</th>
                    <th class="wdl-head">{{ 'analysis.explorer.results' | translate }}</th>
                    <th class="info-head"></th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of rows(); track row.move.uci) {
                    <tr (click)="play(row.move)" [matTooltip]="row.move.opening ? (row.move.eco + ' ' + row.move.opening) : ''">
                      <td class="san"><button type="button" class="san-btn" (click)="$event.stopPropagation(); play(row.move)">{{ row.move.san }}</button></td>
                      <td class="num">
                        <span class="share">{{ pct(row.share) }}</span>
                        <span class="count">{{ compact(row.move.games) }}</span>
                      </td>
                      <td>
                        <div class="wdl" [attr.aria-label]="wdlLabel(row)">
                          <span class="w" [style.width.%]="row.w * 100">{{ row.w >= 0.14 ? pct(row.w) : '' }}</span>
                          <span class="d" [style.width.%]="row.d * 100">{{ row.d >= 0.14 ? pct(row.d) : '' }}</span>
                          <span class="b" [style.width.%]="row.b * 100">{{ row.b >= 0.14 ? pct(row.b) : '' }}</span>
                        </div>
                      </td>
                      <td class="info">
                        <button mat-icon-button type="button" class="info-btn" [class.on]="expanded() === row.move.uci"
                                (click)="$event.stopPropagation(); toggleGames(row.move)"
                                [matTooltip]="'analysis.explorer.gamesInfo' | translate"
                                [attr.aria-label]="'analysis.explorer.gamesInfo' | translate"
                                [attr.aria-expanded]="expanded() === row.move.uci">
                          <mat-icon>info_outline</mat-icon>
                        </button>
                      </td>
                    </tr>
                    @if (expanded() === row.move.uci) {
                      <tr class="games-row">
                        <td colspan="4">
                          @if (gamesLoading()) {
                            <mat-progress-bar mode="indeterminate" />
                          } @else if (games(); as gs) {
                            @switch (gs.status) {
                              @case ('ok') {
                                @if (gs.games.length === 0) {
                                  <span class="note">{{ 'analysis.explorer.gamesNone' | translate }}</span>
                                } @else {
                                  <ul class="games">
                                    @for (g of gs.games; track g.id) {
                                      <li>
                                        <span class="res">{{ gameResult(g) }}</span>
                                        @if (g.url) {
                                          <a [href]="g.url" target="_blank" rel="noopener">{{ players(g) }}</a>
                                        } @else {
                                          <span>{{ players(g) }}</span>
                                        }
                                        <span class="date">{{ g.date }}@if (g.speed) { · {{ 'repertoire.holes.speed.' + g.speed | translate }} }</span>
                                      </li>
                                    }
                                  </ul>
                                }
                              }
                              @case ('rateLimited') {
                                <span class="note">{{ 'repertoire.holes.rateLimited' | translate: { seconds: gs.retryAfterSeconds ?? 60 } }}</span>
                              }
                              @case ('tokenMissing') {
                                <span class="note">{{ 'repertoire.holes.tokenMissing' | translate }}</span>
                              }
                              @default {
                                <span class="note">{{ 'analysis.explorer.failed' | translate }}</span>
                              }
                            }
                          }
                        </td>
                      </tr>
                    }
                  }
                </tbody>
              </table>
              <div class="foot">
                <span>{{ 'analysis.explorer.total' | translate: { count: compact(r.total) } }}</span>
                @if (r.opening) { <span class="opening">{{ r.eco }} {{ r.opening }}</span> }
              </div>
            }
          }
        }
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .head { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
    .title { display: inline-flex; align-items: center; gap: 2px; font: inherit; font-weight: 600; color: inherit;
      background: none; border: 0; cursor: pointer; padding: 0; margin-right: auto; }
    .small { font-size: 12px; }
    .chips { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 6px; }
    .chip { font: inherit; font-size: 12px; padding: 2px 8px; border-radius: 12px; cursor: pointer;
      border: 1px solid color-mix(in srgb, currentColor 30%, transparent); background: transparent; color: inherit; }
    .chip.on { background: var(--mat-sys-primary, #3f51b5); color: var(--mat-sys-on-primary, #fff); border-color: transparent; }
    .bar-slot { height: 4px; margin: 6px 0 2px; }
    .note { font-size: 13px; margin: 6px 0; color: color-mix(in srgb, currentColor 75%, transparent); }
    table.moves { width: 100%; border-collapse: collapse; font-size: 13px; }
    th { text-align: left; font-weight: 500; font-size: 11px; color: color-mix(in srgb, currentColor 60%, transparent); padding: 2px 4px; }
    td { padding: 3px 4px; border-top: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    tbody tr { cursor: pointer; }
    tbody tr:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .num { text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; }
    .share { display: inline-block; min-width: 3.4em; }
    .count { display: inline-block; min-width: 3.6em; color: color-mix(in srgb, currentColor 60%, transparent); }
    .san { width: 1%; white-space: nowrap; }
    .san-btn { font: inherit; font-weight: 600; font-family: 'Roboto Mono', monospace; background: none; border: 0;
      padding: 0; color: inherit; cursor: pointer; }
    .wdl-head { width: 55%; }
    .wdl { display: flex; height: 16px; border-radius: 3px; overflow: hidden; font-size: 10px; line-height: 16px;
      border: 1px solid color-mix(in srgb, currentColor 25%, transparent); }
    .wdl span { text-align: center; overflow: hidden; white-space: nowrap; }
    .wdl .w { background: #f2f2f2; color: #222; }
    .wdl .d { background: #9e9e9e; color: #111; }
    .wdl .b { background: #303030; color: #eee; }
    .info-head, .info { width: 1%; padding: 0; }
    .info-btn { width: 28px; height: 28px; padding: 2px; --mdc-icon-button-state-layer-size: 28px; }
    .info-btn mat-icon { font-size: 18px; width: 18px; height: 18px; color: color-mix(in srgb, currentColor 55%, transparent); }
    .info-btn.on mat-icon { color: var(--mat-sys-primary, #3f51b5); }
    tr.games-row { cursor: default; }
    tr.games-row:hover { background: none; }
    ul.games { list-style: none; margin: 0; padding: 2px 0 4px; display: flex; flex-direction: column; gap: 3px; font-size: 12px; }
    ul.games li { display: flex; gap: 6px; align-items: baseline; flex-wrap: wrap; }
    .res { font-family: 'Roboto Mono', monospace; min-width: 2.6em; }
    .date { color: color-mix(in srgb, currentColor 55%, transparent); }
    .foot { display: flex; justify-content: space-between; gap: 8px; flex-wrap: wrap; margin-top: 4px; font-size: 12px;
      color: color-mix(in srgb, currentColor 60%, transparent); }
  `],
})
export class OpeningExplorerComponent implements OnInit, OnChanges, OnDestroy {
  @Input({ required: true }) fen = '';
  /** SAN des angeklickten Zugs — die Analyse spielt ihn aufs Brett. */
  @Output() playMove = new EventEmitter<string>();

  readonly open = signal(readOpen());
  readonly showFilters = signal(false);
  readonly settings = signal<ExplorerSettings>(readExplorerSettings());
  readonly sources = signal<ExplorerSources | null>(null);
  readonly loading = signal(false);
  readonly result = signal<ExplorerPosition | null>(null);
  /** Aufgeklappte Zeile (Partien zum Zug), als UCI des Zugs; null = keine. */
  readonly expanded = signal<string | null>(null);
  readonly games = signal<ExplorerGames | null>(null);
  readonly gamesLoading = signal(false);

  readonly ratings = computed(() => {
    const src = this.sources();
    return this.settings().source === 'local' && src ? src.localRatings : EXPLORER_RATINGS;
  });
  readonly speeds = computed(() => {
    const src = this.sources();
    return this.settings().source === 'local' && src ? src.localSpeeds : EXPLORER_SPEEDS;
  });

  readonly rows = computed<Row[]>(() => {
    const r = this.result();
    if (!r || r.status !== 'ok' || r.total === 0) return [];
    return r.moves.map(move => {
      const results = move.white + move.draws + move.black;
      return {
        move,
        share: move.games / r.total,
        w: results ? move.white / results : 0,
        d: results ? move.draws / results : 0,
        b: results ? move.black / results : 0,
      };
    });
  });

  private readonly requests = new Subject<void>();
  private readonly cache = new Map<string, ExplorerPosition>();
  private readonly gamesCache = new Map<string, ExplorerGames>();
  private gamesSub: Subscription | null = null;
  private sub: Subscription | null = null;
  private retry: Subscription | null = null;
  private ready = false;

  constructor(private explorer: RepertoireExplorerService) {}

  ngOnInit(): void {
    this.sub = this.requests.pipe(
      debounceTime(DEBOUNCE_MS),
      switchMap(() => this.load()),
    ).subscribe(({ key, r }) => {
      // Eine späte Antwort für eine schon verlassene Stellung/Auswahl nicht anzeigen.
      if (key !== this.cacheKey()) return;
      this.loading.set(false);
      this.result.set(r);
      this.scheduleRetry(r);
    });
    this.explorer.sources().subscribe(src => {
      this.sources.set(src);
      // Gemerkt „lokal", aber hier nicht eingerichtet → online; lokal nur, wofür es dort Partien gibt.
      const s = this.settings();
      if (s.source === 'local') this.settings.set(src.local ? fitToLocal(s, src) : { ...s, source: 'online' });
      this.ready = true;
      this.request();
    });
  }

  ngOnChanges(): void {
    this.closeGames();   // andere Stellung → die aufgeklappten Partien gehören nicht mehr dazu
    if (this.ready) this.request();
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.retry?.unsubscribe();
    this.gamesSub?.unsubscribe();
  }

  /** (i) einer Zeile: die Partien mit diesem Zug auf- bzw. zuklappen. Gefragt wird die Stellung NACH
   *  dem Zug — das sind genau die Partien, in denen er gespielt wurde (Zugumstellungen eingeschlossen). */
  toggleGames(move: ExplorerPositionMove): void {
    if (this.expanded() === move.uci) { this.closeGames(); return; }
    this.closeGames();
    const after = this.fenAfter(move);
    if (!after) return;
    this.expanded.set(move.uci);
    const key = `${after.split(' ').slice(0, 3).join(' ')}|${this.settingsKey()}`;
    const hit = this.gamesCache.get(key);
    if (hit) { this.games.set(hit); return; }
    this.gamesLoading.set(true);
    this.gamesSub = this.explorer.games(after, this.settings()).pipe(
      catchError(() => of<ExplorerGames>({ status: 'failed', retryAfterSeconds: null, games: [] })),
    ).subscribe(g => {
      if (g.status === 'ok') this.gamesCache.set(key, g);
      this.gamesLoading.set(false);
      this.games.set(g);
    });
  }

  /** „Caruana, Fabiano (2818) – Carlsen, Magnus (2882)". */
  players(g: ExplorerGame): string {
    const one = (name: string, rating: number | null) => rating ? `${name} (${rating})` : name;
    return `${one(g.white, g.whiteRating)} – ${one(g.black, g.blackRating)}`;
  }

  gameResult(g: ExplorerGame): string {
    return g.winner === 'white' ? '1-0' : g.winner === 'black' ? '0-1' : '½-½';
  }

  private closeGames(): void {
    this.gamesSub?.unsubscribe();
    this.gamesSub = null;
    this.expanded.set(null);
    this.games.set(null);
    this.gamesLoading.set(false);
  }

  private fenAfter(move: ExplorerPositionMove): string | null {
    try {
      const chess = new Chess(this.fen);
      return chess.move(move.san) ? chess.fen() : null;
    } catch {
      return null;
    }
  }

  toggleOpen(): void {
    this.open.set(!this.open());
    try { localStorage.setItem(OPEN_KEY, this.open() ? '1' : '0'); } catch { /* nur diese Sitzung */ }
    this.request();
  }

  setSource(source: ExplorerSource): void {
    const src = this.sources();
    this.update(source === 'local' && src ? fitToLocal({ ...this.settings(), source }, src) : { source });
  }

  setDatabase(database: 'lichess' | 'masters'): void { this.update({ database }); }

  toggleRating(r: number): void {
    const cur = this.settings().ratings;
    if (cur.includes(r) && cur.length === 1) return;   // ohne Stufe gäbe es keine Abfrage
    this.update({ ratings: cur.includes(r) ? cur.filter(x => x !== r) : [...cur, r].sort((a, b) => a - b) });
  }

  toggleSpeed(s: string): void {
    const cur = this.settings().speeds;
    if (cur.includes(s) && cur.length === 1) return;
    this.update({ speeds: cur.includes(s) ? cur.filter(x => x !== s) : EXPLORER_SPEEDS.filter(x => x === s || cur.includes(x)) });
  }

  reload(): void {
    this.cache.delete(this.cacheKey());
    this.request();
  }

  play(move: ExplorerPositionMove): void { this.playMove.emit(move.san); }

  pct(x: number): string { return formatPercent(x); }

  compact(n: number): string {
    return n.toLocaleString(undefined, n >= 10_000 ? { notation: 'compact', maximumFractionDigits: 1 } : {});
  }

  ratingLabel(r: number): string {
    if (r === 0) return '<1000';
    return r === 2500 ? '2500+' : String(r);
  }

  wdlLabel(row: Row): string {
    return `${this.pct(row.w)} / ${this.pct(row.d)} / ${this.pct(row.b)}`;
  }

  private update(patch: Partial<ExplorerSettings>): void {
    const next = { ...this.settings(), ...patch };
    this.settings.set(next);
    saveExplorerSettings(next);
    this.closeGames();
    this.request();
  }

  private request(): void {
    this.retry?.unsubscribe();
    if (!this.open() || !this.fen) return;
    const hit = this.cache.get(this.cacheKey());
    if (hit) {
      // Schon bekannt: sofort zeigen, keine Abfrage (auch keine verzögerte, die es überschriebe).
      this.loading.set(false);
      this.result.set(hit);
      this.requests.next();
      return;
    }
    this.loading.set(true);
    this.requests.next();
  }

  /** Nach der Ruhezeit: aus dem Speicher oder vom Server — immer für den DANN aktuellen Stand. */
  private load(): Observable<{ key: string; r: ExplorerPosition }> {
    const key = this.cacheKey();
    const hit = this.cache.get(key);
    if (hit) return of({ key, r: hit });
    return this.explorer.position(this.fen, this.settings()).pipe(
      tap(r => { if (r.status === 'ok') this.remember(key, r); }),
      catchError(() => of(FAILED)),
      map(r => ({ key, r })),
    );
  }

  /** Lichess bremst: nach der genannten Wartezeit von selbst erneut fragen. */
  private scheduleRetry(r: ExplorerPosition): void {
    if (r.status !== 'rateLimited') return;
    this.retry = timer(((r.retryAfterSeconds ?? 60) + 1) * 1000).subscribe(() => this.request());
  }

  private remember(key: string, r: ExplorerPosition): void {
    if (this.cache.size >= CACHE_MAX) this.cache.delete(this.cache.keys().next().value!);
    this.cache.set(key, r);
  }

  private cacheKey(): string {
    return `${this.fen.split(' ').slice(0, 3).join(' ')}|${this.settingsKey()}`;
  }

  private settingsKey(): string {
    const s = this.settings();
    return s.database === 'masters'
      ? `${s.source}|masters`
      : `${s.source}|lichess|${s.ratings.join(',')}|${s.speeds.join(',')}`;
  }
}

const FAILED: ExplorerPosition = {
  status: 'failed', retryAfterSeconds: null, source: 'online', database: 'lichess',
  total: 0, white: 0, draws: 0, black: 0, opening: null, eco: null, moves: [],
};
