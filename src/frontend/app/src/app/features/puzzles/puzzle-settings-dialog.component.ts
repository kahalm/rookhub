import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslateModule } from '@ngx-translate/core';
import { BOARD_THEMES, PIECE_SETS, ThemeMode } from './board-theme.util';

export interface PuzzleSettingsDialogData {
  mode: 'standard' | 'endless' | 'book';
  boardTheme: string;
  pieceSet: string;
  themeMode: ThemeMode;
  visualizationMode: number;
  vizArrowEnabled: boolean;
  stockfishDepth?: number;
  difficulty?: string;
  excludeSolved?: boolean;
  isLoggedIn?: boolean;
}

export interface PuzzleSettingsDialogResult {
  boardTheme: string;
  pieceSet: string;
  themeMode: ThemeMode;
  visualizationMode: number;
  vizArrowEnabled: boolean;
  stockfishDepth?: number;
  difficulty?: string;
  excludeSolved?: boolean;
}

@Component({
  selector: 'app-puzzle-settings-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatSelectModule, MatSlideToggleModule, TranslateModule
  ],
  template: `
    <div class="psd-header" mat-dialog-title>
      <span>{{ 'puzzles.settings.title' | translate }}</span>
      <button mat-icon-button mat-dialog-close class="psd-close-btn">
        <mat-icon>close</mat-icon>
      </button>
    </div>

    <mat-dialog-content>
      <div class="psd-preview">
        @for (sq of previewSquares; track $index) {
          <div class="psd-sq" [style.background-color]="sq.light ? lightColor : darkColor">
            <img class="psd-piece" [src]="'/piece/' + effectivePieceSet + '/' + sq.piece + '.svg'" [alt]="sq.piece" loading="lazy">
          </div>
        }
      </div>

      <div class="psd-rows">
        <div class="psd-row">
          <span class="psd-label">{{ 'puzzles.settings.pieces' | translate }}</span>
          <mat-select [(ngModel)]="pieceSetEdit" class="psd-select">
            @for (ps of PIECE_SETS; track ps.key) {
              <mat-option [value]="ps.key">{{ ps.name }}</mat-option>
            }
          </mat-select>
        </div>

        <div class="psd-row">
          <span class="psd-label">{{ 'puzzles.settings.board' | translate }}</span>
          <mat-select [(ngModel)]="boardThemeEdit" class="psd-select">
            @for (bt of BOARD_THEMES; track bt.key) {
              <mat-option [value]="bt.key">{{ bt.name }}</mat-option>
            }
          </mat-select>
        </div>

        <div class="psd-row">
          <span class="psd-label">{{ 'puzzles.settings.mode' | translate }}</span>
          <mat-select [(ngModel)]="themeModeEdit" class="psd-select">
            <mat-option value="fixed">{{ 'puzzles.settings.modeFixed' | translate }}</mat-option>
            <mat-option value="random">{{ 'puzzles.settings.modeRandom' | translate }}</mat-option>
            <mat-option value="crazy">{{ 'puzzles.settings.modeCrazy' | translate }}</mat-option>
          </mat-select>
        </div>

        <div class="psd-row">
          <span class="psd-label">{{ 'puzzles.settings.visualization' | translate }}</span>
          <mat-select [(ngModel)]="visualizationModeEdit" class="psd-select">
            @for (opt of vizLevelOptions; track opt.value) {
              <mat-option [value]="opt.value">{{ opt.label }}</mat-option>
            }
          </mat-select>
        </div>

        @if (visualizationModeEdit > 0) {
          <div class="psd-row psd-toggle-row">
            <span class="psd-label">{{ 'puzzles.settings.vizArrow' | translate }}</span>
            <mat-slide-toggle [(ngModel)]="vizArrowEnabledEdit"></mat-slide-toggle>
          </div>
        }

        @if (data.mode !== 'endless') {
          <div class="psd-row">
            <span class="psd-label">{{ 'puzzles.settings.stockfishDepth' | translate }}</span>
            <input class="psd-number-input" type="number" [(ngModel)]="stockfishDepthEdit" min="1" max="24" step="1">
          </div>
        }

        @if (data.mode === 'standard') {
          <div class="psd-row">
            <span class="psd-label">{{ 'puzzles.filters.difficulty' | translate }}</span>
            <mat-select [(ngModel)]="difficultyEdit" class="psd-select">
              <mat-option value="sehr_leicht">{{ 'puzzles.difficulty.veryEasy' | translate }}</mat-option>
              <mat-option value="leicht">{{ 'puzzles.difficulty.easy' | translate }}</mat-option>
              <mat-option value="normal">{{ 'puzzles.difficulty.normal' | translate }}</mat-option>
              <mat-option value="schwer">{{ 'puzzles.difficulty.hard' | translate }}</mat-option>
              <mat-option value="sehr_schwer">{{ 'puzzles.difficulty.veryHard' | translate }}</mat-option>
            </mat-select>
          </div>
        }

        @if (data.mode === 'standard' && data.isLoggedIn) {
          <div class="psd-row psd-toggle-row">
            <span class="psd-label">{{ 'puzzles.filters.skipSolved' | translate }}</span>
            <mat-slide-toggle [(ngModel)]="excludeSolvedEdit"></mat-slide-toggle>
          </div>
        }
      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="null">{{ 'common.cancel' | translate }}</button>
      <button mat-raised-button color="primary" (click)="save()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .psd-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0;
      margin-bottom: 4px;
      font-size: 18px;
      font-weight: 500;
    }
    .psd-close-btn { margin-left: 8px; flex-shrink: 0; }
    .psd-preview {
      display: grid;
      grid-template-columns: repeat(4, 52px);
      width: fit-content;
      margin: 0 auto 16px;
      border-radius: 4px;
      overflow: hidden;
      box-shadow: 0 2px 6px rgba(0,0,0,0.2);
    }
    .psd-sq {
      width: 52px; height: 52px;
      display: flex; align-items: center; justify-content: center;
    }
    .psd-piece { width: 44px; height: 44px; object-fit: contain; }
    .psd-rows { display: flex; flex-direction: column; }
    .psd-row {
      display: flex; align-items: center; justify-content: space-between;
      padding: 10px 0; gap: 16px;
      border-bottom: 1px solid rgba(128,128,128,0.12);
    }
    .psd-row:last-child { border-bottom: none; }
    .psd-label { font-size: 14px; flex: 1; }
    .psd-select { width: 140px; flex-shrink: 0; }
    .psd-toggle-row { gap: 16px; }
    .psd-number-input {
      width: 70px; padding: 6px 8px;
      border: 1px solid rgba(128,128,128,0.4); border-radius: 4px;
      font-size: 14px; text-align: center; background: transparent;
      color: inherit;
    }
  `]
})
export class PuzzleSettingsDialogComponent {
  readonly PIECE_SETS = PIECE_SETS;
  readonly BOARD_THEMES = BOARD_THEMES;

