import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { OPERATOR } from '../../../environments/operator';

/**
 * Impressum (AT: §5 ECG / §25 MedienG). Route: /impressum
 * Betreiber-Identität (Name/Anschrift/UID/E-Mail) kommt aus der sprachneutralen
 * Config `environments/operator.ts` — PLATZHALTER, vor dem Go-live ausfüllen.
 * Die i18n-Dateien liefern nur noch die Beschriftungen.
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
          <h4>{{ 'legal.impressum.operatorTitle' | translate }}</h4>
          <p>
            {{ operator.name }}<br>
            {{ operator.address }}
            @if (operator.vatId) { <br>{{ operator.vatId }} }
          </p>

          <h4>{{ 'legal.impressum.contactTitle' | translate }}</h4>
          <p>
            {{ 'legal.impressum.contact' | translate }}:
            <a [href]="'mailto:' + operator.email">{{ operator.email }}</a>
          </p>

          <p class="muted">{{ 'legal.impressum.disclaimer' | translate }}</p>

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
