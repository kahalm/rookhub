import { Component, OnInit, inject, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { PreferencesService } from '../../core/preferences.service';
import { MatDialog } from '@angular/material/dialog';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';
import { RememberedService, RememberedPosition } from '../../core/remembered.service';
import { ExternalEngineService } from '../analysis/external-engine.service';
import { AnalysisJobDialogComponent } from '../analysis/analysis-job-dialog.component';

/**
 * Zeigt die über die RepCheck-Extension („Remember line" auf chessable.com) gemerkten Stellungen
 * des Users — und die Stellungen der Hintergrund-Analyseaufträge (der Server merkt sie beim Anlegen
 * eines Auftrags mit): je Eintrag Brett-Vorschau (FEN), Kursname/-Link bzw. interner Link zur
 * Auftragsseite, Datum, die Analyse-Info des Auftrags (Status, Tiefe, Bewertung) + Aktionen
 * (In Analyse öffnen · Im Hintergrund analysieren · FEN kopieren · Löschen).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-remembered-lines',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressSpinnerModule, MatCheckboxModule, TranslatePipe, ChessBoardComponent,
  ],
  template: `
    <div class="remembered-page">
      <div class="head">
        <h1>{{ 'remembered.title' | translate }}</h1>
        <p class="hint">{{ 'remembered.hint' | translate }}</p>
      </div>

      @if (!loading && auth.isLoggedIn && selectable.length > 0) {
        <div class="select-bar" [class.active]="selected.size > 0">
          <mat-checkbox [checked]="allSelected" [indeterminate]="selected.size > 0 && !allSelected" (change)="toggleAll($event.checked)">
            {{ 'remembered.selectAll' | translate:{ count: selectable.length } }}
          </mat-checkbox>
          @if (selected.size > 0) {
            <span class="sel-count">{{ 'remembered.selectedCount' | translate:{ count: selected.size } }}</span>
            <button mat-flat-button color="primary" (click)="queueSelected()">
              <mat-icon>schedule</mat-icon> {{ 'remembered.analyzeSelected' | translate }}
            </button>
            <button mat-button (click)="selected.clear()">{{ 'remembered.clearSelection' | translate }}</button>
          } @else {
            <span class="sel-hint">{{ 'remembered.selectHint' | translate }}</span>
          }
        </div>
      }

      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (items.length === 0) {
        <mat-card class="empty">
          <mat-icon>bookmark_border</mat-icon>
          <p>{{ 'remembered.empty' | translate }}</p>
        </mat-card>
      } @else {
        <div class="grid">
          @for (p of items; track p.id) {
            <mat-card class="item" [class.selected]="selected.has(p.id)">
              @if (!p.analysis && auth.isLoggedIn) {
                <mat-checkbox class="pick" [checked]="selected.has(p.id)" (change)="toggleOne(p, $event.checked)"
                              [attr.aria-label]="'remembered.selectForAnalysis' | translate" />
              }
              <div class="board">
                <app-chess-board [fen]="p.fen" [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
              </div>
              <div class="meta">
                <div class="course">
                  @if (p.sourceUrl && p.sourceUrl.startsWith('/')) {
                    <a [routerLink]="p.sourceUrl">{{ labelOf(p) }}</a>
                  } @else if (p.sourceUrl) {
                    <a [href]="p.sourceUrl" target="_blank" rel="noopener">{{ labelOf(p) }}<mat-icon class="ext">open_in_new</mat-icon></a>
                  } @else {
                    <span>{{ labelOf(p) }}</span>
                  }
                </div>
                <div class="date">{{ p.createdAt | date:'medium' }}</div>
                @if (p.analysis; as a) {
                  <a class="analysis" routerLink="/analysis/jobs" [matTooltip]="'remembered.analysisTooltip' | translate">
                    <span class="status" [ngClass]="a.status">{{ ('analysisJobs.status.' + a.status) | translate }}</span>
                    <span>{{ 'analysisJobs.depthOf' | translate:{ reached: a.reachedDepth, target: a.targetDepth } }} · {{ 'analysisJobs.lines' | translate:{ count: a.multiPv } }}</span>
                    @if (a.evalText) { <span class="eval" [class.neg]="a.evalText.startsWith('-') || a.evalText.startsWith('#-')">{{ a.evalText }}</span> }
                  </a>
                }
                <div class="fen" [matTooltip]="p.fen">{{ p.fen }}</div>
              </div>
              <div class="actions">
                <a mat-stroked-button routerLink="/analysis" [queryParams]="{ fen: p.fen }">
                  <mat-icon>science</mat-icon> {{ 'remembered.analyze' | translate }}
                </a>
                @if (!p.analysis && auth.isLoggedIn) {
                  <button mat-icon-button (click)="queueAnalysis(p)" [matTooltip]="'analysis.queueBackground' | translate">
                    <mat-icon>schedule</mat-icon>
                  </button>
                }
                <button mat-icon-button (click)="copyFen(p)" [matTooltip]="'remembered.copyFen' | translate">
                  <mat-icon>content_copy</mat-icon>
                </button>
                <button mat-icon-button color="warn" (click)="remove(p)" [matTooltip]="'remembered.delete' | translate">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .remembered-page { max-width: 1100px; margin: 0 auto; padding: 16px; }
    .head h1 { margin: 0 0 4px; }
    .head .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 16px; }
    .item { padding: 10px; display: flex; flex-direction: column; gap: 8px; position: relative; }
    .item.selected { outline: 2px solid var(--mat-sys-primary, #3f51b5); }
    .pick { position: absolute; top: 4px; right: 4px; z-index: 1; background: color-mix(in srgb, var(--mat-sys-surface, #fff) 80%, transparent); border-radius: 4px; }
    .select-bar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; padding: 6px 10px; margin-bottom: 12px;
      border-radius: 8px; background: color-mix(in srgb, currentColor 6%, transparent); position: sticky; top: 0; z-index: 2; }
    .select-bar.active { background: color-mix(in srgb, var(--mat-sys-primary, #3f51b5) 14%, transparent); }
    .sel-count { font-weight: 600; }
    .sel-hint { font-size: .85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .board { width: 100%; }
    .board app-chess-board { display: block; width: 100%; }
    .meta { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
    .course { font-weight: 500; }
    .course a { display: inline-flex; align-items: center; gap: 3px; color: #1976d2; text-decoration: none; }
    .course a:hover { text-decoration: underline; }
    .course .ext { font-size: 14px; width: 14px; height: 14px; }
    .date { font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .fen { font-family: monospace; font-size: 0.72rem; color: color-mix(in srgb, currentColor 55%, transparent);
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .analysis { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; font-size: 0.8rem; margin-top: 2px;
      color: inherit; text-decoration: none; }
    .analysis:hover { text-decoration: underline; }
    .status { font-size: 0.68rem; font-weight: 700; padding: 1px 7px; border-radius: 999px; text-transform: uppercase;
      background: color-mix(in srgb, currentColor 12%, transparent); }
    .status.running { background: rgba(46,125,50,.18); color: #2e7d32; }
    .status.paused { background: rgba(255,160,0,.18); color: #e65100; }
    .status.done { background: rgba(21,101,192,.15); color: #1565c0; }
    .status.failed { background: rgba(198,40,40,.15); color: #c62828; }
    .eval { font-family: monospace; font-weight: 600; color: #2e7d32; }
    .eval.neg { color: #c62828; }
    .actions { display: flex; align-items: center; gap: 4px; margin-top: auto; }
    .actions a { flex: 1; }
  `]
})
export class RememberedLinesComponent implements OnInit {
  items: RememberedPosition[] = [];
  loading = true;
  private destroyRef = inject(DestroyRef);

  /** Hintergrund-Engine des Users (für den Dialog: ohne sie nur der Hinweis). null = noch nicht geladen. */
  private backgroundEngineId: string | null | undefined;

  constructor(
    private remembered: RememberedService,
    public preferences: PreferencesService,
    private snackbar: SnackbarService,
    private translate: TranslateService,
    public auth: AuthService,
    private dialog: MatDialog,
    private externalEngines: ExternalEngineService,
  ) {}

  /** Mehrfachauswahl (nur Stellungen OHNE Auftrag) für „Analysieren" mit einer Tiefe/Linienzahl für alle. */
  readonly selected = new Set<number>();
  get selectable(): RememberedPosition[] { return this.items.filter(p => !p.analysis); }
  get allSelected(): boolean { return this.selectable.length > 0 && this.selectable.every(p => this.selected.has(p.id)); }
  toggleOne(p: RememberedPosition, on: boolean): void { if (on) this.selected.add(p.id); else this.selected.delete(p.id); }
  toggleAll(on: boolean): void {
    if (on) this.selectable.forEach(p => this.selected.add(p.id)); else this.selected.clear();
  }

  /** Alle ausgewählten Stellungen mit EINER Tiefe/Linienzahl vormerken (Batch-Endpoint). */
  queueSelected(): void {
    const fens = this.items.filter(p => this.selected.has(p.id)).map(p => p.fen);
    if (fens.length === 0) return;
    this.withBackgroundEngine(hasEngine => {
      const ref = this.dialog.open(AnalysisJobDialogComponent, {
        width: '440px', data: { fens, depth: 30, lines: 3, hasBackgroundEngine: hasEngine },
      });
      ref.afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(res => {
        if (!res || !('created' in res)) return;
        const skipped = res.skipped.length;
        this.snackbar.success(this.translate.instant('analysisJobs.createdMany', { count: res.created.length })
          + (skipped ? ' ' + this.translate.instant('analysisJobs.skippedSome', { count: skipped }) : ''));
        this.selected.clear();
        this.load();
      });
    });
  }

  /** Hintergrund-Engine einmal ermitteln (für den Dialog: ohne sie nur der Hinweis). */
  private withBackgroundEngine(run: (hasEngine: boolean) => void): void {
    if (this.backgroundEngineId !== undefined) { run(!!this.backgroundEngineId); return; }
    this.externalEngines.listEngines().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: r => { this.backgroundEngineId = r.backgroundEngineId ?? null; run(!!this.backgroundEngineId); },
      error: () => { this.backgroundEngineId = null; run(false); },
    });
  }

  /** Anzeigename: Kursname, sonst Kurs-ID, sonst bei Auftrags-Stellungen „Analyse-Auftrag", sonst „Unbekannter Kurs". */
  labelOf(p: RememberedPosition): string {
    if (p.courseName) return p.courseName;
    if (p.courseId) return p.courseId;
    return this.translate.instant(p.analysis || p.sourceUrl?.startsWith('/') ? 'remembered.analysisOrigin' : 'remembered.unknownCourse');
  }

  /** „Im Hintergrund analysieren" für eine gemerkte Stellung — derselbe Dialog wie im Analysebrett. */
  queueAnalysis(p: RememberedPosition): void {
    this.withBackgroundEngine(hasEngine => {
      const ref = this.dialog.open(AnalysisJobDialogComponent, {
        width: '440px', data: { fen: p.fen, depth: 30, lines: 3, hasBackgroundEngine: hasEngine },
      });
      ref.afterClosed().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(job => {
        if (!job) return;
        this.snackbar.success(this.translate.instant('analysisJobs.created'));
        this.load();   // Karte zeigt jetzt die Analyse-Info
      });
    });
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.remembered.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: items => {
        this.items = items; this.loading = false;
        for (const id of [...this.selected]) if (!items.some(p => p.id === id && !p.analysis)) this.selected.delete(id);
      },
      error: () => { this.loading = false; this.snackbar.info(this.translate.instant('remembered.errors.load')); },
    });
  }

  async copyFen(p: RememberedPosition): Promise<void> {
    try {
      await navigator.clipboard.writeText(p.fen);
      this.snackbar.copy(this.translate.instant('remembered.copied'));
    } catch {
      this.snackbar.info(this.translate.instant('remembered.copyFailed'));
    }
  }

  remove(p: RememberedPosition): void {
    if (!confirm(this.translate.instant('remembered.deleteConfirm'))) return;
    this.remembered.remove(p.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.items = this.items.filter(x => x.id !== p.id); },
      error: () => this.snackbar.info(this.translate.instant('remembered.errors.delete')),
    });
  }
}
