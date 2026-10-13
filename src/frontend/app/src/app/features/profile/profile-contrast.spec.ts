import { Type } from '@angular/core';
import { EngineCardComponent } from './engine-card.component';
import { OfflineSettingsCardComponent } from './offline-settings-card.component';
import { DeleteAccountCardComponent } from './delete-account-card.component';
import { ProfileComponent } from './profile.component';

/**
 * UI-Sweep a1 (a1-rk-profile-hell-blass): Hinweistexte und Zwischentitel auf /profile standen mit festen
 * Hellgrau-Werten (#bdbdbd ≈ 1,8:1, #e0e0e0 ≈ 1,2:1) auf der hellen Karte. Sie leiten sich jetzt aus der
 * Textfarbe ab (color-mix mit currentColor) bzw. aus Tokens — lesbar in hell UND dunkel.
 * Gelesen werden die KOMPILIERTEN Komponenten-Styles.
 */
function css(cmp: Type<unknown>): string {
  return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');
}

describe('Profil-Karten: Kontrast im hellen Design (a1-rk-profile-hell-blass)', () => {
  const CARDS: [string, Type<unknown>][] = [
    ['Externe Engine', EngineCardComponent],
    ['Offline', OfflineSettingsCardComponent],
    ['Konto löschen', DeleteAccountCardComponent],
    ['Profil', ProfileComponent],
  ];

  for (const [name, cmp] of CARDS) {
    it(`${name}: keine festen Hellgrau-Werte und kein weißes rgba`, () => {
      const s = css(cmp).toLowerCase();
      for (const grey of ['#bdbdbd', '#ccc', '#e0e0e0', '#9e9e9e', '#757575', 'rgba(255,255,255', 'rgba(255, 255, 255']) {
        expect(s).withContext(`${name} enthält ${grey}`).not.toContain(grey);
      }
    });
  }

  it('Hinweise mischen die Textfarbe (≥ 55 % Deckkraft)', () => {
    const pick = (cmp: Type<unknown>, cls: string) => {
      const m = css(cmp).match(new RegExp(`\\.${cls}[^{]*\\{[^}]*color:\\s*color-mix\\(in srgb,\\s*currentColor\\s*(\\d+)%`));
      return m ? Number(m[1]) : NaN;
    };
    expect(pick(EngineCardComponent, 'engine-hint')).toBeGreaterThanOrEqual(55);
    expect(pick(OfflineSettingsCardComponent, 'offline-hint')).toBeGreaterThanOrEqual(55);
    expect(pick(DeleteAccountCardComponent, 'danger-hint')).toBeGreaterThanOrEqual(55);
    expect(pick(ProfileComponent, 'discord-hint')).toBeGreaterThanOrEqual(55);
  });

  it('Zwischentitel h5 der Engine-Karte erben die Textfarbe; Titel/Elo über Tokens', () => {
    expect(css(EngineCardComponent)).toMatch(/\.engine-section h5[^{]*\{[^}]*color:\s*inherit/);
    expect(css(ProfileComponent)).toMatch(/\.title[^{]*\{[^}]*color:\s*var\(--rh-warn\)/);
    expect(css(ProfileComponent)).toMatch(/\.elo[^{]*\{[^}]*color:\s*var\(--rh-success\)/);
  });
});
