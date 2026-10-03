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
import { defaults as chessgroundDefaults } from 'chessground/state';
import { Subscription, interval } from 'rxjs';
import { EngineDisplayLine, formatElapsed as formatElapsedUtil, formatKiloNodes, formatKiloNps, toDisplayLines as toDisplayLinesUtil, uciLineToSan as uciLineToSanUtil } from './engine-lines.util';
import { AnalysisBoardComponent } from './analysis-board.component';
import { PositionSetupComponent } from './position-setup.component';
import { AnalysisEngineService, AnalysisLine, RemoteInterruption } from './analysis-engine.service';
import { ExternalEngineService, ExternalEngineInfo, engineTagKey } from './external-engine.service';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SnackbarService } from '../../core/snackbar.service';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { OpeningExplorerComponent } from './opening-explorer.component';
import { AuthService } from '../../core/auth.service';
import { ANALYSIS_DEPTH_KEY, ANALYSIS_LINES_KEY, ANALYSIS_PROVIDER_KEY } from './analysis-settings';
import { PositionMenuComponent } from './position-menu.component';
import { MatDialog } from '@angular/material/dialog';
import { AnalysisHistoryEntry, AnalysisHistoryService } from './analysis-history.service';
import { AnalysisHistoryDialogComponent } from './analysis-history-dialog.component';
import {
  AnalysisNode, addMove, createRoot, fromDto, isWithin, lineThrough, mainline, makeMainline, numberedSan, parsePgnTree, pathTo,
  playSan, playUci, promote, removeNode, starredNodes, toDto,
} from './analysis-tree';
import { AnalysisMoveTreeComponent, MoveTreeAction } from './analysis-move-tree.component';
import { MaiaEngineService } from './maia/maia-engine.service';
import {
  MAIA_CHECK_DEPTH, MAIA_DEFAULT_ELO, MAIA_ELO_KEY, MAIA_ELO_OPTIONS, MAIA_EVALBAR_KEY, MAIA_WARN_KEY,
} from './maia/maia-model';
import { BadMoveVerdict, EvalPoint, SparringWarning, badMoveVerdict } from './maia/sparring-check';
import { MaiaSparringCardComponent } from './maia/maia-sparring-card.component';
import { isBoardHotkey } from '../../shared/keyboard.util';
import { buildSparringPgn } from './maia/sparring-pgn';
import { GamesService } from '../games/games.service';
import { AnalyzeGameService } from '../games/analyze-game.service';
import { GuessUploadStatus } from './game-analysis.service';

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const LINES_KEY = ANALYSIS_LINES_KEY;
const ENGINE_KEY = 'rookhub_analysis_engine';
const DEPTH_KEY = ANALYSIS_DEPTH_KEY;
/** Analyse-Verlauf (0.603.0): so lange nach der letzten Änderung, bis der Stand zum Server geht. */
const HISTORY_SAVE_MS = 1500;
/** 'wasm' oder die Lichess-Engine-ID der zuletzt gewählten External Engine. */
const PROVIDER_KEY = ANALYSIS_PROVIDER_KEY;
/** Vergleichsmodus: an/aus und die Wahl der zweiten Engine. */
const COMPARE_KEY = 'rookhub_analysis_compare';
const COMPARE_ENGINE_KEY = 'rookhub_analysis_compare_engine';
// Bis 50: für eine externe Engine (mehrere Millionen Knoten/s) sind Tiefen jenseits von 30
// gut erreichbar. Mit der Browser-Engine dauern sie sehr lange — sie bleiben trotzdem
// wählbar, statt Optionen je nach Engine verschwinden zu lassen.
// ACHTUNG: Jeder Wert hier muss den Clamp in AnalysisEngineService.setDepth überleben,
// sonst wählt man 50 und bekommt stillschweigend weniger (Test hält das fest).
export const DEPTH_OPTIONS = [12, 16, 18, 20, 22, 26, 30, 35, 40, 45, 50];
// Fünf verschiedene Pinsel (UX-049: die fünfte Linie war wieder Blau). Die Linienliste setzt dieselbe Farbe als Punkt vor
// jede Linie — die Farben kommen aus den Standardpinseln von chessground, also genau die des Pfeils auf dem Brett.
const ARROW_BRUSHES = ['green', 'blue', 'yellow', 'red', 'purple'];
const arrowBrush = (i: number): string => ARROW_BRUSHES[i] || 'blue';
const BOARD_BRUSHES = chessgroundDefaults().drawable.brushes;
/** Ab dieser Tiefe übernimmt die Bewertungsleiste den Wert einer neuen Suche. Darunter schwanken
 *  die Zahlen stark (Tiefe 1–5 liegt gern eine Figur daneben) — die Leiste bliebe sonst bei jedem
 *  Zug unruhig. Muss unter dem kleinsten DEPTH_OPTIONS-Wert liegen, sonst erreicht eine
 *  vollständige Suche die Schwelle nie (endet sie vorher, gilt ihr letzter Wert trotzdem). */
