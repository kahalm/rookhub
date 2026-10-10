import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { FooterPresenceService } from '@rh/shared/app-footer/footer-presence';
import { ClubHubAppComponent } from './app.component';

describe('ClubHubAppComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [ClubHubAppComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'de' })],
  }));

  // Die Fusszeile zeigt Impressum und Datenschutz immer; die Anmeldemaske wiederholte sie darunter (UI-Sweep x-login-legal).
  it('meldet die Fusszeile als immer sichtbar, die Anmeldemaske laesst ihre Rechtslinks weg', () => {
    const f = TestBed.createComponent(ClubHubAppComponent);
    expect(TestBed.inject(FooterPresenceService).presence()).toBe('always');
    f.detectChanges();
    const links = Array.from((f.nativeElement as HTMLElement).querySelectorAll('footer a')).map(a => a.getAttribute('href'));
    expect(links).toEqual(jasmine.arrayContaining(['/impressum', '/privacy']));
  });
});
