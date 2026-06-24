import { of, throwError } from 'rxjs';
import { ProfileComponent } from './profile.component';

/** Direkt instanziiert (ohne TestBed/Template) — testet die Komponenten-Logik. */
function make(overrides: { profileService?: any; offline?: any; auth?: any; discord?: any } = {}) {
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
  const offline = overrides.offline ?? {
    puzzleCount: 10, endlessRuns: 2,
    setPuzzleCount: jasmine.createSpy('setPuzzleCount'),
    setEndlessRuns: jasmine.createSpy('setEndlessRuns'),
    formatSize: () => '0 B', cacheSizeBytes: () => 0, cachedBookCount: () => 0, clearAll: jasmine.createSpy('clearAll'),
  };
  const offlineQueue = { pendingCount: () => 0 };
  const auth = overrides.auth ?? {
    deleteAccount: jasmine.createSpy('deleteAccount').and.returnValue(of({})),
    changePassword: jasmine.createSpy('changePassword').and.returnValue(of({})),
  };
  const theme = {};
  const c = new ProfileComponent(
    profileService as any, snackbar as any, translate as any, discord as any,
    offline as any, offlineQueue as any, auth as any, theme as any,
  );
  return { c, profileService, snackbar, discord, offline, auth };
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

  it('changePassword refuses a mismatch without hitting the API', () => {
    const { c, auth, snackbar } = make();
    c.changePwdCurrent = 'old';
    c.changePwdNew = 'new1';
    c.changePwdConfirm = 'new2';
    c.changePassword();
    expect(auth.changePassword).not.toHaveBeenCalled();
    expect(snackbar.info).toHaveBeenCalledWith('profile.changePwd.mismatch');
  });

  it('changePassword calls auth.changePassword when the new passwords match', () => {
    const { c, auth, snackbar } = make();
    c.changePwdCurrent = 'old';
    c.changePwdNew = 'secret99';
    c.changePwdConfirm = 'secret99';
    c.changePassword();
    expect(auth.changePassword).toHaveBeenCalledWith('old', 'secret99');
    expect(snackbar.success).toHaveBeenCalledWith('profile.changePwd.done');
    expect(c.showChangePwd).toBeFalse();
  });

  it('deleteAccount is a no-op without a password, and calls auth.deleteAccount otherwise', () => {
    const { c, auth } = make();
    c.deletePassword = '';
    c.deleteAccount();
    expect(auth.deleteAccount).not.toHaveBeenCalled();

    c.deletePassword = 'pw';
    c.deleteAccount();
    expect(auth.deleteAccount).toHaveBeenCalledWith('pw');
  });

  it('saveOffline pushes the counts into the offline service and reflects clamped values', () => {
    const offline = {
      puzzleCount: 20, endlessRuns: 5,
      setPuzzleCount: jasmine.createSpy('setPuzzleCount'),
      setEndlessRuns: jasmine.createSpy('setEndlessRuns'),
      formatSize: () => '0 B', cacheSizeBytes: () => 0, cachedBookCount: () => 0, clearAll: jasmine.createSpy('clearAll'),
    };
    const { c } = make({ offline });
    c.offlinePuzzleCount = 999;
    c.offlineEndlessRuns = 999;
    c.saveOffline();
    expect(offline.setPuzzleCount).toHaveBeenCalledWith(999);
    expect(offline.setEndlessRuns).toHaveBeenCalledWith(999);
    // geklemmte Werte aus dem Service zurückgespiegelt
    expect(c.offlinePuzzleCount).toBe(20);
    expect(c.offlineEndlessRuns).toBe(5);
  });
});
