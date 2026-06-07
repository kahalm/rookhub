import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { MatDialog } from '@angular/material/dialog';
import { TranslateService } from '@ngx-translate/core';
import { NavbarComponent } from './navbar.component';
import { AuthService } from '../../core/auth.service';
import { CourseService } from '../../features/courses/course.service';
import { LocaleService } from '../../core/locale.service';
import { ThemeService } from '../../core/theme.service';
import { APK_DOWNLOAD_URL, AppInstallDialogComponent } from '../app-install-dialog/app-install-dialog.component';

describe('NavbarComponent — App-Install', () => {
  let dialogOpen: jasmine.Spy;

  // Über TestBed in einem Injection-Context bauen: NavbarComponent nutzt
  // inject(DestroyRef) als Field-Initializer, ein nacktes `new` würde NG0203 werfen.
  function build(): NavbarComponent {
    dialogOpen = jasmine.createSpy('open');
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { currentUser$: of(null), isAdmin: false } },
        { provide: CourseService, useValue: { checkAccess: () => of({ hasAccess: false }) } },
        { provide: LocaleService, useValue: {} },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        { provide: ThemeService, useValue: { preference: 'system', isDark: false, toggle: () => {} } },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
    return TestBed.runInInjectionContext(() => new NavbarComponent(
      TestBed.inject(AuthService),
      TestBed.inject(CourseService),
      TestBed.inject(LocaleService),
      TestBed.inject(MatDialog),
      TestBed.inject(ThemeService),
      TestBed.inject(TranslateService),
    ));
  }

  it('openInstall() oeffnet den Install-Dialog', () => {
    build().openInstall();
    expect(dialogOpen).toHaveBeenCalledWith(AppInstallDialogComponent, jasmine.any(Object));
  });
});

describe('APK_DOWNLOAD_URL', () => {
  it('zeigt auf das jeweils neueste GitHub-Release (kein hartkodierter Versions-Tag)', () => {
    expect(APK_DOWNLOAD_URL).toBe(
      'https://github.com/kahalm/rookhub/releases/latest/download/app-release-signed.apk',
    );
    expect(APK_DOWNLOAD_URL).not.toMatch(/v\d+\.\d+\.\d+/);
  });
});
