import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { LocaleService } from '@rh/core/locale.service';
import { ThemeService } from '@rh/core/theme.service';
import { LeagueHubAppComponent } from './app.component';

@Component({ standalone: true, template: '' })
class StubPageComponent {}

/** UX-020: „Anmelden“ in der Kopfzeile hatte fest returnUrl „/“ — auch von /verein/neu, /s/<token> oder der Registrierung. */
describe('LeagueHubAppComponent „Anmelden“ behält das Ziel (UX-020)', () => {
  async function loginHrefAt(url: string): Promise<string | null> {
    TestBed.configureTestingModule({
      imports: [LeagueHubAppComponent],
      providers: [
        provideRouter([{ path: '**', component: StubPageComponent }]),
        { provide: AuthService, useValue: { currentUser$: of(null), currentUser: null, has: () => false, logout: () => {} } },
        { provide: HandoffService, useValue: { consumeIncoming: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {}, applyUnsaved: () => {} } },
        { provide: ThemeService, useValue: {} },
      ],
    });
    const fixture = TestBed.createComponent(LeagueHubAppComponent);
    await TestBed.inject(Router).navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('header a.btn-sec')!.getAttribute('href');
  }

  it('auf dem Teilen-Link: zurück zur Begegnung', async () => {
    expect(await loginHrefAt('/s/abc123')).toBe('/login?returnUrl=%2Fs%2Fabc123');
  });

  it('auf der Registrierung mit Ziel: dieses Ziel; ohne Ziel „/“ (LeagueHub hat kein /dashboard)', async () => {
    expect(await loginHrefAt('/register?returnUrl=%2Fverein%2Fneu')).toBe('/login?returnUrl=%2Fverein%2Fneu');
    TestBed.resetTestingModule();
    expect(await loginHrefAt('/register')).toBe('/login?returnUrl=%2F');
  });
});
