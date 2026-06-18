import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { Subscription } from 'rxjs';
import { AnalysisBoardComponent } from './analysis-board.component';
import { AnalysisEngineService, AnalysisLine } from './analysis-engine.service';
import { SnackbarService } from '../../core/snackbar.service';

interface LineNode { san: string; fen: string; uci: string; }
interface EngineDisplayLine { evalText: string; san: string; positive: boolean; }

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const LINES_KEY = 'rookhub_analysis_lines';
const ENGINE_KEY = 'rookhub_analysis_engine';
const DEPTH_KEY = 'rookhub_analysis_depth';
const DEPTH_OPTIONS = [12, 16, 18, 20, 22, 26, 30];
const ARROW_BRUSHES = ['green', 'blue', 'yellow', 'red', 'blue'];

@Component({
  selector: 'app-analysis',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatSlideToggleModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatTooltipModule, TranslateModule, AnalysisBoardComponent
  ],
  template: `
    <div class="analysis-page">
      <h1>{{ 'analysis.title' | translate }}</h1>
      <div class="analysis-layout">
        <div class="board-col">
          <div class="eval-bar" [matTooltip]="evalText">
            <div class="eval-white" [style.height.%]="whiteHeight"></div>
          </div>
          <div class="board-wrap">
            <app-analysis-board
              [fen]="boardFen" [orientation]="orientation" [turnColor]="turnColor"
              [dests]="dests" [lastMove]="lastMove" [check]="isCheck" [shapes]="shapes"
              (moveMade)="onMove($event)" />
          </div>
        </div>

        <div class="side-col">
          @if (returnTo) {
            <button mat-stroked-button class="back-btn" (click)="backToPuzzle()">
              <mat-icon>arrow_back</mat-icon> {{ 'analysis.backToPuzzle' | translate }}
            </button>
          }
          @if (engineCrashed) {
            <mat-card style="background:#b71c1c;color:#fff;">
              <mat-card-content style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;">
                <mat-icon>error_outline</mat-icon>
                <span style="flex:1">{{ 'analysis.engineCrashed' | translate }}</span>
                <button mat-stroked-button style="color:#fff;border-color:#fff" (click)="reloadPage()">{{ 'analysis.engineCrashedReload' | translate }}</button>
              </mat-card-content>
            </mat-card>
          }
          <mat-card class="engine-card">
            <mat-card-content>
              <div class="engine-head">
                <mat-slide-toggle [(ngModel)]="engineOn" (change)="onEngineToggle()">{{ 'analysis.engine' | translate }}</mat-slide-toggle>
                <span class="depth" *ngIf="engineOn">{{ 'analysis.depth' | translate }} {{ depth }}/{{ depthSetting }}</span>
                <span class="he-spacer"></span>
                <mat-form-field appearance="outline" class="num-field" subscriptSizing="dynamic">
                  <mat-label>{{ 'analysis.maxDepth' | translate }}</mat-label>
                  <mat-select [(ngModel)]="depthSetting" (selectionChange)="onDepthChange()">
                    @for (d of depthOptions; track d) { <mat-option [value]="d">{{ d }}</mat-option> }
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline" class="num-field" subscriptSizing="dynamic">
                  <mat-label>{{ 'analysis.lines' | translate }}</mat-label>
                  <mat-select [(ngModel)]="linesCount" (selectionChange)="onLinesChange()">
                    @for (n of [1,2,3,4,5]; track n) { <mat-option [value]="n">{{ n }}</mat-option> }
                  </mat-select>
                </mat-form-field>
              </div>
              @if (engineOn) {
                @if (displayLines.length === 0) {
                  <p class="muted">{{ 'analysis.calculating' | translate }}</p>
                } @else {
                  <div class="lines">
                    @for (l of displayLines; track $index) {
                      <div class="line-row">
                        <span class="line-eval" [class.neg]="!l.positive">{{ l.evalText }}</span>
                        <span class="line-san">{{ l.san }}</span>
                      </div>
                    }
                  </div>
                }
              } @else {
                <p class="muted">{{ 'analysis.engineOff' | translate }}</p>
              }
            </mat-card-content>
          </mat-card>

          <mat-card class="moves-card">
            <mat-card-content>
              <div class="controls">
                <button mat-icon-button (click)="goTo(0)" [disabled]="ply === 0" [matTooltip]="'analysis.start' | translate"><mat-icon>first_page</mat-icon></button>
                <button mat-icon-button (click)="prev()" [disabled]="ply === 0" [matTooltip]="'pgnViewer.nav.previous' | translate"><mat-icon>chevron_left</mat-icon></button>
                <button mat-icon-button (click)="next()" [disabled]="ply >= line.length" [matTooltip]="'pgnViewer.nav.next' | translate"><mat-icon>chevron_right</mat-icon></button>
                <button mat-icon-button (click)="goTo(line.length)" [disabled]="ply >= line.length" [matTooltip]="'pgnViewer.nav.last' | translate"><mat-icon>last_page</mat-icon></button>
                <span class="spacer"></span>
                <button mat-icon-button (click)="flip()" [matTooltip]="'analysis.flip' | translate"><mat-icon>cached</mat-icon></button>
                <button mat-icon-button (click)="reset()" [matTooltip]="'analysis.reset' | translate"><mat-icon>restart_alt</mat-icon></button>
              </div>
              @if (line.length === 0) {
                <p class="muted">{{ 'analysis.noMoves' | translate }}</p>
              } @else {
                <div class="movelist">
                  @for (m of line; track $index) {
                    @if ($index % 2 === 0) { <span class="moveno">{{ $index / 2 + 1 }}.</span> }
                    <span class="move" [class.active]="ply === $index + 1" (click)="goTo($index + 1)">{{ m.san }}</span>
                  }
                </div>
              }
            </mat-card-content>
          </mat-card>

          <mat-card class="io-card">
            <mat-card-content>
              <mat-form-field appearance="outline" class="full">
                <mat-label>{{ 'analysis.fen' | translate }}</mat-label>
                <input matInput [(ngModel)]="fenInput" (keyup.enter)="loadFen()">
              </mat-form-field>
              <div class="io-actions">
                <button mat-stroked-button (click)="loadFen()"><mat-icon>input</mat-icon> {{ 'analysis.loadFen' | translate }}</button>
                <button mat-stroked-button (click)="copyFen()"><mat-icon>content_copy</mat-icon> {{ 'analysis.copyFen' | translate }}</button>
              </div>
              <mat-form-field appearance="outline" class="full">
                <mat-label>{{ 'analysis.pgn' | translate }}</mat-label>
                <textarea matInput rows="3" [(ngModel)]="pgnInput"></textarea>
              </mat-form-field>
              <button mat-stroked-button (click)="loadPgn()"><mat-icon>upload</mat-icon> {{ 'analysis.loadPgn' | translate }}</button>
            </mat-card-content>
          </mat-card>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .analysis-page { max-width: 1100px; margin: 16px auto; padding: 0 12px; }
    .analysis-layout { display: flex; gap: 1.25rem; align-items: flex-start; flex-wrap: wrap; }
    .board-col { display: flex; gap: 8px; flex: 0 0 auto; width: min(64vw, 560px); min-width: 280px; }
    .eval-bar { width: 14px; align-self: stretch; background: #3a3a3a; border-radius: 3px; overflow: hidden; position: relative; min-height: 280px; }
    .eval-white { position: absolute; bottom: 0; left: 0; right: 0; background: #f5f5f5; transition: height .3s; }
    .board-wrap { flex: 1; min-width: 260px; }
    .side-col { flex: 1; min-width: 280px; display: flex; flex-direction: column; gap: 12px; }
    .engine-head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .depth { font-size: .8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .he-spacer { flex: 1 1 auto; }
    .num-field { width: 104px; }
    .back-btn { width: 100%; margin-bottom: 8px; }
    .muted { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; margin: 8px 0 0; }
    .lines { display: flex; flex-direction: column; gap: 4px; margin-top: 6px; }
    .line-row { display: flex; gap: 8px; font-size: .9rem; }
    .line-eval { font-weight: 700; min-width: 48px; font-variant-numeric: tabular-nums; color: #1b5e20; }
    .line-eval.neg { color: #b71c1c; }
    .line-san { font-family: 'Courier New', monospace; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .controls { display: flex; align-items: center; gap: 2px; }
    .controls .spacer { flex: 1; }
    .movelist { margin-top: 8px; line-height: 1.9; }
    .moveno { color: color-mix(in srgb, currentColor 40%, transparent); margin: 0 2px 0 8px; font-size: .85rem; }
    .move { cursor: pointer; padding: 1px 5px; border-radius: 4px; font-family: 'Courier New', monospace; }
    .move:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .move.active { background: #1976d2; color: #fff; }
    .io-card .full { width: 100%; }
    .io-actions { display: flex; gap: 8px; margin-bottom: 8px; }
    @media (max-width: 768px) { .board-col { width: 100%; } }
  `]
})
export class AnalysisComponent implements OnInit, OnDestroy {
  private chess = new Chess();
  startFen = START_FEN;
  line: LineNode[] = [];
  ply = 0;

