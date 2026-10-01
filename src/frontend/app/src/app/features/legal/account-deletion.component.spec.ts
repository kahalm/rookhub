import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { HandoffService } from '../../core/handoff.service';
import { AccountDeletionComponent } from './account-deletion.component';
import { LEGAL_SITE } from './legal-site';

/** Was jede Fassung braucht: Router, Uebersetzung und — fuer den Sprung nach RookHub — HTTP. */
const base = () => [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' })];

describe('AccountDeletionComponent', () => {
  it('renders (template compiles)', async () => {
    await TestBed.configureTestingModule({
      imports: [AccountDeletionComponent],
      providers: base(),
    }).compileComponents();
    const f = TestBed.createComponent(AccountDeletionComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });

  it('nennt den Kontakt der Oberflaeche (KidHub: eigene Adresse)', () => {
    TestBed.configureTestingModule({
      imports: [AccountDeletionComponent],
      providers: [...base(), { provide: LEGAL_SITE, useValue: { contactEmail: 'kidhub@oberschm.id', imprint: false } }],
    });
    const f = TestBed.createComponent(AccountDeletionComponent);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('a[href="mailto:kidhub@oberschm.id"]')).not.toBeNull();
  });

  it('Ruecklink je Oberflaeche: RookHub zur Anmeldung, KidHub zur Startseite (F7-003)', () => {
    const render = (site?: object) => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [AccountDeletionComponent],
        providers: [...base(), ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : [])],
      });
      const f = TestBed.createComponent(AccountDeletionComponent);
      f.detectChanges();
      return f.nativeElement as HTMLElement;
    };
    expect(render().querySelector('.back a')?.getAttribute('href')).toBe('/login');
    const kid = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    expect(kid.querySelector('.back a')?.getAttribute('href')).toBe('/');
    expect(kid.textContent).toContain('legal.backHome');
  });

  it('Titel als h1, Abschnitte als h2 — keine uebersprungene Ebene (UX-057)', () => {
    TestBed.configureTestingModule({ imports: [AccountDeletionComponent], providers: base() });
    const f = TestBed.createComponent(AccountDeletionComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('legal.accountDeletion.title');
    expect(el.querySelectorAll('h2').length).toBeGreaterThan(2);
    expect(el.querySelectorAll('h3, h4, h5, h6').length).toBe(0);
  });

  describe('ein gangbarer Weg zur Loeschung (UX-023)', () => {
    const render = (site?: object, homeUrl: string | null = null) => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [AccountDeletionComponent],
        providers: [...base(), ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : [])],
      });
      spyOnProperty(TestBed.inject(HandoffService), 'accountHomeUrl', 'get').and.returnValue(homeUrl);
      const f = TestBed.createComponent(AccountDeletionComponent);
      f.detectChanges();
      return f.nativeElement as HTMLElement;
    };

    it('RookHub: genau ein Primaerknopf „Konto jetzt loeschen" ins Profil mit aufgeklappter Karte', () => {
      // Vorher nur Fliesstext „Profil → Konto loeschen" — kein Link, der einzige Primaerknopf war „Registrieren".
      const el = render();
      const buttons = el.querySelectorAll('a.delete-now');
      expect(buttons.length).toBe(1);
      expect(buttons[0].getAttribute('href')).toBe('/profile?section=delete');
      // Abgemeldet fuehrt der authGuard ueber die Anmeldung dorthin; die Routen-Specs lassen das zu.
      expect(buttons[0].hasAttribute('data-login-required')).toBeTrue();
      expect(el.textContent).toContain('legal.accountDeletion.deleteNow');
      expect(el.textContent).toContain('legal.accountDeletion.inApp');
      expect(el.textContent).not.toContain('legal.accountDeletion.inPartner');
    });

    it('KidHub/LeagueHub: sagt, dass es ein RookHub-Konto ist, und verlinkt RookHubs Profil', () => {
      const el = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/', accountHome: 'rookhub' },
        'https://rookhub.example');
      const a = el.querySelector('a.delete-now');
      expect(a?.getAttribute('href')).toBe('https://rookhub.example/profile?section=delete');
      expect(a?.hasAttribute('data-login-required')).toBeFalse();
      expect(el.textContent).toContain('legal.accountDeletion.introPartner');
      expect(el.textContent).toContain('legal.accountDeletion.inPartner');
      expect(el.textContent).toContain('legal.accountDeletion.deleteOnRookHub');
      // Kein Profil-Link auf die eigene Seite — dort gibt es keins (der Catch-all fuehrte still zur Startseite).
      expect(el.querySelector('a[href="/profile?section=delete"]')).toBeNull();
    });

    it('der Klick springt mit Anmeldung (Einmal-Code) nach RookHub, Strg-Klick bleibt beim Link', () => {
      render({ contactEmail: 'x@y.z', imprint: true, kind: 'leaguehub', accountHome: 'rookhub' }, 'https://rookhub.example');
      const jump = spyOn(TestBed.inject(HandoffService), 'jumpToAccountHome').and.resolveTo();
      const f = TestBed.createComponent(AccountDeletionComponent);
      f.detectChanges();
      const link = f.debugElement.query(By.css('a.delete-now'));
      // Ueber den Angular-Listener, ohne echtes DOM-Ereignis: ein nicht abgefangener Klick verliesse die Testseite.
      const click = (init: Partial<MouseEvent>) => {
        const e = { button: 0, ctrlKey: false, metaKey: false, shiftKey: false, altKey: false,
          preventDefault: jasmine.createSpy('preventDefault'), ...init };
        link.triggerEventHandler('click', e);
        return e;
      };

      const ctrl = click({ ctrlKey: true });
      expect(jump).not.toHaveBeenCalled();
      expect(ctrl.preventDefault).not.toHaveBeenCalled();

      const plain = click({});
      expect(jump).toHaveBeenCalledOnceWith('profile?section=delete');
      expect(plain.preventDefault).toHaveBeenCalled();
    });

    it('ohne bekannte RookHub-Adresse (localhost, IP) nur der Text, kein Knopf ins Leere', () => {
      const el = render({ contactEmail: 'x@y.z', imprint: true, accountHome: 'rookhub' }, null);
      expect(el.querySelector('a.delete-now')).toBeNull();
      expect(el.textContent).toContain('legal.accountDeletion.inPartner');
    });
  });

  // DeleteAccountAsync loescht weit mehr als „Identitaet, Repertoires, Turnier-Abos, Freunde": eigene Kurse samt
  // Freigaben und fremdem Fortschritt, Partien mit Formular-Fotos, Aufgabenblaetter, Teilen-Links, KidHub-Fortschritt,
  // Verbindungen. Ein Trainer las die alte Liste und hielt seine Kurse fuer sicher (UX-021).
  describe('was verloren geht und was bleibt (UX-021)', () => {
    const render = (site?: object, homeUrl: string | null = null) => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [AccountDeletionComponent],
        providers: [...base(), ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : [])],
      });
      spyOnProperty(TestBed.inject(HandoffService), 'accountHomeUrl', 'get').and.returnValue(homeUrl);
      const f = TestBed.createComponent(AccountDeletionComponent);
      f.detectChanges();
      return f.nativeElement as HTMLElement;
    };
    const removed = (el: HTMLElement) => [...el.querySelectorAll('ul.removed li')].map(li => li.textContent?.trim());

    it('nennt Kurse, Partien, Aufgabenblaetter/Teilen-Links, KidHub und Verbindungen — vor dem Knopf der Sicherungs-Hinweis', () => {
      const el = render();
      expect(removed(el)).toEqual([
        'legal.accountDeletion.removed1', 'legal.accountDeletion.removedCourses', 'legal.accountDeletion.removed2',
        'legal.accountDeletion.removedGames', 'legal.accountDeletion.removedShared', 'legal.accountDeletion.removedKids',
        'legal.accountDeletion.removedConnections',
      ]);
      const backup = el.querySelector('p.backup');
      expect(backup?.textContent).toContain('legal.accountDeletion.backup');
      // Der Hinweis steht VOR dem Knopf, nicht unter der Liste.
      expect(backup!.compareDocumentPosition(el.querySelector('a.delete-now')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      const exports = [...el.querySelectorAll('.backup-links a')];
      expect(exports.map(a => a.getAttribute('href'))).toEqual(['/courses', '/repertoires', '/games']);
      expect(exports.every(a => a.hasAttribute('data-login-required'))).toBeTrue();
      expect(el.textContent).not.toContain('legal.accountDeletion.keptLeague');
    });

    it('LeagueHub: Entwuerfe gehen, Vereinspartien bleiben ohne Hochlader-Vermerk — mit Weg zu den eigenen Uploads', () => {
      const el = render({ contactEmail: 'x@y.z', imprint: true, kind: 'leaguehub', accountHome: 'rookhub' }, 'https://rookhub.example');
      expect(removed(el)).toContain('legal.accountDeletion.removedLeague');
      expect(el.textContent).toContain('legal.accountDeletion.keptLeagueTitle');
      expect(el.textContent).toContain('legal.accountDeletion.keptLeague');
      expect(el.textContent).toContain('legal.accountDeletion.keptLeagueShares');
      expect(el.querySelector('.league-kept a')?.getAttribute('href')).toBe('/verein');
      // Die PGN-Exporte liegen in RookHub.
      expect([...el.querySelectorAll('.backup-links a')].map(a => a.getAttribute('href')))
        .toEqual(['https://rookhub.example/courses', 'https://rookhub.example/repertoires', 'https://rookhub.example/games']);
    });

    it('ohne bekannte RookHub-Adresse keine Export-Links ins Leere', () => {
      const el = render({ contactEmail: 'x@y.z', imprint: false, kind: 'kidhub', back: '/', accountHome: 'rookhub' }, null);
      expect(el.querySelector('.backup-links')).toBeNull();
      expect(el.querySelector('p.backup')).not.toBeNull();
    });

    it('jeder gezeigte Text hat in den gepflegten Sprachen eine Uebersetzung; die Profil-Warnung nennt die Kurse', async () => {
      const el = render({ contactEmail: 'x@y.z', imprint: true, kind: 'leaguehub', accountHome: 'rookhub' });
      // Ohne Uebersetzung stehen die Schluessel ohne Leerzeichen hintereinander — am naechsten „legal." trennen.
      const keys = [...el.textContent!.matchAll(/legal\.accountDeletion\.([A-Za-z0-9]+?)(?=legal\.|[^A-Za-z0-9]|$)/g)].map(m => m[1]);
      expect(keys.length).toBeGreaterThan(10);
      for (const lang of ['en', 'de', 'hr', 'hu']) {
        let json: Record<string, any> | undefined;
        for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
          const res = await fetch(url);
          if (res.ok) { json = await res.json(); break; }
        }
        const section = json!['legal']['accountDeletion'];
        for (const k of keys) expect(section[k]).withContext(`${lang}: ${k}`).toBeTruthy();
      }
      const en = await (await fetch('/i18n/en.json').then(r => r.ok ? r : fetch('/base/i18n/en.json'))).json();
      expect(en.profile.delete.warn).toContain('courses');
      expect(en.profile.delete.warn).toContain('KidHub');
    });
  });
});
