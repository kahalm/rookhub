import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { ThemeService } from '../../core/theme.service';
import { EinkService } from '../../core/eink.service';

/**
 * Karte „Theme-Auswahl" (System/Hell/Dunkel) + E-Ink-Modus (je Gerät). Aus <c>ProfileComponent</c> ausgegliedert;
 * self-contained — liest/schreibt die Präferenz direkt über den <see cref="ThemeService"/>.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-theme-card',
  standalone: true,
  imports: [CommonModule, MatButtonToggleModule, MatIconModule, MatSlideToggleModule, TranslatePipe],
  template: `
    <div class="theme-section">
      <h4>{{ 'profile.theme.title' | translate }}</h4>
      <mat-button-toggle-group [value]="theme.preference" (change)="theme.setPreference($event.value)" class="theme-toggle">
        <mat-button-toggle value="system">
          <mat-icon>brightness_auto</mat-icon>
          {{ 'profile.theme.system' | translate }}
        </mat-button-toggle>
        <mat-button-toggle value="light">
          <mat-icon>light_mode</mat-icon>
          {{ 'profile.theme.light' | translate }}
        </mat-button-toggle>
        <mat-button-toggle value="dark">
          <mat-icon>dark_mode</mat-icon>
          {{ 'profile.theme.dark' | translate }}
        </mat-button-toggle>
      </mat-button-toggle-group>
      @if (eink.enabled()) {
        <p class="theme-eink-note">{{ 'profile.theme.einkForcesLight' | translate }}</p>
      }
      <!-- E-Ink-Modus (docs/DESIGN.md 3.6): je Gerät gespeichert, sofort wirksam. -->
      <div class="eink-row">
        <mat-slide-toggle class="eink-toggle" [checked]="eink.enabled()" (change)="setEink($event.checked)">
          {{ 'profile.theme.eink' | translate }}
        </mat-slide-toggle>
        <p class="eink-hint">{{ 'profile.theme.einkHint' | translate }}</p>
      </div>
    </div>
  `,
  styles: [`
    .theme-section h4 { margin: 0 0 0.75rem; color: var(--rh-accent); }
    /* Nur so breit wie die drei Segmente — auf voller Breite blieb rechts ein leeres Feld stehen. */
    .theme-toggle { display: inline-flex; flex-wrap: wrap; max-width: 100%; }
    .theme-toggle mat-button-toggle { display: flex; align-items: center; gap: 6px; }
    .theme-eink-note { margin: 0.5rem 0 0; color: var(--mat-sys-on-surface-variant); }
    .eink-row { margin-top: 1rem; }
    .eink-toggle { min-height: 44px; display: flex; align-items: center; }
    .eink-hint { margin: 0.25rem 0 0; color: var(--mat-sys-on-surface-variant); }
  `]
})
export class ThemeCardComponent {
  readonly eink = inject(EinkService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  constructor(public theme: ThemeService) {}

  /** Umschalten; scheitert das Speichern (Speicher gesperrt), gilt der Modus nur bis zum Neuladen — das sagen wir. */
  setEink(on: boolean): void {
    if (!this.eink.set(on)) this.snackbar.warn(this.translate.instant('profile.theme.einkNotSaved'));
  }
}
