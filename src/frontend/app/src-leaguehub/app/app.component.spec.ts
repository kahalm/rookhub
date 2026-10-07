import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { HandoffService } from '@rh/core/handoff.service';
import { LocaleService } from '@rh/core/locale.service';
import { ThemeService } from '@rh/core/theme.service';
import { LeagueHubAppComponent } from './app.component';
import { ClubContextService } from './core/club-context.service';
import { TEST_CLUB, provideTestClub } from './core/club-context.testing';

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
        provideTestClub(null),
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

/** UX-036: auf „Formular prüfen" kostete der Werbesatz am Handy drei Zeilen über Prüfteil und Brett. */
describe('LeagueHubAppComponent Werbesatz auf „Formular prüfen" (UX-036)', () => {
  async function ledeAt(url: string): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [LeagueHubAppComponent],
      providers: [
        provideRouter([{ path: '**', component: StubPageComponent }]),
        { provide: AuthService, useValue: { currentUser$: of(null), currentUser: null, has: () => false, logout: () => {} } },
        { provide: HandoffService, useValue: { consumeIncoming: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {}, applyUnsaved: () => {} } },
        { provide: ThemeService, useValue: {} },
        provideTestClub(null),
      ],
    });
    const fixture = TestBed.createComponent(LeagueHubAppComponent);
    await TestBed.inject(Router).navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('p.lede') as HTMLElement;
  }

  it('angemeldet und über den Teilen-Link als Arbeitsseite markiert (am Handy ausgeblendet), sonst nicht', async () => {
    expect((await ledeAt('/verein/formular/7')).classList).toContain('work');
    TestBed.resetTestingModule();
    expect((await ledeAt('/s/abc123/formular/k1')).classList).toContain('work');
    TestBed.resetTestingModule();
    expect((await ledeAt('/verein')).classList).not.toContain('work');
    TestBed.resetTestingModule();
    expect((await ledeAt('/s/abc123')).classList).not.toContain('work');
  });
});

/** Vereine als Mandanten (0.698.0): bei mehreren Vereinen ein Umschalter im Kopf (gemerkt, Seite neu), sonst keiner. */
describe('LeagueHubAppComponent Umschalter zwischen Vereinen', () => {
  const OTHER = { id: 2, name: 'SK Weiler', anonName: 'Weiler', teamPrefix: 'SK Weiler', region: 'bayern' };

  async function create(clubs: typeof TEST_CLUB[]) {
    const user = { userId: 7, username: 'patrik' };
    TestBed.configureTestingModule({
      imports: [LeagueHubAppComponent],
      providers: [
        provideRouter([{ path: '**', component: StubPageComponent }]),
        { provide: AuthService, useValue: { currentUser$: of(user), currentUser: user, has: () => true, logout: () => {} } },
        { provide: HandoffService, useValue: { consumeIncoming: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {}, applyUnsaved: () => {} } },
        { provide: ThemeService, useValue: {} },
        provideTestClub(clubs[0] ?? null, clubs),
      ],
    });
    const fixture = TestBed.createComponent(LeagueHubAppComponent);
    await TestBed.inject(Router).navigateByUrl('/');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('ein Verein: kein Umschalter; die Zeile nennt die Tiroler Liga', async () => {
    const f = await create([TEST_CLUB]);
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.club-switch')).toBeNull();
    expect(el.querySelector('.lede')!.textContent).toContain('Tiroler Mannschaftsmeisterschaft');
  });

  it('zwei Vereine: Umschalter; Wechsel merkt den Verein und lädt neu', async () => {
    const f = await create([TEST_CLUB, OTHER]);
    const el = f.nativeElement as HTMLElement;
    const select = el.querySelector('.club-switch select') as HTMLSelectElement;
    expect(Array.from(select.options).map(o => o.textContent!.trim())).toEqual(['SK Testdorf', 'SK Weiler']);
    const reload = spyOn(f.componentInstance, 'reload');
    f.componentInstance.switchClub(1);              // derselbe: nichts
    expect(reload).not.toHaveBeenCalled();
    f.componentInstance.switchClub(2);
    expect(reload).toHaveBeenCalled();
    expect(TestBed.inject(ClubContextService).current()?.id).toBe(2);
    f.detectChanges();
    expect(el.querySelector('.lede')!.textContent).toContain('bayerischen Mannschaftsligen');
  });
});

/** Vereine verwalten (0.700.0): der Reiter „Vereine" nur für Admins mit league.manage — der Server verlangt beides. */
describe('LeagueHubAppComponent Reiter „Vereine"', () => {
  async function tabs(isAdmin: boolean, manage: boolean): Promise<string[]> {
    const user = { userId: 7, username: 'patrik' };
    TestBed.configureTestingModule({
      imports: [LeagueHubAppComponent],
      providers: [
        provideRouter([{ path: '**', component: StubPageComponent }]),
        { provide: AuthService, useValue: { currentUser$: of(user), currentUser: user, isAdmin, has: (p: string) => p !== 'league.manage' || manage, logout: () => {} } },
        { provide: HandoffService, useValue: { consumeIncoming: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {}, applyUnsaved: () => {} } },
        { provide: ThemeService, useValue: {} },
        provideTestClub(),
      ],
    });
    const fixture = TestBed.createComponent(LeagueHubAppComponent);
    await TestBed.inject(Router).navigateByUrl('/');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('nav.tabs a')).map(a => a.textContent!.trim());
  }

  it('Admin mit league.manage sieht ihn, ein Verwalter ohne Admin nicht', async () => {
    expect(await tabs(true, true)).toContain('Vereine');
    TestBed.resetTestingModule();
    const manager = await tabs(false, true);
    expect(manager).toContain('Übertragungen');
    expect(manager).not.toContain('Vereine');
  });
});
