import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { NgIf } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { defaults as chessgroundDefaults } from 'chessground/state';
import { EngineDisplayLine } from './engine-lines.util';
import { RemoteInterruption } from './analysis-engine.service';
import { ExternalEngineInfo, engineTagKey } from './external-engine.service';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { SanPipe } from '../../shared/chess/san.pipe';

// Fünf verschiedene Pinsel (UX-049: die fünfte Linie war wieder Blau). Die Linienliste setzt dieselbe Farbe als Punkt vor
// jede Linie — die Farben kommen aus den Standardpinseln von chessground, also genau die des Pfeils auf dem Brett.
const ARROW_BRUSHES = ['green', 'blue', 'yellow', 'red', 'purple'];
export const arrowBrush = (i: number): string => ARROW_BRUSHES[i] || 'blue';
const BOARD_BRUSHES = chessgroundDefaults().drawable.brushes;

/** Eine wählbare Engine (Browser + registrierte externe) — für beide Auswahlfelder. */
export interface EngineChoice { id: string; name: string; tag: string | null }

/**
 * Die Engine-Karte des Analysebretts: Schalter, Tiefe/Linien, Engine-Wahl, Vergleichsmodus, Hinweise (Rückfall, Abriss,
 * „rechnet seit …") und die Linienlisten beider Engines. Rein darstellend — Zustand und Suche führt `AnalysisComponent`,
 * jede Wahl geht als Ereignis zurück. Was am Ende der Karte steht (Endspiel-Datenbank), kommt per `<ng-content>`.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-analysis-engine-card',
  standalone: true,
  imports: [
    NgIf, FormsModule, MatCardModule, MatButtonModule, MatIconModule, MatSlideToggleModule, MatFormFieldModule,
    MatSelectModule, MatTooltipModule, TranslatePipe, HelpHintComponent, IconLabelDirective, SanPipe,
  ],
  template: `
    <mat-card class="engine-card">
      <mat-card-content>
        <div class="engine-head">
          <!-- Während des Sparrings gesperrt: die Engine verriete Bewertung und Pfeile (Maia-Regel 2). -->
          <mat-slide-toggle [ngModel]="engineOn" (ngModelChange)="engineToggle.emit($event)" [disabled]="sparring">{{ 'analysis.engine' | translate }}</mat-slide-toggle>
          <span class="depth" *ngIf="engineOn">{{ 'analysis.depth' | translate }} {{ depth }}/{{ depthSetting }} · <span class="search-time" [title]="'analysis.searchTime' | translate">{{ searchTime }}</span></span>
          <span class="he-spacer"></span>
          <span class="num-pair">
            <mat-form-field appearance="outline" class="num-field" subscriptSizing="dynamic">
              <mat-label>{{ 'analysis.depth' | translate }}</mat-label>
              <mat-select [ngModel]="depthSetting" (ngModelChange)="depthChange.emit($event)">
                @for (d of depthOptions; track d) { <mat-option [value]="d">{{ d }}</mat-option> }
              </mat-select>
            </mat-form-field>
            <mat-form-field appearance="outline" class="num-field" subscriptSizing="dynamic">
              <mat-label>{{ 'analysis.lines' | translate }}</mat-label>
              <mat-select [ngModel]="linesCount" (ngModelChange)="linesChange.emit($event)">
                @for (n of [1,2,3,4,5]; track n) { <mat-option [value]="n">{{ n }}</mat-option> }
              </mat-select>
            </mat-form-field>
          </span>
          @if (engines.length > 0) {
            <mat-form-field appearance="outline" class="engine-field" subscriptSizing="dynamic">
              <mat-label>{{ 'analysis.engineProvider' | translate }}</mat-label>
              <mat-select [ngModel]="selectedEngineId" (ngModelChange)="engineSelect.emit($event)">
                <mat-option value="wasm">{{ 'analysis.engineBrowser' | translate }}</mat-option>
                @for (e of engines; track e.id) {
                  <mat-option [value]="e.id">{{ e.name }}@if (tagOf(e); as tag) { <span class="engine-tag">· {{ tag | translate }}</span> }</mat-option>
                }
              </mat-select>
            </mat-form-field>
          }
          @if (engineOn && !terminal) {
            <app-help-hint icon="info_outline" [text]="speedHint" />
          }
          @if (engineOn && (engines.length > 0 || compareOn)) {
            <button mat-icon-button class="cmp-btn" [class.on]="compareOn"
                    [appIconLabel]="'analysis.compareToggle' | translate"
                    (click)="compareToggle.emit()">
              <mat-icon>balance</mat-icon>
            </button>
          }
        </div>
        @if (compareOn && engineOn && engines.length > 0) {
          <div class="cmp-pick">
            <mat-form-field appearance="outline" class="engine-field" subscriptSizing="dynamic">
              <mat-label>{{ 'analysis.compareWith' | translate }}</mat-label>
              <mat-select [ngModel]="compareEngineId" (ngModelChange)="compareEngineSelect.emit($event)">
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
                  <span class="line-mark" [style.background]="lineColor(i)" aria-hidden="true">{{ i + 1 }}</span>
                  <span class="line-eval" [class.neg]="!l.positive">{{ l.evalText }}</span>
                  <span class="line-san">{{ l.san | san:'text' }}</span>
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
                      <span class="line-san">{{ l.san | san:'text' }}</span>
                    </div>
                  }
                </div>
              }
            </div>
          }
        } @else {
          <p class="muted">{{ (sparring ? 'analysis.maia.enginePaused' : 'analysis.engineOff') | translate }}</p>
        }
        <ng-content />
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    :host { display: block; }
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
    .remote-fallback { display: flex; align-items: center; gap: 6px; color: var(--rh-warn); font-size: .85rem; margin: 6px 0 0; }
    .cmp-btn { width: 34px; height: 34px; line-height: 34px; opacity: .55; }
    .cmp-btn.on { opacity: 1; color: var(--rh-accent); }
    .cmp-pick { margin-top: 8px; }
    .cmp-block { margin-top: 10px; padding-top: 8px; border-top: 1px solid color-mix(in srgb, currentColor 18%, transparent); }
    .eng-label { display: flex; align-items: center; gap: 6px; font-size: .8rem; font-weight: 600; margin: 6px 0 2px;
      color: color-mix(in srgb, currentColor 75%, transparent); }
    .cmp-err { display: flex; align-items: center; gap: 6px; color: var(--rh-error); font-size: .85rem; margin: 6px 0 0; }
    .cmp-err mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .cmp-warn { font-size: 16px; width: 16px; height: 16px; color: var(--rh-warn); }
    .eng-depth { font-weight: 400; color: color-mix(in srgb, currentColor 55%, transparent); }
    .terminal-state { display: flex; align-items: center; gap: 6px; font-weight: 600; margin: 8px 0 0; }
    .terminal-state mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .remote-fallback mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .remote-cut, .thinking { display: flex; align-items: center; gap: 6px; font-size: .85rem; margin: 6px 0 0;
      color: color-mix(in srgb, currentColor 65%, transparent); }
    .remote-cut.final { color: var(--rh-warn); }
    .remote-cut mat-icon, .thinking mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .muted { color: color-mix(in srgb, currentColor 47%, transparent); font-style: italic; margin: 8px 0 0; }
    .lines { display: flex; flex-direction: column; gap: 4px; margin-top: 6px; }
    .line-row { display: flex; gap: 8px; font-size: .9rem; }
    /* d5-rk-analyse-pfeile-nur-farbe (DESIGN 3.6): die Marke trägt die Nummer der Linie — derselbe Text steht als
       Beschriftung auf ihrem Pfeil, Zuordnung also auch ohne Farbe (E-Ink: _eink.scss). */
    .line-mark { flex: 0 0 auto; align-self: center; display: inline-flex; align-items: center; justify-content: center;
      width: 16px; height: 16px; border-radius: 50%; font-size: 11px; font-weight: 700; line-height: 1; color: #fff;
      font-variant-numeric: tabular-nums; box-shadow: 0 0 0 1px color-mix(in srgb, currentColor 30%, transparent); }
    /* Bewertung aus den Zustands-Tokens (UX-015): die festen Hellthema-Toene #1b5e20/#b71c1c hatten auf der dunklen
       Karte 2,2:1 bzw. 2,6:1. */
    .line-eval { font-weight: 700; min-width: 48px; font-variant-numeric: tabular-nums; color: var(--rh-success); }
    .line-eval.neg { color: var(--rh-error); }
    .line-san { font-family: 'Courier New', monospace; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  `],
})
export class AnalysisEngineCardComponent {
  @Input() engineOn = true;
  /** Sparring gegen Maia läuft: Schalter gesperrt, statt „Engine aus" steht „pausiert". */
  @Input() sparring = false;
  @Input() depth = 0;
  @Input() depthSetting = 22;
  @Input() depthOptions: readonly number[] = [];
  @Input() linesCount = 3;
  @Input() searchTime = '';
  @Input() speedHint = '';
  /** Externe Engines für die Auswahl (leer = kein Picker). */
  @Input() engines: ExternalEngineInfo[] = [];
  @Input() selectedEngineId = 'wasm';
  @Input() engineChoices: EngineChoice[] = [];
  @Input() terminal: string | null = null;
  @Input() terminalText = '';
  @Input() remoteFallback = false;
  @Input() remoteCut: RemoteInterruption | null = null;
  @Input() showThinking = false;
  @Input() thinkingTime = '';
  @Input() slowConfigHint = false;
  @Input() displayLines: EngineDisplayLine[] = [];
  @Input() mainEngineName = '';
  // Vergleichsmodus
  @Input() compareOn = false;
  @Input() compareRunning = false;
  @Input() compareEngineId = 'wasm';
  @Input() compareEngineName = '';
  @Input() compareDepth = 0;
  @Input() compareSpeedHint = '';
  @Input() compareFallback = false;
  @Input() compareCrashed = false;
  @Input() compareLines: EngineDisplayLine[] = [];

  @Output() readonly engineToggle = new EventEmitter<boolean>();
  @Output() readonly depthChange = new EventEmitter<number>();
  @Output() readonly linesChange = new EventEmitter<number>();
  @Output() readonly engineSelect = new EventEmitter<string>();
  @Output() readonly compareToggle = new EventEmitter<void>();
  @Output() readonly compareEngineSelect = new EventEmitter<string>();

  /** Zusatz hinter dem Engine-Namen in der Auswahl (siehe `engineTagKey`). */
  tagOf(e: ExternalEngineInfo): string | null { return engineTagKey(e); }

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
}
