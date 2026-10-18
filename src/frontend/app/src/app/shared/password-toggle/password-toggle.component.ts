import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * Augen-Knopf zum Ein-/Ausblenden eines Passwortfelds — EINE Form für Anmeldung, Registrierung und
 * Passwort-Zurücksetzen aller fünf Oberflächen (d3-rk-login-passwort-ohne-auge, DESIGN 8.6). Steht als `matSuffix`
 * im Feld; das Eingabefeld liest den Typ über eine Vorlagen-Referenz:
 *
 * ```html
 * <input matInput [type]="pwEye.type()" …>
 * <app-password-toggle matSuffix #pwEye />
 * ```
 */
@Component({
  selector: 'app-password-toggle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <button mat-icon-button type="button" class="pw-toggle" (click)="toggle()"
            [attr.aria-pressed]="shown()"
            [attr.aria-label]="(shown() ? 'auth.passwordToggle.hide' : 'auth.passwordToggle.show') | translate">
      <mat-icon>{{ shown() ? 'visibility_off' : 'visibility' }}</mat-icon>
    </button>
  `,
})
export class PasswordToggleComponent {
  readonly shown = signal(false);
  /** Typ für das zugehörige `<input>`. */
  readonly type = computed(() => (this.shown() ? 'text' : 'password'));

  toggle(): void { this.shown.update(v => !v); }
}
