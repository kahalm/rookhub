import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AccountDeletionComponent } from './account-deletion.component';
import { ImpressumComponent } from './impressum.component';
import { PrivacyComponent } from './privacy.component';
import { setDarkTheme, textContrast } from '../../testing/contrast';

/**
 * Kontrast der Rechtsseiten (Codereview 2026-09-29, F7-015 / UX-055). Zwischenueberschriften und Links trugen fest
 * #90caf9 (fuer RookHubs Dunkelmodus), die Stand-Zeile #bdbdbd. Auf KidHub (immer hell), LeagueHub und RookHub im
 * hellen Modus standen sie damit hellblau bzw. hellgrau auf Weiss — 1,75:1 bzw. 1,9:1. Die Anmeldemaske hatte genau
 * das schon mit Theme-Token behoben. Gerendert wird die echte Seite (Karte samt Material-Stilen), hell und dunkel.
 */
describe('Rechtsseiten: Ueberschriften, Links und Stand-Zeile lesbar in beiden Modi (F7-015)', () => {
  let wasDark: boolean;
  beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
  afterEach(() => setDarkTheme(wasDark));

  const pages: [string, Type<unknown>][] = [
    ['Datenschutz', PrivacyComponent],
    ['Impressum', ImpressumComponent],
    ['Konto loeschen', AccountDeletionComponent],
  ];

  for (const dark of [false, true]) {
    for (const [name, cmp] of pages) {
      it(`${name} (${dark ? 'dunkel' : 'hell'}): jede Ueberschrift, jeder Textlink, die Stand-Zeile ≥ 4,5:1`, () => {
        setDarkTheme(dark);
        TestBed.configureTestingModule({
          imports: [cmp],
          providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
            provideTranslateService({ fallbackLang: 'en' })],
        });
        const f = TestBed.createComponent(cmp);
        f.detectChanges();
        const el = f.nativeElement as HTMLElement;
        const targets = Array.from(el.querySelectorAll('h4, a:not(.mat-mdc-button-base), .muted'));
        expect(targets.length).toBeGreaterThan(1);
        for (const t of targets) {
          const ratio = textContrast(t);
          expect(ratio).withContext(`${t.tagName.toLowerCase()}${t.className ? '.' + t.className : ''} `
            + `„${(t.textContent ?? '').trim().slice(0, 30)}": ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
        }
      });
    }
  }
});
