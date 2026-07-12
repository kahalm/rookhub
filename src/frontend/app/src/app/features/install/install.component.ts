import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { PwaInstallService } from '../../core/pwa-install.service';

/**
 * Direktlink auf die APK des jeweils NEUESTEN GitHub-Releases — bewusst ohne
 * hartkodierten Tag, damit der Link nach jedem neuen Release automatisch auf die
 * aktuelle APK zeigt (Asset-Name muss `app-release-signed.apk` bleiben).
 */
export const APK_DOWNLOAD_URL =
  'https://github.com/kahalm/rookhub/releases/latest/download/app-release-signed.apk';

/**
 * Installationsseite (/install, offen). Bietet zwei Wege an, jeweils plattformabhängig:
 *
 *  1. Android-APK: Download + Anleitung. Nur unter Android sinnvoll installierbar →
 *     auf anderen Systemen (Mac/iOS/Desktop) wird vermerkt, dass die Direktinstallation
 *     hier nicht möglich ist (Download für ein Android-Gerät bleibt verfügbar).
 *  2. PWA: nativer Installieren-Button, wenn der Browser ihn anbietet
 *     (`beforeinstallprompt`); auf iOS Safari die manuelle „Zum Home-Bildschirm"-Anleitung;
 *     bereits installiert → Hinweis; sonst → „in diesem System nicht möglich".
 */
@Component({
  selector: 'app-install',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="install-container">
      <header class="install-header">
        <h1>{{ 'install.pageTitle' | translate }}</h1>
        <p class="subtitle">{{ 'install.pageIntro' | translate }}</p>
      </header>

      <!-- Android-APK -->
      <mat-card class="install-section">
        <mat-card-header>
          <mat-card-title><mat-icon class="sec-icon">android</mat-icon>{{ 'install.apk.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <p>{{ 'install.apk.desc' | translate }}</p>
          @if (pwa.isAndroid) {
            <ol class="steps">
              <li>{{ 'install.apk.step1' | translate }}</li>
              <li>{{ 'install.apk.step2' | translate }}</li>
              <li>{{ 'install.apk.step3' | translate }}</li>
            </ol>
            <a mat-raised-button color="primary" [href]="apkUrl" target="_blank" rel="noopener noreferrer" download>
              <mat-icon>download</mat-icon>{{ 'install.apk.download' | translate }}
            </a>
          } @else {
            <p class="unavailable"><mat-icon>info</mat-icon>{{ 'install.apk.unavailable' | translate }}</p>
            <a mat-stroked-button [href]="apkUrl" target="_blank" rel="noopener noreferrer" download>
              <mat-icon>download</mat-icon>{{ 'install.apk.downloadAnyway' | translate }}
            </a>
          }
        </mat-card-content>
      </mat-card>

      <!-- PWA / Web-App -->
      <mat-card class="install-section">
        <mat-card-header>
          <mat-card-title><mat-icon class="sec-icon">install_mobile</mat-icon>{{ 'install.pwa.title' | translate }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <p>{{ 'install.pwa.desc' | translate }}</p>
          @if (pwa.isInstalled()) {
            <p class="installed"><mat-icon>check_circle</mat-icon>{{ 'install.pwa.installed' | translate }}</p>
          } @else if (pwa.canInstallPwa()) {
            <button mat-raised-button color="primary" (click)="installPwa()">
              <mat-icon>install_desktop</mat-icon>{{ 'install.pwa.button' | translate }}
            </button>
          } @else if (pwa.isIOS) {
            <p class="ios-title">{{ 'install.pwa.iosTitle' | translate }}</p>
            <ol class="steps">
              <li>{{ 'install.pwa.ios1' | translate }}</li>
              <li>{{ 'install.pwa.ios2' | translate }}</li>
              <li>{{ 'install.pwa.ios3' | translate }}</li>
            </ol>
          } @else {
            <p class="unavailable"><mat-icon>info</mat-icon>{{ 'install.pwa.unavailable' | translate }}</p>
          }
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .install-container { max-width: 680px; margin: 0 auto; padding: 1rem; }
    .install-header { text-align: center; margin-bottom: 1rem; }
    .install-header h1 { margin: 0 0 0.25rem; }
    .subtitle { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0; }
    .install-section { margin-bottom: 1rem; }
    mat-card-title { display: flex; align-items: center; }
    .sec-icon { margin-right: 8px; }
    .steps { margin: 0 0 1rem; padding-left: 1.25rem; }
    .steps li { margin-bottom: 0.5rem; }
    .ios-title { font-weight: 500; margin-bottom: 0.5rem; }
    .unavailable, .installed { display: flex; align-items: center; gap: 6px; font-size: 0.9rem; }
    .unavailable { color: color-mix(in srgb, currentColor 55%, transparent); }
    .installed { color: #4caf50; }
    a[mat-raised-button] mat-icon, a[mat-stroked-button] mat-icon,
    button mat-icon { margin-right: 4px; vertical-align: middle; }
  `]
})
export class InstallComponent {
  readonly pwa = inject(PwaInstallService);
  readonly apkUrl = APK_DOWNLOAD_URL;

  installPwa(): void {
    void this.pwa.promptInstall();
  }
}
