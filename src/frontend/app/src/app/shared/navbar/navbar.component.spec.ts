import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { Router, provideRouter } from '@angular/router';
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
import { MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';

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
