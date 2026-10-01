import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { Router, RouterLink, provideRouter } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { NavbarComponent } from './navbar.component';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { CatalogService } from '../../features/catalog/catalog.service';
import { MenuService } from '../../core/menu.service';
import { InAppNotificationService } from '../../core/in-app-notification.service';
import { MessageService } from '../../core/message.service';
import { LocaleService } from '../../core/locale.service';
import { ThemeService } from '../../core/theme.service';
import { HandoffService } from '../../core/handoff.service';
import { MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { FEEDBACK_URL } from '../../core/community';
import { environment } from '../../../environments/environment';

describe('NavbarComponent', () => {
  // Über TestBed in einem Injection-Context bauen: NavbarComponent nutzt
  // inject(DestroyRef) als Field-Initializer, ein nacktes `new` würde NG0203 werfen.
  function build(notifMock?: Partial<InAppNotificationService>): NavbarComponent {
    const notif = { unseenCount$: of(0), refreshCount: () => {}, reset: () => {}, list: () => of([]), markAllSeen: () => of(null), ...notifMock };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { currentUser$: of(null), isAdmin: false } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }), accessChanged$: of(undefined) } },
        { provide: CatalogService, useValue: { access: () => of({ hasAccess: false }) } },
        { provide: MenuService, useValue: { visible$: of(new Set<string>()) } },
        { provide: InAppNotificationService, useValue: notif },
        { provide: MessageService, useValue: { userUnread$: of(0), refreshUserUnread: () => {}, reset: () => {} } },
        { provide: LocaleService, useValue: {} },
        { provide: ThemeService, useValue: { preference: 'system', isDark: false, toggle: () => {} } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: Router, useValue: { navigateByUrl: () => {} } },
        { provide: HandoffService, useValue: { jump: () => Promise.resolve(), partnerUrl: null } },
      ],
    });
    return TestBed.runInInjectionContext(() => new NavbarComponent(
      TestBed.inject(AuthService),
      TestBed.inject(CourseService),
      TestBed.inject(CatalogService),
      TestBed.inject(MenuService),
      TestBed.inject(InAppNotificationService),
      TestBed.inject(MessageService),
      TestBed.inject(LocaleService),
      TestBed.inject(ThemeService),
      TestBed.inject(TranslateService),
      TestBed.inject(Router),
      TestBed.inject(MatIconRegistry),
      TestBed.inject(DomSanitizer),
    ));
  }

  it('baut ohne Fehler (App-Installation verlinkt jetzt auf /install statt Dialog)', () => {
    expect(build()).toBeTruthy();
  });

  it('onBellOpened lädt NUR die ungelesenen, markiert aber NICHT automatisch als gelesen', () => {
    const markAllSeen = jasmine.createSpy('markAllSeen').and.returnValue(of(null));
    const list = jasmine.createSpy('list').and.returnValue(of([{ id: 1, type: 't', data: null, link: null, createdAt: '', seen: false }]));
    const nav = build({ list, markAllSeen });
    nav.onBellOpened();
    expect(list).toHaveBeenCalledWith(20, true); // unseenOnly = true → gelesene verschwinden aus der Glocke
    expect(markAllSeen).not.toHaveBeenCalled();
    expect(nav.hasUnseen()).toBeTrue();
  });

  it('markAllRead leert die Glocke, ruft den Service und hält das Menü offen', () => {
    const markAllSeen = jasmine.createSpy('markAllSeen').and.returnValue(of(null));
    const nav = build({ markAllSeen });
    nav.notifications = [{ id: 1, type: 't', data: null, link: null, createdAt: '', seen: false }];
    const event = { stopPropagation: jasmine.createSpy('stopPropagation') } as unknown as Event;
    nav.markAllRead(event);
    expect(event.stopPropagation).toHaveBeenCalled();
    expect(markAllSeen).toHaveBeenCalled();
    expect(nav.notifications.length).toBe(0); // gelesene bleiben nur über „Alle anzeigen" sichtbar
  });

  it('springt auf den Turnierkalender, nicht auf die Liste der geholten Turniere', () => {
    // Dasselbe Startziel, das die Turnierseite auch beim direkten Aufruf wählt: wer hinüber geht,
    // will wissen, was ansteht — die Liste zeigt nur, was schon jemand geholt hat.
    const nav = build();
    const jump = spyOn(TestBed.inject(HandoffService), 'jump').and.resolveTo();

    nav.toTurnier();

    expect(jump).toHaveBeenCalledWith('tournaments/calendar');
  });

  it('openNotification markiert als gelesen und entfernt die Benachrichtigung aus der Glocke', () => {
    const markSeen = jasmine.createSpy('markSeen').and.returnValue(of(null));
    const nav = build({ markSeen });
    const n = { id: 1, type: 't', data: null, link: null, createdAt: '', seen: false };
    nav.notifications = [n, { id: 2, type: 't', data: null, link: null, createdAt: '', seen: false }];
    nav.openNotification(n);
    expect(markSeen).toHaveBeenCalledWith(1);
    expect(nav.notifications.map(x => x.id)).toEqual([2]); // geklickte verschwindet, Rest bleibt
  });
});

