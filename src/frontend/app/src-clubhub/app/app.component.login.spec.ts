import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ClubHubAppComponent } from './app.component';

@Component({ standalone: true, template: '' })
class StubPageComponent {}

/** UI-Sweep 2026-10-10 (x-login-headbtn): auf der Anmeldemaske selbst kein zweiter „Anmelden“-Knopf oben rechts. */
describe('ClubHubAppComponent „Anmelden“ im Kopf', () => {
  async function headLoginAt(url: string): Promise<HTMLElement | null> {
    TestBed.configureTestingModule({
      imports: [ClubHubAppComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'de' }),
        provideRouter([{ path: '**', component: StubPageComponent }])],
    });
    const f = TestBed.createComponent(ClubHubAppComponent);
    await TestBed.inject(Router).navigateByUrl(url);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return (f.nativeElement as HTMLElement).querySelector('header nav.account a.btn');
  }

  // b1-c-passwort-masken-kopf-anmelden: auch auf Registrieren und den Passwort-Masken (wie die Turnierseite).
  for (const url of ['/login', '/register', '/forgot-password', '/reset-password?token=abc']) {
    it(`auf ${url} ausgeblendet`, async () => {
      expect(await headLoginAt(url)).toBeNull();
    });
  }

  it('auf der Startseite und anderen Seiten da', async () => {
    expect((await headLoginAt('/'))?.textContent).toContain('Anmelden');
    TestBed.resetTestingModule();
    expect((await headLoginAt('/privacy'))?.textContent).toContain('Anmelden');
  });
});
