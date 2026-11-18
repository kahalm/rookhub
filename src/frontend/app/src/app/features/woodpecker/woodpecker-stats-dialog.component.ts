import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { WoodpeckerPlan } from './woodpecker.service';
import { WoodpeckerStatsPanelComponent } from './woodpecker-stats-panel.component';

export interface WoodpeckerStatsDialogData {
  /** Planstand beim Öffnen (Name, Zyklus, Frist); die Zahlen selbst lädt das Fenster frisch über `/stats`. */
  plan: WoodpeckerPlan;
  /** Läuft gerade eine Aufgabe, deren Uhr für die Dauer des Fensters steht? Dann sagt das Fenster es und der Knopf
   *  heißt „Weiter lösen“ statt „Schließen“. */
  solving: boolean;
}

/**
 * Woodpecker-Statistik als modales Fenster über dem Löser (Wunsch 2026-11-18: „mach die Statistik als modales Overlay
 * und pausiere das aktuelle Puzzle währenddessen“; vorher ein aufklappbarer Block in der Kontext-Karte, 0.957.0).
 * Oben die Übersicht (Zeit · Quote · je Tag), darunter die ausführliche Statistik — beides in
 * `app-woodpecker-stats-panel`. Das Anhalten der Aufgabe macht der Öffner (Buch-Löser: `openWoodpeckerStats`), das
 * Fenster zeigt es nur an.
 *
 * Inhalts-Dialog nach DESIGN 5.6: 760 px / 94vw, ein Scrollbereich mit Weiterscroll-Schatten, Titel kurz und der
 * Trainingsname als kleine Zeile darunter, eine Knopfzeile (kein ✕), `autoFocus: 'dialog'`. Schließen (Knopf, Esc,
 * Klick daneben) gibt den Fokus an den öffnenden Knopf zurück (`restoreFocus`, Material-Vorgabe).
 */
@Component({
  selector: 'app-woodpecker-stats-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe, WoodpeckerStatsPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'woodpecker.statistics' | translate }}</h2>
    <mat-dialog-content class="rh-scroll-hint wpd-content">
      <p class="wpd-sub rh-dots">
        <span class="wpd-name">{{ data.plan.name }}</span>
        <span>{{ 'woodpecker.cycleOf' | translate: { n: data.plan.currentCycle, max: data.plan.maxCycles } }}</span>
      </p>
      @if (data.solving) {
        <p class="wpd-paused" role="status">
          <mat-icon aria-hidden="true">pause_circle</mat-icon>
          <span>{{ 'woodpecker.statsPaused' | translate }}</span>
        </p>
      }
      <app-woodpecker-stats-panel [plan]="data.plan" />
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button color="primary" type="button" class="wpd-close" mat-dialog-close>
        {{ (data.solving ? 'woodpecker.statsResume' : 'common.close') | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .wpd-content { display: flex; flex-direction: column; gap: 10px; }
    .wpd-sub { margin: 0; row-gap: 2px; font-size: .85rem; color: var(--mat-sys-on-surface-variant); }
    .wpd-name { overflow-wrap: anywhere; }
    .wpd-paused { margin: 0; display: flex; align-items: center; gap: 6px; font-size: .85rem;
      color: var(--mat-sys-on-surface-variant); }
    .wpd-paused mat-icon { flex: none; font-size: 18px; width: 18px; height: 18px; }
  `],
})
export class WoodpeckerStatsDialogComponent {
  readonly data = inject<WoodpeckerStatsDialogData>(MAT_DIALOG_DATA);

  /** Öffnet das Fenster mit der Größe eines Inhalts-Dialogs (DESIGN 5.6); am Handy füllt es fast den Bildschirm. */
  static open(dialog: MatDialog, data: WoodpeckerStatsDialogData): MatDialogRef<WoodpeckerStatsDialogComponent> {
    return dialog.open(WoodpeckerStatsDialogComponent, {
      data, width: '760px', maxWidth: '94vw', maxHeight: '92vh', autoFocus: 'dialog',
    });
  }
}
