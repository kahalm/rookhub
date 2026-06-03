import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { RouterModule } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

/**
 * Öffentliche Datenschutzerklärung (DSGVO). Route: /privacy — wird auch als
 * Privacy-Policy-URL in der Google Play Console hinterlegt.
 * ENTWURF: rechtlich von Betreiber/Anwalt zu prüfen; Betreiber-Daten im Impressum.
 */
@Component({
  selector: 'app-privacy',
  standalone: true,
  imports: [CommonModule, MatCardModule, RouterModule, TranslateModule],
  template: `
    <div class="legal-container">
      <mat-card>
        <mat-card-header><mat-card-title>{{ 'legal.privacy.title' | translate }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <p class="muted">{{ 'legal.privacy.updated' | translate }}</p>
          <p>{{ 'legal.privacy.intro' | translate }}</p>

          <h4>{{ 'legal.privacy.controllerTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.controller' | translate }} (<a routerLink="/impressum">{{ 'legal.impressum.title' | translate }}</a>).</p>

          <h4>{{ 'legal.privacy.dataTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.dataIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.dataAccount' | translate }}</li>
            <li>{{ 'legal.privacy.dataProfile' | translate }}</li>
            <li>{{ 'legal.privacy.dataUsage' | translate }}</li>
            <li>{{ 'legal.privacy.dataTechnical' | translate }}</li>
          </ul>

          <h4>{{ 'legal.privacy.purposesTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.purposes' | translate }}</p>

          <h4>{{ 'legal.privacy.thirdTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.thirdIntro' | translate }}</p>
          <ul>
            <li>{{ 'legal.privacy.thirdDiscord' | translate }}</li>
            <li>{{ 'legal.privacy.thirdChessresults' | translate }}</li>
            <li>{{ 'legal.privacy.thirdChesssites' | translate }}</li>
            <li>{{ 'legal.privacy.thirdLogging' | translate }}</li>
            <li>{{ 'legal.privacy.thirdHosting' | translate }}</li>
          </ul>

          <h4>{{ 'legal.privacy.storageTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.storage' | translate }}</p>

          <h4>{{ 'legal.privacy.retentionTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.retention' | translate }} <a routerLink="/account-deletion">/account-deletion</a>.</p>

          <h4>{{ 'legal.privacy.rightsTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.rights' | translate }}</p>

          <h4>{{ 'legal.privacy.contactTitle' | translate }}</h4>
          <p>{{ 'legal.privacy.contact' | translate }}: <a href="mailto:p.oberschmid@cp-solutions.at">p.oberschmid&#64;cp-solutions.at</a></p>

          <p class="back"><a routerLink="/login">{{ 'legal.privacy.back' | translate }}</a></p>
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
export class PrivacyComponent {}
