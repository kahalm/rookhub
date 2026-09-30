import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { OPERATOR } from '../../../environments/operator';

/**
 * Impressum. Route: /impressum
 * Zeigt nur den Kontakt (E-Mail aus der sprachneutralen Config `environments/operator.ts`) —
 * kein Abschnitt „Diensteanbieter" mit Name/Anschrift (Entscheidung des Betreibers, 2026-09-30).
 * Die i18n-Dateien liefern die Beschriftungen.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-impressum',
  standalone: true,
  imports: [CommonModule, MatCardModule, RouterModule, TranslatePipe],
  template: `
    <div class="legal-container">
      <mat-card>
        <mat-card-header><mat-card-title>{{ 'legal.impressum.title' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <h4>{{ 'legal.impressum.contactTitle' | translate }}</h4>
          <p>
            {{ 'legal.impressum.contact' | translate }}:
            <a [href]="'mailto:' + operator.email">{{ operator.email }}</a>
          </p>

          <p class="back"><a routerLink="/login">{{ 'legal.impressum.back' | translate }}</a></p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .legal-container { padding: 2rem; display: flex; justify-content: center; }
    mat-card { max-width: 760px; width: 100%; }
    h4 { margin: 1.25rem 0 0.25rem; color: #90caf9; }
    a { color: #90caf9; }
    .muted { color: #bdbdbd; font-size: 0.85rem; }
    .back { margin-top: 1.5rem; }
  `]
})
export class ImpressumComponent {
  readonly operator = OPERATOR;
}
