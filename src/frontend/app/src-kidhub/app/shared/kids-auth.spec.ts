import { isAuthUrl } from './kids-auth';

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
