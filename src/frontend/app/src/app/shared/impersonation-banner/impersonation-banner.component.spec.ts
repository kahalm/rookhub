import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { ImpersonationBannerComponent } from './impersonation-banner.component';

/**
 * Der Streifen ist die einzige Stelle, an der ein Admin SIEHT, dass er nicht er selbst ist — und
 * er steht in beiden Oberflaechen. Faellt er weg, aendert jemand ahnungslos fremde Daten.
 */
describe('ImpersonationBannerComponent', () => {
  let fixture: ComponentFixture<ImpersonationBannerComponent>;
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [ImpersonationBannerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    fixture = TestBed.createComponent(ImpersonationBannerComponent);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  function loginAdminAndImpersonate(): void {
    auth.adoptSession({ token: jwt(), userId: 1, username: 'chef', isAdmin: true });
    // `impersonatorUsername` liefert der SERVER mit dem Token — der Streifen nennt es, es wird
    // hier nicht aus der gesicherten Anmeldung geraten.
    auth.impersonate({
      token: jwt(), userId: 7, username: 'spieler', isAdmin: false,
      impersonating: true, impersonatorUsername: 'chef',
    });
  }

  it('bleibt unsichtbar, solange niemand eingestiegen ist', () => {
    auth.adoptSession({ token: jwt(), userId: 1, username: 'chef', isAdmin: true });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.imp-banner')).toBeNull();
  });

  it('nennt beide Namen, sobald ein Admin eingestiegen ist', () => {
    loginAdminAndImpersonate();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.imp-banner')).toBeTruthy();
    expect(auth.isImpersonating).toBeTrue();
    expect(auth.impersonatorUsername).toBe('chef');
  });

  it('führt über den Knopf zurück in die Admin-Anmeldung', () => {
    loginAdminAndImpersonate();
    fixture.detectChanges();
    const navigate = spyOn(TestBed.inject(Router), 'navigate');

    fixture.nativeElement.querySelector('.imp-exit').click();
    fixture.detectChanges();

    expect(auth.isImpersonating).toBeFalse();
    expect(auth.currentUser?.username).toBe('chef');
    expect(navigate).toHaveBeenCalledWith(['/admin']);
    expect(fixture.nativeElement.querySelector('.imp-banner')).toBeNull();
  });
});

/** Ein Token, das noch eine Stunde gilt — `AuthService` wirft abgelaufene sonst sofort weg. */
function jwt(expSecondsFromNow = 3600): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expSecondsFromNow }));
  return `x.${payload}.y`;
}
