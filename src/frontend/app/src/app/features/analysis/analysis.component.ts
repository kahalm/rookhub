import { Component, HostListener, OnDestroy, OnInit, ChangeDetectionStrategy, ChangeDetectorRef, Inject, LOCALE_ID } from '@angular/core';
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
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Chess } from 'chess.js';
import { Color, Key } from 'chessground/types';
import { DrawShape } from 'chessground/draw';
import { Subscription, interval } from 'rxjs';
import { AnalysisBoardComponent } from './analysis-board.component';
import { PositionSetupComponent } from './position-setup.component';
import { AnalysisEngineService, AnalysisLine, RemoteInterruption } from './analysis-engine.service';
import { ExternalEngineService, ExternalEngineInfo } from './external-engine.service';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { SnackbarService } from '../../core/snackbar.service';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { AuthService } from '../../core/auth.service';

interface LineNode { san: string; fen: string; uci: string; }
interface EngineDisplayLine { evalText: string; san: string; positive: boolean; }

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const LINES_KEY = 'rookhub_analysis_lines';
const ENGINE_KEY = 'rookhub_analysis_engine';
const DEPTH_KEY = 'rookhub_analysis_depth';
/** 'wasm' oder die Lichess-Engine-ID der zuletzt gewählten External Engine. */
const PROVIDER_KEY = 'rookhub_analysis_engine_provider';
/** Vergleichsmodus: an/aus und die Wahl der zweiten Engine. */
const COMPARE_KEY = 'rookhub_analysis_compare';
const COMPARE_ENGINE_KEY = 'rookhub_analysis_compare_engine';
// Bis 50: für eine externe Engine (mehrere Millionen Knoten/s) sind Tiefen jenseits von 30
// gut erreichbar. Mit der Browser-Engine dauern sie sehr lange — sie bleiben trotzdem
// wählbar, statt Optionen je nach Engine verschwinden zu lassen.
// ACHTUNG: Jeder Wert hier muss den Clamp in AnalysisEngineService.setDepth überleben,
// sonst wählt man 50 und bekommt stillschweigend weniger (Test hält das fest).
export const DEPTH_OPTIONS = [12, 16, 18, 20, 22, 26, 30, 35, 40, 45, 50];
const ARROW_BRUSHES = ['green', 'blue', 'yellow', 'red', 'blue'];

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-analysis',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatSlideToggleModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatTooltipModule, TranslatePipe, AnalysisBoardComponent, PositionSetupComponent,
    PositionRepertoiresComponent, HelpHintComponent
  ],
  template: `
    <div class="analysis-page">
      <h1>{{ 'analysis.title' | translate }}</h1>
      <div class="analysis-layout">
        <div class="board-col" [class.editing]="editing">
          @if (editing) {
            <app-position-setup class="editor-full"
              [initialFen]="currentFen" [orientation]="orientation"
              (apply)="onSetupApply($event)" (cancel)="editing = false" />
          } @else {
            <div class="eval-bar" [matTooltip]="evalText">
              <div class="eval-white" [style.height.%]="whiteHeight"></div>
            </div>
            <!-- Mobil: schmale, unsichtbare Tap-Zonen — links = Zug zurück (liegt als Overlay ÜBER der
                 Bewertungsleiste, kostet keine Brettbreite), rechts = Zug vor. goTo() clampt selbst. -->
            <div class="board-tap board-tap-prev" (click)="prev()" aria-hidden="true"></div>
            <div class="board-wrap">
              <app-analysis-board
                [fen]="boardFen" [orientation]="orientation" [turnColor]="turnColor"
                [dests]="dests" [lastMove]="lastMove" [check]="isCheck" [shapes]="shapes"
                (moveMade)="onMove($event)" />
            </div>
            <div class="board-tap board-tap-next" (click)="next()" aria-hidden="true"></div>
          }
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
                @if (externalEnginesList.length > 0) {
                  <mat-form-field appearance="outline" class="engine-field" subscriptSizing="dynamic">
                    <mat-label>{{ 'analysis.engineProvider' | translate }}</mat-label>
                    <mat-select [(ngModel)]="selectedEngineId" (selectionChange)="onEngineSelect()">
                      <mat-option value="wasm">{{ 'analysis.engineBrowser' | translate }}</mat-option>
                      @for (e of externalEnginesList; track e.id) { <mat-option [value]="e.id">{{ e.name }}</mat-option> }
                    </mat-select>
                  </mat-form-field>
                }
                @if (engineOn && !terminal) {
                  <app-help-hint icon="info_outline" [text]="speedHint" />
                }
                @if (engineOn && (externalEnginesList.length > 0 || compareOn)) {
                  <button mat-icon-button class="cmp-btn" [class.on]="compareOn"
                          [matTooltip]="'analysis.compareToggle' | translate"
                          (click)="compareOn = !compareOn; onCompareToggle()">
                    <mat-icon>balance</mat-icon>
                  </button>
                }
              </div>
              @if (compareOn && engineOn && externalEnginesList.length > 0) {
                <div class="cmp-pick">
                  <mat-form-field appearance="outline" class="engine-field" subscriptSizing="dynamic">
                    <mat-label>{{ 'analysis.compareWith' | translate }}</mat-label>
                    <mat-select [(ngModel)]="compareEngineId" (selectionChange)="onCompareEngineSelect()">
                      @for (c of engineChoices; track c.id) {
                        <mat-option [value]="c.id" [disabled]="c.id === selectedEngineId">{{ c.name }}</mat-option>
                      }
                    </mat-select>
                  </mat-form-field>
                </div>
              }
              @if (remoteFallback && selectedEngineId !== 'wasm') {
                <p class="remote-fallback"><mat-icon>cloud_off</mat-icon> {{ 'analysis.remoteFallback' | translate }}</p>
              }
              @if (remoteCut && selectedEngineId !== 'wasm') {
                <p class="remote-cut" [class.final]="!remoteCut.resuming">
                  <mat-icon>{{ remoteCut.resuming ? 'sync' : 'link_off' }}</mat-icon>
                  {{ (remoteCut.resuming ? 'analysis.remoteCutResuming' : 'analysis.remoteCutFinal') | translate:{ depth: remoteCut.depth, target: remoteCut.target } }}
                </p>
              }
              @if (showThinking) {
                <p class="thinking">
                  <mat-icon>hourglass_top</mat-icon>
                  <span>{{ 'analysis.thinkingSince' | translate:{ time: thinkingTime, depth: depth + 1 } }}@if (slowConfigHint) { — {{ 'analysis.slowMultiPvHint' | translate }}}</span>
                </p>
              }
              @if (terminal) {
                <p class="terminal-state"><mat-icon>flag</mat-icon> {{ terminalText }}</p>
              } @else if (engineOn) {
                @if (compareRunning) {
                  <p class="eng-label">{{ mainEngineName }} <span class="eng-depth">· {{ 'analysis.depth' | translate }} {{ depth }}</span></p>
                }
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
                @if (compareRunning) {
                  <div class="cmp-block">
                    <p class="eng-label">
                      {{ compareEngineName }} <span class="eng-depth">· {{ 'analysis.depth' | translate }} {{ compareDepth }}</span>
                      <app-help-hint icon="info_outline" [text]="compareSpeedHint" />
                      @if (compareFallback) {
                        <mat-icon class="cmp-warn" [matTooltip]="'analysis.remoteFallback' | translate">cloud_off</mat-icon>
                      }
                    </p>
                    @if (compareCrashed) {
                      <p class="cmp-err"><mat-icon>error_outline</mat-icon> {{ 'analysis.engineCrashed' | translate }}</p>
                    } @else if (compareLines.length === 0) {
                      <p class="muted">{{ 'analysis.calculating' | translate }}</p>
                    } @else {
                      <div class="lines">
                        @for (l of compareLines; track $index) {
                          <div class="line-row">
                            <span class="line-eval" [class.neg]="!l.positive">{{ l.evalText }}</span>
                            <span class="line-san">{{ l.san }}</span>
                          </div>
                        }
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

          <!-- „Stellung in meinen Repertoires" gibt es nur eingeloggt; ohne dieses @if stand hier
               für anonyme Besucher eine leere graue Karte zwischen Zug- und FEN-Karte. -->
          @if (auth.isLoggedIn) {
            <mat-card class="reps-card">
              <mat-card-content>
                <app-position-repertoires [fen]="currentFen" (playMoves)="playRepertoireMoves($event)" />
              </mat-card-content>
            </mat-card>
          }

          <mat-card class="io-card">
            <mat-card-content>
              <mat-form-field appearance="outline" class="full">
                <mat-label>{{ 'analysis.fen' | translate }}</mat-label>
                <input matInput [(ngModel)]="fenInput" (keyup.enter)="loadFen()">
              </mat-form-field>
              <div class="io-actions">
                <button mat-stroked-button (click)="loadFen()"><mat-icon>input</mat-icon> {{ 'analysis.loadFen' | translate }}</button>
                <button mat-stroked-button (click)="copyFen()"><mat-icon>content_copy</mat-icon> {{ 'analysis.copyFen' | translate }}</button>
                <button mat-stroked-button (click)="startEditing()"><mat-icon>grid_view</mat-icon> {{ 'analysis.setup.button' | translate }}</button>
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
    /* App-Vollbild (Host-Klasse auf app-root): Seitentitel weg — das Brett bekommt den Platz. */
    :host-context(.app-fullscreen) h1 { display: none; }
    .analysis-page { max-width: 1100px; margin: 16px auto; padding: 0 12px; }
    .analysis-layout { display: flex; gap: 1.25rem; align-items: flex-start; flex-wrap: wrap; }
    .board-col { display: flex; gap: 8px; flex: 0 0 auto; width: min(64vw, 560px); min-width: 280px; }
    .board-col.editing { display: block; }
    .editor-full { display: block; width: 100%; }
    .eval-bar { width: 14px; align-self: stretch; background: #3a3a3a; border-radius: 3px; overflow: hidden; position: relative; min-height: 280px; }
    .eval-white { position: absolute; bottom: 0; left: 0; right: 0; background: #f5f5f5; transition: height .3s; }
    .board-wrap { flex: 1; min-width: 260px; }
    .side-col { flex: 1; min-width: 280px; display: flex; flex-direction: column; gap: 12px; }
    .engine-head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .depth { font-size: .8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .he-spacer { flex: 1 1 auto; }
    .num-field { width: 104px; }
    .engine-field { width: 190px; }
    .remote-fallback { display: flex; align-items: center; gap: 6px; color: #ffb74d; font-size: .85rem; margin: 6px 0 0; }
    .cmp-btn { width: 34px; height: 34px; line-height: 34px; opacity: .55; }
    .cmp-btn.on { opacity: 1; color: #64b5f6; }
    .cmp-pick { margin-top: 8px; }
    .cmp-block { margin-top: 10px; padding-top: 8px; border-top: 1px solid color-mix(in srgb, currentColor 18%, transparent); }
    .eng-label { display: flex; align-items: center; gap: 6px; font-size: .8rem; font-weight: 600; margin: 6px 0 2px;
      color: color-mix(in srgb, currentColor 75%, transparent); }
    .cmp-err { display: flex; align-items: center; gap: 6px; color: #ef9a9a; font-size: .85rem; margin: 6px 0 0; }
    .cmp-err mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .cmp-warn { font-size: 16px; width: 16px; height: 16px; color: #ffb74d; }
    .eng-depth { font-weight: 400; color: color-mix(in srgb, currentColor 55%, transparent); }
    .terminal-state { display: flex; align-items: center; gap: 6px; font-weight: 600; margin: 8px 0 0; }
    .terminal-state mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .remote-fallback mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .remote-cut, .thinking { display: flex; align-items: center; gap: 6px; font-size: .85rem; margin: 6px 0 0;
      color: color-mix(in srgb, currentColor 65%, transparent); }
    .remote-cut.final { color: #e65100; }
    .remote-cut mat-icon, .thinking mat-icon { font-size: 18px; width: 18px; height: 18px; }
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
    .board-tap { display: none; }
    @media (max-width: 768px) {
      .board-col { width: 100%; min-width: 0; position: relative; }
      .board-wrap { min-width: 0; }
      /* Schmale (halb so breite), unsichtbare Tap-Streifen — kein Button-Look. */
      .board-tap { display: block; flex: 0 0 auto; width: 15px; align-self: stretch; border-radius: 6px;
        cursor: pointer; touch-action: manipulation; -webkit-tap-highlight-color: transparent; }
      .board-tap:active { background: color-mix(in srgb, currentColor 12%, transparent); }
      /* „Zurück" liegt als Overlay ÜBER der Bewertungsleiste (links), statt eine eigene Spalte zu
         belegen → das Brett bekommt die gesparte Breite. */
      .board-tap-prev { position: absolute; left: 0; top: 0; bottom: 0; width: 15px; z-index: 2; }
    }
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
  editing = false;

  /** External Engines des Lichess-Kontos (leer = kein Picker); Auswahl 'wasm' = Browser. */
  externalEnginesList: ExternalEngineInfo[] = [];
  selectedEngineId = 'wasm';
  remoteFallback = false;
  /** Abriss der Remote-Suche vor der Zieltiefe (Hinweis in der Karte; null = keiner). */
  remoteCut: RemoteInterruption | null = null;
  private cutSub?: Subscription;
  /** Lebenszeichen: läuft die Suche, und wie viele Sekunden kam keine neue Engine-Zeile? (nur Anzeige) */
  running = false;
  sinceUpdateSec = 0;
  private lastUpdateAt = Date.now();
  private tickSub?: Subscription;
  /** Suchleistung der laufenden Analyse (0 = noch kein Messwert). */
  nodes = 0;
  nps = 0;
  /** Partie-Ende in der aktuellen Stellung (keine legalen Züge) — dort rechnet keine Engine. */
  terminal: 'mate-white-wins' | 'mate-black-wins' | 'stalemate' | null = null;

  // ---- Vergleichsmodus: eine ZWEITE Engine rechnet dieselbe Stellung ----
  // Möglich, weil AnalysisEngineService keine DI-Abhängigkeiten hat und sich schlicht ein
  // zweites Mal instanziieren lässt — jede Instanz hat eigenen Worker, eigenen Zustand und
  // eigene Generationszählung, die beiden Suchen kommen sich also nicht ins Gehege.
  compareOn = false;
  /** 'wasm' oder Engine-ID der Vergleichs-Engine. */
  compareEngineId = 'wasm';
  compareLines: EngineDisplayLine[] = [];
  compareDepth = 0;
  compareNps = 0;
  /** True, wenn die VERGLEICHS-Engine auf die Browser-Engine zurückgefallen ist. Ohne diese
   *  Anzeige verglichen zwei Etiketten („RookHub PC") etwas, das in Wahrheit die Browser-Engine
   *  gerechnet hat — ein Vergleich, der genau das Gegenteil von dem zeigt, was draufsteht. */
  compareFallback = false;
  /** Die zweite Instanz hat aufgegeben (Worker-Absturz/Start gescheitert). Ohne diese Anzeige
   *  stünde dort für immer „Berechne…", obwohl nichts mehr rechnet. */
  compareCrashed = false;
  private compareEngine?: AnalysisEngineService;
  private compareSub?: Subscription;
  private compareFallbackSub?: Subscription;
  private compareErrorSub?: Subscription;

  private sub?: Subscription;
  private errorSub?: Subscription;
  private fallbackSub?: Subscription;
  private enginesSub?: Subscription;

  constructor(private engine: AnalysisEngineService, private route: ActivatedRoute, private snackbar: SnackbarService,
              private router: Router, public auth: AuthService, private externalEngines: ExternalEngineService,
              private cdr: ChangeDetectorRef, private translate: TranslateService,
              @Inject(LOCALE_ID) private locale: string) {
    try {
      const l = parseInt(localStorage.getItem(LINES_KEY) || '', 10);
      if (l >= 1 && l <= 5) this.linesCount = l;
      this.engineOn = localStorage.getItem(ENGINE_KEY) !== '0';
      const d = parseInt(localStorage.getItem(DEPTH_KEY) || '', 10);
      if (DEPTH_OPTIONS.includes(d)) this.depthSetting = d;
      this.compareOn = localStorage.getItem(COMPARE_KEY) === '1';
      this.compareEngineId = localStorage.getItem(COMPARE_ENGINE_KEY) || 'wasm';
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
    this.sub = this.engine.analysis$.subscribe(s => {
      this.running = s.running;
      this.lastUpdateAt = Date.now();
      this.onEngineUpdate(s.fen, s.depth, s.lines, s.nodes, s.nps);
    });
    this.errorSub = this.engine.engineFatalError$.subscribe(e => { this.engineCrashed = e !== null; this.cdr.markForCheck(); });
    this.fallbackSub = this.engine.remoteFallback$.subscribe(f => { this.remoteFallback = f; this.cdr.markForCheck(); });
    this.cutSub = this.engine.remoteInterrupted$.subscribe(c => { this.remoteCut = c; this.cdr.markForCheck(); });
    // Sekundentakt nur für „rechnet seit …": bei MultiPV 5 vergehen ab Tiefe ~27 Minuten ohne neue
    // Zeile — ohne sichtbare Uhr sieht das aus wie ein Hänger. markForCheck nur bei Wertänderung.
    this.tickSub = interval(1000).subscribe(() => {
      const v = this.running ? Math.floor((Date.now() - this.lastUpdateAt) / 1000) : 0;
      if (v !== this.sinceUpdateSec) { this.sinceUpdateSec = v; this.cdr.markForCheck(); }
    });

    // External Engines des Lichess-Kontos laden (nur eingeloggt; stiller Hintergrund-Feed —
    // ohne Liste bleibt es einfach beim Browser-WASM). War zuletzt eine External Engine gewählt
    // und existiert sie noch, wird sie wieder aktiv; die evtl. schon laufende WASM-Analyse der
    // Startstellung wechselt dann auf die Remote-Suche.
    if (this.auth.isLoggedIn) {
      // Subscription festhalten: der Engine-Service ist ein App-weites Singleton. Verlässt der
      // Nutzer die Seite, WÄHREND die Liste noch unterwegs ist, würde die Antwort danach eine
      // Engine im längst zerstörten Zustand scharf schalten und eine Analyse starten, die
      // niemand mehr sieht.
      this.enginesSub = this.externalEngines.listEngines().subscribe({
        next: r => {
          this.externalEnginesList = r.engines;
          let stored: string | null = null;
          try { stored = localStorage.getItem(PROVIDER_KEY); } catch {}
          if (stored && stored !== 'wasm' && r.engines.some(e => e.id === stored)) {
            this.selectedEngineId = stored;
            this.applyEngineSelection();
          }
          // NICHT unbedingt: applyEngineSelection() oben startet den Vergleich bereits selbst,
          // wenn Haupt- und Vergleichswahl kollidieren. Ohne diese Bedingung wuerde die eben
          // gebaute Instanz Millisekunden spaeter wieder zerstoert und neu aufgebaut — im
          // Browser-Fall eine 7-MB-WASM-Instanziierung fuer nichts.
          if (this.compareOn && !this.compareEngine) this.startCompare();
          this.cdr.markForCheck();   // sonst erscheint der Picker erst beim nächsten DOM-Event
        },
        error: () => {},
      });
    }

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
    this.fallbackSub?.unsubscribe();
    this.cutSub?.unsubscribe();
    this.tickSub?.unsubscribe();
    this.enginesSub?.unsubscribe();
    this.stopCompare();          // eigene Instanz + deren Worker/Streams beenden
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

  /** Baummodus des Repertoire-Panels: die geklickte Zugfolge ab der aktuellen Stellung aufs Brett
   * spielen (wie selbst gezogen — ab hier wird die bisherige Fortsetzung ersetzt). Illegale/unbekannte
   * SAN brechen still ab, statt die Linie halb zu zerschießen. */
  playRepertoireMoves(sans: string[]): void {
    if (!sans?.length) return;
    let c: Chess;
    try { c = new Chess(this.currentFen); } catch { return; }
    const added: LineNode[] = [];
    for (const san of sans) {
      let mv;
      try { mv = c.move(san); } catch { break; }
      if (!mv) break;
      added.push({ san: mv.san, fen: c.fen(), uci: mv.from + mv.to + (mv.promotion ?? '') });
    }
    if (!added.length) return;
    if (this.ply < this.line.length) this.line = this.line.slice(0, this.ply);   // ab hier neu
    this.line = this.line.concat(added);
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
    this.nodes = 0;
    this.nps = 0;
    // Terminale Stellung (Matt/Patt → keine legalen Züge): der Engine kein `go` schicken. Ein
    // Suchlauf ohne legale Züge ist sinnlos und ein vermeidbarer Sonderfall im WASM-Kern.
    // Das Ergebnis MUSS dann aber benannt werden: sonst stünde dort dauerhaft „Berechne…",
    // obwohl nichts mehr gerechnet wird und auch nichts mehr zu rechnen ist.
    this.terminal = this.dests.size > 0 ? null : this.terminalStateOf(c);
    this.compareLines = [];
    this.compareDepth = 0;
    this.compareNps = 0;
    // MUSS mit zurueck: sonst klebt die Absturzmeldung an einer Stellung, in der die Engine
    // noch gar nicht gerechnet hat. Der Service setzt bei neuer FEN selbst crashStreak
    // zurueck, die Suche kann also problemlos gelingen — die Karte behauptete trotzdem weiter,
    // die Engine sei abgestuerzt, statt „Berechne…" zu zeigen.
    this.compareCrashed = false;
    if (this.engineOn && this.dests.size > 0) {
      this.runAnalysis(this.engine, fen);
      this.runAnalysis(this.compareEngine, fen);
    } else {
      this.engine.stop();
      this.compareEngine?.stop();
      this.updateEval(null);
    }
  }

  /** Matt oder Patt? Nur aufrufen, wenn es keine legalen Züge gibt. Wirft nicht: bei einer
   *  illegalen Stellung (Buch-Diagramme ohne König) liefert chess.js keinen Zustand — dann
   *  lieber gar keine Aussage als eine falsche. */
  private terminalStateOf(c: Chess): 'mate-white-wins' | 'mate-black-wins' | 'stalemate' | null {
    try {
      if (c.isStalemate()) return 'stalemate';
      // Matt heißt: die Seite AM ZUG hat verloren.
      if (c.isCheckmate()) return c.turn() === 'w' ? 'mate-black-wins' : 'mate-white-wins';
    } catch { /* illegale Stellung */ }
    return null;
  }

  /** Übersetzter Satz für das Partie-Ende (leer, wenn die Stellung nicht terminal ist). */
  get terminalText(): string {
    switch (this.terminal) {
      case 'mate-white-wins': return this.translate.instant('analysis.mateWhiteWins');
      case 'mate-black-wins': return this.translate.instant('analysis.mateBlackWins');
      case 'stalemate': return this.translate.instant('analysis.stalemate');
      default: return '';
    }
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
  private onEngineUpdate(fen: string, depth: number, lines: AnalysisLine[], nodes = 0, nps = 0): void {
    if (!this.engineOn || fen !== this.currentFen) return;
    this.nodes = nodes;
    this.nps = nps;
    // Angular 22 refresht eine unmarkierte View nach async/HTTP NICHT mehr von selbst (siehe
    // CLAUDE.md-Konvention). Beim WASM-Pfad kaschieren Worker-/Event-Ticks das noch; bei der
    // externen Engine kommen die Zeilen NUR aus einem HTTP-Stream — ohne diese Marke bliebe die
    // Linienliste stehen, obwohl der Zustand längst stimmt.
    this.cdr.markForCheck();
    this.depth = depth;
    this.displayLines = this.toDisplayLines(fen, lines);
    this.shapes = lines.map((l, i) => {
      const u = l.pvUci[0];
      return u ? { orig: u.substring(0, 2) as Key, dest: u.substring(2, 4) as Key, brush: ARROW_BRUSHES[i] || 'blue' } as DrawShape : null;
    }).filter((s): s is DrawShape => !!s);
    this.updateEval(lines[0] ?? null);
  }

  /** Engine-Linien in Anzeigezeilen. Beide Engine-Seiten MUESSEN hier durch: eine
   *  Nebeneinander-Ansicht, die dieselbe Bewertung links anders einfaerbt als rechts, waere
   *  schlimmer als gar kein Vergleich. Frueher lag die Abbildung zweimal im Code, inklusive der
   *  feinen Unterscheidung `score > 0` (Matt) gegen `score >= 0` (Zentibauern). */
  private toDisplayLines(fen: string, lines: AnalysisLine[]): EngineDisplayLine[] {
    return lines.map(l => ({
      evalText: l.evalText,
      positive: l.scoreType === 'mate' ? l.score > 0 : l.score >= 0,
      san: this.uciLineToSan(fen, l.pvUci, 12),
    }));
  }

  /** Gemeinsame Tempo-Formatierung. `nodes === null` = Kurzform (Vergleichs-Engine). */
  private speedHintFor(nps: number, nodes: number | null): string {
    if (nps <= 0) return this.translate.instant('analysis.speedWaiting');
    const speed = this.formatNps(nps);
    return nodes === null
      ? this.translate.instant('analysis.speedShort', { speed })
      : this.translate.instant('analysis.speedHint', { speed, nodes: this.formatCount(nodes) });
  }

  /** Text hinter dem (i): Rechengeschwindigkeit der laufenden Analyse.
   *  ACHTUNG: template-gebundener Getter — er läuft MITTEN in der Change-Detection und darf
   *  deshalb unter keinen Umständen werfen (ein Wurf hier ließe die halbe Karte unrendert,
   *  siehe CLAUDE.md-Konvention). Daher `toLocaleString` (Browser-Intl, fällt bei unbekannter
   *  Sprache selbst zurück) statt Angulars formatNumber, das bei nicht registrierten
   *  Locale-Daten NG0701 wirft — und zusätzlich ein try/catch. */
  get speedHint(): string { return this.speedHintFor(this.nps, this.nodes); }

  /** Lebenszeichen der Remote-Suche — erst ab 5 s ohne neue Zeile, damit es im Normalbetrieb nicht flackert. */
  get showThinking(): boolean {
    return this.engineOn && !this.terminal && this.running && this.selectedEngineId !== 'wasm' && this.sinceUpdateSec >= 5;
  }
  get thinkingTime(): string {
    const m = Math.floor(this.sinceUpdateSec / 60), sec = this.sinceUpdateSec % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  }
  /** Erwartung setzen: 4+ Linien × Tiefe ≥ 27 braucht auf einem PC je Iteration Minuten. */
  get slowConfigHint(): boolean { return this.linesCount >= 4 && this.depthSetting >= 27; }

  /** 8234567 → „8,2 MN/s" (Tausender/Millionen wie in Schach-Oberflächen üblich). */
  private formatNps(nps: number): string {
    if (nps >= 1000000) return this.formatCount(nps / 1000000, 1) + ' MN/s';
    if (nps >= 1000) return this.formatCount(nps / 1000) + ' kN/s';
    return this.formatCount(nps) + ' N/s';
  }

  private formatCount(value: number, digits = 0): string {
    try {
      return value.toLocaleString(this.locale, { minimumFractionDigits: digits, maximumFractionDigits: digits });
    } catch {
      return value.toFixed(digits);
    }
  }

  private updateEval(best: AnalysisLine | null): void {
    if (!best) {
      // Partie-Ende: die Leiste zeigt das ERGEBNIS, nicht eine ausgeglichene Stellung.
      switch (this.terminal) {
        case 'mate-white-wins': this.evalText = '1-0'; this.whiteHeight = 100; return;
        case 'mate-black-wins': this.evalText = '0-1'; this.whiteHeight = 0; return;
        case 'stalemate': this.evalText = '½-½'; this.whiteHeight = 50; return;
      }
      this.evalText = '0.00'; this.whiteHeight = 50; return;
    }
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
    this.compareEngine?.setMultiPv(this.linesCount);
    this.restartSearches();
  }
  onDepthChange(): void {
    try { localStorage.setItem(DEPTH_KEY, String(this.depthSetting)); } catch {}
    this.engine.setDepth(this.depthSetting);
    this.compareEngine?.setDepth(this.depthSetting);
    this.restartSearches();
  }
  /** Vergleich ein/aus. Aus = zweite Instanz vollständig abräumen (Worker/Streams beenden). */
  onCompareToggle(): void {
    try { localStorage.setItem(COMPARE_KEY, this.compareOn ? '1' : '0'); } catch {}
    if (this.compareOn) this.startCompare();
    else this.stopCompare();
  }

  /** Läuft der Vergleich WIRKLICH (Schalter an UND zweite Instanz vorhanden)? Das Template
   *  hängt daran statt an `compareOn` — sonst stünde ein Vergleichsblock da, hinter dem gar
   *  keine Engine steckt (etwa abgemeldet, Engine-Liste nicht ladbar) und der ewig „Berechne…"
   *  zeigt. */
  get compareRunning(): boolean { return this.compareOn && !!this.compareEngine; }

  /** Startet beide Suchen neu — mit DEMSELBEN Vorbehalt wie refresh(): in einer terminalen
   *  Stellung (Matt/Patt) bekommt keine Engine ein `go`. Ohne diesen gemeinsamen Weg setzten
   *  Tiefen-/Linienwechsel den Matt-Fall wieder außer Kraft. */
  private restartSearches(): void {
    if (!this.engineOn || this.dests.size === 0) return;
    this.runAnalysis(this.engine, this.currentFen);
    this.runAnalysis(this.compareEngine, this.currentFen);
  }

  /** analyze() lehnt ab, wenn init() scheitert oder die Engine waehrend des Handshakes zerstoert
   *  wird. Gemeldet ist das dann bereits ueber engineFatalError$ + reportEngineEvent — hier nur
   *  noch schlucken, damit daraus kein „Uncaught (in promise)" in der Konsole wird. */
  private runAnalysis(engine: AnalysisEngineService | undefined, fen: string): void {
    engine?.analyze(fen).catch(() => {});
  }

  /** Sorgt dafür, dass die Vergleichs-Engine eine ANDERE ist als die Haupt-Engine, und merkt
   *  sich die Korrektur. Muss an EINER Stelle passieren, die jeder Weg durchläuft — sonst
   *  entsteht die Selbstvergleichs-Kombination über den Haupt-Picker oder nach einem Neuladen
   *  doch wieder (zwei Instanzen rechnen dann dasselbe, im Browser-Fall zweimal 7 MB WASM). */
  private ensureDistinctCompareEngine(): void {
    if (this.compareEngineId !== this.selectedEngineId) return;
    const other = this.engineChoices.find(c => c.id !== this.selectedEngineId);
    if (!other) return;
    this.compareEngineId = other.id;
    try { localStorage.setItem(COMPARE_ENGINE_KEY, this.compareEngineId); } catch {}
  }

  onCompareEngineSelect(): void {
    try { localStorage.setItem(COMPARE_ENGINE_KEY, this.compareEngineId); } catch {}
    this.startCompare();
  }

  /** Alle wählbaren Engines (Browser + registrierte externe) — für beide Auswahlfelder.
   *  ACHTUNG: template-gebundener Getter in einer Default-Change-Detection-Component, die unter
   *  einer externen Engine viele Male pro Sekunde markiert wird. Unmemoisiert baute er bei JEDEM
   *  Durchlauf ein frisches Array frischer Objekte und schlug `analysis.engineBrowser` neu nach —
   *  und das mehrfach je Durchlauf, weil beide Namens-Getter ihn ebenfalls aufrufen. Der Cache
   *  haelt bewusst die Sprache mit fest, sonst bliebe die Beschriftung nach einem Sprachwechsel
   *  auf der alten stehen. */
  private choicesCache?: { lang: string | null; list: ExternalEngineInfo[]; value: { id: string; name: string }[] };
  get engineChoices(): { id: string; name: string }[] {
    const lang = this.translate.currentLang();   // ngx-translate 18: Signal, kein String
    const c = this.choicesCache;
    if (c && c.lang === lang && c.list === this.externalEnginesList) return c.value;
    const value = [
      { id: 'wasm', name: this.translate.instant('analysis.engineBrowser') },
      ...this.externalEnginesList.map(e => ({ id: e.id, name: e.name })),
    ];
    this.choicesCache = { lang, list: this.externalEnginesList, value };
    return value;
  }

  /** Anzeigename der Vergleichs-Engine. Ist sie zurückgefallen, wird die TATSÄCHLICH rechnende
   *  Engine genannt — ein Vergleich mit falschem Etikett wäre schlimmer als gar keiner. */
  get compareEngineName(): string {
    if (this.compareFallback) return this.translate.instant('analysis.engineBrowser');
    return this.engineChoices.find(c => c.id === this.compareEngineId)?.name ?? '';
  }
  /** Anzeigename der Haupt-Engine — im Vergleichsmodus muss beschriftet sein, welche welche ist. */
  get mainEngineName(): string {
    if (this.remoteFallback && this.selectedEngineId !== 'wasm') return this.translate.instant('analysis.engineBrowser');
    return this.engineChoices.find(c => c.id === this.selectedEngineId)?.name ?? '';
  }

  /** Erzeugung der Vergleichs-Engine als Seam (in Tests ueberschreibbar) — analog zu
   *  createWorker() im Service. Ohne ihn lief in den Compare-Specs der ECHTE Service: auf dem
   *  Remote-Pfad in einen TypeError (der analyse-Spy liefert kein Observable), auf dem
   *  WASM-Pfad in einen echten 7-MB-Worker im Karma-Browser. Die Specs pruefte damit
   *  Vergleichszustand, den nie jemand angetrieben hatte. */
  protected createCompareEngine(): AnalysisEngineService { return new AnalysisEngineService(); }

  /** Baut die zweite Engine-Instanz auf (bzw. richtet sie neu aus) und startet ihre Suche. */
  private startCompare(): void {
    this.stopCompare();
    if (!this.compareOn) return;
    // Gespeicherte Wahl kann veraltet sein (Engine abgemeldet, umbenannt): unbekannte ID auf
    // „Browser" zurücksetzen, statt sie stumm als null durchzureichen — das ergäbe eine
    // Browser-Suche unter leerem Etikett.
    if (this.compareEngineId !== 'wasm' && !this.externalEnginesList.some(e => e.id === this.compareEngineId)) {
      this.compareEngineId = 'wasm';
    }
    this.ensureDistinctCompareEngine();
    // Blieb nur EINE Engine uebrig (keine externe registriert, Token abgelaufen, Liste leer),
    // konnte ensureDistinctCompareEngine() nichts ausweichen lassen. Dann verglichen sich zwei
    // Instanzen derselben Engine — im Browser-Fall zwei 7-MB-WASM-Kerne, die sich denselben
    // Prozessorkern teilen und sich gegenseitig die Rechenleistung halbieren, fuer zwei
    // garantiert identische Linienlisten. Lieber ehrlich abschalten als das anzubieten.
    if (this.compareEngineId === this.selectedEngineId) {
      this.compareOn = false;
      try { localStorage.setItem(COMPARE_KEY, '0'); } catch {}
      this.cdr.markForCheck();
      return;
    }
    const engine = this.createCompareEngine();
    // Die DI-Instanz bekommt ihren Telemetrie-Hook in app.component; diese hier wird von Hand
    // gebaut und haette gar keinen. Ausgerechnet der Vergleichsmodus verdoppelt aber den
    // WASM-Speicherdruck und ist damit die wahrscheinlichste Absturzquelle — seine Crashes
    // duerfen nicht die einzigen sein, die nirgends auftauchen.
    engine.reportEngineEvent = (kind, detail) => this.engine.reportEngineEvent?.('compare_' + kind, detail);
    engine.setDepth(this.depthSetting);
    engine.setMultiPv(this.linesCount);
    const info = this.externalEnginesList.find(e => e.id === this.compareEngineId) ?? null;
    engine.setRemoteEngine(info, (id, work) => this.externalEngines.analyse(id, work));
    this.compareSub = engine.analysis$.subscribe(st => this.onCompareUpdate(st.fen, st.depth, st.lines, st.nps));
    this.compareFallbackSub = engine.remoteFallback$.subscribe(f => { this.compareFallback = f; this.cdr.markForCheck(); });
    this.compareErrorSub = engine.engineFatalError$.subscribe(e => { this.compareCrashed = e !== null; this.cdr.markForCheck(); });
    this.compareEngine = engine;
    if (this.engineOn && this.dests.size > 0) this.runAnalysis(engine, this.currentFen);
  }

  private stopCompare(): void {
    this.compareSub?.unsubscribe();
    this.compareSub = undefined;
    this.compareFallbackSub?.unsubscribe();
    this.compareFallbackSub = undefined;
    this.compareErrorSub?.unsubscribe();
    this.compareErrorSub = undefined;
    this.compareFallback = false;
    this.compareCrashed = false;
    this.compareEngine?.destroy();
    this.compareEngine = undefined;
    this.compareLines = [];
    this.compareDepth = 0;
    this.compareNps = 0;
  }

  private onCompareUpdate(fen: string, depth: number, lines: AnalysisLine[], nps: number): void {
    if (fen !== this.currentFen) return;   // Antwort einer bereits verlassenen Stellung
    this.compareDepth = depth;
    this.compareNps = nps;
    this.compareLines = this.toDisplayLines(fen, lines);
    this.cdr.markForCheck();
  }

  /** Tempo der Vergleichs-Engine für deren (i) — gleiche Formatierung wie bei der Haupt-Engine. */
  get compareSpeedHint(): string { return this.speedHintFor(this.compareNps, null); }

  onEngineSelect(): void {
    try { localStorage.setItem(PROVIDER_KEY, this.selectedEngineId); } catch {}
    this.applyEngineSelection();
  }
  /** Verdrahtet die aktuelle Auswahl in den Engine-Service (Transport = HTTP-Proxy) und startet neu. */
  private applyEngineSelection(): void {
    const info = this.externalEnginesList.find(e => e.id === this.selectedEngineId) ?? null;
    this.engine.setRemoteEngine(info, (id, work) => this.externalEngines.analyse(id, work));
    // Wandert die Haupt-Engine auf die, die gerade als Vergleich läuft, muss die Vergleichs-
    // seite ausweichen — sonst rechnen beide Instanzen dasselbe.
    // ensureDistinctCompareEngine() NICHT hier aufrufen: startCompare() macht es als zweiten
    // Schritt ohnehin. Zwei Aufrufstellen fuer dieselbe Invariante lesen sich, als sicherten sie
    // Verschiedenes ab, und laden dazu ein, nur eine davon zu „reparieren".
    if (this.compareOn && this.compareEngineId === this.selectedEngineId) this.startCompare();
    if (this.engineOn && this.dests.size > 0) this.runAnalysis(this.engine, this.currentFen);
  }
  backToPuzzle(): void {
    if (this.returnTo) this.router.navigateByUrl(this.returnTo);
  }
  reloadPage(): void { window.location.reload(); }
  flip(): void { this.orientation = this.orientation === 'white' ? 'black' : 'white'; }

  reset(): void { this.startFen = START_FEN; this.resetToStart(); }
  private resetToStart(): void { this.line = []; this.ply = 0; this.refresh(); }

  // ---- Stellung aufbauen (Brett-Editor) ----
  startEditing(): void { this.editing = true; }
  onSetupApply(fen: string): void {
    this.editing = false;
    this.startFen = fen;
    this.fenInput = '';
    this.resetToStart();
  }

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