  boardThemeEdit: string;
  pieceSetEdit: string;
  themeModeEdit: ThemeMode;
  visualizationModeEdit: number;
  vizArrowEnabledEdit: boolean;
  stockfishDepthEdit: number;
  difficultyEdit: string;
  excludeSolvedEdit: boolean;

  readonly previewSquares = [
    { piece: 'wK', light: true  }, { piece: 'wQ', light: false },
    { piece: 'wR', light: true  }, { piece: 'wN', light: false },
    { piece: 'bK', light: false }, { piece: 'bQ', light: true  },
    { piece: 'bR', light: false }, { piece: 'bN', light: true  },
  ];

  constructor(
    private dialogRef: MatDialogRef<PuzzleSettingsDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: PuzzleSettingsDialogData
  ) {
    this.boardThemeEdit = data.boardTheme;
    this.pieceSetEdit = data.pieceSet;
    this.themeModeEdit = data.themeMode;
    this.visualizationModeEdit = data.visualizationMode;
    this.vizArrowEnabledEdit = data.vizArrowEnabled;
    this.stockfishDepthEdit = data.stockfishDepth ?? 16;
    this.difficultyEdit = data.difficulty ?? 'normal';
    this.excludeSolvedEdit = data.excludeSolved ?? false;
  }

  get effectivePieceSet(): string {
    return PIECE_SETS.find(p => p.key === this.pieceSetEdit) ? this.pieceSetEdit : 'cburnett';
  }

  get lightColor(): string {
    return BOARD_THEMES.find(t => t.key === this.boardThemeEdit)?.light ?? '#f0d9b5';
  }

  get darkColor(): string {
    return BOARD_THEMES.find(t => t.key === this.boardThemeEdit)?.dark ?? '#b58863';
  }

  get vizLevelOptions(): { value: number; label: string }[] {
    return [
      { value: 0, label: 'Normal' },
      { value: 1, label: 'Blindfold' },
      { value: 2, label: 'Checker' },
      { value: 3, label: 'Dark' },
      { value: 4, label: 'Invisible' },
    ];
  }

  save(): void {
    this.dialogRef.close({
      boardTheme: this.boardThemeEdit,
      pieceSet: this.pieceSetEdit,
      themeMode: this.themeModeEdit,
      visualizationMode: this.visualizationModeEdit,
      vizArrowEnabled: this.vizArrowEnabledEdit,
      stockfishDepth: this.stockfishDepthEdit,
      difficulty: this.difficultyEdit,
      excludeSolved: this.excludeSolvedEdit,
    } as PuzzleSettingsDialogResult);
  }
}
