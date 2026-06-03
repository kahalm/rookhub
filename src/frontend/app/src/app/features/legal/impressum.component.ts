import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

/**
 * Impressum (AT: §5 ECG / §25 MedienG). Route: /impressum
 * ACHTUNG: Betreiber-Identität (Name/Anschrift/UID) sind PLATZHALTER — vor dem
 * Go-live durch echte Daten ersetzen (legal.impressum.* in den i18n-Dateien).
 */
@Component({
  selector: 'app-impressum',
  standalone: true,
  imports: [CommonModule, MatCardModule, RouterModule, TranslateModule],
  template: `
    <div class="legal-container">
      <mat-card>
        <mat-card-header><mat-card-title>{{ 'legal.impressum.title' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <h4>{{ 'legal.impressum.operatorTitle' | translate }}</h4>
          <p>
            {{ 'legal.impressum.name' | translate }}<br>
            {{ 'legal.impressum.address' | translate }}<br>
            {{ 'legal.impressum.uid' | translate }}
          </p>

          <h4>{{ 'legal.impressum.contactTitle' | translate }}</h4>
          <p>
            {{ 'legal.impressum.contact' | translate }}:
            <a href="mailto:p.oberschmid@cp-solutions.at">p.oberschmid&#64;cp-solutions.at</a>
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
export class ImpressumComponent {}