describe('NavbarComponent entrümpelte Toolbar (UI-Welle Navbar)', () => {
  function render(opts: { loggedIn?: boolean; keys?: string[] } = {}) {
    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {
          currentUser$: of(opts.loggedIn ? { username: 'u' } : null),
          isLoggedIn: !!opts.loggedIn, isAdmin: false, logout: () => {},
        } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }), accessChanged$: of(undefined) } },
        { provide: CatalogService, useValue: { access: () => of({ hasAccess: false }) } },
        { provide: MenuService, useValue: { visible$: of(new Set<string>(opts.keys ?? [])) } },
        { provide: InAppNotificationService, useValue: { unseenCount$: of(0), refreshCount: () => {}, reset: () => {}, list: () => of([]), markAllSeen: () => of(null) } },
        { provide: MessageService, useValue: { userUnread$: of(0), refreshUserUnread: () => {}, reset: () => {} } },
        { provide: LocaleService, useValue: { languages: [], current: 'en', use: () => {} } },
        { provide: ThemeService, useValue: { preference: 'system', isDark: false, toggle: () => {} } },
        provideRouter([]),
      ],
    });
    const fixture = TestBed.createComponent(NavbarComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('eingeloggt zeigt die Leiste genau 3 Icon-Buttons (Vollbild, Glocke, Menü)', () => {
    const fixture = render({ loggedIn: true, keys: ['dashboard', 'puzzles', 'analysis'] });
    const icons = (fixture.nativeElement as HTMLElement)
      .querySelectorAll('mat-toolbar button[mat-icon-button], mat-toolbar a[mat-icon-button]');
    // Headless-Chrome unterstützt Element-Vollbild → Vollbild-Icon zählt mit.
    expect(icons.length).toBe(3);   // Nachrichten + Konto liegen im EINEN ☰-Menü
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-toolbar .msg-mail mat-icon')?.textContent)
      .toContain('menu');           // Mail-Badge hängt jetzt am ☰-Knopf
  });

  it('Kategorie-Untermenüs erscheinen nur mit sichtbarem Inhalt', () => {
    const fixture = render({ loggedIn: true, keys: ['puzzles'] });
    const c = fixture.componentInstance;
    expect(c.anyTraining).toBeTrue();     // puzzles sichtbar
    expect(c.anyLibrary).toBeFalse();     // nichts aus Analyse & Sammlung freigegeben
  });

  it('ausgeloggt: nur Puzzles/Analyse + ☰ + Login/Registrieren in der Leiste', () => {
    const fixture = render({ loggedIn: false, keys: ['puzzles', 'analysis', 'help'] });
    const el: HTMLElement = fixture.nativeElement;
    const iconBtns = el.querySelectorAll('mat-toolbar button[mat-icon-button], mat-toolbar a[mat-icon-button]');
    expect(iconBtns.length).toBe(2);      // Vollbild + ☰ — keine Icon-Reihe mehr
  });

  // Das Logo führt auf die Startadresse, nicht hart aufs Dashboard: „/“ entscheidet selbst — Gäste in einen
  // offenen Bereich, Angemeldete aufs Dashboard (UX-025). Vorher landete ein Gast so vor der Anmeldesperre.
  for (const loggedIn of [false, true]) {
    it(`das Logo führt auf „/“ (${loggedIn ? 'angemeldet' : 'Gast'})`, () => {
      const fixture = render({ loggedIn, keys: ['dashboard', 'puzzles'] });
      const logo = fixture.debugElement.query(By.css('mat-toolbar .logo'));
      expect(logo.injector.get(RouterLink).urlTree?.toString()).toBe('/');
    });
  }

  it('ausgeloggt: das ☰-Menü bietet den Theme-Umschalter (Anonyme haben keine Profil-Theme-Karte)', () => {
    const fixture = render({ loggedIn: false, keys: ['puzzles'] });
    const el: HTMLElement = fixture.nativeElement;
    // aria-label ist der rohe Key, weil im Test keine Übersetzungen geladen sind.
    const trigger = el.querySelector('mat-toolbar button[aria-label="nav.menu"]') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const items = Array.from(document.querySelectorAll('.cdk-overlay-container button'));
    // preference 'system' → brightness_auto (siehe ThemeService-Mock oben)
    expect(items.some(b => b.textContent?.includes('brightness_auto'))).toBeTrue();
    trigger.click();
    fixture.detectChanges();
  });

  // Impressum/Datenschutz standen nur unter der Anmeldekarte — eingeloggt nie erreichbar, am Handy (Fusszeile aus)
  // auch abgemeldet nicht (UX-017). mat-menu-item ist 48 px hoch, also ein volles Beruehrziel.
  const legalItems = () => Array.from(document.querySelectorAll('.cdk-overlay-container button.legal-item'))
    .map(b => b.textContent?.trim());

  it('ausgeloggt: das ☰-Menü führt zu Datenschutz und Impressum', () => {
    const fixture = render({ loggedIn: false, keys: ['puzzles'] });
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar button[aria-label="nav.menu"]') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    expect(legalItems()).toEqual(['legal.privacy.title', 'legal.impressum.title']);
    trigger.click();
    fixture.detectChanges();
  });

  // Am Handy ist die Fusszeile aus (hide-on-mobile) — „Feedback / Bug melden“ und die Versionsnummer waren dort
  // nirgends erreichbar, das Gast-Menü hatte beides nicht, das Konto-Menü kein Feedback (UX-053).
  const overlayItem = (cls: string) => document.querySelector(`.cdk-overlay-container .${cls}`) as HTMLElement | null;

  it('ausgeloggt: das ☰-Menü führt zum Feedback und zum Changelog mit Versionsnummer', () => {
    const fixture = render({ loggedIn: false, keys: ['puzzles'] });
    const emitted = spyOn(fixture.componentInstance.changelogClick, 'emit');
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar button[aria-label="nav.menu"]') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    expect(overlayItem('feedback-item')?.getAttribute('href')).toBe(FEEDBACK_URL);
    const changelog = overlayItem('changelog-item')!;
    expect(changelog.textContent).toContain('v' + environment.version);
    changelog.click();
    expect(emitted).toHaveBeenCalled();
    fixture.detectChanges();
  });

  it('eingeloggt: ☰ → Konto führt zum Feedback, der Changelog zeigt die Versionsnummer', () => {
    const fixture = render({ loggedIn: true, keys: ['dashboard'] });
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar .msg-mail') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const account = Array.from(document.querySelectorAll('.cdk-overlay-container button'))
      .find(b => b.textContent?.includes('nav.account')) as HTMLButtonElement;
    account.click();
    fixture.detectChanges();
    expect(overlayItem('feedback-item')?.getAttribute('href')).toBe(FEEDBACK_URL);
    expect(overlayItem('changelog-item')?.textContent).toContain('v' + environment.version);
    trigger.click();
    fixture.detectChanges();
  });

  it('eingeloggt: ☰ → Konto führt zu Datenschutz und Impressum', () => {
    const fixture = render({ loggedIn: true, keys: ['dashboard'] });
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar .msg-mail') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const account = Array.from(document.querySelectorAll('.cdk-overlay-container button'))
      .find(b => b.textContent?.includes('nav.account')) as HTMLButtonElement;
    account.click();
    fixture.detectChanges();
    expect(legalItems()).toEqual(['legal.privacy.title', 'legal.impressum.title']);
    trigger.click();
    fixture.detectChanges();
  });
});

