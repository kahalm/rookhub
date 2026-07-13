import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { OPERATOR } from '../../../environments/operator';

/**
 * Öffentlich (ohne Login) erreichbare Info-Seite zur Konto-Löschung — erfüllt die
 * Google-Play-Anforderung einer öffentlich zugänglichen URL zur Löschanforderung.
 * Route: /account-deletion
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-account-deletion',
  standalone: true,
  imports: [CommonModule, MatCardModule, RouterModule, TranslatePipe],
  template: `
    <div class="legal-container">
      <mat-card>
        <mat-card-header>
          <mat-card-title>{{ 'legal.accountDeletion.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <p>{{ 'legal.accountDeletion.intro' | translate }}</p>

          <h4>{{ 'legal.accountDeletion.inAppTitle' | translate }}</h4>
          <p>{{ 'legal.accountDeletion.inApp' | translate }}</p>

          <h4>{{ 'legal.accountDeletion.removedTitle' | translate }}</h4>
          <ul>
            <li>{{ 'legal.accountDeletion.removed1' | translate }}</li>
            <li>{{ 'legal.accountDeletion.removed2' | translate }}</li>
          </ul>

          <h4>{{ 'legal.accountDeletion.keptTitle' | translate }}</h4>
          <p>{{ 'legal.accountDeletion.kept' | translate }}</p>

          <h4>{{ 'legal.accountDeletion.contactTitle' | translate }}</h4>
          <p>
            {{ 'legal.accountDeletion.contact' | translate }}:
            <a [href]="'mailto:' + operator.email">{{ operator.email }}</a>
          </p>

          <p class="back"><a routerLink="/login">{{ 'legal.accountDeletion.back' | translate }}</a></p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .legal-container { padding: 2rem; display: flex; justify-content: center; }
    mat-card { max-width: 720px; width: 100%; }
    h4 { margin: 1.25rem 0 0.25rem; color: #90caf9; }
    a { color: #90caf9; }
    .back { margin-top: 1.5rem; }
  `]
})
export class AccountDeletionComponent {
  readonly operator = OPERATOR;
}
