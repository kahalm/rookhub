import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { OPERATOR } from '../../../environments/operator';
import { legalBackLink } from './legal-site';

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
        <mat-card-header><h1 mat-card-title>{{ 'legal.impressum.title' | translate }}</h1></mat-card-header>
        <mat-card-content>
          <h2>{{ 'legal.impressum.contactTitle' | translate }}</h2>
          <p>
            {{ 'legal.impressum.contact' | translate }}:
            <a [href]="'mailto:' + operator.email">{{ operator.email }}</a>
          </p>

<p class="back">
            <!-- Aus der App gekommen: ein Schritt zurueck; direkt aufgerufen: das Ersatzziel (UX-017). -->
            @if (back.history) {
              <a [href]="back.href" (click)="back.go($event)">{{ back.label | translate }}</a>
            } @else {
              <a [routerLink]="back.link">{{ back.label | translate }}</a>
            }
          </p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .legal-container { padding: 2rem; display: flex; justify-content: center; }
    mat-card { max-width: 760px; width: 100%; }
    /* Theme-Token statt festem Hellblau/-grau (F7-015, wie die Anmeldemaske): #90caf9 hatte im hellen Modus — KidHub
       immer, RookHub/LeagueHub auf Wunsch — 1,75:1 auf Weiss, #bdbdbd 1,9:1. */
    /* Abschnitte als h2 unter dem h1-Titel (UX-057) — Aussehen wie vorher als h4. */
    h2 { margin: 1.25rem 0 0.25rem; font-size: 1em; font-weight: bold; color: var(--mat-sys-primary); }
    a { color: var(--mat-sys-primary); }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: 0.85rem; }
    .back { margin-top: 1.5rem; }
  `]
})
export class ImpressumComponent {
  readonly operator = OPERATOR;
  /** „Zurueck" dorthin, wo man herkam; direkt aufgerufen zur Anmeldung (vorher immer fest /login). */
  readonly back = legalBackLink('legal.impressum.back');
}