describe('NavbarComponent App-Vollbild', () => {
  function buildNav(): NavbarComponent {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { currentUser$: of(null), isAdmin: false } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }), accessChanged$: of(undefined) } },
        { provide: CatalogService, useValue: { access: () => of({ hasAccess: false }) } },
        { provide: MenuService, useValue: { visible$: of(new Set<string>()) } },
        { provide: InAppNotificationService, useValue: { unseenCount$: of(0), refreshCount: () => {}, reset: () => {}, list: () => of([]), markAllSeen: () => of(null) } },
        { provide: MessageService, useValue: { userUnread$: of(0), refreshUserUnread: () => {}, reset: () => {} } },
        { provide: LocaleService, useValue: {} },
        { provide: ThemeService, useValue: { preference: 'system', isDark: false, toggle: () => {} } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
        { provide: Router, useValue: { navigateByUrl: () => {} } },
      ],
    });
    return TestBed.runInInjectionContext(() => new NavbarComponent(
      TestBed.inject(AuthService), TestBed.inject(CourseService), TestBed.inject(CatalogService),
      TestBed.inject(MenuService), TestBed.inject(InAppNotificationService), TestBed.inject(MessageService),
      TestBed.inject(LocaleService), TestBed.inject(ThemeService), TestBed.inject(TranslateService),
      TestBed.inject(Router), TestBed.inject(MatIconRegistry), TestBed.inject(DomSanitizer),
    ));
  }

  it('schaltet das GANZE Dokument ins Vollbild (nicht nur ein Brett)', () => {
    const nav = buildNav();
    const request = spyOn(document.documentElement, 'requestFullscreen').and.returnValue(Promise.resolve());
    nav.toggleAppFullscreen();
    expect(request).toHaveBeenCalled();
  });

  it('folgt dem Vollbild-Zustand des Dokuments — ein Brett-Vollbild zählt nicht als aktiv', () => {
    let current: Element | null = null;
    spyOnProperty(document, 'fullscreenElement', 'get').and.callFake(() => current);
    const nav = buildNav();
    nav.ngOnInit();
    expect(nav.fsActive).toBeFalse();
    expect(nav.fsLabel).toBe('nav.fullscreen');

    current = document.createElement('div');          // ein Brett im Vollbild
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(nav.fsActive).toBeFalse();

    current = document.documentElement;               // die ganze GUI
    document.dispatchEvent(new Event('fullscreenchange'));
    expect(nav.fsActive).toBeTrue();
    expect(nav.fsLabel).toBe('nav.fullscreenExit');
  });
});