  orientation: Color = 'white';
  boardFen = START_FEN;
  turnColor: Color = 'white';
  dests = new Map<Key, Key[]>();
  lastMove?: [Key, Key];
  isCheck = false;
  shapes: DrawShape[] = [];

  engineOn = true;
  linesCount = 3;
  depth = 0;
  depthSetting = 22;
  readonly depthOptions = DEPTH_OPTIONS;
  returnTo: string | null = null;
  displayLines: EngineDisplayLine[] = [];
  evalText = '0.00';
  whiteHeight = 50;
  engineCrashed = false;

  fenInput = '';
  pgnInput = '';

  private sub?: Subscription;
  private errorSub?: Subscription;

  constructor(private engine: AnalysisEngineService, private route: ActivatedRoute, private snackbar: SnackbarService, private router: Router) {
    try {
      const l = parseInt(localStorage.getItem(LINES_KEY) || '', 10);
      if (l >= 1 && l <= 5) this.linesCount = l;
      this.engineOn = localStorage.getItem(ENGINE_KEY) !== '0';
      const d = parseInt(localStorage.getItem(DEPTH_KEY) || '', 10);
      if (DEPTH_OPTIONS.includes(d)) this.depthSetting = d;
    } catch {}
  }

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const fenParam = params.get('fen');
    if (fenParam && this.isValidFen(fenParam)) {
      this.startFen = fenParam;
    }
    const orientationParam = params.get('orientation');
    if (orientationParam === 'white' || orientationParam === 'black') {
      this.orientation = orientationParam;
    }
    // Herkunft (z.B. das Puzzle) für den Zurück-Button merken.
    const from = params.get('from');
    if (from && from.startsWith('/') && !from.startsWith('//') && !from.includes('://')) {
      this.returnTo = from;
    }
    this.engine.setDepth(this.depthSetting);
    this.engine.setMultiPv(this.linesCount);
    this.sub = this.engine.analysis$.subscribe(s => this.onEngineUpdate(s.fen, s.depth, s.lines));
    this.errorSub = this.engine.engineFatalError$.subscribe(e => this.engineCrashed = e !== null);

