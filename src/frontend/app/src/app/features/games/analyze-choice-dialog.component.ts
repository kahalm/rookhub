import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/** Antwort des Fensters: wie analysiert wird, und ob das die Vorgabe werden soll. */
export interface AnalyzeChoice {
  mode: 'engine' | 'blind';
  remember: boolean;
}

/**
 * „Partie analysieren — mit oder ohne Engine?" (0.855.0, Wunsch 2026-10-27). Mit Engine wie bisher (Kurve, Klassen, Fehler);
 * ohne Engine öffnet das Analysebrett ohne jede Bewertung, die eigenen Linien rechnet der Hintergrund, aufgedeckt wird später.
 * „Entscheidung merken" macht die Wahl zur Vorgabe (änderbar im Profil).
 */
@Component({
  selector: 'app-analyze-choice-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatCheckboxModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'games.analyzeChoice.title' | translate }}</h2>
    <mat-dialog-content>
      <div class="options">
        <button type="button" class="option" (click)="pick('engine')">
          <mat-icon>insights</mat-icon>
          <span class="text"><strong>{{ 'games.analyzeChoice.engine' | translate }}</strong>
            <span class="desc">{{ 'games.analyzeChoice.engineDesc' | translate }}</span></span>
        </button>
        <button type="button" class="option" (click)="pick('blind')">
          <mat-icon>visibility_off</mat-icon>
          <span class="text"><strong>{{ 'games.analyzeChoice.blind' | translate }}</strong>
            <span class="desc">{{ 'games.analyzeChoice.blindDesc' | translate }}</span></span>
        </button>
      </div>
      <mat-checkbox class="remember" [checked]="remember()" (change)="remember.set($event.checked)">
        {{ 'games.analyzeChoice.remember' | translate }}
      </mat-checkbox>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .options { display: flex; flex-direction: column; gap: 8px; }
    .option { font: inherit; color: inherit; text-align: left; cursor: pointer; display: flex; gap: 12px; align-items: flex-start;
      padding: 12px; border-radius: 12px; background: none;
      border: 1px solid color-mix(in srgb, currentColor 25%, transparent); }
    .option:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .text { display: flex; flex-direction: column; gap: 2px; }
    .desc { color: var(--mat-sys-on-surface-variant); font-size: 13px; }
    .remember { margin-top: 12px; display: block; }
  `],
})
export class AnalyzeChoiceDialogComponent {
  readonly remember = signal(false);
  private readonly ref = inject<MatDialogRef<AnalyzeChoiceDialogComponent, AnalyzeChoice>>(MatDialogRef);
  pick(mode: 'engine' | 'blind'): void { this.ref.close({ mode, remember: this.remember() }); }
}
