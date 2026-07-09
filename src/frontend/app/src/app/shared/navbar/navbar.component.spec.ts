import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { Router } from '@angular/router';
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
