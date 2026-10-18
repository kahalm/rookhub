import { CUSTOM_ELEMENTS_SCHEMA, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { contrast, parseColor } from '@rh/testing/contrast';
import { KID_AUTH_HINT_COLOR, KID_AUTH_STYLES, isAuthUrl } from './kids-auth';

/** UI-Sweep 2026-10-11 (r2-k-login): auf den Anmelde-Masken steht oben „← Start". */
describe('isAuthUrl', () => {
  it('erkennt die vier Masken, auch mit Abfrage', () => {
    expect(isAuthUrl('/login')).toBeTrue();
    expect(isAuthUrl('/login?returnUrl=%2F')).toBeTrue();
    expect(isAuthUrl('/register')).toBeTrue();
    expect(isAuthUrl('/forgot-password')).toBeTrue();
    expect(isAuthUrl('/reset-password?token=x')).toBeTrue();
    expect(isAuthUrl('/')).toBeFalse();
    expect(isAuthUrl('/levels')).toBeFalse();
    expect(isAuthUrl('/privacy')).toBeFalse();
  });
});

/** a1-k-auth-mail-link-button-gap: die Pillenreihe klebte mit 8 px an der Kontaktzeile bzw. am Hauptknopf. */
describe('KID_AUTH_STYLES', () => {
  it('Pillenreihe der Masken mit 16 px Abstand nach oben', () => {
    const rule = KID_AUTH_STYLES.split('\n').find(l => l.includes('.mat-mdc-card-actions {'))!;
    expect(rule).toContain('padding: 16px 16px 8px;');
  });

  // b1-k-register-hinweise-abstand: Hinweise klebten am nächsten Feld bzw. am Knopf, eingerückt und dunkel wie Fließtext.
  it('Feldhinweise: 14 px Luft nach unten, an der Feldkante, leise Farbe mit ≥ 4,5:1 auf der weißen Karte', () => {
    const line = (needle: string) => KID_AUTH_STYLES.split('\n').find(l => l.includes(needle)) ?? '';
    expect(line('.mat-mdc-form-field-subscript-wrapper {')).toContain('margin-bottom: 14px;');
    expect(KID_AUTH_STYLES).toMatch(/\.mat-mdc-form-field-error-wrapper \{ padding: 4px 4px 0; \}/);
    expect(line('.mat-mdc-form-field-hint {')).toContain(`color: ${KID_AUTH_HINT_COLOR};`);
    const rgb = (hex: string) => parseColor(`rgb(${[1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)).join(', ')})`);
    const [hint, white] = [rgb(KID_AUTH_HINT_COLOR), rgb('#ffffff')];
    expect(contrast(hint, white)).toBeGreaterThanOrEqual(4.5);
    expect(contrast(hint, white)).withContext('leiser als der Kartentext').toBeLessThan(contrast(rgb('#1f2d3d'), white));
  });
});

/** Nachbau der Anmeldung: zwei Felder ohne Hinweis, ein Feld mit Hinweis — in den Stilen der KidHub-Hülle. */
@Component({
  standalone: true,
  imports: [MatFormFieldModule, MatInputModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  styles: [KID_AUTH_STYLES],
  template: `<app-login><div class="mat-mdc-card">
    <mat-form-field appearance="outline" class="full-width" style="display: block" id="user"><mat-label>Benutzername</mat-label><input matInput></mat-form-field>
    <mat-form-field appearance="outline" class="full-width" style="display: block" id="pass"><mat-label>Passwort</mat-label><input matInput></mat-form-field>
    <mat-form-field appearance="outline" class="full-width" style="display: block" id="nick"><mat-label>Spitzname</mat-label><input matInput><mat-hint>Ein Spitzname reicht.</mat-hint></mat-form-field>
    <mat-form-field appearance="outline" class="full-width" style="display: block" id="after"><mat-label>Weiter</mat-label><input matInput></mat-form-field>
  </div></app-login>`,
})
class MaskHostComponent {}

// d3-k-kid-login-feldluecken-ungleich: auf der Anmeldung (ohne Hinweise) standen 44–50 px Leerraum zwischen den Feldern.
describe('KID_AUTH_STYLES — Feldabstände', () => {
  it('ohne Hinweis höchstens 24 px zwischen zwei Feldern, mit Hinweis bleibt die Luft darunter', () => {
    TestBed.configureTestingModule({ imports: [MaskHostComponent], providers: [provideNoopAnimations()] });
    const f = TestBed.createComponent(MaskHostComponent);
    document.body.appendChild(f.nativeElement);
    try {
      f.detectChanges();
      const el = f.nativeElement as HTMLElement;
      const flex = (id: string) => el.querySelector(`#${id} .mat-mdc-text-field-wrapper`)!.getBoundingClientRect();
      expect(flex('pass').top - flex('user').bottom).withContext('Benutzername → Passwort').toBeLessThanOrEqual(24);
      const hint = el.querySelector('#nick .mat-mdc-form-field-hint')!.getBoundingClientRect();
      expect(hint.top).toBeGreaterThanOrEqual(flex('nick').bottom);
      expect(flex('after').top - hint.bottom).withContext('Luft unter dem Hinweis (14 px Rand an der Unterzeile)').toBeGreaterThanOrEqual(8);
    } finally {
      f.nativeElement.remove();
    }
  });
});
