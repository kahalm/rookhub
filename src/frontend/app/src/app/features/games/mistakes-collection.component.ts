import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { Subscription, forkJoin, from, of } from 'rxjs';
import { catchError, map, mergeMap, toArray } from 'rxjs/operators';
import { PreferencesService } from '../../core/preferences.service';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { GamesService, SavedGame } from './games.service';
import { distinctClassifiers, filterByClassifiers, hasUnclassified, NO_CLASSIFIER } from './classifier.util';
import { Mistake } from './mistakes.util';
import { MistakesSession } from './mistakes-session';
import { MistakesTrainerComponent } from './mistakes-trainer.component';
import { MistakeJudgeService } from './mistake-judge.service';
import { byPlayedAt, classifierFromParam, classifierToParam, gameMistakes } from './mistakes-collection.util';

/** So viele Partien lädt die Seite gleichzeitig (Detail + Bewertungen je Partie). */
const LOAD_CONCURRENCY = 4;

/**
 * „Fehler aus Liga/Saison nachspielen" (0.748.0, Wunsch 2026-10-11: „bring mir alle Fehler aus einer Liga — Auswahl Liga
 * und/oder Saison"). Wählt die eigenen Partien nach den beiden Klassifizierern der Partienliste (Ligapartien: Liga +
 * Jahrgang), sammelt die offenen Fehler aller fertig analysierten und fragt sie auf EINEM Brett nacheinander ab — mit
 * demselben Trainer wie auf der Partieseite. Gefundenes und „nicht mehr zeigen" gehen je Partie an den Server, wie dort.
 */
