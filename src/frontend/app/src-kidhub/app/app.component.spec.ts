import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { KidHubAppComponent, KIDS_LANGUAGES } from './app.component';
import { FORMAT_LOCALES } from '@rh/core/locale.service';

describe('KidHubAppComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [KidHubAppComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
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

  it('bietet nur die vollstaendig uebersetzten Sprachen an', () => {
    expect(KIDS_LANGUAGES.map(l => l.code as string).sort()).toEqual([...FORMAT_LOCALES].sort());
  });

  it('verlinkt Impressum und Datenschutz', () => {
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    const hrefs = Array.from(f.nativeElement.querySelectorAll('footer a') as NodeListOf<HTMLAnchorElement>)
      .map(a => a.getAttribute('href'));
    expect(hrefs).toContain('/impressum');
    expect(hrefs).toContain('/privacy');
  });
});
