import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, ElementRef, HostListener, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, interval } from 'rxjs';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { fenAfterUci } from '../../shared/pgn-viewer/board-moves.util';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GameAnalysis, GameAnalysisPosition, GameAnalysisService, GameConvergence } from './game-analysis.service';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SanPipe } from '../../shared/chess/san.pipe';
import { localizeSan } from '../puzzles/keyboard-move.util';
import { analysisTitle } from './analysis-title.util';
import { PctPipe } from '../../shared/format/pct';

/**
 * Eine Partie-Analyse durchblättern (`/analysis/games/:id`): Brett links, Zugliste rechts, je Zug
 * die Bewertung der Engine. Kein Motor im Browser — alles kommt aus der gespeicherten Analyse.
 *
 * <p>Solange die Partie noch rechnet, frischt sich die Seite alle 10 s auf und die noch offenen
 * Züge stehen grau da; fertig gerechnete zeigen ihre Bewertung. So kann man die Partie ansehen,
 * während hinten weitergerechnet wird.</p>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-game-analysis-detail',
  standalone: true,
  imports: [IconLabelDirective, CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatProgressBarModule, TranslatePipe, ChessBoardComponent, LoadingSpinnerComponent, SanPipe, PctPipe],
  template: `
    <div class="gad-container">
      @if (loading) {
        <app-loading-spinner />
      } @else if (!analysis) {
        <mat-card><mat-card-content>
          <p>{{ 'gameAnalysis.notFound' | translate }}</p>
          <a mat-stroked-button routerLink="/analysis/games">{{ 'gameAnalysis.back' | translate }}</a>
        </mat-card-content></mat-card>
      } @else {
        <div class="header">
          <div>
            <h1>{{ title() }}</h1>
            <p class="muted small">
              @if (analysis.targetNodes) {
                {{ (analysis.multiPv === 1 ? 'gameAnalysis.nodesLinesOne' : 'gameAnalysis.nodesLines') | translate:{ nodes: (analysis.targetNodes | number), lines: analysis.multiPv } }}
              } @else {
                {{ (analysis.multiPv === 1 ? 'gameAnalysis.depthLinesOne' : 'gameAnalysis.depthLines') | translate:{ depth: analysis.targetDepth, lines: analysis.multiPv } }}
              }
              @if (analysis.result) { <span>· {{ analysis.result }}</span> }
            </p>
          </div>
          <a mat-stroked-button routerLink="/analysis/games"><mat-icon>arrow_back</mat-icon> {{ 'gameAnalysis.back' | translate }}</a>
        </div>

        @if (analysis.status !== 'done') {
          <div class="progress">
            <mat-progress-bar mode="determinate" [value]="percent"></mat-progress-bar>
            <span class="muted small">{{ 'gameAnalysis.progress' | translate:{ done: analysis.analyzedPlies, total: analysis.plyCount } }}</span>
          </div>
        }

        <div class="body">
          <div class="board-col">
            <app-chess-board [fen]="currentFen" [lastMove]="lastMove" [flipped]="flipped"
                             [boardTheme]="boardTheme" [pieceSet]="pieceSet" />
            <div class="nav">
              <button mat-icon-button (click)="go(-1)" [disabled]="index < 0"
                      [appIconLabel]="'gameAnalysis.prev' | translate">
                <mat-icon>chevron_left</mat-icon>
              </button>
              <span class="pos-label">{{ label }}</span>
              <button mat-icon-button (click)="go(1)" [disabled]="index >= positions.length - 1"
                      [appIconLabel]="'gameAnalysis.next' | translate">
                <mat-icon>chevron_right</mat-icon>
              </button>
              <button mat-icon-button (click)="flipped = !flipped"
                      [appIconLabel]="'gameAnalysis.flip' | translate">
                <mat-icon>swap_vert</mat-icon>
              </button>
            </div>
          </div>

          <mat-card class="moves-col">
            <mat-card-content>
              <!-- b1-rk-analyse-zugliste-halbzuege: eine Zeile je Vollzug (Nr. | Weiß | Schwarz) statt eines Halbzug-
                   Rasters, das Weiß und Schwarz in wechselnde Spalten warf. b1-rk-analyse-zugliste-scrollkasten: zweispaltig
                   scrollt die Liste in sich und blendet unten aus, solange noch etwas kommt (.more); einspaltig läuft sie
                   mit der Seite. -->
              <div class="moves" #movesBox [class.more]="movesMore()" (scroll)="updateMovesMore()">
                @for (r of moveRows; track r.no) {
                  <span class="no">{{ r.no }}.</span>
                  <ng-container *ngTemplateOutlet="cell; context: { $implicit: r.white }" />
                  <ng-container *ngTemplateOutlet="cell; context: { $implicit: r.black }" />
                }
              </div>
              <ng-template #cell let-p>
                @if (p) {
                  <button class="move" type="button" [class.current]="p.ply === index" [class.pending]="!p.analyzed"
                          (click)="select(p.ply)">
                    <span class="san">{{ p.san | san }}</span>
                    <span class="ev">{{ p.analyzed ? (p.evalText || '') : '…' }}</span>
                  </button>
                } @else {
                  <span class="gap" aria-hidden="true"></span>
                }
              </ng-template>
            </mat-card-content>
          </mat-card>
        </div>

        @if (analysis.targetNodes && convergence) {
          <mat-card class="conv">
            <mat-card-content>
              <h2>{{ 'gameAnalysis.convergence.title' | translate }}</h2>
              @if (convergence.rows.length === 0) {
                <p class="muted small">{{ 'gameAnalysis.convergence.empty' | translate }}</p>
              } @else {
                <p class="muted small">
                  {{ 'gameAnalysis.convergence.hint' | translate }}
                  {{ 'gameAnalysis.convergence.positions' | translate:{ count: convergence.positions } }}
                </p>
                <div class="conv-scroll">
                  <table class="conv-table">
                    <thead>
                      <tr>
                        <th>{{ 'gameAnalysis.convergence.nodes' | translate }}</th>
                        <th>{{ 'gameAnalysis.convergence.count' | translate }}</th>
                        <th>{{ 'gameAnalysis.convergence.same' | translate }}</th>
                        <th>{{ 'gameAnalysis.convergence.median' | translate }}</th>
                        <th>{{ 'gameAnalysis.convergence.p90' | translate }}</th>
                        <th>{{ 'gameAnalysis.convergence.changes' | translate }}</th>
                        <th>{{ 'gameAnalysis.convergence.reached' | translate }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (r of convergence.rows; track r.threshold) {
                        <tr>
                          <td>{{ r.threshold | number }}</td>
                          <td>{{ r.positions }}</td>
                          <td>{{ r.sameMovePercent | pct }}</td>
                          <td>{{ r.medianCp | number:'1.0-0' }} cp · {{ r.medianWinPct | pct:1 }}</td>
                          <td>{{ r.p90Cp | number:'1.0-0' }} cp · {{ r.p90WinPct | pct:1 }}</td>
                          <td>{{ r.moveChanges }}</td>
                          <td>{{ r.meanNodes | number:'1.0-0' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
                <p class="muted small">
                  {{ 'gameAnalysis.convergence.note' | translate }}
                  @if (mateMismatches > 0) { {{ 'gameAnalysis.convergence.mate' | translate:{ n: mateMismatches } }} }
                </p>
              }
            </mat-card-content>
          </mat-card>
        }
      }
    </div>
  `,
  styles: [`
    .gad-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 12px; }
    .header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h1 { margin: 0; font-size: 1.4rem; }
    .progress { display: flex; align-items: center; gap: 10px; margin: 10px 0; flex-wrap: wrap; }
    .progress mat-progress-bar { flex: 1 1 240px; }
    .body { display: flex; gap: 16px; align-items: flex-start; flex-wrap: wrap; margin-top: 12px; }
    .board-col { flex: 1 1 320px; max-width: 520px; }
    .nav { display: flex; align-items: center; gap: 4px; justify-content: center; margin-top: 6px; }
    .pos-label { min-width: 90px; text-align: center; font-variant-numeric: tabular-nums; }
    .moves-col { flex: 1 1 260px; max-width: 420px; }
    /* Eine Zeile je Vollzug: Nr. | Weiß + Bewertung | Schwarz + Bewertung (wie der Zugbaum im Analysebrett). Neben dem
       Brett scrollt die Liste in sich und blendet unten aus, solange noch etwas kommt (DESIGN 5.3). */
    .moves { display: grid; grid-template-columns: 2.6em minmax(0, 1fr) minmax(0, 1fr); gap: 2px 8px; align-items: baseline;
             max-height: 60vh; overflow-y: auto; font-variant-numeric: tabular-nums; }
    .moves.more { -webkit-mask-image: linear-gradient(to bottom, #000 calc(100% - 24px), transparent);
                  mask-image: linear-gradient(to bottom, #000 calc(100% - 24px), transparent); }
    .move { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; min-width: 0;
            background: none; border: 1px solid transparent;
            border-radius: 4px; padding: 3px 6px; font: inherit; color: inherit; cursor: pointer; text-align: left; }
    .move:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .move.current { border-color: var(--rh-accent); background: color-mix(in srgb, var(--rh-accent) 12%, transparent); }
    .move.pending .ev { opacity: .5; }
    .no { color: color-mix(in srgb, currentColor 65%, transparent); font-size: .8rem; text-align: right; }
    .san { font-weight: 600; white-space: nowrap; }
    .ev { min-width: 5ch; padding-left: 8px; text-align: right; font-size: .8rem; white-space: nowrap;
          color: color-mix(in srgb, currentColor 70%, transparent); }
    /* Einspaltig (Liste unter dem Brett) läuft die Zugliste mit der Seite — kein Kasten im Kasten. */
    @media (max-width: 768.98px) {
      .moves { max-height: none; overflow: visible; }
    }
    /* b1-rk-analyse-unterseiten-brett-1920: ab 1200 px wächst das Brett bis 760 px, höchstens bis zur Fensterhöhe; die
       Zugliste ist so hoch wie Brett + Leiste darunter (Seite bleibt in --page-max-width). */
    @media (min-width: 1200px) {
      .board-col { max-width: min(760px, calc(100vh - 240px)); flex-basis: min(760px, calc(100vh - 240px)); }
      .moves { max-height: calc(100vh - 260px); }
    }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
    .conv { margin-top: 16px; }
    .conv h2 { margin: 0 0 4px; font-size: 1.1rem; }
    .conv-scroll { overflow-x: auto; margin: 8px 0; }
    .conv-table { border-collapse: collapse; width: 100%; font-variant-numeric: tabular-nums; }
    .conv-table th, .conv-table td { padding: 4px 10px; text-align: right; white-space: nowrap;
                                     border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .conv-table th { font-weight: 600; font-size: .8rem; }
    /* a1-rk-analyse-unterseiten-ultrawide: Breitbild-Stufe (DESIGN 4.1) — ab 2400 px 1 880-px-Spalte, das Brett wächst
       bis 1 000 px, höchstens bis zur Fensterhöhe; Zugliste breiter und so hoch wie das Brett. Darunter unverändert. */
    @media (min-width: 2400px) {
      .gad-container { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
      .board-col { max-width: min(1000px, calc(100vh - 220px)); flex-basis: min(1000px, calc(100vh - 220px)); }
      .moves-col { max-width: 640px; }
      .moves { max-height: calc(100vh - 260px); }
    }
  `],
})
export class GameAnalysisDetailComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private service = inject(GameAnalysisService);
  private prefs = inject(PreferencesService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  analysis: GameAnalysis | null = null;
  /** Konvergenz der Zwischenstände — nur bei Knotenanalysen geladen. */
  convergence: GameConvergence | null = null;
  positions: GameAnalysisPosition[] = [];
  loading = true;
  flipped = false;
  /** Angezeigter Halbzug; -1 = Ausgangsstellung vor dem ersten Zug. */
  index = -1;

  private poll?: Subscription;
  private movesObserver?: ResizeObserver;
  private movesEl?: HTMLElement;

  /** Vollzüge der Zugliste: je Zeile Nummer, Weiß- und Schwarzzug (fehlt einer, bleibt die Zelle leer). */
  moveRows: { no: number; white?: GameAnalysisPosition; black?: GameAnalysisPosition }[] = [];
  /** Liegen unter dem sichtbaren Teil der (in sich scrollenden) Zugliste noch Züge? Dann blendet sie unten aus. */
  readonly movesMore = signal(false);

  @ViewChild('movesBox') set movesBox(ref: ElementRef<HTMLElement> | undefined) {
    const el = ref?.nativeElement;
    if (el === this.movesEl) return;
    this.movesObserver?.disconnect();
    this.movesEl = el;
    if (el && typeof ResizeObserver === 'function') {
      this.movesObserver = new ResizeObserver(() => this.updateMovesMore());
      this.movesObserver.observe(el);
    }
    this.updateMovesMore();
  }

  /** Prüft beim Scrollen und bei Größenänderung, ob unter dem sichtbaren Rand noch Züge liegen. */
  updateMovesMore(): void {
    const el = this.movesEl;
    this.movesMore.set(!!el && el.scrollHeight - el.scrollTop - el.clientHeight > 1);
  }

  get boardTheme(): string { return this.prefs.boardTheme; }
  get pieceSet(): string { return this.prefs.pieceSet; }

  /** Wie oft Matt gegen „kein Matt" über alle Stufen auseinanderlag (nur als Fußnote). */
  get mateMismatches(): number {
    return (this.convergence?.rows ?? []).reduce((n, r) => n + r.mateMismatch, 0);
  }

  get percent(): number {
    const a = this.analysis;
    return a && a.plyCount > 0 ? Math.round((100 * a.analyzedPlies) / a.plyCount) : 0;
  }

  /**
   * Stellung NACH dem gewählten Zug — also die FEN des nächsten Halbzugs.
   *
   * <p>Für den LETZTEN Zug gibt es keinen nächsten Halbzug mehr: gespeichert ist je Zeile nur die
   * Stellung DAVOR. Ohne `endFen` zeigte das Brett dort dieselbe Stellung wie einen Zug zuvor — die
   * Schlussstellung der Partie liess sich gar nicht ansehen. Sie wird deshalb einmal beim Laden
   * nachgespielt.</p>
   */
  get currentFen(): string {
    if (this.positions.length === 0) return 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
    const next = this.positions[this.index + 1];
    if (next) return next.fen;
    return this.endFen ?? this.positions[this.positions.length - 1].fen;
  }

  /** Schlussstellung (letzte Zeile + ihr Zug); `null`, wenn sie sich nicht nachspielen laesst. */
  private endFen: string | null = null;

  get lastMove(): [string, string] | undefined {
    const p = this.positions[this.index];
    if (!p?.uci || p.uci.length < 4) return undefined;
    return [p.uci.slice(0, 2), p.uci.slice(2, 4)];
  }

  get label(): string {
    const p = this.positions[this.index];
    if (!p) return this.translate.instant('gameAnalysis.startPosition');
    return `${p.moveNumber}${p.white ? '.' : '…'} ${localizeSan(p.san, this.translate.currentLang())}`;
  }

  /** Titel ohne PGN-Platzhalter („? – ?" → „Ohne Titel", v1-rk-analysis-pgn-placeholder-title). */
  title(): string {
    return analysisTitle(this.analysis?.title, {
      untitled: this.translate.instant('gameAnalysis.untitled'), noName: this.translate.instant('gameAnalysis.noName'),
    });
  }

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.load(id);
    this.poll = interval(10000).subscribe(() => {
      if (this.analysis && this.analysis.status !== 'done' && this.analysis.status !== 'failed') this.load(id, true);
    });
  }

  ngOnDestroy(): void { this.poll?.unsubscribe(); this.movesObserver?.disconnect(); }

  @HostListener('window:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!isBoardHotkey(e)) return;
    if (e.key === 'ArrowLeft') { this.go(-1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { this.go(1); e.preventDefault(); }
  }

  select(ply: number): void { this.index = ply; }

  go(delta: number): void {
    const next = this.index + delta;
    if (next >= -1 && next < this.positions.length) this.index = next;
  }

  private load(id: number, silent = false): void {
    if (!silent) this.loading = true;
    this.service.get(id).subscribe({
      next: a => {
        this.analysis = a;
        this.positions = a.positions ?? [];
        this.moveRows = toMoveRows(this.positions);
        setTimeout(() => this.updateMovesMore());
        const last = this.positions[this.positions.length - 1];
        this.endFen = last ? fenAfterUci(last.fen, last.uci) : null;
        this.loading = false;
        this.cdr.markForCheck();
        if (a.targetNodes) this.loadConvergence(id);
      },
      error: () => {
        this.loading = false;
        if (!silent) this.snackbar.warn(this.translate.instant('gameAnalysis.loadFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  /** Fehler hier sind still: die Tabelle ist eine Zugabe, die Partie selbst steht schon da. */
  private loadConvergence(id: number): void {
    this.service.convergence(id).subscribe({
      next: c => { this.convergence = c; this.cdr.markForCheck(); },
      error: () => { this.cdr.markForCheck(); },
    });
  }
}

/** Halbzüge → Vollzug-Zeilen. Beginnt die Partie mit Schwarz am Zug, bleibt die Weiß-Zelle der ersten Zeile leer. */
export function toMoveRows(positions: GameAnalysisPosition[]): { no: number; white?: GameAnalysisPosition; black?: GameAnalysisPosition }[] {
  const rows: { no: number; white?: GameAnalysisPosition; black?: GameAnalysisPosition }[] = [];
  for (const p of positions) {
    let row = rows[rows.length - 1];
    if (!row || row.no !== p.moveNumber || (p.white && row.white) || (!p.white && row.black)) {
      row = { no: p.moveNumber };
      rows.push(row);
    }
    if (p.white) row.white = p; else row.black = p;
  }
  return rows;
}