    // Optional: eine Zugfolge (UCI, durch Leerzeichen/Komma getrennt) ab startFen vorladen
    // und an die aktuelle (letzte) Stellung springen — genutzt vom „Analysieren"-Button der Puzzles.
    const movesParam = params.get('moves');
    const uci = movesParam ? movesParam.split(/[ ,]+/).filter(Boolean) : [];
    // Eine ganze Partie kann per Router-State übergeben werden (z.B. „In Analyse öffnen"
    // im Bereich „Partien") — zu lang/unhandlich für einen Query-Param.
    const statePgn = (window.history.state && window.history.state.pgn) as string | undefined;
    if (typeof statePgn === 'string' && statePgn.trim()) {
      this.pgnInput = statePgn;
      this.loadPgn();
    } else if (uci.length) {
      this.loadFromUci(this.startFen, uci);
    } else {
      this.resetToStart();
    }
  }

  /** Baut die Hauptlinie aus UCI-Zügen ab `fromFen` und springt ans Ende (aktuelle Stellung). */
  private loadFromUci(fromFen: string, uciMoves: string[]): void {
    let replay: Chess;
    try { replay = new Chess(fromFen); } catch { this.resetToStart(); return; }
    const built: LineNode[] = [];
    for (const u of uciMoves) {
      let mv;
      try { mv = replay.move({ from: u.substring(0, 2), to: u.substring(2, 4), promotion: u.length > 4 ? u[4] : undefined }); }
      catch { break; }
      if (!mv) break;
      built.push({ san: mv.san, fen: replay.fen(), uci: mv.from + mv.to + (mv.promotion ?? '') });
    }
    this.line = built;
    this.ply = built.length;
    this.refresh();
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.errorSub?.unsubscribe();
    this.engine.destroy();
  }

  // ---- Navigation ----
  get currentFen(): string { return this.ply === 0 ? this.startFen : this.line[this.ply - 1].fen; }

  goTo(ply: number): void {
    this.ply = Math.max(0, Math.min(ply, this.line.length));
    this.refresh();
  }
  prev(): void { this.goTo(this.ply - 1); }
  next(): void { this.goTo(this.ply + 1); }

  @HostListener('window:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    const tag = (e.target as HTMLElement)?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); this.prev(); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); this.next(); }
    else if (e.key === 'Home') { e.preventDefault(); this.goTo(0); }
    else if (e.key === 'End') { e.preventDefault(); this.goTo(this.line.length); }
  }

  // ---- User move ----
  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    let c: Chess;
    try { c = new Chess(this.currentFen); } catch { return; }
    const piece = c.get(ev.orig as any);
    const isPromo = piece?.type === 'p' && (ev.dest[1] === '8' || ev.dest[1] === '1');
    // Umwandlungsfigur kommt jetzt aus dem Picker; Dame nur als Fallback.
    const promotion = isPromo ? (ev.promotion ?? 'q') : undefined;
    let mv;
    try {
      mv = c.move({ from: ev.orig, to: ev.dest, promotion });
    } catch { this.refresh(); return; }   // illegaler Zug -> Brett zurücksetzen
    if (!mv) { this.refresh(); return; }

    if (this.ply < this.line.length) this.line = this.line.slice(0, this.ply);   // ab hier neu
    this.line.push({ san: mv.san, fen: c.fen(), uci: mv.from + mv.to + (mv.promotion ?? '') });
    this.ply = this.line.length;
    this.refresh();
  }

  // ---- Refresh board + engine for current ply ----
  private refresh(): void {
    const fen = this.currentFen;
    let c: Chess;
    try { c = new Chess(fen); } catch { return; }
    this.boardFen = fen;
    this.turnColor = c.turn() === 'w' ? 'white' : 'black';
    this.isCheck = c.isCheck();
    this.dests = this.computeDests(c);
    const lm = this.ply > 0 ? this.line[this.ply - 1].uci : undefined;
    this.lastMove = lm ? [lm.substring(0, 2) as Key, lm.substring(2, 4) as Key] : undefined;
    this.shapes = [];
    this.displayLines = [];
    this.depth = 0;
    // Terminale Stellung (Matt/Patt → keine legalen Züge): der Engine kein `go` schicken. Ein
    // Suchlauf ohne legale Züge ist sinnlos und ein vermeidbarer Sonderfall im WASM-Kern.
    if (this.engineOn && this.dests.size > 0) this.engine.analyze(fen);
    else { this.engine.stop(); this.updateEval(null); }
  }

  private computeDests(c: Chess): Map<Key, Key[]> {
    const map = new Map<Key, Key[]>();
    for (const m of c.moves({ verbose: true }) as any[]) {
      const arr = map.get(m.from) || [];
      arr.push(m.to);
      map.set(m.from, arr);
    }
    return map;
  }

  // ---- Engine updates ----
  private onEngineUpdate(fen: string, depth: number, lines: AnalysisLine[]): void {
    if (!this.engineOn || fen !== this.currentFen) return;
    this.depth = depth;
    this.displayLines = lines.map(l => ({
      evalText: l.evalText,
      positive: l.scoreType === 'mate' ? l.score > 0 : l.score >= 0,
      san: this.uciLineToSan(fen, l.pvUci, 12),
    }));
    this.shapes = lines.map((l, i) => {
      const u = l.pvUci[0];
      return u ? { orig: u.substring(0, 2) as Key, dest: u.substring(2, 4) as Key, brush: ARROW_BRUSHES[i] || 'blue' } as DrawShape : null;
    }).filter((s): s is DrawShape => !!s);
    this.updateEval(lines[0] ?? null);
  }

  private updateEval(best: AnalysisLine | null): void {
    if (!best) { this.evalText = '0.00'; this.whiteHeight = 50; return; }
    this.evalText = best.evalText;
    if (best.scoreType === 'mate') {
      this.whiteHeight = best.score > 0 ? 100 : 0;
    } else {
      const cp = best.score;
      this.whiteHeight = Math.max(2, Math.min(98, 50 + 50 * (2 / (1 + Math.exp(-0.004 * cp)) - 1)));
    }
  }

  private uciLineToSan(fromFen: string, uci: string[], maxPlies: number): string {
    let c: Chess;
    try { c = new Chess(fromFen); } catch { return ''; }
    const out: string[] = [];
    let moveNo = Math.floor((c.moveNumber?.() ?? 1));
    let white = c.turn() === 'w';
    for (let i = 0; i < uci.length && i < maxPlies; i++) {
      const u = uci[i];
      let mv;
      try { mv = c.move({ from: u.substring(0, 2), to: u.substring(2, 4), promotion: u.length > 4 ? u[4] : undefined }); }
      catch { break; }
      if (!mv) break;
      if (white) out.push(moveNo + '. ' + mv.san);
      else { if (out.length === 0) out.push(moveNo + '... ' + mv.san); else out.push(mv.san); moveNo++; }
      white = !white;
    }
    return out.join(' ');
  }

  // ---- Controls / IO ----
  onEngineToggle(): void {
    try { localStorage.setItem(ENGINE_KEY, this.engineOn ? '1' : '0'); } catch {}
    this.refresh();
  }
  onLinesChange(): void {
    try { localStorage.setItem(LINES_KEY, String(this.linesCount)); } catch {}
    this.engine.setMultiPv(this.linesCount);
    if (this.engineOn) this.engine.analyze(this.currentFen);
  }
  onDepthChange(): void {
    try { localStorage.setItem(DEPTH_KEY, String(this.depthSetting)); } catch {}
    this.engine.setDepth(this.depthSetting);
    if (this.engineOn) this.engine.analyze(this.currentFen);
  }
  backToPuzzle(): void {
    if (this.returnTo) this.router.navigateByUrl(this.returnTo);
  }
  reloadPage(): void { window.location.reload(); }
  flip(): void { this.orientation = this.orientation === 'white' ? 'black' : 'white'; }

  reset(): void { this.startFen = START_FEN; this.resetToStart(); }
  private resetToStart(): void { this.line = []; this.ply = 0; this.refresh(); }

  loadFen(): void {
    const fen = this.fenInput.trim();
    if (!fen) return;
    if (!this.isValidFen(fen)) { this.snackbar.show('Invalid FEN', { action: 'OK', rawAction: true, duration: 2500 }); return; }
    this.startFen = fen;
    this.fenInput = '';
    this.resetToStart();
  }

  copyFen(): void {
    navigator.clipboard?.writeText(this.currentFen).then(
      () => this.snackbar.show(this.currentFen, { action: 'OK', rawAction: true, duration: 2000 }),
      () => {}
    );
  }

  loadPgn(): void {
    const pgn = this.pgnInput.trim();
    if (!pgn) return;
    const c = new Chess();
    try { c.loadPgn(pgn); } catch { this.snackbar.show('Invalid PGN', { action: 'OK', rawAction: true, duration: 2500 }); return; }
    const history = c.history({ verbose: true }) as any[];
    if (history.length === 0) { this.snackbar.show('Invalid PGN', { action: 'OK', rawAction: true, duration: 2500 }); return; }
    // Hauptlinie ab Standard-Grundstellung nachspielen.
    const replay = new Chess();
    this.startFen = START_FEN;
    this.line = history.map(h => {
      const mv = replay.move({ from: h.from, to: h.to, promotion: h.promotion });
      return { san: mv.san, fen: replay.fen(), uci: mv.from + mv.to + (mv.promotion ?? '') };
    });
    this.pgnInput = '';
    this.ply = this.line.length;
    this.refresh();
  }

  private isValidFen(fen: string): boolean {
    try { new Chess(fen); return true; } catch { return false; }
  }
}
