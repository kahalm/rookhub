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

  it('auf /login ausgeblendet, auf /register und der Startseite da', async () => {
    expect(await headLoginAt('/login')).toBeNull();
    TestBed.resetTestingModule();
    expect((await headLoginAt('/register'))?.textContent).toContain('Anmelden');
    TestBed.resetTestingModule();
    expect((await headLoginAt('/'))?.textContent).toContain('Anmelden');
  });
});
