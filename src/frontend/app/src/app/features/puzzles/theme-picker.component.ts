import { Component, Input, Output, EventEmitter } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';
import { BOARD_THEMES, PIECE_SETS, ThemeMode } from './board-theme.util';

/**
 * Wiederverwendbarer Brett-/Figuren-Picker (Einstellungs-Chips) für Standard-,
 * Buch- und Endless-Puzzle. Rendert nur den Inhalt (Labels + Chips) — der äußere
 * Rahmen (mat-card bzw. div.theme-section) bleibt in der Eltern-Komponente.
 * `BOARD_THEMES`/`PIECE_SETS` sind app-weit dieselben Konstanten und daher hier
 * gekapselt; nur die i18n-Namespace-Wurzel unterscheidet sich je Modus.
 */
@Component({
  selector: 'app-theme-picker',
  standalone: true,
  imports: [MatIconModule, TranslateModule],
  template: `
    <div class="theme-label">{{ namespace + '.mode' | translate }}</div>
    <div class="theme-chips">
      <div class="theme-chip" [class.active]="themeMode === 'fixed'" (click)="themeModeChanged.emit('fixed')">
        <mat-icon>palette</mat-icon><span class="theme-name">{{ namespace + '.modeNormal' | translate }}</span>
      </div>
      <div class="theme-chip" [class.active]="themeMode === 'random'" (click)="themeModeChanged.emit('random')">
        <mat-icon>shuffle</mat-icon><span class="theme-name">{{ namespace + '.modeRandom' | translate }}</span>
      </div>
      <div class="theme-chip" [class.active]="themeMode === 'crazy'" (click)="themeModeChanged.emit('crazy')">
        <mat-icon>auto_awesome</mat-icon><span class="theme-name">{{ namespace + '.modeCrazy' | translate }}</span>
      </div>
    </div>
    @if (themeMode === 'fixed') {
      <div class="theme-label" style="margin-top: 0.75rem;">{{ namespace + '.boardTheme' | translate }}</div>
      <div class="theme-chips">
        @for (t of boardThemes; track t.key) {
          <div class="theme-chip" [class.active]="boardTheme === t.key" (click)="boardThemeChanged.emit(t.key)">
            @if (t.img) {
              <div class="theme-img" [style.backgroundImage]="'url(' + t.img + ')'"></div>
            } @else {
              <div class="theme-preview">
                <div class="tp-light" [style.background]="t.light"></div>
                <div class="tp-dark" [style.background]="t.dark"></div>
              </div>
            }
            <span class="theme-name">{{ t.name }}</span>
          </div>
        }
      </div>
      <div class="theme-label" style="margin-top: 0.75rem;">{{ namespace + '.pieces' | translate }}</div>
      <div class="theme-chips">
        @for (p of pieceSets; track p.key) {
          <div class="theme-chip" [class.active]="pieceSet === p.key" (click)="pieceSetChanged.emit(p.key)">
            <div class="piece-preview" [style.backgroundImage]="'url(' + p.preview + ')'"></div>
            <span class="theme-name">{{ p.name }}</span>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .theme-label { font-size: 0.85em; color: rgba(0,0,0,0.6); margin-bottom: 0.5rem; }
    .theme-chips { display: flex; gap: 0.5rem; flex-wrap: wrap; }
    .theme-chip {
      display: flex; flex-direction: column; align-items: center; gap: 4px;
      cursor: pointer; padding: 6px; border-radius: 8px; border: 2px solid transparent;
      transition: border-color 0.15s;
    }
    .theme-chip.active { border-color: #1976d2; }
    .theme-chip:hover { background: rgba(0,0,0,0.04); }
    .piece-preview { width: 28px; height: 28px; background-size: contain; background-repeat: no-repeat; background-position: center; }
    .theme-img { width: 32px; height: 16px; border-radius: 3px; background-size: cover; background-position: center; }
    .theme-preview { display: flex; width: 32px; height: 16px; border-radius: 3px; overflow: hidden; }
    .tp-light, .tp-dark { flex: 1; }
    .theme-name { font-size: 0.75em; color: rgba(0,0,0,0.7); }
  `],
})
export class ThemePickerComponent {
  /** i18n-Namespace-Wurzel: 'puzzles.theme' / 'book.settings' / 'endless.config'. */
  @Input() namespace = '';
  @Input() themeMode: ThemeMode = 'fixed';
  @Input() boardTheme = '';
  @Input() pieceSet = '';

  @Output() themeModeChanged = new EventEmitter<ThemeMode>();
  @Output() boardThemeChanged = new EventEmitter<string>();
  @Output() pieceSetChanged = new EventEmitter<string>();

  readonly boardThemes = BOARD_THEMES;
  readonly pieceSets = PIECE_SETS;
}
