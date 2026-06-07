import { Component } from '@angular/core';
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';

/**
 * Direktlink auf die APK des jeweils NEUESTEN GitHub-Releases — bewusst ohne
 * hartkodierten Tag, damit der Link nach jedem neuen Release automatisch auf die
 * aktuelle APK zeigt (Asset-Name muss `app-release-signed.apk` bleiben).
 */
export const APK_DOWNLOAD_URL =
  'https://github.com/kahalm/rookhub/releases/latest/download/app-release-signed.apk';

@Component({
  selector: 'app-app-install-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslateModule],
  template: `
    <h2 mat-dialog-title>{{ 'install.title' | translate }}</h2>
    <mat-dialog-content>
      <p>{{ 'install.intro' | translate }}</p>
      <ol class="steps">
        <li>{{ 'install.step1' | translate }}</li>
        <li>{{ 'install.step2' | translate }}</li>
        <li>{{ 'install.step3' | translate }}</li>
      </ol>
      <p class="ios-note">{{ 'install.iosNote' | translate }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button>
      <a mat-raised-button color="primary" [href]="apkUrl" target="_blank" rel="noopener noreferrer" download>
        <mat-icon>android</mat-icon>
        {{ 'install.download' | translate }}
      </a>
    </mat-dialog-actions>
  `,
  styles: [`
    .steps { margin: 0 0 1rem; padding-left: 1.25rem; }
    .steps li { margin-bottom: 0.5rem; }
    .ios-note { font-size: 0.85rem; color: color-mix(in srgb, currentColor 47%, transparent); margin-top: 0.5rem; }
    a[mat-raised-button] mat-icon { margin-right: 4px; vertical-align: middle; }
  `]
})
export class AppInstallDialogComponent {
  readonly apkUrl = APK_DOWNLOAD_URL;
}
