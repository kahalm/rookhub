import { isLc0Engine } from './external-engine.service';
import { Component, Inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { JOB_LINE_OPTIONS } from './analysis-job-dialog.component';

export interface GameAnalysisNodesDialogData {
  /** Die Hintergrund-Engines des Nutzers (Kennung + Anzeigename). Leer → nur Hinweis auf das Profil. */
  engines: { id: string; name: string }[];
  /** Tiefe der Stockfish-Analyse, die optional mitläuft (von der Seite). */
  depth: number;
  lines: number;
}

export interface GameAnalysisNodesDialogResult {
  engineId: string;
  nodes: number;
  multiPv: number;
  /** Zusätzlich die normale Tiefen-Analyse derselben Partie anlegen. */
  alsoDepth: boolean;
}

/** Knoten zur Auswahl; „eigener Wert" daneben. 50 000 ≈ gute Analyse, bei ~4 000 Knoten/s etwa 12 s je Stellung. */
export const NODE_OPTIONS = [10_000, 25_000, 50_000, 100_000, 200_000, 400_000];
export const CUSTOM_NODES = -1;
/** Gleiche Grenzen wie der Server (`AnalysisJobService.MinTargetNodes/MaxTargetNodes`). */
export const MIN_NODES = 1_000;
export const MAX_NODES = 50_000_000;

/**
 * „Mit lc0 …" auf der Seite Partie-Analysen: Engine + Knoten je Stellung wählen und die ganze Partie von dieser
 * Engine rechnen lassen — auf Wunsch zusätzlich zur normalen Stockfish-Analyse. Der Dialog legt nichts an, er gibt die
 * Wahl zurück; die Seite sendet die Anfragen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-game-analysis-nodes-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatDialogModule, MatButtonModule, MatCheckboxModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'gameAnalysis.nodes.title' | translate }}</h2>
    <mat-dialog-content>
      @if (data.engines.length === 0) {
        <p class="warn"><mat-icon>info_outline</mat-icon> {{ 'gameAnalysis.nodes.noEngine' | translate }}</p>
        <a mat-stroked-button routerLink="/profile" (click)="ref.close(null)">{{ 'gameAnalysis.nodes.toProfile' | translate }}</a>
      } @else {
        <p class="hint">{{ 'gameAnalysis.nodes.hint' | translate }}</p>
        <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
          <mat-label>{{ 'gameAnalysis.nodes.engine' | translate }}</mat-label>
          <mat-select [(ngModel)]="engineId" name="engine">
            @for (e of data.engines; track e.id) { <mat-option [value]="e.id">{{ e.name }}</mat-option> }
          </mat-select>
        </mat-form-field>
        <div class="row">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ 'gameAnalysis.nodes.nodes' | translate }}</mat-label>
            <mat-select [(ngModel)]="choice" name="nodes">
              @for (n of nodeOptions; track n) { <mat-option [value]="n">{{ n | number }}</mat-option> }
              <mat-option [value]="custom">{{ 'gameAnalysis.nodes.custom' | translate }}</mat-option>
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ 'gameAnalysis.nodes.lines' | translate }}</mat-label>
            <mat-select [(ngModel)]="lines" name="lines">
              @for (n of lineOptions; track n) { <mat-option [value]="n">{{ n }}</mat-option> }
            </mat-select>
          </mat-form-field>
        </div>
        @if (choice === custom) {
          <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic">
            <mat-label>{{ 'gameAnalysis.nodes.customLabel' | translate:{ min: min | number, max: max | number } }}</mat-label>
            <input matInput type="number" [(ngModel)]="customNodes" name="customNodes" [min]="min" [max]="max" />
          </mat-form-field>
        }
        <mat-checkbox [(ngModel)]="alsoDepth" name="alsoDepth">
          {{ 'gameAnalysis.nodes.alsoDepth' | translate:{ depth: data.depth } }}
        </mat-checkbox>
        <p class="hint small">{{ 'gameAnalysis.nodes.costHint' | translate }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(null)">{{ 'common.cancel' | translate }}</button>
      @if (data.engines.length > 0) {
        <button mat-flat-button color="primary" [disabled]="!valid" (click)="submit()">
          {{ 'gameAnalysis.nodes.submit' | translate }}
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: [`
    .row { display: flex; gap: 12px; flex-wrap: wrap; margin: 12px 0; }
    .row mat-form-field { flex: 1 1 140px; }
    .full { width: 100%; margin-bottom: 12px; }
    .hint { color: color-mix(in srgb, currentColor 65%, transparent); font-size: .9rem; margin: 0 0 12px; }
    .hint.small { font-size: .8rem; margin: 8px 0 0; }
    .warn { display: flex; align-items: center; gap: 6px; margin: 0 0 12px; }
  `],
})
export class GameAnalysisNodesDialogComponent {
  readonly nodeOptions = NODE_OPTIONS;
  readonly lineOptions = JOB_LINE_OPTIONS;
  readonly custom = CUSTOM_NODES;
  readonly min = MIN_NODES;
  readonly max = MAX_NODES;
  engineId: string;
  choice = 50_000;
  customNodes: number | null = null;
  lines: number;
  alsoDepth = true;

  constructor(
    public ref: MatDialogRef<GameAnalysisNodesDialogComponent, GameAnalysisNodesDialogResult | null>,
    @Inject(MAT_DIALOG_DATA) public data: GameAnalysisNodesDialogData,
  ) {
    // lc0 vorwählen, wenn es so heißt — sonst die erste Engine.
    this.engineId = (data.engines.find(e => isLc0Engine(e.name)) ?? data.engines[0])?.id ?? '';
    this.lines = JOB_LINE_OPTIONS.includes(data.lines) ? data.lines : 3;
  }

  /** Die gewählte Knotenzahl; `null`, solange der eigene Wert fehlt oder außerhalb des Bereichs liegt. */
  get nodes(): number | null {
    const n = this.choice === CUSTOM_NODES ? this.customNodes : this.choice;
    return typeof n === 'number' && Number.isInteger(n) && n >= MIN_NODES && n <= MAX_NODES ? n : null;
  }

  get valid(): boolean { return !!this.engineId && this.nodes !== null; }

  submit(): void {
    const nodes = this.nodes;
    if (!this.engineId || nodes === null) return;
    this.ref.close({ engineId: this.engineId, nodes, multiPv: this.lines, alsoDepth: this.alsoDepth });
  }
}
