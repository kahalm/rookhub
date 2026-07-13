import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { ThemeService } from '../../core/theme.service';

/**
 * Karte „Theme-Auswahl" (System/Hell/Dunkel). Aus <c>ProfileComponent</c> ausgegliedert;
 * self-contained — liest/schreibt die Präferenz direkt über den <see cref="ThemeService"/>.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-theme-card',
  standalone: true,
  imports: [CommonModule, MatButtonToggleModule, MatIconModule, TranslatePipe],
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
    </div>
  `,
  styles: [`
    .theme-section h4 { margin: 0 0 0.75rem; color: #90caf9; }
    .theme-toggle { display: flex; flex-wrap: wrap; }
    .theme-toggle mat-button-toggle { display: flex; align-items: center; gap: 6px; }
  `]
})
export class ThemeCardComponent {
  constructor(public theme: ThemeService) {}
}
