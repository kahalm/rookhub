import { ChangeDetectionStrategy, ChangeDetectorRef, Component, EventEmitter, Input, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { WorksheetService, WorksheetSummary } from './worksheet.service';

/**
 * Menü-Eintrag „An Aufgabenblatt senden" mit Ziel-Untermenü: ZWISCHENABLAGE zuerst (der
 * Standardweg — dort sammelt man, sortiert und wirft weg, bevor gedruckt wird), darunter die
 * benannten Blätter für alle, die schon wissen, wohin es soll.
 *
 * <p>Gehört IN ein `<mat-menu>` (Kurs, Kapitel, Puzzle-⋮). Der Host selbst darf dabei keine Box
 * aufspannen — `display: contents` lässt die Menüzeile direkt im Menü hängen, sonst bricht die
 * Material-Tastaturnavigation.</p>
 *
 * <p>Die Zielliste kommt erst beim Aufklappen (und danach aus dem Service-Gedächtnis) — ein Menü,
 * das bei jedem Öffnen der Kurs-Seite Blätter nachlädt, kostet bei jedem Klick eine Anfrage.</p>
 */
@Component({
  // Default + markForCheck: Angular 22 refresht nach HTTP-Antworten keine unmarkierte View.
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-send-to-worksheet',
  standalone: true,
  imports: [
    CommonModule, MatButtonModule, MatMenuModule, MatIconModule, MatDividerModule, MatTooltipModule,
    TranslatePipe,
  ],
  template: `
    @if (asButton) {
      <!-- Eigenständiger Knopf (Werkzeugleisten) statt Menüzeile — gleiches Ziel-Menü. -->
      <button mat-icon-button [matMenuTriggerFor]="targetMenu" [disabled]="disabled"
              (menuOpened)="loadTargets()"
              [matTooltip]="labelKey | translate" [attr.aria-label]="labelKey | translate">
        <mat-icon>post_add</mat-icon>
      </button>
    } @else {
      <button mat-menu-item [matMenuTriggerFor]="targetMenu" [disabled]="disabled"
              (menuOpened)="loadTargets()">
        <mat-icon>post_add</mat-icon>
        <span>{{ labelKey | translate }}</span>
      </button>
    }

    <mat-menu #targetMenu="matMenu">
      <button mat-menu-item (click)="pick.emit(null)">
        <mat-icon>content_paste</mat-icon>
        <span>{{ 'worksheets.clipboard' | translate }}</span>
      </button>
      @if (named.length > 0) {
        <mat-divider />
        @for (w of named; track w.id) {
          <button mat-menu-item (click)="pick.emit(w.id)">
            <mat-icon>description</mat-icon>
            <span>{{ w.name }}</span>
            <span class="stw-count">{{ w.itemCount }}</span>
          </button>
        }
      }
    </mat-menu>
  `,
  styles: [`
    :host { display: contents; }
    .stw-count { margin-left: 0.5rem; opacity: 0.6; font-size: 0.85em; }
  `],
})
export class SendToWorksheetComponent {
  /** Beschriftung der Menüzeile — je Ort anders („Kapitel", „ganzer Kurs", „letztes Puzzle"). */
  @Input() labelKey = 'worksheets.send.menu';
  @Input() disabled = false;
  /** `true` = eigenständiger Icon-Knopf für Werkzeugleisten; sonst Zeile in einem `<mat-menu>`. */
  @Input() asButton = false;

  /** Gewähltes Ziel: `null` = Zwischenablage, sonst die Blatt-ID. */
  @Output() pick = new EventEmitter<number | null>();

  named: WorksheetSummary[] = [];
  private worksheets = inject(WorksheetService);
  private cdr = inject(ChangeDetectorRef);

  loadTargets(): void {
    this.worksheets.targetList().subscribe({
      next: list => { this.named = list.filter(w => !w.isClipboard); this.cdr.markForCheck(); },
      error: () => { this.named = []; this.cdr.markForCheck(); },   // ohne Liste bleibt die Zwischenablage
    });
  }
}
