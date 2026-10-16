import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { ThemeService } from '../../core/theme.service';
import { EinkService } from '../../core/eink.service';
import { UiScaleService } from '../../core/ui-scale.service';

/**
 * Karte „Theme-Auswahl" (System/Hell/Dunkel) + E-Ink-Modus + UI-Größe (beide je Gerät). Aus <c>ProfileComponent</c> ausgegliedert;
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
      <!-- UI-Größe (docs/DESIGN.md 3.7): je Gerät gespeichert, sofort wirksam (Meta-Viewport bzw. zoom). -->
      <div class="ui-scale-row">
        <span class="ui-scale-label" id="ui-scale-label">{{ 'profile.theme.uiScale' | translate }}</span>
        <!-- Falz-Runde 16.10.: ohne Häkchen (der gewählte Wert ist eingefärbt) — die fünf Stufen passen am Handy in EINE Zeile. -->
        <mat-button-toggle-group class="ui-scale-toggle" aria-labelledby="ui-scale-label" hideSingleSelectionIndicator
                                 [value]="uiScale.scale()" (change)="setUiScale($event.value)">
          @for (step of uiScale.steps; track step) {
            <mat-button-toggle [value]="step">{{ 'profile.theme.uiScaleValue' | translate: { value: step } }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
        <p class="ui-scale-hint">{{ 'profile.theme.uiScaleHint' | translate }}</p>
        @if (uiScale.unsupported() && uiScale.scale() !== 100) {
          <p class="ui-scale-note">{{ 'profile.theme.uiScaleUnsupported' | translate }}</p>
        }
        <!-- Befund zur Fehlersuche am Gerät (0.800.2): Weg + Breiten, damit man ablesen kann, was der Browser tut. -->
        @if (uiScale.diagnostics(); as d) {
          <p class="ui-scale-diag">{{ 'profile.theme.uiScaleDiag' | translate: { mode: d.mode ?? '–', layout: d.layoutWidth, screen: d.screenWidth, ok: d.viewportOk === null ? '–' : (d.viewportOk ? '✓' : '✗') } }}</p>
        }
      </div>
    </div>
  `,
  styles: [`
    .theme-section h4 { margin: 0 0 0.75rem; color: var(--rh-accent); }
    /* Nur so breit wie die drei Segmente — auf voller Breite blieb rechts ein leeres Feld stehen. */
    .theme-toggle { display: inline-flex; flex-wrap: wrap; max-width: 100%; }
    .theme-toggle mat-button-toggle { display: flex; align-items: center; gap: 6px; }
    .theme-eink-note { margin: 0.5rem 0 0; color: var(--mat-sys-on-surface-variant); }
    /* Falz-Runde 16.10.: die Karte steht auf Nutzerwunsch GANZ OBEN im Profil — daher knapp: Hinweise in kleiner Schrift
       direkt unter ihrem Schalter, 12 px zwischen den Zeilen, Gerätebefund noch eine Stufe leiser (0,75 rem). */
    .eink-row { margin-top: 0.75rem; }
    .eink-toggle { min-height: 44px; display: flex; align-items: center; }
    .eink-hint, .ui-scale-hint { margin: 0 0 0; font-size: 0.8125rem; line-height: 1.35; color: var(--mat-sys-on-surface-variant); }
    .ui-scale-row { margin-top: 0.75rem; }
    .ui-scale-label { display: block; margin-bottom: 0.375rem; }
    .ui-scale-toggle { display: inline-flex; flex-wrap: wrap; max-width: 100%; margin-bottom: 0.25rem; }
    .ui-scale-note { margin: 0.5rem 0 0; color: var(--rh-warn); }
    .ui-scale-diag { margin: 0.125rem 0 0; font-size: 0.75rem; line-height: 1.3; color: var(--mat-sys-on-surface-variant); }
    @media (max-width: 599.98px) {
      .ui-scale-toggle ::ng-deep .mat-button-toggle-label-content { padding: 0 10px; }
    }
  `]
})
export class ThemeCardComponent {
  readonly eink = inject(EinkService);
  readonly uiScale = inject(UiScaleService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);

  constructor(public theme: ThemeService) {
    this.uiScale.refreshDiagnostics();
  }

  /** Umschalten; scheitert das Speichern (Speicher gesperrt), gilt der Modus nur bis zum Neuladen — das sagen wir. */
  setEink(on: boolean): void {
    if (!this.eink.set(on)) this.snackbar.warn(this.translate.instant('profile.theme.einkNotSaved'));
  }

  /** UI-Größe wählen; scheitert das Speichern, gilt sie nur bis zum Neuladen — das sagen wir. */
  setUiScale(pct: number): void {
    if (!this.uiScale.set(pct)) this.snackbar.warn(this.translate.instant('profile.theme.uiScaleNotSaved'));
  }
}
