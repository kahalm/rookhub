import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { NavbarComponent } from './navbar.component';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { MenuService } from '../../core/menu.service';
import { InAppNotificationService } from '../../core/in-app-notification.service';
import { MessageService } from '../../core/message.service';
import { LocaleService } from '../../core/locale.service';
import { ThemeService } from '../../core/theme.service';

describe('NavbarComponent', () => {
  // Über TestBed in einem Injection-Context bauen: NavbarComponent nutzt
  // inject(DestroyRef) als Field-Initializer, ein nacktes `new` würde NG0203 werfen.
  function build(notifMock?: Partial<InAppNotificationService>): NavbarComponent {
    const notif = { unseenCount$: of(0), refreshCount: () => {}, reset: () => {}, list: () => of([]), markAllSeen: () => of(null), ...notifMock };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { currentUser$: of(null), isAdmin: false } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }) } },
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
      TestBed.inject(MenuService),
      TestBed.inject(InAppNotificationService),
      TestBed.inject(MessageService),
      TestBed.inject(LocaleService),
      TestBed.inject(ThemeService),
      TestBed.inject(TranslateService),
      TestBed.inject(Router),
    ));
  }

  it('baut ohne Fehler (App-Installation verlinkt jetzt auf /install statt Dialog)', () => {
    expect(build()).toBeTruthy();
  });

  it('onBellOpened lädt die Liste, markiert aber NICHT automatisch als gelesen', () => {
    const markAllSeen = jasmine.createSpy('markAllSeen').and.returnValue(of(null));
    const list = jasmine.createSpy('list').and.returnValue(of([{ id: 1, type: 't', data: null, link: null, createdAt: '', seen: false }]));
    const nav = build({ list, markAllSeen });
    nav.onBellOpened();
    expect(list).toHaveBeenCalled();
    expect(markAllSeen).not.toHaveBeenCalled();
    expect(nav.hasUnseen()).toBeTrue();
  });

  it('markAllRead markiert die offene Liste lokal als gelesen, ruft den Service und hält das Menü offen', () => {
    const markAllSeen = jasmine.createSpy('markAllSeen').and.returnValue(of(null));
    const nav = build({ markAllSeen });
    nav.notifications = [{ id: 1, type: 't', data: null, link: null, createdAt: '', seen: false }];
    const event = { stopPropagation: jasmine.createSpy('stopPropagation') } as unknown as Event;
    nav.markAllRead(event);
    expect(event.stopPropagation).toHaveBeenCalled();
    expect(markAllSeen).toHaveBeenCalled();
    expect(nav.hasUnseen()).toBeFalse();
  });
});
