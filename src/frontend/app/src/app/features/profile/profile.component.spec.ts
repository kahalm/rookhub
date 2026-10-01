import { ElementRef } from '@angular/core';
import { convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ProfileComponent } from './profile.component';

/** Direkt instanziiert (ohne TestBed/Template) — testet die Komponenten-Logik.
 *  Offline/Theme/Passwort/Konto-Löschen sind in eigene Kind-Komponenten ausgelagert
 *  (siehe *-card.component.spec.ts). */
function make(overrides: { profileService?: any; discord?: any; impersonating?: boolean; query?: Record<string, string> } = {}) {
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
  const auth = { isImpersonating: overrides.impersonating ?? false };
  const route = overrides.query ? { snapshot: { queryParamMap: convertToParamMap(overrides.query) } } : undefined;
  const c = new ProfileComponent(
    profileService as any, snackbar as any, translate as any, discord as any, auth as any, route as any,
  );
  return { c, profileService, snackbar, discord };
}

describe('ProfileComponent', () => {
  it('ngOnInit loads the profile, remembers the saved email and clears loading', () => {
    const { c, profileService } = make();
    c.ngOnInit();
    expect(profileService.getProfile).toHaveBeenCalled();
    expect(c.profile).toEqual({ email: 'a@b.c' } as any);
    expect(c.savedEmail).toBe('a@b.c');
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
    c.savedEmail = 'a@b.c';
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
    c.savedEmail = 'a@b.c';
    c.profile = { email: 'a@b.c' } as any;
    c.save();
    expect(snackbar.info).toHaveBeenCalledWith('profile.emailTaken');
    expect(c.saving).toBeFalse();
  });

  // Die E-Mail ist der Reset-Anker: der Server verlangt fuer einen WECHSEL (auch Erst-Setzen und
  // Entfernen) das aktuelle Passwort und antwortet sonst mit 403. Ohne Feld und ohne eigene
  // Meldung liess sich die Adresse in der Oberflaeche gar nicht mehr aendern.
  describe('E-Mail-Wechsel', () => {
    const failing = (status: number) => ({
      getProfile: jasmine.createSpy('getProfile').and.returnValue(of({})),
      updateProfile: jasmine.createSpy('updateProfile').and.returnValue(throwError(() => ({ status }))),
      searchPlayer: jasmine.createSpy('searchPlayer'),
    });

    it('schickt bei unveraenderter Adresse (andere Schreibweise) kein Passwort mit', () => {
      const { c, profileService } = make();
      c.savedEmail = 'A@B.c';
      c.profile = { email: ' a@b.c ' } as any;
      c.currentPassword = 'stale';
      c.save();
      const body = profileService.updateProfile.calls.mostRecent().args[0];
      expect('currentPassword' in body).toBeFalse();
    });

    it('schickt einen Wechsel ohne Passwort gar nicht erst ab und fragt danach', () => {
      const { c, profileService, snackbar } = make();
      c.savedEmail = 'a@b.c';
      c.profile = { email: 'neu@b.c' } as any;
      c.save();
      expect(profileService.updateProfile).not.toHaveBeenCalled();
      expect(snackbar.info).toHaveBeenCalledWith('profile.emailPasswordRequired');
      expect(c.saving).toBeFalse();
    });

    it('schickt das Passwort mit, merkt sich danach die neue Adresse und leert das Feld', () => {
      const profileService = {
        getProfile: jasmine.createSpy('getProfile').and.returnValue(of({})),
        updateProfile: jasmine.createSpy('updateProfile').and.returnValue(of({ email: 'neu@b.c' })),
        searchPlayer: jasmine.createSpy('searchPlayer'),
      };
      const { c, snackbar } = make({ profileService });
      c.savedEmail = 'a@b.c';
      c.profile = { email: 'neu@b.c' } as any;
      c.currentPassword = 'Secret123!';
      c.save();
      const body = profileService.updateProfile.calls.mostRecent().args[0];
      expect(body.email).toBe('neu@b.c');
      expect(body.currentPassword).toBe('Secret123!');
      expect(c.savedEmail).toBe('neu@b.c');
      expect(c.currentPassword).toBe('');
      expect(snackbar.success).toHaveBeenCalledWith('profile.saved');
    });

    it('verlangt das Passwort auch beim Entfernen der Adresse', () => {
      const { c, profileService } = make();
      c.savedEmail = 'a@b.c';
      c.profile = { email: null } as any;
      c.currentPassword = 'Secret123!';
      c.save();
      const body = profileService.updateProfile.calls.mostRecent().args[0];
      expect(body.email).toBe('');
      expect(body.currentPassword).toBe('Secret123!');
    });

    it('meldet eine 403 beim Wechsel als falsches Passwort und leert das Feld', () => {
      const { c, snackbar } = make({ profileService: failing(403) });
      c.savedEmail = 'a@b.c';
      c.profile = { email: 'neu@b.c' } as any;
      c.currentPassword = 'falsch';
      c.save();
      expect(snackbar.info).toHaveBeenCalledWith('profile.emailPasswordWrong');
      expect(c.currentPassword).toBe('');
      expect(c.savedEmail).withContext('Adresse gilt weiter als nicht gespeichert').toBe('a@b.c');
      expect(c.saving).toBeFalse();
    });

    it('fragt unter Impersonation nicht nach dem Passwort; deren 403 bleibt die allgemeine Meldung', () => {
      const profileService = failing(403);
      const { c, snackbar } = make({ profileService, impersonating: true });
      c.savedEmail = 'a@b.c';
      c.profile = { email: 'neu@b.c' } as any;
      c.save();
      const body = profileService.updateProfile.calls.mostRecent().args[0];
      expect('currentPassword' in body).toBeFalse();
      expect(snackbar.info).toHaveBeenCalledWith('profile.saveFailed');
    });
  });

  // Der Knopf „Konto jetzt loeschen" auf /account-deletion fuehrt auf /profile?section=delete (UX-023): die Karte ist
  // der letzte von elf Bloecken — ohne Sprung scrollte man am Handy an allen anderen vorbei.
  describe('?section=delete', () => {
    const card = () => {
      const el = document.createElement('div');
      const scroll = spyOn(el, 'scrollIntoView');
      return { ref: new ElementRef<HTMLElement>(el), scroll };
    };

    it('klappt die Loesch-Karte auf und scrollt einmal zu ihr, sobald sie da ist', () => {
      const { c } = make({ query: { section: 'delete' } });
      expect(c.openDelete).toBeTrue();
      c.deleteCard = undefined;                 // vor dem Laden: noch keine Karte
      const first = card();
      c.deleteCard = first.ref;
      expect(first.scroll).toHaveBeenCalledTimes(1);
      const again = card();
      c.deleteCard = again.ref;                 // spaeteres Neuzeichnen springt nicht erneut
      expect(again.scroll).not.toHaveBeenCalled();
    });

    it('ohne den Parameter bleibt alles wie gehabt', () => {
      for (const query of [undefined, { section: 'other' }]) {
        const { c } = make({ query });
        expect(c.openDelete).toBeFalse();
        const x = card();
        c.deleteCard = x.ref;
        expect(x.scroll).not.toHaveBeenCalled();
      }
    });
  });
});
