import { ChangeDetectionStrategy, ChangeDetectorRef, Component, HostListener, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { START_FEN } from '../analysis/position-setup.component';
import { PreferencesService } from '../../core/preferences.service';
import { withSideToMove } from './reconstruct-detail.component';
import { PartKind, ReconstructService, SharedReconstruction, SharedReconstructionPart } from './reconstruct.service';

/**
 * Eine Zeile der geteilten Partie: ein Zug, eine erinnerte Stellung oder eine LÜCKE.
 *
 * <p>Die Lücke ist hier eine Zeile wie jede andere und kein Fehler — eine Rekonstruktion besteht
 * aus Bruchstücken, und wer den Link bekommt, soll genau sehen, wo die Partie abreißt.</p>
 */
export interface SharedRow {
  type: 'gap' | 'position' | 'move';
  /** Stellung, die das Brett bei dieser Zeile zeigt (Lücken-Zeilen haben keine). */
  fen?: string;
  san?: string;
  /** Zugnummer, solange die Kette ab der Grundstellung durchgeht (sonst null). */
  moveNo?: number | null;
  /** Zug von Schwarz? (Entscheidet über „12…" statt „12.".) */
  black?: boolean;
  /** War sich der Besitzer bei dem Teil sicher, aus dem die Zeile stammt? */
  certain: boolean;
  /** Notiz des Teils — steht nur an seiner ERSTEN Zeile. */
  note?: string | null;
  /** Erste Zeile ihres Teils? Dort stehen Notiz und „unsicher". */
  first: boolean;
}

/**
 * Die Teile einer geteilten Rekonstruktion in die Zeilen der Anzeige übersetzen.
 *
 * <p>Bewusst eine reine Funktion ohne Angular: das Nachspielen der Züge (chess.js ab der Stellung
 * vor dem Teil) ist die ganze Logik dieser Seite und soll einzeln prüfbar bleiben. Ein Zug, der
 * sich nicht spielen lässt, bricht das Teil ab — er steht noch in der Liste, aber das Brett bleibt
 * bei der letzten Stellung, die es wirklich gab.</p>
 */
export function buildRows(parts: SharedReconstructionPart[]): SharedRow[] {
  const rows: SharedRow[] = [];
  parts.forEach((part, index) => {
    // Kein Anschluss heißt: dazwischen fehlt etwas. Das erste Teil hat nichts vor sich.
    if (index > 0 && !part.continuesPrevious) rows.push({ type: 'gap', certain: true, first: true });

    if (part.kind === PartKind.Position) {
      rows.push({ type: 'position', fen: part.fen ?? '', certain: part.certain, note: part.note, first: true });
      return;
    }

    // Ohne Anker ist die Stellung davor unbekannt — dann sagt nur der gespeicherte Haken, wer zog.
    const start = part.startFen || withSideToMove(START_FEN, part.blackToMove);
    let chess: Chess | null;
    try { chess = new Chess(start); } catch { chess = null; }

    const tokens = (part.moves ?? '').split(/\s+/).filter(t => t);
    tokens.forEach((san, i) => {
      const ply = part.startPly == null ? null : part.startPly + i;
      const black = chess ? chess.turn() === 'b' : ply !== null && ply % 2 === 1;
      let fen: string | undefined;
      if (chess) {
        try { chess.move(san); fen = chess.fen(); } catch { chess = null; }
      }
      rows.push({
        type: 'move', san, fen,
        moveNo: ply === null ? null : Math.floor(ply / 2) + 1,
        black,
        certain: part.certain,
        note: i === 0 ? part.note : null,
        first: i === 0,
      });
    });
  });
  return rows;
}

/**
 * Die öffentliche Ansicht einer geteilten Rekonstruktion (Route <c>/r/:token</c>, ohne Anmeldung).
 *
 * <p>Sie zeigt die Partie so, wie sie zusammengetragen ist: Züge, erinnerte Stellungen und die
 * Lücken dazwischen. Bewusst kein PGN-Viewer — ein PGN kann eine Stellung ohne den Weg dorthin
 * nicht ausdrücken, und genau daraus besteht eine Rekonstruktion. Wer den Link bekommt, soll die
 * offenen Stellen sehen; das ist der Grund, warum man so einen Link verschickt.</p>
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-shared-reconstruction',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule,
    MatTooltipModule, TranslatePipe, ChessBoardComponent,
  ],
  template: `
    <div class="shared-page">
      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (!game) {
        <mat-card class="empty">
          <mat-icon>link_off</mat-icon>
          <p>{{ 'reconstruct.shared.notFound' | translate }}</p>
        </mat-card>
      } @else {
        <mat-card class="viewer">
          <div class="header">
            <h1>{{ game.title }}</h1>
            @if (game.white || game.black) {
              <span class="players"><strong>{{ game.white || '?' }}</strong> – <strong>{{ game.black || '?' }}</strong></span>
            }
            <span class="meta">
              @if (game.event) { <span>{{ game.event }}</span> }
              @if (game.playedOn) { <span>{{ game.playedOn | date:'mediumDate' }}</span> }
              @if (game.result) { <span class="result">{{ game.result }}</span> }
            </span>
            <div class="chips">
              <span class="chip ok">{{ 'reconstruct.knownPlies' | translate:{ count: game.knownPlies } }}</span>
              @if (game.gaps > 0) {
                <span class="chip warn">{{ 'reconstruct.gaps' | translate:{ count: game.gaps } }}</span>
              }
            </div>
            <p class="muted small">{{ 'reconstruct.shared.intro' | translate }}</p>
          </div>

          <div class="body">
            <div class="board-section">
              <div class="board-wrap">
                <app-chess-board [fen]="boardFen()" [flipped]="flipped"
                                 [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
              </div>
              <div class="nav">
                <button mat-icon-button [disabled]="stepIndex <= 0" (click)="go('start')"
                        [matTooltip]="'reconstruct.plyFirst' | translate"
                        [attr.aria-label]="'reconstruct.plyFirst' | translate">
                  <mat-icon>first_page</mat-icon>
                </button>
                <button mat-icon-button [disabled]="stepIndex <= 0" (click)="go(-1)"
                        [matTooltip]="'reconstruct.plyPrev' | translate"
                        [attr.aria-label]="'reconstruct.plyPrev' | translate">
                  <mat-icon>chevron_left</mat-icon>
                </button>
                <span class="muted ply-count">{{ 'reconstruct.plyPosition' | translate:{ shown: stepIndex + 1, total: steps.length } }}</span>
                <button mat-icon-button [disabled]="stepIndex >= steps.length - 1" (click)="go(1)"
                        [matTooltip]="'reconstruct.plyNext' | translate"
                        [attr.aria-label]="'reconstruct.plyNext' | translate">
                  <mat-icon>chevron_right</mat-icon>
                </button>
                <button mat-icon-button [disabled]="stepIndex >= steps.length - 1" (click)="go('end')"
                        [matTooltip]="'reconstruct.plyLast' | translate"
                        [attr.aria-label]="'reconstruct.plyLast' | translate">
                  <mat-icon>last_page</mat-icon>
                </button>
                <button mat-icon-button (click)="flipped = !flipped"
                        [matTooltip]="'pgnViewer.nav.flip' | translate"
                        [attr.aria-label]="'pgnViewer.nav.flip' | translate">
                  <mat-icon>swap_vert</mat-icon>
                </button>
              </div>
            </div>

            <!-- Die Partie als Folge: Züge, erinnerte Stellungen, Lücken — in dieser Reihenfolge. -->
            <div class="moves-section">
              @if (rows.length === 0) {
                <p class="muted empty-line">{{ 'reconstruct.shared.nothing' | translate }}</p>
              }
              @for (row of rows; track $index) {
                @if (row.type === 'gap') {
                  <div class="gap-row">
                    <mat-icon class="gap-icon">more_horiz</mat-icon>
                    <span class="muted">{{ 'reconstruct.gap.here' | translate }}</span>
                  </div>
                } @else if (row.type === 'position') {
                  <div class="pos-row" [class.current]="isCurrent(row)" (click)="show(row)">
                    <mat-icon>grid_on</mat-icon>
                    <span>{{ 'reconstruct.shared.position' | translate }}</span>
                    @if (!row.certain) {
                      <span class="chip warn">{{ 'reconstruct.unsure' | translate }}</span>
                    }
                  </div>
                  @if (row.note) { <div class="muted note">{{ row.note }}</div> }
                } @else {
                  <span class="move" [class.current]="isCurrent(row)" [class.unsure]="!row.certain"
                        (click)="show(row)">
                    @if (row.moveNo !== null && row.moveNo !== undefined) {
                      <span class="no">{{ row.moveNo }}{{ row.black ? '…' : '.' }}</span>
                    }
                    {{ row.san }}
                  </span>
                  @if (row.first && row.note) { <div class="muted note">{{ row.note }}</div> }
                }
              }
            </div>
          </div>

          @if (game.note) { <p class="head-note">{{ game.note }}</p> }
        </mat-card>
      }
    </div>
  `,
  styles: [`
    .shared-page { max-width: 980px; margin: 0 auto; padding: 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .viewer { padding: 16px; }
    .header { display: flex; flex-direction: column; gap: 4px; margin-bottom: 12px; }
    .header h1 { margin: 0; font-size: 1.3rem; }
    .meta { display: flex; flex-wrap: wrap; gap: 10px; font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .result { color: #1976d2; font-weight: 600; }
    .chips { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 4px; }
    .chip { font-size: 0.75rem; padding: 2px 8px; border-radius: 10px; background: color-mix(in srgb, currentColor 10%, transparent); }
    .chip.ok { background: color-mix(in srgb, #2e7d32 18%, transparent); }
    .chip.warn { background: color-mix(in srgb, #ed6c02 22%, transparent); }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: 0.85rem; }
    .body { display: flex; gap: 16px; align-items: flex-start; }
    .board-section { width: 400px; display: flex; flex-direction: column; align-items: center; gap: 8px; flex-shrink: 0; }
    .board-wrap { width: 400px; }
    .board-wrap app-chess-board { display: block; width: 400px; }
    .nav { display: flex; gap: 4px; align-items: center; }
    .ply-count { font-size: 0.8rem; }
    .moves-section {
      flex: 1; min-width: 200px; max-height: 62vh; overflow: auto; padding: 8px;
      border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px;
      line-height: 2;
    }
    .empty-line { margin: 0; }
    .move { cursor: pointer; padding: 1px 5px; border-radius: 4px; white-space: nowrap; }
    .move:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .move.current { background: color-mix(in srgb, #1976d2 28%, transparent); font-weight: 600; }
    .move.unsure { font-style: italic; opacity: 0.85; }
    .move .no { color: color-mix(in srgb, currentColor 55%, transparent); margin-right: 2px; }
    .gap-row, .pos-row { display: flex; align-items: center; gap: 6px; margin: 6px 0; line-height: 1.4; }
    .pos-row { cursor: pointer; padding: 2px 4px; border-radius: 4px; }
    .pos-row:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .pos-row.current { background: color-mix(in srgb, #1976d2 28%, transparent); }
    .gap-icon { opacity: 0.6; }
    .note { font-size: 0.8rem; line-height: 1.4; margin: 0 0 4px 4px; }
    .head-note { margin: 12px 0 0; white-space: pre-wrap; }
    @media (max-width: 768px) {
      .shared-page { padding: 0; }
      .viewer { padding: 0; border-radius: 0; }
      .header { padding: 12px 16px 0; }
      .body { flex-direction: column; align-items: stretch; padding: 8px; }
      .board-section { width: 100%; }
      .board-wrap { width: 100%; }
      .board-wrap app-chess-board { width: 100%; }
      .nav { justify-content: center; }
      .moves-section { width: auto; max-height: 40vh; }
      .head-note { padding: 0 16px 16px; }
    }
  `],
})
export class SharedReconstructionComponent implements OnInit {
  game: SharedReconstruction | null = null;
  loading = true;
  flipped = false;
  rows: SharedRow[] = [];
  /** Die anspringbaren Zeilen (alles außer den Lücken) — sie tragen eine Stellung. */
  steps: SharedRow[] = [];
  stepIndex = 0;

  private service = inject(ReconstructService);
  private route = inject(ActivatedRoute);
  private cdr = inject(ChangeDetectorRef);
  readonly preferences = inject(PreferencesService);

  ngOnInit(): void {
    const token = this.route.snapshot.paramMap.get('token') || '';
    this.service.getShared(token).subscribe({
      next: game => {
        this.game = game;
        this.rows = buildRows(game.parts);
        this.steps = this.rows.filter(r => r.type !== 'gap' && !!r.fen);
        this.stepIndex = 0;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => { this.loading = false; this.cdr.markForCheck(); },
    });
  }

  /** Die Stellung auf dem Brett — vor dem ersten Schritt die Grundstellung. */
  boardFen(): string {
    return this.steps[this.stepIndex]?.fen || START_FEN;
  }

  isCurrent(row: SharedRow): boolean {
    return this.steps[this.stepIndex] === row;
  }

  /** Eine angeklickte Zeile aufs Brett holen. */
  show(row: SharedRow): void {
    const index = this.steps.indexOf(row);
    if (index >= 0) this.stepIndex = index;
  }

  go(delta: number | 'start' | 'end'): void {
    if (this.steps.length === 0) return;
    if (delta === 'start') { this.stepIndex = 0; return; }
    if (delta === 'end') { this.stepIndex = this.steps.length - 1; return; }
    this.stepIndex = Math.max(0, Math.min(this.stepIndex + delta, this.steps.length - 1));
  }

  @HostListener('window:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'ArrowLeft') { event.preventDefault(); this.go(-1); }
    else if (event.key === 'ArrowRight') { event.preventDefault(); this.go(1); }
  }
}
