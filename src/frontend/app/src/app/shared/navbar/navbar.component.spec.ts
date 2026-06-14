import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { TranslateService } from '@ngx-translate/core';
import { NavbarComponent } from './navbar.component';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { MenuService } from '../../core/menu.service';
import { ChallengeService } from '../../core/challenge.service';
import { LocaleService } from '../../core/locale.service';
import { ThemeService } from '../../core/theme.service';

describe('NavbarComponent', () => {
  // Über TestBed in einem Injection-Context bauen: NavbarComponent nutzt
  // inject(DestroyRef) als Field-Initializer, ein nacktes `new` würde NG0203 werfen.
  function build(): NavbarComponent {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { currentUser$: of(null), isAdmin: false } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }) } },
        { provide: MenuService, useValue: { visible$: of(new Set<string>()) } },
        { provide: ChallengeService, useValue: { incomingCount$: of(0), refreshCount: () => {} } },
        { provide: LocaleService, useValue: {} },
        { provide: ThemeService, useValue: { preference: 'system', isDark: false, toggle: () => {} } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
    return TestBed.runInInjectionContext(() => new NavbarComponent(
      TestBed.inject(AuthService),
      TestBed.inject(CourseService),
      TestBed.inject(MenuService),
      TestBed.inject(ChallengeService),
      TestBed.inject(LocaleService),
      TestBed.inject(ThemeService),
      TestBed.inject(TranslateService),
    ));
  }

  it('baut ohne Fehler (App-Installation verlinkt jetzt auf /install statt Dialog)', () => {
    expect(build()).toBeTruthy();
  });
});
