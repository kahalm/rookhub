import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { By } from '@angular/platform-browser';
import { AppFooterComponent } from '@rh/shared/app-footer/app-footer.component';
import { TurnierAppComponent } from './app.component';

/**
 * Die Huelle der Turnierseite. Geprueft wird, dass sie die GETEILTE Fusszeile benutzt — nicht
 * eine eigene Kopie, die beim naechsten Eintrag auseinanderlaeuft.
 */
describe('TurnierAppComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TurnierAppComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
  });

  it('zeigt dieselbe Fusszeile wie RookHub', () => {
    const fixture = TestBed.createComponent(TurnierAppComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.directive(AppFooterComponent)))
      .withContext('keine geteilte Fusszeile').not.toBeNull();
  });

  it('nennt die Version und haelt das Changelog bis zum Oeffnen leer', () => {
    const fixture = TestBed.createComponent(TurnierAppComponent);
    fixture.detectChanges();

    const text: string = fixture.nativeElement.querySelector('.app-footer')?.textContent ?? '';
    expect(text).toContain('v');
    expect(fixture.nativeElement.querySelector('.changelog-overlay')).toBeNull();
  });
});
