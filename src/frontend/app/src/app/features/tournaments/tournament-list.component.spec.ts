import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { TournamentListComponent } from './tournament-list.component';

describe('TournamentListComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [TournamentListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(TournamentListComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  // Robustheit: ein beschädigter hiddenTournaments-Wert (bzw. gültiges Nicht-Array) darf
  // ngOnInit nicht werfen lassen — sonst ist die Turnierseite dauerhaft tot, bis der User
  // den localStorage von Hand leert. Fallback: leeres Set (nichts versteckt).
  for (const [label, raw] of [['kaputtes JSON', '{not json'], ['gültiges Nicht-Array', '{"a":1}']] as const) {
    it(`überlebt ${label} in hiddenTournaments (leeres Set statt Wurf)`, async () => {
      localStorage.setItem('hiddenTournaments', raw);
      try {
        await TestBed.configureTestingModule({
          imports: [TournamentListComponent],
          providers: [
            provideHttpClient(),
            provideHttpClientTesting(),
            provideRouter([]),
            provideNoopAnimations(),
            provideTranslateService({ fallbackLang: 'en' }),
          ],
        }).compileComponents();
        const fixture = TestBed.createComponent(TournamentListComponent);
        expect(() => fixture.componentInstance.ngOnInit()).not.toThrow();
        expect(((fixture.componentInstance as any).hiddenIds as Set<string>).size).toBe(0);
      } finally {
        localStorage.removeItem('hiddenTournaments');
      }
    });
  }
});
