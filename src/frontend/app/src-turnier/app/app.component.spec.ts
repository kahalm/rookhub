import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { By } from '@angular/platform-browser';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { AppFooterComponent } from '@rh/shared/app-footer/app-footer.component';
import { AppUpdateService } from '@rh/core/app-update.service';
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
        // Im Prod-Build stellt `provideServiceWorker` ihn bereit; der TestBed kennt die
        // App-Konfiguration nicht, also hier als Attrappe.
        {
          provide: SwUpdate,
          useValue: {
            isEnabled: false, versionUpdates: new Subject<unknown>(),
            unrecoverable: new Subject<unknown>(), checkForUpdate: () => Promise.resolve(false),
          },
        },
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

  /**
   * Die Turnierseite registriert seit ihrem ersten Tag einen Service Worker, hatte aber nie einen
   * Hinweis auf eine neue Fassung — ein offener Tab lief nach einem Deploy also unbegrenzt auf
   * der ALTEN weiter, ohne jedes Anzeichen. Gemeldet als „ich bekomm keine Refresh-Aufforderung".
   */
  it('haengt den Hinweis auf neue Fassungen an — wie RookHub', () => {
    const start = spyOn(TestBed.inject(AppUpdateService), 'start');

    TestBed.createComponent(TurnierAppComponent).detectChanges();

    expect(start).toHaveBeenCalled();
  });
});
