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

/** UI-Sweep 2026-10-10: Kopf auf /login ohne „Anmelden" (x-login-headbtn), am Handy ein Konto-Menü (l-nav-mobile),
 *  Vereinspartien breiter (l-club-table). */
describe('LeagueHubAppComponent Kopf (UI-Sweep 2026-10-10)', () => {
  async function create(url: string, user: { userId: number; username: string } | null, clubs = [TEST_CLUB]) {
    TestBed.configureTestingModule({
      imports: [LeagueHubAppComponent],
      providers: [
        provideRouter([{ path: '**', component: StubPageComponent }]),
        { provide: AuthService, useValue: { currentUser$: of(user), currentUser: user, has: () => !!user, logout: () => {} } },
        { provide: HandoffService, useValue: { consumeIncoming: () => Promise.resolve(false) } },
        { provide: LocaleService, useValue: { init: () => {}, applyUnsaved: () => {} } },
        { provide: ThemeService, useValue: {} },
        provideTestClub(user ? clubs[0] : null, user ? clubs : []),
      ],
    });
    const fixture = TestBed.createComponent(LeagueHubAppComponent);
    await TestBed.inject(Router).navigateByUrl(url);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('auf /login kein „Anmelden" im Kopf, auf /register und anderswo schon', async () => {
    let f = await create('/login?returnUrl=%2Fverein', null);
    expect((f.nativeElement as HTMLElement).querySelector('header a.btn-sec')).toBeNull();
    TestBed.resetTestingModule();
    f = await create('/register', null);
    expect((f.nativeElement as HTMLElement).querySelector('header a.btn-sec')?.textContent).toContain('Anmelden');
    TestBed.resetTestingModule();
    f = await create('/s/abc', null);
    expect((f.nativeElement as HTMLElement).querySelector('header a.btn-sec')?.textContent).toContain('Anmelden');
  });

  it('angemeldet: Konto-Knopf mit Menü (Name, Verein wechseln, Abmelden)', async () => {
    const OTHER = { id: 2, name: 'SK Weiler', anonName: 'Weiler', teamPrefix: 'SK Weiler', region: 'bayern' };
    const f = await create('/', { userId: 7, username: 'patrik' }, [TEST_CLUB, OTHER]);
    const el = f.nativeElement as HTMLElement;
    const btn = el.querySelector('header button.acct-btn') as HTMLButtonElement;
    expect(btn).not.toBeNull();
    expect(el.querySelector('header .who')?.classList).toContain('desk');
    btn.click();
    f.detectChanges();
    const items = Array.from(document.querySelectorAll('.mat-mdc-menu-item')).map(b => b.textContent?.trim());
    expect(items).toEqual(['patrik', '✓ SK Testdorf', 'SK Weiler', 'Abmelden']);
    const reload = spyOn(f.componentInstance, 'reload');
    (Array.from(document.querySelectorAll('.mat-mdc-menu-item')).find(b => b.textContent?.includes('SK Weiler')) as HTMLButtonElement).click();
    expect(reload).toHaveBeenCalled();
  });

  it('Vereinspartien bekommen die breitere Seite, andere Seiten nicht', async () => {
    let f = await create('/verein', { userId: 7, username: 'patrik' });
    expect((f.nativeElement as HTMLElement).querySelector('main')?.classList).toContain('mid');
    TestBed.resetTestingModule();
    f = await create('/verein/neu', { userId: 7, username: 'patrik' });
    expect((f.nativeElement as HTMLElement).querySelector('main')?.classList).not.toContain('mid');
  });
});