const EVAL_SETTLE_DEPTH = 10;

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-analysis',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatSlideToggleModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatTooltipModule, TranslatePipe, AnalysisBoardComponent, PositionSetupComponent,
    PositionRepertoiresComponent, HelpHintComponent, OpeningExplorerComponent, PositionMenuComponent, AnalysisMoveTreeComponent,
    IconLabelDirective, MaiaSparringCardComponent
  ],
  template: `
    <div class="analysis-page">
      <div class="page-head">
        <h1>{{ 'analysis.title' | translate }}</h1>
        @if (auth.isLoggedIn) {
          <button mat-stroked-button type="button" class="history-btn" (click)="openHistory()">
            <mat-icon>history</mat-icon> {{ 'analysis.history.open' | translate }}
          </button>
        }
      </div>
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
              <mat-icon>arrow_back</mat-icon> {{ 'common.back' | translate }}
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
                <!-- Während des Sparrings gesperrt: die Engine verriete Bewertung und Pfeile (Maia-Regel 2). -->
                <mat-slide-toggle [(ngModel)]="engineOn" [disabled]="!!sparring" (change)="onEngineToggle()">{{ 'analysis.engine' | translate }}</mat-slide-toggle>
                <span class="depth" *ngIf="engineOn">{{ 'analysis.depth' | translate }} {{ depth }}/{{ depthSetting }} · <span class="search-time" [title]="'analysis.searchTime' | translate">{{ searchTime }}</span></span>
                <span class="he-spacer"></span>
                <span class="num-pair">
                  <mat-form-field appearance="outline" class="num-field" subscriptSizing="dynamic">
                    <mat-label>{{ 'analysis.depth' | translate }}</mat-label>
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
                </span>
                @if (externalEnginesList.length > 0) {
                  <mat-form-field appearance="outline" class="engine-field" subscriptSizing="dynamic">
                    <mat-label>{{ 'analysis.engineProvider' | translate }}</mat-label>
                    <mat-select [(ngModel)]="selectedEngineId" (selectionChange)="onEngineSelect()">
                      <mat-option value="wasm">{{ 'analysis.engineBrowser' | translate }}</mat-option>
                      @for (e of externalEnginesList; track e.id) {
                        <mat-option [value]="e.id">{{ e.name }}@if (tagOf(e); as tag) { <span class="engine-tag">· {{ tag | translate }}</span> }</mat-option>
                      }
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
                        <mat-option [value]="c.id" [disabled]="c.id === selectedEngineId">{{ c.name }}@if (c.tag) { <span class="engine-tag">· {{ c.tag | translate }}</span> }</mat-option>
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
                    @for (l of displayLines; track $index; let i = $index) {
                      <div class="line-row">
                        <span class="line-mark" [style.background]="lineColor(i)" aria-hidden="true"></span>
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
                <p class="muted">{{ (sparring ? 'analysis.maia.enginePaused' : 'analysis.engineOff') | translate }}</p>
              }
            </mat-card-content>
          </mat-card>

          <!-- Sparring gegen Maia: die Karte führt den Lade-Ablauf selbst und meldet „start" erst mit fertigem Modell.
               Im Stellungs-Editor ausgeblendet — dort gibt es keine Stellung, von der aus man starten könnte. -->
          @if (!editing) {
            <app-maia-sparring-card
              [active]="!!sparring" [userColor]="sparring?.userColor ?? turnColor" [thinking]="maiaThinking"
              [maiaToMove]="maiaToMove" [elo]="maiaElo"
              [showAnalyze]="sparringAnalyzeVisible" [analyzing]="analyzingSparring"
              [analyzeBlocked]="analyzeStatus?.engineAvailable === false"
              [warnBadMoves]="warnBadMoves" [keepEvalBar]="keepEvalBar" [warning]="sparringWarning"
              (start)="startSparring()" (stop)="stopSparring()" (switchSides)="switchSparringSides()"
              (restart)="restartSparring()" (maiaMove)="requestMaiaMove()" (eloChange)="onMaiaEloChange($event)"
              (analyze)="analyzeSparring()" (warnBadMovesChange)="onWarnBadMovesChange($event)"
              (keepEvalBarChange)="onKeepEvalBarChange($event)" (analyzeWarning)="analyzeWarning()" />
          }

          <mat-card class="moves-card">
            <mat-card-content>
              <div class="controls">
                <!-- appIconLabel statt matTooltip (Codereview UX-014): derselbe Text wird auch der zugängliche Name —
                     mit Tooltip allein hießen die Knöpfe für Screenreader nur „Schaltfläche". -->
                <button mat-icon-button (click)="goTo(0)" [disabled]="ply === 0" [appIconLabel]="'analysis.start' | translate"><mat-icon>first_page</mat-icon></button>
                <button mat-icon-button (click)="prev()" [disabled]="ply === 0" [appIconLabel]="'pgnViewer.nav.previous' | translate"><mat-icon>chevron_left</mat-icon></button>
                <button mat-icon-button (click)="next()" [disabled]="ply >= line.length" [appIconLabel]="'pgnViewer.nav.next' | translate"><mat-icon>chevron_right</mat-icon></button>
                <button mat-icon-button (click)="goTo(line.length)" [disabled]="ply >= line.length" [appIconLabel]="'pgnViewer.nav.last' | translate"><mat-icon>last_page</mat-icon></button>
                <button mat-icon-button class="star-btn" [class.on]="!!currentNode.starred" (click)="toggleStar()"
                        [appIconLabel]="(currentNode.starred ? 'analysis.star.remove' : 'analysis.star.add') | translate">
                  <mat-icon>{{ currentNode.starred ? 'star' : 'star_border' }}</mat-icon>
                </button>
                <span class="spacer"></span>
                <button mat-icon-button (click)="flip()" [appIconLabel]="'analysis.flip' | translate"><mat-icon>cached</mat-icon></button>
                <button mat-icon-button (click)="reset()" [appIconLabel]="'analysis.reset' | translate"><mat-icon>restart_alt</mat-icon></button>
                <!-- ⋮ für die Stellung (0.527.0): Chessable-Suche, teilen, FEN kopieren, Hintergrund-Analyse + Aufträge —
                     die beiden letzten standen vorher als eigene Symbole in der Engine-Zeile. -->
                <app-position-menu [fen]="currentFen" [orientation]="orientation" [depth]="depthSetting" [lines]="linesCount"
                                   [candidates]="engineCandidates"
                                   [engines]="{ hasEngines: hasExternalEngines, hasBackground: backgroundEngineIds.length > 0 }" />
              </div>
              @if (starredList.length > 0) {
                <div class="star-jumps" [attr.aria-label]="'analysis.star.jumps' | translate">
                  <mat-icon class="star-icon">star</mat-icon>
                  @for (n of starredList; track n) {
                    <button type="button" class="jump" [class.active]="n === currentNode" (click)="goToNode(n)">{{ starLabel(n) }}</button>
                  }
                </div>
              }
              @if (root.children.length === 0) {
                <p class="muted">{{ 'analysis.noMoves' | translate }}</p>
              } @else {
                <!-- Zugbaum wie auf Lichess (0.604.0): Hauptlinie als Tabelle mit Bewertung, Varianten als Block,
                     Rechtsklick bzw. langer Druck: Stern, hochstufen, zur Hauptvariante, ab hier löschen. -->
                <app-analysis-move-tree class="movetree" [root]="root" [current]="currentNode" [version]="treeVersion"
                                        (select)="goToNode($event)" (action)="onTreeAction($event)" />
                <p class="tree-hint">{{ 'analysis.tree.hint' | translate }}</p>
              }
            </mat-card-content>
          </mat-card>

          <!-- Eröffnungs-Explorer (Lichess/Meister, online oder lokal) — nur eingeloggt: die Online-Quelle
               verbraucht das gemeinsame Kontingent des Server-Tokens. Ein Klick spielt den Zug. -->
          @if (auth.isLoggedIn) {
            <mat-card class="explorer-card">
              <mat-card-content>
                <app-opening-explorer [fen]="currentFen" (playMove)="playRepertoireMoves([$event])" />
              </mat-card-content>
            </mat-card>
          }

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
    .search-time { font-variant-numeric: tabular-nums; }
    .he-spacer { flex: 1 1 auto; }
    /* Tiefe + Linien als Paar (Nacharbeit UX-048): fehlt der Platz, rücken beide gemeinsam in die nächste Zeile, statt
       „Linien" allein an den linken Rand zu schieben. 116 px: „Tiefe"/„Dubina"/„Mélység" passen ungekürzt, und bei der
       520 px breiten Desktop-Seitenleiste bleibt das Paar in en/de/hr neben Schalter und Zähler (8 px Abstand = Reserve). */
    .num-pair { display: flex; align-items: center; gap: 8px; flex: 0 0 auto; }
    .num-field { width: 116px; }
    .engine-field { width: 190px; }
    .engine-tag { opacity: 0.65; font-size: 0.85em; }
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
    .line-mark { flex: 0 0 auto; align-self: center; width: 10px; height: 10px; border-radius: 50%;
      box-shadow: 0 0 0 1px color-mix(in srgb, currentColor 30%, transparent); }
    /* Bewertung aus den Zustands-Tokens (UX-015): die festen Hellthema-Toene #1b5e20/#b71c1c hatten auf der dunklen
       Karte 2,2:1 bzw. 2,6:1. */
    .line-eval { font-weight: 700; min-width: 48px; font-variant-numeric: tabular-nums; color: var(--rh-success); }
    .line-eval.neg { color: var(--rh-error); }
    .line-san { font-family: 'Courier New', monospace; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .controls { display: flex; align-items: center; gap: 2px; }
    .page-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    .page-head h1 { margin: 0 0 8px; }
    .star-btn.on mat-icon { color: #f9a825; }
    .star-jumps { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; margin: 2px 0 8px; }
    .star-jumps .star-icon { color: #f9a825; font-size: 18px; width: 18px; height: 18px; }
    .star-jumps .jump { font: inherit; font-family: 'Courier New', monospace; font-size: .85rem; padding: 1px 8px; border-radius: 999px;
      border: 1px solid rgba(249, 168, 37, .6); background: transparent; color: inherit; cursor: pointer; }
    .star-jumps .jump.active { background: rgba(249, 168, 37, .25); }
    .controls .spacer { flex: 1; }
    .movetree { margin-top: 8px; }
    .tree-hint { margin: 6px 0 0; font-size: .75rem; color: color-mix(in srgb, currentColor 50%, transparent); }
    .io-card .full { width: 100%; }
    .io-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
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
  /** Zugbaum der Analyse (0.604.0): die Wurzel ist die Ausgangsstellung, `children[0]` jeweils die Fortsetzung. */
  root: AnalysisNode = createRoot(START_FEN);
  /** Die Linie durch den aktuellen Knoten: der Weg dorthin und seine Fortsetzung — Pfeiltasten laufen auf ihr. */
  line: AnalysisNode[] = [];
  /** Wie weit man auf `line` steht (0 = Ausgangsstellung). */
  ply = 0;
  /** Zählt Änderungen am Baum (Zug, Variante, Stern, Bewertung) — die Zugliste baut danach neu auf. */
  treeVersion = 0;

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
  /** Erste Züge der aktuellen Engine-Linien samt Bewertung — „Züge vergleichen" wählt sie vor. */
  engineCandidates: { uci: string; evalText: string }[] = [];
  evalText = '0.00';
  whiteHeight = 50;
  engineCrashed = false;

  fenInput = '';
  pgnInput = '';
  editing = false;

  // ---- Sparring gegen Maia (0.632.0) — einfache Felder + markForCheck nach allem Asynchronen ----
  /** Läuft ein Sparring: ab welcher Stellung, welche Seite der Nutzer spielt, und ob die Engine vorher an war. `tip` = der
   *  zuletzt gespielte Zug im Teilbaum von `start` — die Partie fürs Analysieren ist die Linie `start` → `tip` (nicht die
   *  Fortsetzung von `start`: mitten in einer geladenen Partie stehen dort deren Züge). */
  sparring: { start: AnalysisNode; tip: AnalysisNode; userColor: Color; engineWasOn: boolean } | null = null;
  /** Die zuletzt beendete Sparring-Partie mit mindestens zwei Halbzügen — „Partie analysieren" nimmt sie. Verfällt mit
   *  jedem neuen Baum und wenn ihre Züge aus dem Baum gelöscht werden. */
  lastSparring: { start: AnalysisNode; tip: AnalysisNode; userColor: Color; elo: number } | null = null;
  /** „Partie analysieren" läuft (speichern + einreihen) — Doppelklick-Schutz. */
  analyzingSparring = false;
  /** Steht eine Engine bereit? Einmal gefragt, sobald der Knopf zum ersten Mal sichtbar würde; `null` = unbekannt. */
  analyzeStatus: GuessUploadStatus | null = null;
  private analyzeStatusAsked = false;
  maiaThinking = false;
  /** Gewählte Stärke (je Gerät gemerkt, nur Werte aus MAIA_ELO_OPTIONS). */
  maiaElo: number = MAIA_DEFAULT_ELO;
  /** Jede Navigation/jeder Abbruch zählt hoch — eine späte Maia-Antwort für eine verlassene Stellung verfällt. */
  private maiaEpoch = 0;
  /** Mindest-Bedenkzeit, damit Maias Zug nicht im selben Augenblick wie der eigene aufs Brett knallt (Specs: 0). */
  protected maiaDelayMs = 500;
  /** Schalter der Maia-Karte, je Gerät gemerkt (Vorgabe aus): eigene schlechte Züge melden / die Bewertungsleiste
   *  während des Sparrings anlassen. Beide lassen die Analyse-Engine STILL mitlaufen (`sparringEngineQuiet`). */
  warnBadMoves = false;
  keepEvalBar = false;
  /** Warnung zum zuletzt geprüften eigenen Zug — `before` = Stellung VOR dem Zug („Analysieren" springt dorthin). */
  sparringWarning: (SparringWarning & { before: AnalysisNode; drop: number }) | null = null;
  /** Spur der Bewertungen der aktuellen Stellung je Tiefe (Weiß-Sicht) — die Vorher-Werte der Zug-Prüfung. */
  private evalTrack: { fen: string; points: EvalPoint[] } = { fen: '', points: [] };
  /** Ein eigener Zug am Brett: die Stellung davor und ihre Spur — `requestMaiaMove` übernimmt sie. */
  private pendingBefore: { node: AnalysisNode; points: EvalPoint[] } | null = null;
  /** Die laufende Prüfung der Stellung NACH dem eigenen Zug, bedient aus onEngineUpdate. */
  private pendingCheck: {
    fen: string; mover: Color; before: EvalPoint[]; epoch: number;
    resolve: (v: BadMoveVerdict | null) => void; timer: ReturnType<typeof setTimeout>;
  } | null = null;
  /** Spätestens so lange wartet Maias Antwort auf das Urteil (Specs: klein). */
  protected maiaCheckTimeoutMs = 4000;

  // ---- Analyse-Verlauf + Sterne (0.603.0; Sterne seit 0.604.0 am Knoten des Zugbaums) ----
  /** Kennung des Verlauf-Eintrags dieser Analyse (null = neue Analyse, der Server vergibt sie beim ersten Speichern). */
  historyId: number | null = null;
  /** Titel aus den PGN-Kopfdaten („Weiß – Schwarz"), sonst leer. */
  private historyTitle: string | null = null;
  private historyTimer: ReturnType<typeof setTimeout> | null = null;
  private historySaving = false;
  private historyAgain = false;
  private lastHistorySig = '';
  /** Zählt die Analysen dieser Seite — eine Antwort, die nach einem Wechsel eintrifft, gehört nicht mehr dazu. */
  private historySession = 0;

  /** External Engines des Lichess-Kontos (leer = kein Picker); Auswahl 'wasm' = Browser. */
  externalEnginesList: ExternalEngineInfo[] = [];
  selectedEngineId = 'wasm';
  /** Im Profil gewählte Hintergrund-Engine — gehört den Aufträgen, fehlt deshalb im Live-Picker. */
  /** Alle als Hintergrund gewaehlten Engines — der Live-Picker blendet sie aus. */
  backgroundEngineIds: string[] = [];
  /** Per `?engine=` gewünschte Engine (von der Auftragsseite) — gilt einmalig für diesen Aufruf. */
  private requestedEngineId: string | null = null;
  /** Mindestens eine externe Engine registriert (inkl. Hintergrund-Engine) → Aufträge sind möglich. */
  hasExternalEngines = false;
  remoteFallback = false;
  /** Abriss der Remote-Suche vor der Zieltiefe (Hinweis in der Karte; null = keiner). */
  remoteCut: RemoteInterruption | null = null;
  private cutSub?: Subscription;
  /** Lebenszeichen: läuft die Suche, und wie viele Sekunden kam keine neue Engine-Zeile? (nur Anzeige) */
  running = false;
  sinceUpdateSec = 0;
  private lastUpdateAt = Date.now();
  private tickSub?: Subscription;
  /** Suchzeit der aktuellen Stellung — startet mit dem ersten „läuft" einer Stellung, läuft über eine
   *  Fortsetzung nach Abriss weiter und friert beim Ende ein. Permanent im Kopf neben „Tiefe x/y". */
  searchElapsedSec = 0;
  private searchStartedAt = Date.now();
  private searchEndedAt: number | null = null;
  private searchFen = '';
  /** Uhrquelle (in Tests überschreibbar). */
  private nowFn: () => number = () => Date.now();
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
              @Inject(LOCALE_ID) private locale: string, private history: AnalysisHistoryService, private dialog: MatDialog,
              private maia: MaiaEngineService, private games: GamesService, private analyzeGame: AnalyzeGameService) {
    try {
      const l = parseInt(localStorage.getItem(LINES_KEY) || '', 10);
      if (l >= 1 && l <= 5) this.linesCount = l;
      this.engineOn = localStorage.getItem(ENGINE_KEY) !== '0';
      const d = parseInt(localStorage.getItem(DEPTH_KEY) || '', 10);
      if (DEPTH_OPTIONS.includes(d)) this.depthSetting = d;
      this.compareOn = localStorage.getItem(COMPARE_KEY) === '1';
      this.compareEngineId = localStorage.getItem(COMPARE_ENGINE_KEY) || 'wasm';
      const elo = parseInt(localStorage.getItem(MAIA_ELO_KEY) || '', 10);
      if ((MAIA_ELO_OPTIONS as readonly number[]).includes(elo)) this.maiaElo = elo;
      this.warnBadMoves = localStorage.getItem(MAIA_WARN_KEY) === '1';
      this.keepEvalBar = localStorage.getItem(MAIA_EVALBAR_KEY) === '1';
    } catch {}
  }

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const fenParam = params.get('fen');
    const startFen = fenParam && this.isValidFen(fenParam) ? fenParam : START_FEN;
    this.root = createRoot(startFen);
    const orientationParam = params.get('orientation');
    if (orientationParam === 'white' || orientationParam === 'black') {
      this.orientation = orientationParam;
    }
    // Herkunft für den Zurück-Button merken — Puzzle, Favoriten, Partienliste, Partie oder Kalkulation, daher heißt
    // der Knopf neutral „Zurück" (F4-012; vorher stand dort immer „Zurück zum Puzzle").
    const from = params.get('from');
    if (from && from.startsWith('/') && !from.startsWith('//') && !from.includes('://')) {
      this.returnTo = from;
    }
    // Von der Auftragsseite: Engine + Suchparameter des Auftrags übernehmen, damit hier WEITERgerechnet
    // wird statt neu zu beginnen (gleiche Engine = warmer Hash). Die Wahl gilt nur für diesen Aufruf und
    // wird bewusst NICHT als Dauereinstellung gespeichert.
    const engineParam = params.get('engine');
    if (engineParam && /^[A-Za-z0-9_-]{1,64}$/.test(engineParam)) this.requestedEngineId = engineParam;
    const depthParam = parseInt(params.get('depth') || '', 10);
    if (DEPTH_OPTIONS.includes(depthParam)) this.depthSetting = depthParam;
    const linesParam = parseInt(params.get('lines') || '', 10);
    if (linesParam >= 1 && linesParam <= 5) this.linesCount = linesParam;

    this.engine.setDepth(this.depthSetting);
    this.engine.setMultiPv(this.linesCount);
    this.sub = this.engine.analysis$.subscribe(s => {
      const wasRunning = this.running;
      const now = this.nowFn();
      this.running = s.running;
      this.lastUpdateAt = now;
      if (s.running && (!wasRunning || s.fen !== this.searchFen)) {
        this.searchStartedAt = now; this.searchEndedAt = null; this.searchFen = s.fen;
      } else if (!s.running && wasRunning) {
        this.searchEndedAt = now;
      }
      this.onEngineUpdate(s.fen, s.depth, s.lines, s.nodes, s.nps);
    });
    this.errorSub = this.engine.engineFatalError$.subscribe(e => { this.engineCrashed = e !== null; this.cdr.markForCheck(); });
    this.fallbackSub = this.engine.remoteFallback$.subscribe(f => { this.remoteFallback = f; this.cdr.markForCheck(); });
    this.cutSub = this.engine.remoteInterrupted$.subscribe(c => { this.remoteCut = c; this.cdr.markForCheck(); });
    // Sekundentakt nur für „rechnet seit …": bei MultiPV 5 vergehen ab Tiefe ~27 Minuten ohne neue
    // Zeile — ohne sichtbare Uhr sieht das aus wie ein Hänger. markForCheck nur bei Wertänderung.
    this.tickSub = interval(1000).subscribe(() => this.updateClocks());

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
          // Die Hintergrund-Engine gehört den Aufträgen — im Live-Picker (und im Vergleich) taucht sie
          // nicht auf, sonst konkurrierten zwei Suchen um dieselben Kerne.
          this.backgroundEngineIds = r.backgroundEngineIds ?? [];
          this.hasExternalEngines = r.engines.length > 0;
          this.externalEnginesList = r.engines.filter(e => !this.backgroundEngineIds.includes(e.id));
          // Kam der Aufruf von einem Auftrag („im Analysebrett öffnen"), gilt DESSEN Engine — auch wenn es
          // die Hintergrund-Engine ist, die hier sonst ausgeblendet wird. Der Provider hat die Stellung noch
          // im Hash, die Suche ist damit sofort wieder auf der erreichten Tiefe statt bei null.
          const wanted = this.requestedEngineId;
          if (wanted && r.engines.some(e => e.id === wanted)) {
            if (!this.externalEnginesList.some(e => e.id === wanted))
              this.externalEnginesList = [...this.externalEnginesList, r.engines.find(e => e.id === wanted)!];
            this.selectedEngineId = wanted;
            this.applyEngineSelection();
            this.requestedEngineId = null;   // nur für diesen Aufruf, nicht als Dauerwahl merken
            this.cdr.markForCheck();
            return;
          }
          let stored: string | null = null;
          try { stored = localStorage.getItem(PROVIDER_KEY); } catch {}
          if (stored && stored !== 'wasm' && this.externalEnginesList.some(e => e.id === stored)) {
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
    // … oder über die Adresse (`?pgn=`, 0.592.0): der Router-State kommt über Seitengrenzen nicht an — LeagueHub springt
    // so mit einer Vereinspartie her, samt Kopfdaten im PGN-Feld.
    const paramPgn = params.get('pgn');
    const pgn = typeof statePgn === 'string' && statePgn.trim() ? statePgn : paramPgn?.trim() ? paramPgn : null;
    // Ein Eintrag des Verlaufs per Adresse (`?history=<id>`): erst die Grundstellung, dann der Eintrag, sobald er da ist.
    const historyParam = parseInt(params.get('history') || '', 10);
    if (historyParam > 0 && this.auth.isLoggedIn) {
      this.resetToStart();
      this.history.get(historyParam).subscribe({ next: e => this.openHistoryEntry(e), error: () => {} });
      return;
    }
    if (pgn) {
      this.pgnInput = pgn;
      this.loadPgn(true);      // aus einer Partie hergesprungen: das PGN bleibt im Feld stehen (kopierbar, Wunsch 2026-09-28)
    } else if (uci.length) {
      this.loadFromUci(startFen, uci);
    } else {
      this.resetToStart(startFen);
    }
  }

  /** Baut die Hauptlinie aus UCI-Zügen ab `fromFen` und springt ans Ende (aktuelle Stellung); am ersten Zug, der nicht
   *  geht, ist Schluss. */
  private loadFromUci(fromFen: string, uciMoves: string[]): void {
    if (!this.isValidFen(fromFen)) { this.resetToStart(START_FEN); return; }
    const root = createRoot(fromFen);
    let node = root;
    for (const u of uciMoves) {
      const next = playUci(node, u);
      if (!next) break;
      node = next;
    }
    this.setTree(root, node);
  }

  ngOnDestroy(): void {
    // Wer die Seite verlässt, bevor die Drossel abläuft, soll den letzten Stand trotzdem im Verlauf haben.
    if (this.historyTimer) { clearTimeout(this.historyTimer); this.historyTimer = null; this.saveHistoryNow(); }
    this.sub?.unsubscribe();
    this.errorSub?.unsubscribe();
    this.fallbackSub?.unsubscribe();
    this.cutSub?.unsubscribe();
    this.tickSub?.unsubscribe();
    this.enginesSub?.unsubscribe();
    this.stopCompare();          // eigene Instanz + deren Worker/Streams beenden
    this.engine.destroy();
    // Maia freigeben (~150 MB: Modell + Sitzung im Worker) — beim nächsten Besuch kommt es in ~1 s aus dem Cache.
    this.maiaEpoch++;
    this.cancelMoveCheck();
    this.maia.release();
  }

  // ---- Navigation ----
  get startFen(): string { return this.root.fen; }
  /** Der Knoten, dessen Stellung auf dem Brett steht (die Wurzel = Ausgangsstellung). */
  get currentNode(): AnalysisNode { return this.ply === 0 ? this.root : this.line[this.ply - 1]; }
  get currentFen(): string { return this.currentNode.fen; }

  goTo(ply: number): void {
    this.abortMaia();
    this.ply = Math.max(0, Math.min(ply, this.line.length));
    this.refresh();
  }

  /** Auf einen beliebigen Knoten springen (Klick in Zugliste, Variante, Sternliste) — die Linie läuft dann durch ihn. */
  goToNode(node: AnalysisNode): void {
    this.abortMaia();
    this.line = lineThrough(node);
    this.ply = pathTo(node).length;
    this.refresh();
  }

  /** Einen neuen Baum aufs Brett (Laden, Zurücksetzen) und dort auf `current` stehen. */
  private setTree(root: AnalysisNode, current: AnalysisNode = root): void {
    // Ein neuer Baum beendet ein Sparring (Maia-Regel 9) — die Engine kommt zurück wie beim „Beenden". Die alte Partie
    // gehört nicht mehr aufs Brett, „Partie analysieren" verfällt mit ihr.
    if (this.sparring) this.endSparring();
    this.lastSparring = null;
    this.root = root;
    this.treeVersion++;
    this.goToNode(current);
  }
  prev(): void { this.goTo(this.ply - 1); }
  next(): void { this.goTo(this.ply + 1); }

  @HostListener('window:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!isBoardHotkey(e)) return;
    if (e.key === 'ArrowLeft') { e.preventDefault(); this.prev(); }
    else if (e.key === 'ArrowRight') { e.preventDefault(); this.next(); }
    else if (e.key === 'Home') { e.preventDefault(); this.goTo(0); }
    else if (e.key === 'End') { e.preventDefault(); this.goTo(this.line.length); }
    else if ((e.key === 's' || e.key === 'S') && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); this.toggleStar(); }
  }

  // ---- User move ----
  onMove(ev: { orig: Key; dest: Key; promotion?: string }): void {
    let c: Chess;
    try { c = new Chess(this.currentFen); } catch { return; }
    const mover: Color = c.turn() === 'w' ? 'white' : 'black';
    const piece = c.get(ev.orig as any);
    const isPromo = piece?.type === 'p' && (ev.dest[1] === '8' || ev.dest[1] === '1');
    // Umwandlungsfigur kommt jetzt aus dem Picker; Dame nur als Fallback.
    const promotion = isPromo ? (ev.promotion ?? 'q') : undefined;
    let mv;
    try {
      mv = c.move({ from: ev.orig, to: ev.dest, promotion });
    } catch { this.refresh(); return; }   // illegaler Zug -> Brett zurücksetzen
    if (!mv) { this.refresh(); return; }

    // „Schlechte Züge melden": die Stellung davor samt ihrer Bewertungs-Spur merken, BEVOR goToNode sie verlässt — nur für
    // eigene Züge am Brett (Explorer/Repertoire-Züge werden nicht geprüft).
    if (this.sparring && this.warnBadMoves && mover === this.sparring.userColor) {
      const before = this.currentNode;
      this.pendingBefore = { node: before, points: this.evalTrack.fen === before.fen ? [...this.evalTrack.points] : [] };
    }
    // Zugbaum (0.604.0): ein anderer Zug als die Fortsetzung wird eine VARIANTE, derselbe Zug geht in die vorhandene.
    const node = addMove(this.currentNode, { san: mv.san, uci: mv.from + mv.to + (mv.promotion ?? ''), fen: c.fen() });
    this.treeVersion++;
    this.goToNode(node);
    this.noteSparringMove(node);
    // Sparring: auf den EIGENEN Zug antwortet Maia. Zieht der Nutzer für Maias Seite, kommt keine Antwort.
    if (this.sparring && mover === this.sparring.userColor) this.requestMaiaMove();
  }

  /** Baummodus des Repertoire-Panels bzw. Explorer: die geklickte Zugfolge ab der aktuellen Stellung aufs Brett
   * spielen (wie selbst gezogen — weicht sie von der Fortsetzung ab, wird sie eine Variante). Illegale/unbekannte
   * SAN brechen still ab, statt die Linie halb zu zerschießen. */
  playRepertoireMoves(sans: string[]): void {
    if (!sans?.length) return;
    let node = this.currentNode;
    for (const san of sans) {
      const next = playSan(node, san);
      if (!next) break;
      node = next;
    }
    if (node === this.currentNode) return;
    this.treeVersion++;
    this.goToNode(node);
    this.noteSparringMove(node);
    // Sparring: ein Zug aus Explorer/Repertoire zählt wie ein eigener — steht danach Maia am Zug, antwortet sie. Zog die
    // Folge für Maias Seite, ist danach der Nutzer dran, und es kommt (wie in onMove) keine Anfrage.
    if (this.maiaToMove) this.requestMaiaMove();
  }

  // ---- Refresh board + engine for current ply ----
  private refresh(): void {
    this.scheduleHistorySave();
    const fen = this.currentFen;
    let c: Chess;
    try { c = new Chess(fen); } catch { return; }
    this.boardFen = fen;
    this.turnColor = c.turn() === 'w' ? 'white' : 'black';
    this.isCheck = c.isCheck();
    this.dests = this.computeDests(c);
    const lm = this.ply > 0 ? this.currentNode.uci : undefined;
    this.lastMove = lm ? [lm.substring(0, 2) as Key, lm.substring(2, 4) as Key] : undefined;
    this.shapes = [];
    this.displayLines = [];
    this.engineCandidates = [];
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
    // Die Bewertungs-Spur gehört zur Stellung — die der alten hat ein eigener Zug vorher übernommen (pendingBefore).
    this.evalTrack = { fen, points: [] };
    if (this.engineOn && this.dests.size > 0) {
      this.runAnalysis(this.engine, fen);
      this.runAnalysis(this.compareEngine, fen);
    } else if (this.sparringEngineQuiet && this.dests.size > 0) {
      // Sparring mit „Schlechte Züge melden"/„Bewertungsleiste anlassen": nur die Haupt-Engine rechnet, still.
      this.compareEngine?.stop();
      this.runAnalysis(this.engine, fen);
      if (!this.keepEvalBar) this.updateEval(null);
    } else {
      this.engine.stop();
      this.compareEngine?.stop();
      this.updateEval(null);
    }
  }

  /** Während des Sparrings rechnet die Engine still mit (keine Linien, Pfeile, Kandidaten), wenn einer der beiden
   *  Maia-Schalter an ist — für die Zug-Prüfung bzw. die Bewertungsleiste. */
  get sparringEngineQuiet(): boolean {
    return !!this.sparring && (this.warnBadMoves || this.keepEvalBar);
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
    const quiet = !this.engineOn && this.sparringEngineQuiet;
    if (!(this.engineOn || quiet) || fen !== this.currentFen) return;
    this.nodes = nodes;
    this.nps = nps;
    // Angular 22 refresht eine unmarkierte View nach async/HTTP NICHT mehr von selbst (siehe
    // CLAUDE.md-Konvention). Beim WASM-Pfad kaschieren Worker-/Event-Ticks das noch; bei der
    // externen Engine kommen die Zeilen NUR aus einem HTTP-Stream — ohne diese Marke bliebe die
    // Linienliste stehen, obwohl der Zustand längst stimmt.
    this.cdr.markForCheck();
    this.depth = depth;
    const best = lines[0];
    if (best) this.trackEval(fen, best);
    // Stille Engine im Sparring: Linien, Kandidaten und Pfeile verrieten den besten Zug — sie bleiben leer.
    if (!quiet) {
      this.displayLines = this.toDisplayLines(fen, lines);
      this.engineCandidates = lines.filter(l => !!l.pvUci[0]).map(l => ({ uci: l.pvUci[0], evalText: l.evalText }));
      this.shapes = lines.map((l, i) => {
        const u = l.pvUci[0];
        return u ? { orig: u.substring(0, 2) as Key, dest: u.substring(2, 4) as Key, brush: arrowBrush(i) } as DrawShape : null;
      }).filter((s): s is DrawShape => !!s);
    }
    // Bewertungsleiste HALTEN, bis die neue Suche etwas Belastbares liefert. Jede Suche beginnt mit
    // einem Zwischenstand ohne Linien; früher sprang die Leiste darauf auf 0.00 und erst Sekunden
    // später (externe Engine: Netzweg + Anlauf) auf den echten Wert — bei jedem Zug ein Ausschlag
    // zur Mitte und zurück. Die Bewertung ist aus Weiß-Sicht, der Wert der Vorstellung liegt also
    // meist nah am neuen. Übernommen wird ab EVAL_SETTLE_DEPTH, bei Matt sofort (ein gefundenes
    // Matt ist auch flach verlässlich) und sobald die Suche endet, egal wie tief sie kam.
    // Engine aus / Partie-Ende setzen die Leiste weiterhin direkt über refresh() → updateEval(null).
    // Im Sparring bekommen Leiste und Zugliste die Bewertung nur mit „Bewertungsleiste anlassen".
    if (best && (!quiet || this.keepEvalBar)
        && (best.depth >= EVAL_SETTLE_DEPTH || best.scoreType === 'mate' || !this.running)) {
      this.updateEval(best);
      // Die Bewertung wandert an den Zug in der Zugliste (wie auf Lichess) und mit dem Baum in den Verlauf.
      const node = this.currentNode;
      if (node !== this.root && node.evalText !== best.evalText) {
        node.evalText = best.evalText;
        this.treeVersion++;
        this.scheduleHistorySave();
      }
    }
    this.serveMoveCheck(fen);
  }

  /** Die Bewertung der besten Linie je Tiefe merken (nur die erste Zeile einer Tiefe zählt). */
  private trackEval(fen: string, best: AnalysisLine): void {
    if (this.evalTrack.fen !== fen) this.evalTrack = { fen, points: [] };
    if (this.evalTrack.points.some(p => p.depth === best.depth)) return;
    this.evalTrack.points.push({ depth: best.depth, score: best.score, scoreType: best.scoreType, evalText: best.evalText });
  }

  /** Der tiefste Punkt der Spur für `fen` (ab `minDepth`), sonst null. */
  private deepestPoint(fen: string, minDepth = 0): EvalPoint | null {
    if (this.evalTrack.fen !== fen) return null;
    let deepest: EvalPoint | null = null;
    for (const p of this.evalTrack.points) if (p.depth >= minDepth && (!deepest || p.depth > deepest.depth)) deepest = p;
    return deepest;
  }

  /** Farbe des Pfeils der i-ten Engine-Linie — der Punkt davor in der Linienliste (UX-049). Mit der Deckkraft des
   *  Pinsels, wie chessground den Pfeil zeichnet: 'purple' deckt nur zu 65 %, voll deckend wirkte der Punkt dunkler als
   *  sein Pfeil und läge nah am dunklen Rot der vierten Linie. */
  lineColor(i: number): string {
    const brush = BOARD_BRUSHES[arrowBrush(i)];
    if (!brush) return 'transparent';
    const alpha = brush.opacity || 1; // wie chessground: 0/fehlend zählt als voll deckend
    const hex = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(brush.color);
    return hex && alpha < 1
      ? `rgba(${parseInt(hex[1], 16)}, ${parseInt(hex[2], 16)}, ${parseInt(hex[3], 16)}, ${alpha})`
      : brush.color;
  }

  /** Engine-Linien in Anzeigezeilen. Beide Engine-Seiten MUESSEN hier durch: eine
   *  Nebeneinander-Ansicht, die dieselbe Bewertung links anders einfaerbt als rechts, waere
   *  schlimmer als gar kein Vergleich. Frueher lag die Abbildung zweimal im Code, inklusive der
   *  feinen Unterscheidung `score > 0` (Matt) gegen `score >= 0` (Zentibauern). */
  private toDisplayLines(fen: string, lines: AnalysisLine[]): EngineDisplayLine[] {
    return toDisplayLinesUtil(fen, lines, 12);
  }

  /** Gemeinsame Tempo-Formatierung. `nodes === null` = Kurzform (Vergleichs-Engine). */
  private speedHintFor(nps: number, nodes: number | null): string {
    if (nps <= 0) return this.translate.instant('analysis.speedWaiting');
    const speed = formatKiloNps(nps, this.locale);
    return nodes === null
      ? this.translate.instant('analysis.speedShort', { speed })
      : this.translate.instant('analysis.speedHint', { speed, nodes: formatKiloNodes(nodes, this.locale) });
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
  /** Beide Uhren nachziehen (Sekundentakt): Funkstille seit der letzten Zeile + Suchzeit der Stellung. */
  private updateClocks(): void {
    const now = this.nowFn();
    const v = this.running ? Math.floor((now - this.lastUpdateAt) / 1000) : 0;
    const e = Math.max(0, Math.floor(((this.searchEndedAt ?? now) - this.searchStartedAt) / 1000));
    if (v !== this.sinceUpdateSec || e !== this.searchElapsedSec) {
      this.sinceUpdateSec = v; this.searchElapsedSec = e; this.cdr.markForCheck();
    }
  }

  get thinkingTime(): string { return AnalysisComponent.formatElapsed(this.sinceUpdateSec); }
  /** Suchzeit der aktuellen Stellung als m:ss (läuft, bis die Suche endet; dann eingefroren). */
  get searchTime(): string { return AnalysisComponent.formatElapsed(this.searchElapsedSec); }
  static formatElapsed(totalSec: number): string { return formatElapsedUtil(totalSec); }
  /** Erwartung setzen: 4+ Linien × Tiefe ≥ 27 braucht auf einem PC je Iteration Minuten. */
  get slowConfigHint(): boolean { return this.linesCount >= 4 && this.depthSetting >= 27; }

  /** 8234567 → „8,2 MN/s" (Tausender/Millionen wie in Schach-Oberflächen üblich). */

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
    return uciLineToSanUtil(fromFen, uci, maxPlies);
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
    if (this.dests.size === 0) return;
    if (this.engineOn) {
      this.runAnalysis(this.engine, this.currentFen);
      this.runAnalysis(this.compareEngine, this.currentFen);
    } else if (this.sparringEngineQuiet) {
      this.runAnalysis(this.engine, this.currentFen);   // stille Engine im Sparring: nur die Haupt-Engine
    }
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
  private choicesCache?: { lang: string | null; list: ExternalEngineInfo[]; value: { id: string; name: string; tag: string | null }[] };
  get engineChoices(): { id: string; name: string; tag: string | null }[] {
    const lang = this.translate.currentLang();   // ngx-translate 18: Signal, kein String
    const c = this.choicesCache;
    if (c && c.lang === lang && c.list === this.externalEnginesList) return c.value;
    const value = [
      { id: 'wasm', name: this.translate.instant('analysis.engineBrowser'), tag: null },
      // `tag`: „· über Lichess" / „· offline" hinter dem Namen — die Auswahl mischt beide Quellen.
      ...this.externalEnginesList.map(e => ({ id: e.id, name: e.name, tag: engineTagKey(e) })),
    ];
    this.choicesCache = { lang, list: this.externalEnginesList, value };
    return value;
  }

  /** Zusatz hinter dem Engine-Namen in der Auswahl (siehe `engineTagKey`). */
  tagOf(e: ExternalEngineInfo): string | null { return engineTagKey(e); }

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
    if ((this.engineOn || this.sparringEngineQuiet) && this.dests.size > 0) this.runAnalysis(this.engine, this.currentFen);
  }

  backToPuzzle(): void {
    if (this.returnTo) this.router.navigateByUrl(this.returnTo);
  }
  reloadPage(): void { window.location.reload(); }
  flip(): void { this.orientation = this.orientation === 'white' ? 'black' : 'white'; }

  reset(): void { this.newSession(); this.resetToStart(START_FEN); }
  private resetToStart(fen = this.startFen): void { this.setTree(createRoot(fen)); }

  // ---- Stellung aufbauen (Brett-Editor) ----
  startEditing(): void { this.abortMaia(); this.editing = true; }
  onSetupApply(fen: string): void {
    this.editing = false;
    this.newSession();
    this.fenInput = '';
    this.resetToStart(fen);
  }

  loadFen(): void {
    const fen = this.fenInput.trim();
    if (!fen) return;
    if (!this.isValidFen(fen)) { this.snackbar.show(this.translate.instant('analysis.invalidFen'), { action: 'common.ok', duration: 2500 }); return; }
    this.newSession();
    this.fenInput = '';
    this.resetToStart(fen);
  }

  /** PGN samt Varianten laden (0.604.0 — vorher nur die Hauptlinie) und ans Ende der Hauptlinie springen.
   *  @param keepText Das PGN im Feld stehen lassen (Sprung aus einer Partie) — beim Einfügen von Hand wird es geleert. */
  loadPgn(keepText = false): void {
    const pgn = this.pgnInput.trim();
    if (!pgn) return;
    const parsed = parsePgnTree(pgn);
    if (!parsed) { this.snackbar.show(this.translate.instant('analysis.invalidPgn'), { action: 'common.ok', duration: 2500 }); return; }
    this.newSession(pgnTitle(parsed.headers));
    if (!keepText) this.pgnInput = '';
    const main = mainline(parsed.root);
    this.setTree(parsed.root, main[main.length - 1] ?? parsed.root);
  }

  // ---- Sterne + Zugbaum ----

  private starCache: { version: number; root: AnalysisNode | null; list: AnalysisNode[] } = { version: -1, root: null, list: [] };
  /** Markierte Stellungen, Hauptlinie zuerst (template-gebunden, deshalb je Baumstand nur einmal gesucht). */
  get starredList(): AnalysisNode[] {
    const c = this.starCache;
    if (c.version !== this.treeVersion || c.root !== this.root) {
      this.starCache = { version: this.treeVersion, root: this.root, list: starredNodes(this.root) };
    }
    return this.starCache.list;
  }

  /** Stern auf der Stellung, die gerade auf dem Brett steht (Taste S). */
  toggleStar(): void { this.toggleStarOn(this.currentNode); }

  private toggleStarOn(node: AnalysisNode): void {
    if (node.starred) delete node.starred; else node.starred = true;
    this.treeVersion++;
    this.scheduleHistorySave();
  }

  /** „12...Nf6" — der Zug, der zur markierten Stellung führte; die Wurzel = Ausgangsstellung. */
  starLabel(node: AnalysisNode): string {
    return node === this.root || !node.parent ? this.translate.instant('analysis.star.start') : numberedSan(node);
  }

  /** Menü der Zugliste (Rechtsklick bzw. langer Druck): Stern, Variante hochstufen, zur Hauptvariante, ab hier löschen. */
  onTreeAction(e: { kind: MoveTreeAction; node: AnalysisNode }): void {
    const current = this.currentNode;
    switch (e.kind) {
      case 'star': this.toggleStarOn(e.node); return;
      case 'promote': promote(e.node); break;
      case 'mainline': makeMainline(e.node); break;
      case 'delete': {
        const within = isWithin(current, e.node);
        // Fällt die Ausgangsstellung des Sparrings weg, endet es (Maia-Regel 9).
        const endsSparring = !!this.sparring && isWithin(this.sparring.start, e.node);
        const parent = removeNode(e.node);
        if (!parent) return;
        if (endsSparring) this.endSparring();
        // Fiel nur ein Teil der laufenden Partie weg, ist ihr letzter Zug jetzt der Zug davor (er liegt noch unter `start`).
        if (this.sparring && !this.isAttached(this.sparring.tip)) this.sparring.tip = parent;
        // Hängt die zuletzt gespielte Partie nicht mehr am Baum, gibt es sie nicht mehr zu analysieren.
        if (this.lastSparring && !this.isAttached(this.lastSparring.tip)) this.lastSparring = null;
        this.treeVersion++;
        // Stand man in dem, was wegfällt, geht es beim Zug davor weiter.
        if (within) { this.goToNode(parent); return; }
        if (endsSparring) {
          this.treeVersion++;
          this.line = lineThrough(current);
          this.refresh();   // die Engine läuft wieder (falls sie vorher an war)
          return;
        }
        break;
      }
    }
    // Brett und Engine bleiben, nur die Reihenfolge änderte sich: die Linie durch den aktuellen Zug neu ziehen.
    this.treeVersion++;
    this.line = lineThrough(current);
    this.scheduleHistorySave();
  }

  // ---- Sparring gegen Maia ----
  //
  // Maias Züge laufen über goToNode → refresh → scheduleHistorySave: die gespielte Linie landet ohne weiteres Zutun im
  // Zugbaum und im Analyse-Verlauf. Die Karte (MaiaSparringCardComponent) lädt das Modell; hier steht nur das Spiel.

  /** Sparring läuft, Maia ist am Zug, und die Stellung ist nicht zu Ende — die Karte bietet dann „Maia zieht" an. */
  get maiaToMove(): boolean {
    return !!this.sparring && this.turnColor !== this.sparring.userColor && this.dests.size > 0;
  }

  /** Von der Karte, sobald das Modell bereit ist: ab HIER, der Nutzer spielt die Seite am Zug, die Engine geht aus
   *  (ihr Zustand wird gemerkt, aber NICHT in localStorage — die Dauereinstellung des Nutzers bleibt). */
  startSparring(): void {
    if (this.sparring || this.editing || this.dests.size === 0) return;   // zu Ende: es gibt nichts zu spielen
    this.sparring = { start: this.currentNode, tip: this.currentNode, userColor: this.turnColor, engineWasOn: this.engineOn };
    this.engineOn = false;
    this.orientation = this.sparring.userColor;
    this.refresh();
    this.cdr.markForCheck();
  }

  /** „Beenden": eine laufende Antwort verfällt, die Engine kommt in den Zustand von vorher zurück. */
  stopSparring(): void {
    if (!this.sparring) return;
    this.endSparring();
    if (this.sparringAnalyzeVisible) this.ensureAnalyzeStatus();
    this.refresh();
    this.cdr.markForCheck();
  }

  /** „Seite wechseln": ist jetzt Maia am Zug, zieht sie gleich. */
  switchSparringSides(): void {
    if (!this.sparring) return;
    this.abortMaia();
    const userColor: Color = this.sparring.userColor === 'white' ? 'black' : 'white';
    this.sparring = { ...this.sparring, userColor };
    this.sparringWarning = null;
    this.orientation = userColor;
    if (this.maiaToMove) this.requestMaiaMove();
    this.cdr.markForCheck();
  }

  /** „Nochmal ab der Ausgangsstellung": zurück zum Start; ist dort Maia am Zug, zieht sie. */
  restartSparring(): void {
    if (!this.sparring) return;
    this.sparringWarning = null;
    this.goToNode(this.sparring.start);
    if (this.maiaToMove) this.requestMaiaMove();
    this.cdr.markForCheck();
  }

  /** Maia nach ihrem Zug in der aktuellen Stellung fragen und ihn — wenn er noch gefragt ist — wie einen eigenen Zug
   *  in den Baum spielen. Eine Navigation, ein Abbruch oder das Ende des Sparrings dazwischen lassen ihn verfallen. */
  requestMaiaMove(): void {
    // Der eigene Zug davor (aus onMove) — nur dieser Aufruf darf ihn prüfen.
    const before = this.pendingBefore;
    this.pendingBefore = null;
    if (!this.maiaToMove) return;
    const epoch = ++this.maiaEpoch;
    const node = this.currentNode;
    this.maiaThinking = true;
    this.cdr.markForCheck();
    const pause = this.maiaDelayMs > 0
      ? new Promise<void>(resolve => setTimeout(resolve, this.maiaDelayMs))
      : Promise.resolve();
    // „Schlechte Züge melden": Maias Antwort wartet auf das Urteil. So steht die Warnung schon da, wenn ihr Zug aufs Brett
    // kommt, und die Suche der Stellung nach dem eigenen Zug wird nicht von Maias Zug abgewürgt.
    const check = before && this.warnBadMoves && this.sparring
      ? this.awaitMoveCheck(node.fen, this.sparring.userColor, before.points, epoch)
      : Promise.resolve(null);
    Promise.all([this.maia.chooseMove(node.fen, this.maiaElo), pause, check]).then(
      ([uci, , verdict]) => {
        if (!this.maiaStillWanted(epoch, node)) return;
        this.maiaThinking = false;
        if (before) {
          this.sparringWarning = verdict
            ? { san: numberedSan(node), before: before.node, beforeText: verdict.before.evalText,
                afterText: verdict.after.evalText, drop: verdict.drop }
            : null;
        }
        if (uci) {
          const next = playUci(node, uci);
          if (next) {
            this.treeVersion++;
            this.goToNode(next);   // zählt maiaEpoch selbst hoch — die Antwort ist da schon verbucht
            this.noteSparringMove(next);
          } else {
            this.snackbar.warn(this.translate.instant('analysis.maia.moveFailed'));
          }
        }
        this.cdr.markForCheck();
      },
      () => {
        this.cancelMoveCheck(epoch);   // Maias Zug kam nicht — die Prüfung dieses Aufrufs braucht niemand mehr
        if (!this.maiaStillWanted(epoch, node)) return;
        this.maiaThinking = false;
        this.snackbar.warn(this.translate.instant('analysis.maia.moveFailed'));   // das Sparring bleibt aktiv
        // Ist die Sitzung selbst weg (Worker gestorben → Status `error`), im Hintergrund neu aufbauen: das Modell liegt
        // im Cache, der nächste Klick auf „Maia zieht" geht dann wieder. War es nur diese eine Anfrage, bleibt alles.
        if (this.maia.status() !== 'ready') this.maia.prepare().catch(() => {});
        this.cdr.markForCheck();
      },
    );
  }

  onMaiaEloChange(elo: number): void {
    if (!(MAIA_ELO_OPTIONS as readonly number[]).includes(elo)) return;
    this.maiaElo = elo;   // gilt ab dem nächsten Maia-Zug
    try { localStorage.setItem(MAIA_ELO_KEY, String(elo)); } catch {}
  }

  private maiaStillWanted(epoch: number, node: AnalysisNode): boolean {
    return epoch === this.maiaEpoch && !!this.sparring && this.currentNode === node;
  }

  /** Eine laufende Maia-Antwort verfallen lassen (Navigation, Seite wechseln, Editor, Ende). */
  private abortMaia(): void {
    this.maiaEpoch++;
    this.maiaThinking = false;
    this.cancelMoveCheck();
  }

  /** Sparring beenden, ohne neu zu zeichnen — die Aufrufer tun es selbst (refresh bzw. goToNode). Die gespielte Partie
   *  bleibt für „Partie analysieren" stehen, wenn sie mindestens zwei Halbzüge hat. */
  private endSparring(): void {
    const sparring = this.sparring;
    if (!sparring) return;
    this.abortMaia();
    this.engineOn = sparring.engineWasOn;
    this.sparring = null;
    this.pendingBefore = null;
    this.sparringWarning = null;
    this.lastSparring = this.sparringPlies(sparring.start, sparring.tip) >= 2
      ? { start: sparring.start, tip: sparring.tip, userColor: sparring.userColor, elo: this.maiaElo }
      : null;
  }

  // ---- „Schlechte Züge melden" + „Bewertungsleiste anlassen" (0.645.0) ----

  onWarnBadMovesChange(on: boolean): void {
    this.warnBadMoves = on;
    try { localStorage.setItem(MAIA_WARN_KEY, on ? '1' : '0'); } catch {}
    if (!on) { this.pendingBefore = null; this.cancelMoveCheck(); this.sparringWarning = null; }
    if (this.sparring) this.refresh();   // die stille Engine startet bzw. stoppt sofort
    this.cdr.markForCheck();
  }

  onKeepEvalBarChange(on: boolean): void {
    this.keepEvalBar = on;
    try { localStorage.setItem(MAIA_EVALBAR_KEY, on ? '1' : '0'); } catch {}
    if (this.sparring) this.refresh();   // aus → Leiste neutral; an → sie folgt ab der nächsten belastbaren Tiefe
    this.cdr.markForCheck();
  }

  /** „Analysieren" an der Warnung: Sparring beenden (die Partie bleibt für „Partie analysieren"), Engine EIN — nur für
   *  diese Sitzung, nicht in localStorage — und auf die Stellung VOR dem schlechten Zug; er steht dort als Fortsetzung im
   *  Zugbaum, die Linien zeigen, was besser war. */
  analyzeWarning(): void {
    const w = this.sparringWarning;
    if (!w || !this.sparring) return;
    this.endSparring();
    this.sparringWarning = null;
    if (this.sparringAnalyzeVisible) this.ensureAnalyzeStatus();
    this.engineOn = true;
    if (this.isAttached(w.before)) this.goToNode(w.before);
    else this.refresh();
    this.cdr.markForCheck();
  }

  /** Urteil über den eigenen Zug: sobald die Suche der Stellung nach dem Zug `min(MAIA_CHECK_DEPTH, Tiefe)` erreicht oder
   *  vorher endet; spätestens nach `maiaCheckTimeoutMs` mit dem besten Wert ab EVAL_SETTLE_DEPTH, sonst `null`. */
  private awaitMoveCheck(fen: string, mover: Color, before: EvalPoint[], epoch: number): Promise<BadMoveVerdict | null> {
    this.cancelMoveCheck();
    return new Promise(resolve => {
      const timer = setTimeout(() => this.finishMoveCheck(this.deepestPoint(fen, EVAL_SETTLE_DEPTH)), this.maiaCheckTimeoutMs);
      this.pendingCheck = { fen, mover, before, epoch, resolve, timer };
      this.serveMoveCheck(fen);   // die Spur der Stellung kann schon tief genug sein
    });
  }

  /** Aus onEngineUpdate: ist die Prüfung dieser Stellung so weit? */
  private serveMoveCheck(fen: string): void {
    const check = this.pendingCheck;
    if (!check || check.fen !== fen) return;
    const after = this.deepestPoint(fen);
    if (!after) return;
    if (after.depth >= Math.min(MAIA_CHECK_DEPTH, this.depthSetting) || !this.running) this.finishMoveCheck(after);
  }

  private finishMoveCheck(after: EvalPoint | null): void {
    const check = this.pendingCheck;
    if (!check) return;
    clearTimeout(check.timer);
    this.pendingCheck = null;
    check.resolve(after ? badMoveVerdict(check.before, after, check.mover) : null);
  }

  /** Eine offene Prüfung ohne Urteil beenden (Navigation, Abbruch, Ende) — nur die eines bestimmten Aufrufs, wenn
   *  `epoch` angegeben ist. */
  private cancelMoveCheck(epoch?: number): void {
    const check = this.pendingCheck;
    if (!check || (epoch !== undefined && check.epoch !== epoch)) return;
    clearTimeout(check.timer);
    this.pendingCheck = null;
    check.resolve(null);
  }

  // ---- „Partie analysieren" nach dem Sparring ----
  //
  // Derselbe Weg wie in „Meine Partien": die Sparring-Partie wird eine gewöhnliche Partie (`POST /api/games/import`, mit
  // der eigenen Seite) und läuft dann durch `AnalyzeGameService.submit` — keine eigene Analyse, keine eigene Engine-Wahl.

  /** Ein Zug kam während des Sparrings aufs Brett (eigener, Maias, aus Explorer/Repertoire): liegt er unter `start`, ist
   *  er der neue letzte Zug der Partie — auch nach Zurückgehen und anders Weiterspielen. */
  private noteSparringMove(node: AnalysisNode): void {
    if (!this.sparring || !isWithin(node, this.sparring.start)) return;
    this.sparring.tip = node;
    if (this.sparringAnalyzeVisible) this.ensureAnalyzeStatus();
  }

  /** Den Knopf zeigen: angemeldet, eine Partie mit mindestens zwei Halbzügen, und entweder ist das Sparring vorbei oder
   *  seine Stellung zu Ende (mitten in der Partie hat die Karte schon vier Symbole). */
  get sparringAnalyzeVisible(): boolean {
    if (!this.auth.isLoggedIn) return false;
    const s = this.sparring;
    if (s) return this.sparringPlies(s.start, s.tip) >= 2 && this.currentNode === s.tip && this.dests.size === 0;
    return !!this.lastSparring;
  }

  analyzeSparring(): void {
    if (this.analyzingSparring || !this.sparringAnalyzeVisible) return;
    if (this.sparring) this.stopSparring();   // läuft es noch (Stellung zu Ende), endet es wie mit „Beenden"
    const game = this.lastSparring;
    if (!game) return;
    const startDepth = pathTo(game.start).length;
    const pgn = buildSparringPgn({
      startFen: game.start.fen,
      sans: pathTo(game.tip).slice(startDepth).map(n => n.san),
      userColor: game.userColor,
      userName: this.auth.currentUser?.username ?? '',
      elo: game.elo,
      date: new Date(),
    });
    if (!pgn) { this.snackbar.warn(this.translate.instant('analysis.maia.saveFailed')); return; }
    this.analyzingSparring = true;
    this.cdr.markForCheck();
    const done = () => { this.analyzingSparring = false; this.cdr.markForCheck(); };
    this.games.importPgn(pgn, game.userColor).subscribe({
      next: res => {
        const id = res?.ids?.[0];
        if (!id) { this.snackbar.warn(this.translate.instant('analysis.maia.saveFailed')); done(); return; }
        // Absage (keine Engine, Deckel): die Snackbar nennt den Grund, die Partie liegt trotzdem in „Meine Partien".
        this.analyzeGame.submit(this.games.analyzeUrl(id), this.analyzeStatus).subscribe(ok => {
          done();
          if (ok) void this.router.navigate(['/games', id]);
        });
      },
      error: () => { this.snackbar.warn(this.translate.instant('analysis.maia.saveFailed')); done(); },
    });
  }

  /** Halbzüge von `start` bis `tip` (`tip` liegt im Teilbaum von `start`). */
  private sparringPlies(start: AnalysisNode, tip: AnalysisNode): number {
    return pathTo(tip).length - pathTo(start).length;
  }

  /** Hängt der Knoten noch am aktuellen Baum? (Gelöschte Zweige verlieren ihren Elternknoten.) */
  private isAttached(node: AnalysisNode): boolean {
    let n: AnalysisNode | null = node;
    while (n?.parent) n = n.parent;
    return n === this.root;
  }

  /** Die Engine-Auskunft höchstens einmal holen — nur angemeldet. */
  private ensureAnalyzeStatus(): void {
    if (this.analyzeStatusAsked || !this.auth.isLoggedIn) return;
    this.analyzeStatusAsked = true;
    this.analyzeGame.status().subscribe(status => { this.analyzeStatus = status; this.cdr.markForCheck(); });
  }

  // ---- Analyse-Verlauf ----

  openHistory(): void {
    this.dialog.open(AnalysisHistoryDialogComponent, { width: '560px', maxWidth: '96vw' }).afterClosed()
      .subscribe((e: AnalysisHistoryEntry | undefined) => { if (e) this.openHistoryEntry(e); });
  }

  /** Einen Eintrag aufs Brett: Ausgangsstellung, Zugbaum samt Varianten und Sternen, der Zug von damals — und weiter
   *  speichern unter DERSELBEN Kennung. Die Liste trägt den Baum nicht mit; dann wird der Eintrag erst geholt. */
  openHistoryEntry(e: AnalysisHistoryEntry): void {
    if (!this.isValidFen(e.startFen)) return;
    if (!e.tree) {
      this.history.get(e.id).subscribe({
        next: full => { if (full.tree) this.openHistoryEntry(full); },
        error: () => this.snackbar.warn(this.translate.instant('analysis.history.loadFailed')),
      });
      return;
    }
    this.flushHistory();
    this.historySession++;
    this.historyId = e.id;
    this.historyTitle = e.title;
    const { root, nodes } = fromDto(e.startFen, e.tree);
    this.setTree(root, (e.current >= 0 ? nodes[e.current] : null) ?? root);
    this.lastHistorySig = this.historySignature();   // eben geladen = schon gespeichert
    this.cdr.markForCheck();
  }

  /** Eine neue Analyse beginnt (Zurücksetzen, FEN/PGN/Stellung geladen): der bisherige Stand geht noch raus, dann
   *  ohne Kennung weiter (der neue Baum bringt keine Sterne mit). */
  private newSession(title: string | null = null): void {
    this.flushHistory();
    this.historySession++;
    this.historyId = null;
    this.historyTitle = title;
    this.lastHistorySig = '';
  }

  private flushHistory(): void {
    if (this.historyTimer) { clearTimeout(this.historyTimer); this.historyTimer = null; this.saveHistoryNow(); }
  }

  /** Gedrosselt speichern — nur angemeldet, und nur, was eine Analyse IST (Züge, eigene Stellung oder Sterne). */
  private scheduleHistorySave(): void {
    if (!this.auth.isLoggedIn) return;
    if (this.historyTimer) clearTimeout(this.historyTimer);
    this.historyTimer = setTimeout(() => { this.historyTimer = null; this.saveHistoryNow(); }, HISTORY_SAVE_MS);
  }

  /** Was gespeichert würde, als Vergleichswert — gleich = nichts zu tun. */
  private historySignature(): string {
    const { tree, current } = toDto(this.root, this.currentNode);
    return JSON.stringify([this.startFen, tree, current, this.historyTitle ?? '']);
  }

  private saveHistoryNow(): void {
    if (!this.auth.isLoggedIn) return;
    if (this.root.children.length === 0 && this.startFen === START_FEN && !this.root.starred) return;
    const { tree, current } = toDto(this.root, this.currentNode);
    const sig = JSON.stringify([this.startFen, tree, current, this.historyTitle ?? '']);
    if (sig === this.lastHistorySig) return;
    // Läuft noch ein Speichern (ohne Kennung), würde ein zweites einen zweiten Eintrag anlegen — danach nachholen.
    if (this.historySaving) { this.historyAgain = true; return; }
    this.historySaving = true;
    const session = this.historySession;
    this.history.save({ id: this.historyId, startFen: this.startFen, title: this.historyTitle, tree, current }).subscribe({
      next: e => {
        this.historySaving = false;
        // Nur übernehmen, wenn inzwischen keine NEUE Analyse begonnen hat.
        if (this.historySession === session) { this.historyId = e.id; this.lastHistorySig = sig; }
        if (this.historyAgain) { this.historyAgain = false; this.saveHistoryNow(); }
      },
      error: () => { this.historySaving = false; this.historyAgain = false; },   // Hintergrund: der nächste Zug versucht es wieder
    });
  }

  private isValidFen(fen: string): boolean {
    try { new Chess(fen); return true; } catch { return false; }
  }
}

/** „Weiß – Schwarz" aus den PGN-Kopfdaten (ohne „?"), sonst null. */
function pgnTitle(h: Record<string, string>): string | null {
  const white = h['White'] && h['White'] !== '?' ? h['White'] : null;
  const black = h['Black'] && h['Black'] !== '?' ? h['Black'] : null;
  if (white && black) return `${white} – ${black}`;
  return white ?? black ?? (h['Event'] && h['Event'] !== '?' ? h['Event'] : null);
}
