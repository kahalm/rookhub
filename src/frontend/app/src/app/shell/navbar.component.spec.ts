import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { Router, RouterLink, provideRouter } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { NAV_DOCKED_CLASS, NAV_WIDTH_VAR, NavbarComponent } from './navbar.component';
import { AuthService } from '../core/auth.service';
import { CourseService } from '../features/courses/course.service';
import { CatalogService } from '../features/catalog/catalog.service';
import { MenuService } from '../core/menu.service';
import { InAppNotificationService } from '../core/in-app-notification.service';
import { MessageService } from '../core/message.service';
import { LocaleService } from '../core/locale.service';
import { ThemeService } from '../core/theme.service';
import { EinkService } from '../core/eink.service';
import { HandoffService } from '../core/handoff.service';
import { MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';
import { FEEDBACK_URL } from '../core/community';
import { environment } from '../../environments/environment';

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
  let themeMock: { preference: string; isDark: boolean; toggle: () => void; setPreference: jasmine.Spy };
  function render(opts: { loggedIn?: boolean; keys?: string[]; perms?: string[]; eink?: boolean; theme?: string } = {}) {
    themeMock = { preference: opts.theme ?? 'system', isDark: false, toggle: () => {}, setPreference: jasmine.createSpy('setPreference') };
    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {
          currentUser$: of(opts.loggedIn ? { username: 'u' } : null),
          isLoggedIn: !!opts.loggedIn, isAdmin: false, isAdminLive: false, logout: () => {},
          has: (p: string) => (opts.perms ?? []).includes(p),
        } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }), accessChanged$: of(undefined) } },
        { provide: CatalogService, useValue: { access: () => of({ hasAccess: false }) } },
        { provide: MenuService, useValue: { visible$: of(new Set<string>(opts.keys ?? [])) } },
        { provide: InAppNotificationService, useValue: { unseenCount$: of(0), refreshCount: () => {}, reset: () => {}, list: () => of([]), markAllSeen: () => of(null) } },
        { provide: MessageService, useValue: { userUnread$: of(0), refreshUserUnread: () => {}, reset: () => {} } },
        { provide: LocaleService, useValue: { languages: [], current: 'en', use: () => {} } },
        { provide: ThemeService, useValue: themeMock },
        { provide: EinkService, useValue: { enabled: signal(!!opts.eink) } },
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

  // F5-005: der Admin-Eintrag folgt derselben Regel wie adminAreaGuard (canOpenAdmin) — Recht genügt, Flag nicht nötig.
  it('Admin-Eintrag nur, wenn der Admin-Bereich sich öffnen würde (je Recht, ohne Admin-Flag)', () => {
    expect(render({ loggedIn: true, keys: ['puzzles'], perms: ['messages.admin'] }).componentInstance.showAdmin).toBeTrue();
    TestBed.resetTestingModule();
    expect(render({ loggedIn: true, keys: ['puzzles'], perms: ['ci.view'] }).componentInstance.showAdmin).toBeTrue();
    TestBed.resetTestingModule();
    expect(render({ loggedIn: true, keys: ['puzzles'], perms: ['league.view', 'catalog.manage'] }).componentInstance.showAdmin).toBeFalse();
  });

  it('„Gegner vorbereiten" nur mit dem Recht prep.view — dann auch ohne andere Einträge in „Analyse & Sammlung"', () => {
    expect(render({ loggedIn: true, keys: ['puzzles'], perms: ['prep.view'] }).componentInstance.anyLibrary).toBeTrue();
    TestBed.resetTestingModule();
    expect(render({ loggedIn: true, keys: ['puzzles'], perms: ['league.view', 'prep.manage'] }).componentInstance.anyLibrary).toBeFalse();
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

  // Logo und Gast-Navigation sind ANKER (UX-059): ein span mit routerLink bekommt zwar tabindex 0, reagiert aber
  // nur auf click — Enter tat nichts, Screenreader lasen bloßen Text, Mittelklick öffnete keinen neuen Tab.
  it('das Logo ist ein Link mit href', () => {
    const fixture = render({ loggedIn: true, keys: ['dashboard'] });
    const logo = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar .logo') as HTMLElement;
    expect(logo.tagName).toBe('A');
    expect(logo.getAttribute('href')).toBe('/');
  });

  it('ausgeloggt: Puzzles, Analyse, Anmelden und Registrieren sind Links mit href, keine Knöpfe', () => {
    const fixture = render({ loggedIn: false, keys: ['puzzles', 'analysis'] });
    const hrefs = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-toolbar a[mat-button], mat-toolbar a[mat-flat-button]'))
      .map(a => a.getAttribute('href'));
    // Anmelden/Registrieren tragen den Rücksprung als Query (UX-020, eigener Test unten) — hier zählt nur, dass es Links sind.
    expect(hrefs.map(h => h?.split('?')[0])).toEqual(['/puzzles', '/analysis', '/login', '/register']);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('mat-toolbar button[mat-button], mat-toolbar button[mat-flat-button]').length).toBe(0);
  });

  const openMenu = (fixture: ReturnType<typeof render>) => {
    // aria-label ist der rohe Key, weil im Test keine Übersetzungen geladen sind.
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar button[aria-label="nav.menu"]') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    return () => { trigger.click(); fixture.detectChanges(); };
  };

  // d3-rk-theme-menu-label-zustand: der Eintrag nannte den IST-Zustand („Dunkles Design“) und schaltete beim Tippen weiter
  // (dunkel → System). Jetzt ein Untermenü „Design“ wie „Sprache“: drei Wahlen, die aktive mit Häkchen.
  it('ausgeloggt: ☰ → Design öffnet System/Hell/Dunkel, die aktive Wahl trägt das Häkchen', () => {
    const fixture = render({ loggedIn: false, keys: ['puzzles'], theme: 'dark' });
    const close = openMenu(fixture);
    const themeTrigger = document.querySelector('.cdk-overlay-container button.theme-menu-trigger') as HTMLButtonElement;
    expect(themeTrigger).withContext('Design-Eintrag im ☰-Menü').not.toBeNull();
    expect(themeTrigger.textContent).toContain('nav.theme');
    themeTrigger.click();
    fixture.detectChanges();
    const options = Array.from(document.querySelectorAll('.cdk-overlay-container .nav-submenu button.theme-option'));
    expect(options.map(o => o.querySelector('span')?.textContent?.trim()))
      .toEqual(['profile.theme.system', 'profile.theme.light', 'profile.theme.dark']);
    const checked = options.filter(o => o.querySelector('mat-icon')?.textContent?.trim() === 'check');
    expect(checked.length).toBe(1);
    expect(checked[0].textContent).toContain('profile.theme.dark');
    expect(checked[0].getAttribute('aria-checked')).toBe('true');
    (options[1] as HTMLButtonElement).click();
    expect(themeMock.setPreference).toHaveBeenCalledWith('light');
    close();
  });

  // d3-rk-helles-design-bei-eink: im E-Ink-Modus ist die App immer hell — die Wahl war wirkungslos. Statt ihrer steht der
  // Grund im Menü (wie in der Profilkarte), als Text in Sekundärfarbe statt eines blassen gesperrten Eintrags.
  it('ausgeloggt im E-Ink-Modus: statt der Design-Wahl steht der Hinweis „bleibt immer hell“', () => {
    const fixture = render({ loggedIn: false, keys: ['puzzles'], eink: true });
    const close = openMenu(fixture);
    expect(document.querySelector('.cdk-overlay-container button.theme-menu-trigger')).toBeNull();
    const note = document.querySelector('.cdk-overlay-container .theme-eink-note') as HTMLElement;
    expect(note).not.toBeNull();
    expect(note.textContent).toContain('profile.theme.einkForcesLight');
    expect(getComputedStyle(note).opacity).toBe('1');
    close();
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
    expect(legalItems()).toEqual(['legal.privacy.link', 'legal.impressum.title']);
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

  // d5-rk-konto-menue-sprache-icon: im Konto-Menü trägt kein Eintrag ein Symbol — auch „Sprache“ nicht (fluchtet).
  it('eingeloggt: ☰ → Konto — „Sprache“ ohne Globus, kein Eintrag mit Symbol', () => {
    const fixture = render({ loggedIn: true, keys: ['dashboard'] });
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar .msg-mail') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const account = Array.from(document.querySelectorAll('.cdk-overlay-container button'))
      .find(b => b.textContent?.includes('nav.account')) as HTMLButtonElement;
    account.click();
    fixture.detectChanges();
    const lang = overlayItem('lang-item')!;
    expect(lang).not.toBeNull();
    expect(lang.textContent).toContain('nav.language');
    const panel = lang.closest('.mat-mdc-menu-panel')!;
    expect(panel.querySelectorAll('[mat-menu-item] mat-icon:not(.mat-mdc-menu-submenu-icon)').length).toBe(0);
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
    expect(legalItems()).toEqual(['legal.privacy.link', 'legal.impressum.title']);
    trigger.click();
    fixture.detectChanges();
  });

  // b1-rk-nav-konto-untermenue-handy: am Handy schob die CDK das Konto-Untermenü an x = 0, es schnitt das Hauptmenü an.
  // Die Untermenüs tragen eine eigene Klasse (Panel, Pane per :has); unter 600 px liegen sie 8 px vom rechten Rand über dem
  // Hauptmenü. Karma läuft 1 400 px breit → Lage als CSS-Text, Klassen am geöffneten Menü.
  it('eingeloggt: ☰ → Konto öffnet ein Untermenü mit eigener Klasse; unter 600 px rechts über dem Hauptmenü', () => {
    const fixture = render({ loggedIn: true, keys: ['dashboard'] });
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar .msg-mail') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const account = Array.from(document.querySelectorAll('.cdk-overlay-container button'))
      .find(b => b.textContent?.includes('nav.account')) as HTMLButtonElement;
    account.click();
    fixture.detectChanges();
    const panel = document.querySelector('.cdk-overlay-container .nav-submenu.mat-mdc-menu-panel');
    expect(panel).not.toBeNull();
    expect(panel!.parentElement!.classList).toContain('cdk-overlay-pane');
    // Genau dieses Untermenü trifft die Regel, das Hauptmenü bleibt unberührt.
    expect(document.querySelectorAll('.cdk-overlay-pane:has(> .nav-submenu)').length).toBe(1);
    expect(document.querySelectorAll('.cdk-overlay-pane:not(:has(> .nav-submenu)) .mat-mdc-menu-panel').length).toBe(1);
    trigger.click();
    fixture.detectChanges();

    const css = ((NavbarComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s/g, '');
    const at = css.indexOf('@media(max-width:599.98px)');
    expect(at).toBeGreaterThan(-1);
    const block = css.slice(at, css.indexOf('}}', at) + 2);
    expect(block).toContain('.cdk-overlay-pane:has(>.nav-submenu){left:auto!important;right:8px!important');
    expect(block).toContain('.nav-submenu.mat-mdc-menu-panel{min-width:232px');
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

  /** UI-Review header-anon-mobile: unter 480px nur „Anmelden“ + ☰ in der Leiste — „Registrieren“ und Vollbild stehen
   *  dann im ☰ (.anon-narrow; breiter per CSS aus), in der Leiste tragen sie .anon-wide (schmal per CSS aus). */
  it('Gast am Handy: „Registrieren“ und Vollbild auch im ☰-Menü, in der Leiste nur breit', async () => {
    const { el } = await renderAt('/puzzles');
    expect(el.querySelector('mat-toolbar a[routerLink="/register"]')!.classList).toContain('anon-wide');
    expect(el.querySelector('mat-toolbar button[aria-label="nav.fullscreen"]')!.classList).toContain('anon-wide');
    expect(el.querySelector('mat-toolbar a[routerLink="/login"]')!.classList).not.toContain('anon-wide');
    (el.querySelector('mat-toolbar button[aria-label="nav.menu"]') as HTMLButtonElement).click();
    const narrow = Array.from(document.querySelectorAll('.cdk-overlay-container .anon-narrow'));
    expect(narrow.length).toBe(2);
    expect(narrow[0].getAttribute('href')).toBe('/register?returnUrl=%2Fpuzzles');
    expect(narrow[1].textContent).toContain('nav.fullscreen');
  });

  // b1-rk-navbar-gast-menue-mitte: das ☰ stand zwischen „Anmelden“ und „Registrieren“. Jetzt: Anmelden · Registrieren · ☰.
  it('Gast: „Registrieren“ steht direkt hinter „Anmelden“, das ☰ ganz am Ende', async () => {
    const { el } = await renderAt('/puzzles');
    const login = el.querySelector('mat-toolbar a[routerLink="/login"]')!;
    const register = el.querySelector('mat-toolbar a[routerLink="/register"]')!;
    const menu = el.querySelector('mat-toolbar button[aria-label="nav.menu"]')!;
    expect(login.nextElementSibling).toBe(register);
    expect(register.compareDocumentPosition(menu) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('auf einer offenen Seite: zurück zu genau dieser Seite', async () => {
    expect((await renderAt('/puzzles/daily/today')).login).toBe('/login?returnUrl=%2Fpuzzles%2Fdaily%2Ftoday');
  });
});

/** UI-Sweep u1 (u1-rk-navbar-raender): ab 2400 px rückt der Leisteninhalt auf die Kante des breiten Inhalts (1 880 px). */
describe('NavbarComponent Breitbild-Stufe (u1-rk-navbar-raender)', () => {
  const css = ((NavbarComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
  it('polstert die Toolbar erst ab 2400 px auf die Inhaltskante (--nav-edge, Rückfall 1 880 px), darunter unverändert', () => {
    const at = css.indexOf('@media (min-width: 2400px)');
    expect(at).toBeGreaterThan(-1);
    // Sass schreibt calc() in max() als reinen Ausdruck aus — beides ist gültiges CSS.
    const m = /mat-toolbar[^{]*\{ ?padding-inline: (max\(16px, (?:calc\()?\(100% - var\(--(?:%NS%)?nav-edge, var\(--(?:%NS%)?page-max-width-wide, 1880px\)\)\) \/ 2 \+ 16px\)?\))/.exec(css.slice(at));
    expect(m).not.toBeNull();
    expect(CSS.supports('padding-inline', m![1].replace(/%NS%/g, ''))).toBeTrue();
    expect(css.slice(0, at)).not.toContain('padding-inline');
  });
});


/** EINE Leiste (0.785.x, E-Book-Leser kompakt): body.nav-docked legt die Kopfleiste oben rechts auf die Leiste der Seite. */
describe('NavbarComponent angedockt (body.nav-docked)', () => {
  function render(loggedIn: boolean, unseen = 0) {
    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {
          currentUser$: of(loggedIn ? { username: 'u' } : null), isLoggedIn: loggedIn, isAdmin: false, logout: () => {}, has: () => false,
        } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }), accessChanged$: of(undefined) } },
        { provide: CatalogService, useValue: { access: () => of({ hasAccess: false }) } },
        { provide: MenuService, useValue: { visible$: of(new Set<string>(['puzzles', 'analysis'])) } },
        { provide: InAppNotificationService, useValue: { unseenCount$: of(unseen), refreshCount: () => {}, reset: () => {}, list: () => of([]), markAllSeen: () => of(null) } },
        { provide: MessageService, useValue: { userUnread$: of(unseen), refreshUserUnread: () => {}, reset: () => {} } },
        { provide: LocaleService, useValue: { languages: [], current: 'en', use: () => {} } },
        { provide: ThemeService, useValue: { preference: 'system', isDark: false, toggle: () => {} } },
        provideRouter([]),
      ],
    });
    const fixture = TestBed.createComponent(NavbarComponent);
    fixture.detectChanges();
    return fixture;
  }
  afterEach(() => document.body.classList.remove(NAV_DOCKED_CLASS));

  it('d3-rk-leser-glocken-abzeichen-abgeschnitten: angedockt liegen die Abzeichen von Glocke und ☰ ganz im Fenster (auch beim Pulsieren)', () => {
    document.body.classList.add(NAV_DOCKED_CLASS);
    const fixture = render(true, 3);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    for (const sel of ['.notif-bell', '.msg-mail']) {
      const badge = el.querySelector<HTMLElement>(`${sel} .mat-badge-content`)!;
      expect(badge).withContext(sel).not.toBeNull();
      badge.style.animation = 'none';
      const r = badge.getBoundingClientRect();
      // Pulsieren ×1,28 um die Mitte: Oberkante wandert um 14 % der Höhe nach oben.
      expect(r.top - r.height * 0.14).withContext(sel).toBeGreaterThanOrEqual(0);
    }
    fixture.destroy();
  });

  it('ohne Klasse unverändert: im Fluss, volle Höhe, Logo sichtbar', () => {
    const el = render(true).nativeElement as HTMLElement;
    expect(getComputedStyle(el).position).toBe('static');
    expect(getComputedStyle(el.querySelector('.logo')!).display).not.toBe('none');
  });

  it('angedockt: fixed oben rechts, 48 px hoch, ohne Logo und Hintergrund — Glocke und ☰ bleiben dieselben Knöpfe', async () => {
    document.body.classList.add(NAV_DOCKED_CLASS);
    const fixture = render(true);
    const el = fixture.nativeElement as HTMLElement;
    const host = getComputedStyle(el);
    expect(host.position).toBe('fixed');
    expect(host.top).toBe('0px');
    expect(host.right).toBe('0px');
    expect(Number(host.zIndex)).toBeLessThan(900);   // unter dem Handy-Vollbild-Brett und den Overlays
    const bar = el.querySelector('mat-toolbar') as HTMLElement;
    expect(bar.getBoundingClientRect().height).toBe(48);
    expect(getComputedStyle(bar).backgroundColor).toBe('rgba(0, 0, 0, 0)');
    expect(getComputedStyle(el.querySelector('.logo')!).display).toBe('none');
    expect(el.querySelector('.notif-bell')).not.toBeNull();
    expect(el.querySelector('.msg-mail')).not.toBeNull();
    // Schmal: nur die Knöpfe, nicht die ganze Fensterbreite.
    expect(el.getBoundingClientRect().width).toBeLessThan(200);
    // Breite für die andockende Seite.
    const want = `${Math.ceil(el.getBoundingClientRect().width)}px`;
    for (let i = 0; i < 20 && document.documentElement.style.getPropertyValue(NAV_WIDTH_VAR) !== want; i++) {
      await new Promise(r => requestAnimationFrame(() => r(null)));
    }
    expect(document.documentElement.style.getPropertyValue(NAV_WIDTH_VAR)).toBe(`${Math.ceil(el.getBoundingClientRect().width)}px`);
    fixture.destroy();
    expect(document.documentElement.style.getPropertyValue(NAV_WIDTH_VAR)).toBe('');
  });

  it('angedockt als Gast: Text-Links Puzzles/Analyse ausgeblendet (stehen im ☰), Anmelden + ☰ bleiben', () => {
    document.body.classList.add(NAV_DOCKED_CLASS);
    const el = render(false).nativeElement as HTMLElement;
    const anon = Array.from(el.querySelectorAll<HTMLElement>('.nav-anon'));
    expect(anon.length).toBe(2);
    expect(anon.every(a => getComputedStyle(a).display === 'none')).toBeTrue();
    expect(getComputedStyle(el.querySelector('.nav-login')!).display).not.toBe('none');
    expect(el.querySelector('.anon-menu')).not.toBeNull();
  });
});
