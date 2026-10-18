import { TestBed } from '@angular/core/testing';
import { GUEST_START_URL } from '../../core/handoff.service';
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

  it('Ruecklink ohne Verlauf: RookHub zur Startseite (z1-rk-legal-back-anon), KidHub ebenso (F7-003)', () => {
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
    const rh = render().querySelector('.back a')!;
    expect(rh.getAttribute('href')).toBe(GUEST_START_URL);
    expect(rh.textContent).toContain('legal.backHome');
    const kid = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    expect(kid.querySelector('.back a')?.getAttribute('href')).toBe('/');
    expect(kid.textContent).toContain('legal.backHome');
  });

  // UI-Sweep 2026-10-11 (d2-l-kontoloeschen-flucht, d2-rk-legal-zeilenlaenge)
  // Runde z1 (z1-t-privacy-karte-rechts-leer, z1-rk-privacy-zeilen): Karte = Textspalte + Polster statt 760 px mit
  // ≈ 130 px leerem Rand rechts; Fließtext mit 1,55 Zeilenhöhe und Abstand zwischen den Aufzählungspunkten.
  it('Karte so breit wie ihre 33em-Textspalte (≤ ~75 Zeichen) (kein leerer Rand rechts), 1,55 Zeilenhöhe, Abstand zwischen Punkten; KidHub ohne Begrenzung', () => {
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
    const el = render();
    const content = el.querySelector<HTMLElement>('mat-card-content')!;
    // z2-rk-legal-zeilenlaenge: 33em statt 68ch (68ch ergab in Roboto noch 80–85 Zeichen). Gemessen an einem deutschen
    // Satz in der Schrift der Karte: höchstens ~75 Zeichen je Zeile.
    const cs = getComputedStyle(content);
    const fs = parseFloat(cs.fontSize);
    expect(parseFloat(cs.maxWidth)).toBeCloseTo(33 * fs, 0);
    const ctx = document.createElement('canvas').getContext('2d')!;
    ctx.font = `${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`;
    const sample = 'Der Knopf unten öffnet in deinem Profil die Karte „Konto löschen“ – bist du nicht angemeldet, meldest du dich zuerst an.';
    const perChar = ctx.measureText(sample).width / sample.length;
    expect(parseFloat(cs.maxWidth) / perChar).withContext('Zeichen je Zeile').toBeLessThanOrEqual(76);
    const cardMax = parseFloat(getComputedStyle(el.querySelector('mat-card')!).maxWidth);
    expect(cardMax).toBeLessThan(760);
    expect(Math.abs(cardMax - 32 - parseFloat(getComputedStyle(content).maxWidth))).toBeLessThan(1);
    const p = el.querySelector<HTMLElement>('mat-card-content > p')!;
    expect(parseFloat(getComputedStyle(p).lineHeight) / parseFloat(getComputedStyle(p).fontSize)).toBeCloseTo(1.55, 2);
    const second = el.querySelector<HTMLElement>('mat-card-content > ul > li + li');
    if (second) expect(getComputedStyle(second).marginTop).not.toBe('0px');
    const kid = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    expect(getComputedStyle(kid.querySelector('mat-card-content')!).maxWidth).toBe('none');
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

    it('RookHub: genau ein Primaerknopf „Konto löschen …" ins Profil mit aufgeklappter Karte', () => {
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

    // UI-Sweep d3 (d3-rk-konto-jetzt-loeschen-beschriftung, d3-rk-konto-loeschen-url-kuerzel): der Knopf fuehrt nur zur
    // Karte im Profil (geloescht wird dort mit Passwort) — er verspricht kein „jetzt“, sondern zeigt mit „…“ den
    // Folgeschritt; der Text nennt keine internen Pfadkuerzel (/g/ …).
    it('Beschriftung passt zur Wirkung, keine Pfadkuerzel im Loeschtext (de/en/hr/hu)', async () => {
      for (const lang of ['de', 'en', 'hr', 'hu']) {
        let res = await fetch(`/i18n/${lang}.json`).catch(() => null);
        if (!res?.ok) res = await fetch(`/base/i18n/${lang}.json`);
        expect(res!.ok).withContext(`${lang}.json`).toBeTrue();
        const a = (await res!.json()).legal.accountDeletion;
        for (const k of ['deleteNow', 'deleteOnRookHub']) {
          expect(a[k]).withContext(`${lang}.${k}`).not.toMatch(/jetzt|\bnow\b|\bsada\b|\bmost\b/i);
          expect(a[k]).withContext(`${lang}.${k}`).toMatch(/…$/);
        }
        expect(a.removedShared).withContext(lang).not.toMatch(/\(\/[a-z]\/|\/[glrw]\//);
      }
    });

    // UI-Sweep 2026-10-11 (d1-x-account-delete-warn-ignored): im M3-Thema nahm der Knopf die Primärfarbe.
    it('der Löschknopf trägt die Fehler- statt der Primärfarbe (RookHub und Partnerseiten)', () => {
      const color = (v: string, at: HTMLElement) => {
        const p = document.createElement('span');
        p.style.backgroundColor = `var(${v})`;
        at.appendChild(p);
        return getComputedStyle(p).backgroundColor;
      };
      for (const el of [render(), render({ contactEmail: 'x@y', imprint: false, kind: 'kidhub', accountHome: 'rookhub' }, 'https://rookhub.example')]) {
        document.body.appendChild(el);
        try {
          const bg = getComputedStyle(el.querySelector('a.delete-now')!).backgroundColor;
          expect(bg).toBe(color('--mat-sys-error', el));
          expect(bg).not.toBe(color('--mat-sys-primary', el));
        } finally { el.remove(); }
      }
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
      // Link mitten im Satz bleibt inline — als inline-block riss er den Absatz auf (d2-l-kontoloeschen-flucht).
      expect(getComputedStyle(el.querySelector('.league-kept a')!).display).toBe('inline');
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

  // d4-rk-konto-loeschen-textlinks-klein: der Rücklink hatte ohne Polster nur eine Zeilenhöhe (~24 px) Trefferfläche.
  it('d4: Rücklink „← Zurück …“ mindestens 44 px hoch (gemeinsame Regel der Rechtsseiten)', () => {
    TestBed.configureTestingModule({ imports: [AccountDeletionComponent], providers: base() });
    const f = TestBed.createComponent(AccountDeletionComponent);
    document.body.appendChild(f.nativeElement);
    try {
      f.detectChanges();
      const a = (f.nativeElement as HTMLElement).querySelector('.back a') as HTMLElement;
      expect(a.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
    } finally {
      (f.nativeElement as HTMLElement).remove();
    }
  });
});