@Component({
  selector: 'app-mistakes-collection',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Default,
  imports: [FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatIconModule, MatProgressSpinnerModule,
    TranslatePipe, ChessBoardComponent, MistakesTrainerComponent],
  template: `
    <div class="page">
      <div class="head">
        <a mat-button routerLink="/games"><mat-icon>arrow_back</mat-icon> {{ 'games.title' | translate }}</a>
        <h1>{{ 'games.mistakes.collection.title' | translate }}</h1>
      </div>

      @if (!games()) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (!training()) {
        <p class="hint">{{ 'games.mistakes.collection.hint' | translate }}</p>
        <div class="filters">
          <label>{{ 'games.classifier.first' | translate }}
            <select [(ngModel)]="filter1" name="filter1" [attr.aria-label]="'games.classifier.first' | translate">
              <option value="">{{ 'games.classifier.all' | translate }}</option>
              @for (v of firstOptions(); track v) { <option [value]="v">{{ v }}</option> }
              @if (unclassified1()) { <option [value]="none">{{ 'games.classifier.none' | translate }}</option> }
            </select>
          </label>
          <label>{{ 'games.classifier.second' | translate }}
            <select [(ngModel)]="filter2" name="filter2" [attr.aria-label]="'games.classifier.second' | translate">
              <option value="">{{ 'games.classifier.all' | translate }}</option>
              @for (v of secondOptions(); track v) { <option [value]="v">{{ v }}</option> }
              @if (unclassified2()) { <option [value]="none">{{ 'games.classifier.none' | translate }}</option> }
            </select>
          </label>
          <mat-checkbox [(ngModel)]="includeSolved" name="includeSolved">{{ 'games.mistakes.collection.includeSolved' | translate }}</mat-checkbox>
        </div>
        <p class="count">{{ 'games.mistakes.collection.selection' | translate: { games: selected().length, analysed: analysed().length } }}</p>
        @if (loadProgress(); as p) {
          <p class="loading"><mat-spinner diameter="18"></mat-spinner> {{ 'games.mistakes.collection.loading' | translate: p }}</p>
        } @else {
          <button mat-flat-button color="primary" class="start" [disabled]="analysed().length === 0" (click)="start()">
            <mat-icon>replay</mat-icon> {{ 'games.mistakes.collection.start' | translate }}
          </button>
          @if (empty()) { <p class="empty">{{ 'games.mistakes.collection.none' | translate }}</p> }
        }
      } @else {
        @let t = training()!;
        <div class="body">
          <div class="board-wrap">
            <app-chess-board [fen]="t.boardFen()" [lastMove]="t.lastMove()" [flipped]="t.flipped()"
                             [playable]="t.playable()" (userMove)="t.onMove($event)"
                             [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
          </div>
          <app-mistakes-trainer class="trainer" [session]="t" [analyzable]="false"
                                title="games.mistakes.collection.title" (closed)="stop()" />
          <p class="source">{{ 'games.mistakes.collection.source' | translate: { games: sessionGames(), mistakes: t.bySide.white.length } }}</p>
        </div>
      }
    </div>
  `,
  styles: [`
    .page { max-width: 720px; margin: 0 auto; padding: 12px 16px 32px; }
    .head { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 12px; }
    .head h1 { margin: 0; font-size: 1.4rem; }
    .hint, .count, .empty, .source { color: color-mix(in srgb, currentColor 75%, transparent); }
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; margin: 8px 0; font-size: 0.9rem; }
    .filters label { display: inline-flex; align-items: center; gap: 6px; }
    .filters select { font: inherit; color: inherit; background: transparent; padding: 4px 8px;
      border: 1px solid color-mix(in srgb, currentColor 30%, transparent); border-radius: 4px; }
    .filters option { color: initial; }
    .loading { display: flex; align-items: center; gap: 8px; }
    .center { display: flex; justify-content: center; padding: 32px; }
    .body { display: flex; flex-direction: column; gap: 8px; margin-top: 8px; }
    .board-wrap { width: 100%; max-width: 560px; margin: 0 auto; }
    .trainer { display: block; width: 100%; }
    .source { font-size: 0.8rem; margin: 0; }
  `],
})
export class MistakesCollectionComponent implements OnInit {
  private readonly service = inject(GamesService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly judge = inject(MistakeJudgeService);
  readonly preferences = inject(PreferencesService);

  readonly none = NO_CLASSIFIER;
  /** Die eigenen Partien (Liste ohne PGN); `null` = lädt noch. */
  readonly games = signal<SavedGame[] | null>(null);
  readonly filter1Sig = signal('');
  readonly filter2Sig = signal('');
  get filter1(): string { return this.filter1Sig(); }
  set filter1(v: string) { this.filter1Sig.set(v); this.empty.set(false); this.syncUrl(); }
  get filter2(): string { return this.filter2Sig(); }
  set filter2(v: string) { this.filter2Sig.set(v); this.empty.set(false); this.syncUrl(); }
  /** Auch schon selbst gefundene Fehler noch einmal abfragen (Vorgabe: nur offene). */
  includeSolved = false;

  readonly firstOptions = computed(() => distinctClassifiers(this.games() ?? [], 1));
  readonly secondOptions = computed(() => distinctClassifiers(this.games() ?? [], 2));
  readonly unclassified1 = computed(() => hasUnclassified(this.games() ?? [], 1));
  readonly unclassified2 = computed(() => hasUnclassified(this.games() ?? [], 2));
  readonly selected = computed(() => filterByClassifiers(this.games() ?? [], this.filter1Sig(), this.filter2Sig()));
  /** Nur Partien mit fertiger Analyse geben Aufgaben her. */
  readonly analysed = computed(() => this.selected().filter(g => g.analysis?.status === 'done'));

  readonly loadProgress = signal<{ done: number; total: number } | null>(null);
  /** Die Auswahl gab keinen offenen Fehler her. */
  readonly empty = signal(false);
  readonly training = signal<MistakesSession | null>(null);
  readonly sessionGames = signal(0);

  /** Je Partie: alle Aufgaben der eigenen Seite (für `total`) und was schon gemeldet ist. */
  private totals = new Map<number, number>();
  private reported = new Map<number, Set<number>>();
  private loadSub?: Subscription;

  /** Selbst Gefundenes sofort je Partie melden — die Sammlung hat kein „Ende", das man sicher erreicht. */
  private readonly reportEffect = effect(() => {
    const solved = this.training()?.solvedMistakes() ?? [];
    untracked(() => this.report(solved));
  });

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    this.filter1Sig.set(classifierFromParam(q?.get('c1')));
    this.filter2Sig.set(classifierFromParam(q?.get('c2')));
    this.service.list(500).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => this.games.set(list),
      error: () => this.games.set([]),
    });
  }

  start(): void {
    const games = [...this.analysed()].sort(byPlayedAt);
    if (!games.length) return;
    const includeSolved = this.includeSolved;
    this.empty.set(false);
    this.loadProgress.set({ done: 0, total: games.length });
    this.loadSub?.unsubscribe();
    this.loadSub = from(games).pipe(
      mergeMap(g => forkJoin({
        detail: this.service.get(g.id).pipe(catchError(() => of(null))),
        evals: this.service.evals(this.service.evalsUrl(g.id)).pipe(catchError(() => of(null))),
      }).pipe(map(({ detail, evals }) => {
        this.loadProgress.update(p => p ? { ...p, done: p.done + 1 } : p);
        return { g, r: detail ? gameMistakes(g, detail.pgn, detail.ownerSide, evals, includeSolved) : { list: [], total: 0 } };
      })), LOAD_CONCURRENCY),
      toArray(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(results => {
      this.loadProgress.set(null);
      // Reihenfolge wie gespielt — mergeMap liefert in Ankunftsreihenfolge.
      const order = new Map(games.map((g, i) => [g.id, i]));
      results.sort((a, b) => order.get(a.g.id)! - order.get(b.g.id)!);
      this.totals = new Map(results.map(x => [x.g.id, x.r.total]));
      this.reported = new Map(results.map(x => [x.g.id, new Set(x.g.mistakes?.solvedPlies ?? [])]));
      const all: Mistake[] = results.flatMap(x => x.r.list);
      if (!all.length) { this.empty.set(true); return; }
      this.sessionGames.set(results.filter(x => x.r.list.length > 0).length);
      // Alle Aufgaben stehen auf der „weißen" Liste; das Brett dreht sich je Aufgabe nach dem Ziehenden (MistakesSession.flipped).
      this.training.set(new MistakesSession({ white: all, black: [] }, 'white',
        (m, fen) => this.judge.judge(m, fen), fen => this.judge.evaluate(fen), m => this.dismiss(m)));
    });
  }

  stop(): void {
    this.training.set(null);
    // Liste neu holen: Fortschritt und Ausgeblendetes stehen jetzt anders.
    this.service.list(500).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: list => this.games.set(list) });
  }

  private dismiss(m: Mistake): void {
    if (m.gameId == null) return;
    this.service.dismissMistake(m.gameId, m.ply, true, this.totals.get(m.gameId) ?? 0)
      .subscribe({ error: () => { /* still — wie die Fortschrittsmeldung */ } });
  }

  private report(solved: readonly Mistake[]): void {
    const neu = new Map<number, number[]>();
    for (const m of solved) {
      if (m.gameId == null || this.reported.get(m.gameId)?.has(m.ply)) continue;
      neu.set(m.gameId, [...(neu.get(m.gameId) ?? []), m.ply]);
    }
    for (const [gameId, plies] of neu) {
      const seen = this.reported.get(gameId) ?? new Set<number>();
      plies.forEach(p => seen.add(p));
      this.reported.set(gameId, seen);
      this.service.recordMistakes(gameId, this.totals.get(gameId) ?? plies.length, plies)
        .subscribe({ error: () => plies.forEach(p => seen.delete(p)) });
    }
  }

  /** Auswahl in der Adresse, damit ein Lesezeichen „Landesliga 2026/27" wieder dahin führt. */
  private syncUrl(): void {
    this.router.navigate([], {
      relativeTo: this.route, replaceUrl: true,
      queryParams: { c1: classifierToParam(this.filter1Sig()), c2: classifierToParam(this.filter2Sig()) },
    });
  }
}
