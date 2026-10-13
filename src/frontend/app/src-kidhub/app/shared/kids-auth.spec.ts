import { KID_AUTH_STYLES, isAuthUrl } from './kids-auth';

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
});