/** UX-020: die Kopfzeilen-Knöpfe reichen das Ziel weiter — bisher führten sie nach der Anmeldung aufs Dashboard. */
describe('NavbarComponent Anmelden/Registrieren behalten das Ziel (UX-020)', () => {
  @Component({ standalone: true, template: '' })
  class StubPageComponent {}

  async function renderAt(url: string) {
    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { currentUser$: of(null), isLoggedIn: false, isAdmin: false, logout: () => {} } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }), accessChanged$: of(undefined) } },
        { provide: CatalogService, useValue: { access: () => of({ hasAccess: false }) } },
        { provide: MenuService, useValue: { visible$: of(new Set<string>()) } },
        { provide: InAppNotificationService, useValue: { unseenCount$: of(0), refreshCount: () => {}, reset: () => {}, list: () => of([]), markAllSeen: () => of(null) } },
        { provide: MessageService, useValue: { userUnread$: of(0), refreshUserUnread: () => {}, reset: () => {} } },
        { provide: LocaleService, useValue: { languages: [], current: 'en', use: () => {} } },
        { provide: ThemeService, useValue: { preference: 'system', isDark: false, toggle: () => {} } },
        provideRouter([{ path: '**', component: StubPageComponent }]),
      ],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(NavbarComponent);
    fixture.detectChanges();
    const link = (path: string) =>
      fixture.debugElement.query(By.css(`mat-toolbar [routerLink="${path}"]`))?.injector.get(RouterLink).urlTree!.toString();
    return { login: link('/login'), register: link('/register'), el: fixture.nativeElement as HTMLElement };
  }

  /** Codereview W5 UX-052: auf den Auth-Seiten standen „Anmelden“/„Registrieren“ doppelt (Kopfzeile + Karte) — auf
   *  /register zwei gleiche Knöpfe, von denen der obere scheinbar nichts tat —, dazu ein nutzloses Vollbild-Symbol.
   *  Die Karte trägt die Links samt Ziel selbst (login/register.component). */
  it('auf den Auth-Seiten: keine Kopie von „Anmelden“/„Registrieren“ und kein Vollbild in der Kopfzeile', async () => {
    for (const url of ['/login?returnUrl=%2Ffriends%2F7%2Frevenge&authRequired=1', '/register', '/forgot-password', '/reset-password?token=x']) {
      const { register, login, el } = await renderAt(url);
      expect(register).withContext(url).toBeUndefined();
      expect(login).withContext(url).toBeUndefined();
      expect(el.querySelector('mat-toolbar button[aria-label="nav.fullscreen"]')).withContext(url).toBeNull();
      TestBed.resetTestingModule();
    }
  });

  it('auf offenen Seiten: „Registrieren“ ist die (gefüllte) Primäraktion der Leiste', async () => {
    const { el } = await renderAt('/puzzles');
    expect(el.querySelector('mat-toolbar [routerLink="/register"]')!.classList).toContain('mat-primary');
    expect(el.querySelector('mat-toolbar button[aria-label="nav.fullscreen"]')).not.toBeNull();
  });

  it('auf einer offenen Seite: zurück zu genau dieser Seite', async () => {
    expect((await renderAt('/puzzles/daily/today')).login).toBe('/login?returnUrl=%2Fpuzzles%2Fdaily%2Ftoday');
  });
});
