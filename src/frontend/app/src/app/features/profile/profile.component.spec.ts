import { of, throwError } from 'rxjs';
import { ProfileComponent } from './profile.component';

/** Direkt instanziiert (ohne TestBed/Template) — testet die Komponenten-Logik.
 *  Offline/Theme/Passwort/Konto-Löschen sind in eigene Kind-Komponenten ausgelagert
 *  (siehe *-card.component.spec.ts). */
function make(overrides: { profileService?: any; discord?: any } = {}) {
  const profileService = overrides.profileService ?? {
    getProfile: jasmine.createSpy('getProfile').and.returnValue(of({ email: 'a@b.c' })),
    updateProfile: jasmine.createSpy('updateProfile').and.returnValue(of({ email: 'a@b.c' })),
    searchPlayer: jasmine.createSpy('searchPlayer').and.returnValue(of({})),
  };
  const snackbar = {
    success: jasmine.createSpy('success'),
    info: jasmine.createSpy('info'),
  };
  const translate = { instant: (k: string) => k };
  const discord = overrides.discord ?? { unlink: jasmine.createSpy('unlink').and.returnValue(of({})) };
  const c = new ProfileComponent(
    profileService as any, snackbar as any, translate as any, discord as any,
  );
  return { c, profileService, snackbar, discord };
}

describe('ProfileComponent', () => {
  it('ngOnInit loads the profile and clears loading', () => {
    const { c, profileService } = make();
    c.ngOnInit();
    expect(profileService.getProfile).toHaveBeenCalled();
    expect(c.profile).toEqual({ email: 'a@b.c' } as any);
    expect(c.loading).toBeFalse();
  });

  it('ngOnInit clears loading even on error', () => {
    const profileService = {
      getProfile: jasmine.createSpy('getProfile').and.returnValue(throwError(() => ({ status: 500 }))),
      updateProfile: jasmine.createSpy('updateProfile'),
      searchPlayer: jasmine.createSpy('searchPlayer'),
    };
    const { c } = make({ profileService });
    c.ngOnInit();
    expect(c.loading).toBeFalse();
    expect(c.profile).toBeNull();
  });

  it('save is a no-op when there is no profile', () => {
    const { c, profileService } = make();
    c.profile = null;
    c.save();
    expect(profileService.updateProfile).not.toHaveBeenCalled();
  });

  it('save updates the profile and shows success', () => {
    const { c, profileService, snackbar } = make();
    c.profile = { email: 'a@b.c' } as any;
    c.save();
    expect(profileService.updateProfile).toHaveBeenCalled();
    expect(c.saving).toBeFalse();
    expect(snackbar.success).toHaveBeenCalledWith('profile.saved');
  });

  it('save maps a 409 to the emailTaken message', () => {
    const profileService = {
      getProfile: jasmine.createSpy('getProfile').and.returnValue(of({})),
      updateProfile: jasmine.createSpy('updateProfile').and.returnValue(throwError(() => ({ status: 409 }))),
      searchPlayer: jasmine.createSpy('searchPlayer'),
    };
    const { c, snackbar } = make({ profileService });
    c.profile = { email: 'a@b.c' } as any;
    c.save();
    expect(snackbar.info).toHaveBeenCalledWith('profile.emailTaken');
    expect(c.saving).toBeFalse();
  });
});
