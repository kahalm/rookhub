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
