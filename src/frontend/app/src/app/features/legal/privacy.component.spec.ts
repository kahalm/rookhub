import { TestBed } from '@angular/core/testing';
import { GUEST_START_URL } from '../../core/handoff.service';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { PrivacyComponent } from './privacy.component';
import { LEGAL_SITE, LegalSite, LegalSiteKind } from '../../core/legal-site';
import { PRIVACY_PERIODS } from './privacy-periods';
import { OPERATOR } from '../../../environments/operator';

describe('PrivacyComponent', () => {
  function render(site?: LegalSite): HTMLElement {
    TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : []),
      ],
    });
    const f = TestBed.createComponent(PrivacyComponent);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  // UI-Sweep 2026-10-11, Runde d2 (d2-t-privacy-toc-mobile, d2-l-datenschutz-lang)
  describe('Inhaltsverzeichnis am Handy und Ruecksprung', () => {
    let wideScreen = false;
    beforeEach(() => {
      spyOn(window, 'matchMedia').and.callFake((q: string) => ({ matches: wideScreen, media: q } as MediaQueryList));
    });

    function renderAt(wide: boolean, site?: LegalSite) {
      wideScreen = wide;
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [PrivacyComponent],
        providers: [
          provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
          ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : []),
        ],
      });
      const f = TestBed.createComponent(PrivacyComponent);
      f.detectChanges();
      return f;
    }

    it('am Handy zugeklappt mit der Zahl der Abschnitte, breiter offen', () => {
      const narrow = renderAt(false).nativeElement as HTMLElement;
      const fold = narrow.querySelector<HTMLDetailsElement>('nav.toc details')!;
      expect(fold.open).toBeFalse();
      const count = narrow.querySelectorAll('nav.toc li').length;
      expect(narrow.querySelector('nav.toc summary')!.textContent).toContain(`(${count})`);
      const wide = renderAt(true).nativeElement as HTMLElement;
      expect(wide.querySelector<HTMLDetailsElement>('nav.toc details')!.open).toBeTrue();
    });

    it('„↑ Inhalt" erscheint erst, wenn das Verzeichnis aus dem Bild ist, und klappt es auf', () => {
      const f = renderAt(false);
      const el = f.nativeElement as HTMLElement;
      expect(el.querySelector('.to-toc')).toBeNull();
      f.componentInstance.tocAway.set(true);
      f.detectChanges();
      const link = el.querySelector<HTMLAnchorElement>('.to-toc')!;
      expect(link.getAttribute('href')).toMatch(/#privacy-toc-title$/);
      const head = el.querySelector<HTMLElement>('#privacy-toc-title')!;
      spyOn(head, 'scrollIntoView');
      link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
      f.detectChanges();
      expect(el.querySelector<HTMLDetailsElement>('nav.toc details')!.open).toBeTrue();
      expect(head.scrollIntoView).toHaveBeenCalled();
      expect(document.activeElement).toBe(head);
    });

    // UI-Sweep Runde z1 (z1-rk-privacy-toc-offen): am Laptop (768 px hoch) schob das offene, einspaltige Verzeichnis
    // den Text auf 70 % der Höhe — offen nur noch auf großen Bildschirmen, aufgeklappt immer zweispaltig.
    it('offen nur ab 769 px Breite UND 900 px Höhe; aufgeklappt zweispaltig in jeder Breite', () => {
      expect(PrivacyComponent.TOC_OPEN_QUERY).toContain('min-width: 769px');
      expect(PrivacyComponent.TOC_OPEN_QUERY).toContain('min-height: 900px');
      const f = renderAt(false);   // TestBed hängt die Seite ins Dokument — die Stile gelten
      expect(getComputedStyle((f.nativeElement as HTMLElement).querySelector('nav.toc ul')!).columnCount).toBe('2');
    });

    // UI-Sweep Runde z1 (z1-rk-privacy-inhalt-knopf): der Knopf verdeckte Wörter und am Ende die Kontaktzeile.
    it('„↑ Inhalt": rundes 44-px-Symbol mit Vorlese-Beschriftung, Leiste hinter dem Rücklink mit eigener Höhe', () => {
      const f = renderAt(false);
      f.componentInstance.tocAway.set(true);
      f.detectChanges();
      const el = f.nativeElement as HTMLElement;
      {
        const bar = el.querySelector<HTMLElement>('.to-toc-bar')!;
        const backP = el.querySelector('p.back')!;
        expect(backP.compareDocumentPosition(bar) & Node.DOCUMENT_POSITION_FOLLOWING).withContext('Leiste nach dem letzten Absatz').toBeTruthy();
        const link = bar.querySelector<HTMLElement>('.to-toc')!;
        expect(link.getAttribute('aria-label')).toBe('legal.privacy.tocTitle');
        expect(link.textContent!.trim()).toBe('↑');
        const r = link.getBoundingClientRect();
        expect(Math.round(r.width)).toBe(44);
        expect(Math.round(r.height)).toBe(44);
        expect(getComputedStyle(link).borderTopLeftRadius).toBe('50%');
        if (window.innerWidth < 1200) {
          // Unter dem Rücklink bleibt Platz für den Knopf — am Seitenende liegt er nicht auf dem Text.
          expect(bar.getBoundingClientRect().height).toBeGreaterThanOrEqual(r.height);
          expect(r.top).toBeGreaterThanOrEqual(backP.getBoundingClientRect().bottom);
        }
      }
    });

    it('KidHub: kein „↑ Inhalt" (dort gibt es kein Verzeichnis)', () => {
      const f = renderAt(false, { contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
      f.componentInstance.tocAway.set(true);
      f.detectChanges();
      expect((f.nativeElement as HTMLElement).querySelector('.to-toc')).toBeNull();
    });

    // UI-Sweep z2 (z2-l-datenschutz-laenge, vom User freigegeben): LeagueHub-Datenschutz war am Handy 12 Bildschirme lang.
    describe('LeagueHub am Handy: Abschnitte ab dem dritten als Aufklapper', () => {
      const league: LegalSite = { contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub' };
      const folds = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLDetailsElement>('mat-card-content > details.legal-fold'));

      it('unter 600 px: Verantwortlicher und Liga-Abschnitt offen, jeder weitere zugeklappt mit der Überschrift als summary', () => {
        expect(PrivacyComponent.SECTIONS_OPEN_QUERY).toBe('(min-width: 600px)');
        const el = renderAt(false, league).nativeElement as HTMLElement;
        const heads = Array.from(el.querySelectorAll('h2'));
        const d = folds(el);
        expect(d.length).toBe(heads.length - 2);
        expect(heads.slice(0, 2).map(h => h.id)).toEqual(['privacy-controller', 'privacy-league']);
        expect(heads.slice(0, 2).every(h => !h.closest('details'))).toBeTrue();
        expect(el.textContent).toContain('legal.privacy.introLeagueHub');
        for (const f of d) {
          expect(f.open).withContext('zugeklappt').toBeFalse();
          const summary = f.querySelector(':scope > summary')!;
          expect(summary.querySelector(':scope > h2')).withContext('Überschrift im summary').not.toBeNull();
          expect(summary.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
          expect(getComputedStyle(summary, '::before').content).toContain('▸');
        }
        // Inhalt steckt im Aufklapper, nichts geht verloren; Rücklink bleibt draußen.
        expect(el.querySelector('#privacy-rights')!.closest('details')!.textContent).toContain('legal.privacy.rights');
        expect(el.querySelector('p.back')!.closest('details')).toBeNull();
        expect(Array.from(el.querySelectorAll<HTMLAnchorElement>('nav.toc a')).map(a => a.getAttribute('href')!.split('#')[1]))
          .toEqual(heads.map(h => h.id));
      });

      it('Sprung aus dem Verzeichnis öffnet das Ziel vor dem Scrollen', () => {
        const el = renderAt(false, league).nativeElement as HTMLElement;
        const target = el.querySelector<HTMLElement>('#privacy-rights')!;
        const fold = target.closest<HTMLDetailsElement>('details')!;
        let openWhenScrolled: boolean | null = null;
        spyOn(target, 'scrollIntoView').and.callFake(() => { openWhenScrolled = fold.open; });
        const link = el.querySelector<HTMLAnchorElement>('nav.toc a[href$="#privacy-rights"]')!;
        link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
        expect(openWhenScrolled as boolean | null).toBe(true);
        expect(document.activeElement).toBe(target);
      });

      it('eine Sprungmarke in der Adresse (#…) öffnet das Ziel', () => {
        const el = renderAt(false, league).nativeElement as HTMLElement;
        const target = el.querySelector<HTMLElement>('#privacy-storage')!;
        spyOn(target, 'scrollIntoView');
        const before = location.href;
        history.replaceState(history.state, '', '#privacy-storage');
        try {
          window.dispatchEvent(new HashChangeEvent('hashchange'));
          expect(target.closest<HTMLDetailsElement>('details')!.open).toBeTrue();
          expect(target.scrollIntoView).toHaveBeenCalled();
        } finally {
          history.replaceState(history.state, '', before);
        }
      });

      it('Druck: alle Abschnitte offen (beforeprint löst die Aufklapper auf, afterprint klappt wieder zu)', () => {
        const el = renderAt(false, league).nativeElement as HTMLElement;
        const order = () => Array.from(el.querySelectorAll('h2')).map(h => h.id);
        const ids = order();
        window.dispatchEvent(new Event('beforeprint'));
        expect(folds(el).length).toBe(0);
        expect(order()).toEqual(ids);
        expect(el.querySelector('#privacy-rights')!.nextElementSibling!.textContent).toContain('legal.privacy.rights');
        window.dispatchEvent(new Event('afterprint'));
        expect(folds(el).length).toBeGreaterThan(0);
      });

      it('breit und in allen anderen Oberflächen unverändert offen', () => {
        expect(folds(renderAt(true, league).nativeElement as HTMLElement).length).toBe(0);
        const others: (LegalSite | undefined)[] = [
          undefined,
          { contactEmail: OPERATOR.email, imprint: true, kind: 'turnier', accountHome: 'rookhub' },
          { contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' },
        ];
        for (const site of others) {
          const el = renderAt(false, site).nativeElement as HTMLElement;
          expect(el.querySelectorAll('details.legal-fold').length).withContext(site?.kind ?? 'rookhub').toBe(0);
          expect(el.querySelectorAll('mat-card-content > h2').length).toBeGreaterThan(5);
        }
      });
    });

    // UI-Sweep d6 (d6-c-privacy-mobile-zu-lang): ClubHub-Datenschutz war am Handy 14 Bildschirme lang — klappt wie LeagueHub zu.
    describe('ClubHub am Handy: Abschnitte ab dem dritten als Aufklapper', () => {
      const club: LegalSite = { contactEmail: OPERATOR.email, imprint: true, kind: 'clubhub', accountHome: 'rookhub' };
      const folds = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLDetailsElement>('mat-card-content > details.legal-fold'));

      it('unter 600 px: Verantwortlicher und Kartei-Abschnitt offen, ab dem dritten h2 zugeklappt', () => {
        const el = renderAt(false, club).nativeElement as HTMLElement;
        const heads = Array.from(el.querySelectorAll('h2'));
        expect(heads.slice(0, 2).map(h => h.id)).toEqual(['privacy-controller', 'privacy-club']);
        expect(heads.slice(0, 2).every(h => !h.closest('details'))).toBeTrue();
        const d = folds(el);
        expect(d.length).toBe(heads.length - 2);
        for (const f of d) {
          expect(f.open).toBeFalse();
          expect(f.querySelector(':scope > summary > h2')).not.toBeNull();
        }
        // Der Rechtstext selbst bleibt vollständig — nur zugeklappt.
        expect(el.querySelector('#privacy-rights')!.closest('details')!.textContent).toContain('legal.privacy.rights');
        expect(el.querySelector('p.back')!.closest('details')).toBeNull();
      });

      it('breiter als 600 px bleibt alles offen', () => {
        const el = renderAt(true, club).nativeElement as HTMLElement;
        expect(folds(el).length).toBe(0);
        expect(el.querySelectorAll('mat-card-content > h2').length).toBeGreaterThan(5);
      });
    });

    // UI-Sweep z2 (z2-rk-privacy-knopf-handy): „↑" verdeckte beim Lesen das letzte Wort der Zeile darunter.
    it('„↑ Inhalt" ueber dem Text nur beim Hochscrollen (sonst .idle, ausgeblendet unter 1200 px)', () => {
      const f = renderAt(false);
      f.componentInstance.tocAway.set(true);
      f.detectChanges();
      const el = f.nativeElement as HTMLElement;
      expect(el.querySelector('.to-toc')!.classList).toContain('idle');
      const box = document.createElement('div');
      box.style.cssText = 'height:100px;overflow:auto';
      box.innerHTML = '<div style="height:2000px"></div>';
      document.body.appendChild(box);
      try {
        const scrollTo = (y: number) => { box.scrollTop = y; box.dispatchEvent(new Event('scroll')); };
        scrollTo(500);
        scrollTo(300);
        f.detectChanges();
        expect(f.componentInstance.scrollingUp()).toBeTrue();
        expect(el.querySelector('.to-toc')!.classList).not.toContain('idle');
        scrollTo(600);
        f.detectChanges();
        expect(el.querySelector('.to-toc')!.classList).toContain('idle');
      } finally {
        box.remove();
      }
    });

    it('LeagueHub: die Quellen als Unterliste mit vier Punkten statt einer Textwand', () => {
      const el = renderAt(true, { contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub' }).nativeElement as HTMLElement;
      const sub = el.querySelectorAll('#privacy-league ~ ul ul.sub > li');
      expect(sub.length).toBe(4);
    });
  });

  const hrefs = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('a') as NodeListOf<HTMLAnchorElement>).map(a => a.getAttribute('href'));

  // UI-Sweep 2026-10-11 (r2-rk-privacy-operator): der Verantwortliche wurde nur als „der im Impressum genannte Betreiber"
  // bezeichnet — das Impressum nennt aber nur die Kontaktadresse. Jetzt steht er hier mit der Adresse der Oberfläche.
  it('RookHub: nennt den Verantwortlichen über die Kontaktadresse statt aufs Impressum zu verweisen', () => {
    const el = render();
    const controller = el.querySelector('#privacy-controller')!.nextElementSibling as HTMLElement;
    expect(controller.textContent).toContain('legal.privacy.controllerNamed');
    expect(controller.querySelector('a')!.getAttribute('href')).toBe('mailto:' + OPERATOR.email);
    expect(el.textContent).not.toContain('legal.privacy.controller ');
    expect(hrefs(el)).not.toContain('/impressum');
  });

  it('RookHub: Erwachsenen-Einleitung, Ruecklink ohne Verlauf zur Startseite (z1-rk-legal-back-anon)', () => {
    const el = render();
    expect(el.textContent).toContain('legal.privacy.intro');
    expect(el.textContent).not.toContain('legal.privacy.kidIntro');
    expect(el.querySelector('.back a')!.getAttribute('href')).toBe(GUEST_START_URL);
    expect(hrefs(el)).not.toContain('/login');
  });

  it('KidHub: einfache Sprache mit Elternhinweis, Verantwortlicher nur mit Kontaktadresse, Ruecklink zur Startseite (F7-003)', () => {
    const el = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    const text = el.textContent ?? '';
    expect(text).toContain('legal.privacy.kidIntro');
    expect(text).toContain('legal.privacy.kidParents');
    expect(text).toContain('legal.privacy.kidDevice');
    expect(text).not.toContain('legal.privacy.intro');
    // Ohne Impressum nennt die Erklaerung beim Verantwortlichen die Kontaktadresse der Oberflaeche — bewusst
    // OHNE Name/Anschrift und ohne Platzhalter (Entscheidung des Betreibers, 2026-09-30).
    expect(text).toContain('legal.privacy.controllerNamed');
    expect(text).toContain('kidhub@oberschm.id');
    expect(text).not.toContain('[');
    expect(text).not.toContain('bitte eintragen');
    expect(hrefs(el)).toContain('/');
    expect(hrefs(el)).not.toContain('/login');
    expect(hrefs(el)).not.toContain('/impressum');
  });

  it('KidHub: kein Impressum, eigene Adresse — auch fuer den Verantwortlichen', () => {
    const el = render({ contactEmail: 'kidhub@oberschm.id', imprint: false });
    expect(hrefs(el)).not.toContain('/impressum');
    expect(hrefs(el).filter(h => h === 'mailto:kidhub@oberschm.id').length).toBe(2);
    expect(el.textContent).not.toContain(OPERATOR.email);
  });

  it('LeagueHub: Abschnitt ueber Ligaspieler ohne Konto, nur dort (F7-006)', () => {
    const keys = ['leagueTitle', 'leagueIntro', 'leagueSourcesTitle', 'leagueSourcesPairings', 'leagueSourcesGames',
      'leagueSourcesClub', 'leagueSourcesOnline', 'leagueData', 'leagueBasis', 'leagueRecipients',
      'leagueRetention', 'leagueObjection'].map(k => 'legal.privacy.' + k);
    const league = render({ contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub' });
    for (const k of keys) expect(league.textContent).withContext(k).toContain(k);
    expect(league.textContent).toContain('legal.privacy.controllerNamed');
    TestBed.resetTestingModule();
    const rookhub = render();
    expect(rookhub.textContent).not.toContain('legal.privacy.leagueTitle');
    TestBed.resetTestingModule();
    const kid = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    expect(kid.textContent).not.toContain('legal.privacy.leagueTitle');
  });

  // UI-Sweep 2026-10-10 (x-privacy-intro): LeagueHub und ClubHub stellten sich mit RookHubs Einleitung vor,
  // ClubHub beschrieb die Kartei gar nicht.
  it('Einleitung je Oberflaeche; ClubHub mit eigenem Abschnitt „Kartei“ samt Inhaltsverzeichnis', () => {
    const league = render({ contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub' });
    expect(league.textContent).toContain('legal.privacy.introLeagueHub');
    expect(Array.from(league.querySelectorAll('p')).some(p => p.textContent?.trim() === 'legal.privacy.intro')).toBeFalse();
    expect(league.textContent).not.toContain('legal.privacy.clubTitle');
    TestBed.resetTestingModule();
    const club = render({ contactEmail: OPERATOR.email, imprint: true, kind: 'clubhub', accountHome: 'rookhub' });
    const keys = ['introClubHub', 'clubTitle', 'clubIntro', 'clubData', 'clubContacts', 'clubAttendance', 'clubAccess',
      'clubPhotos', 'clubLink', 'clubRetention'].map(k => 'legal.privacy.' + k);
    for (const k of keys) expect(club.textContent).withContext(k).toContain(k);
    expect(club.textContent).not.toContain('legal.privacy.leagueTitle');
    expect(club.querySelector('nav.toc a[href$="#privacy-club"]')).not.toBeNull();
    TestBed.resetTestingModule();
    const rookhub = render();
    expect(rookhub.textContent).toContain('legal.privacy.intro');
    expect(rookhub.textContent).not.toContain('legal.privacy.introClubHub');
    expect(rookhub.textContent).not.toContain('legal.privacy.clubTitle');
  });

  it('LeagueHub: nennt den Verein, für den das Konto LeagueHub gerade nutzt — nur, wenn er bekannt ist (0.698.0)', () => {
    const known = render({ contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub', leagueClub: () => 'SK Testdorf' });
    expect(known.textContent).toContain('legal.privacy.leagueClub');
    TestBed.resetTestingModule();
    const unknown = render({ contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub', leagueClub: () => null });
    expect(unknown.textContent).not.toContain('legal.privacy.leagueClub');
  });

  it('beschreibt, was der Discord-Bot speichert (Codereview S4-008)', () => {
    const text = render().textContent ?? '';
    for (const k of ['botTitle', 'botIntro', 'botDmLog', 'botActivity', 'botLogs'])
      expect(text).withContext(k).toContain('legal.privacy.' + k);
  });

  it('nennt die Turnierdaten von chess-results.com samt Speicherdauer (Codereview S3-018)', () => {
    const text = render().textContent ?? '';
    for (const k of ['tournamentTitle', 'tournamentIntro', 'tournamentData', 'tournamentRetention'])
      expect(text).withContext(k).toContain('legal.privacy.' + k);
  });

  // UI-Sweep 2026-10-11 (r2-t-privacy-intro): die Turnierseite begann mit „RookHub ist eine Schach-Trainingsplattform“,
  // ihr eigener Abschnitt stand weit unten.
  it('Turnierseite: eigene Einleitung, „Turnierdaten“ gleich nach dem Verantwortlichen', () => {
    const el = render({ contactEmail: OPERATOR.email, imprint: true, kind: 'turnier', accountHome: 'rookhub' });
    expect(el.textContent).toContain('legal.privacy.introTurnier');
    expect(Array.from(el.querySelectorAll('p')).some(p => p.textContent?.trim() === 'legal.privacy.intro')).toBeFalse();
    const headings = Array.from(el.querySelectorAll('h2')).map(h => h.id);
    expect(headings.slice(0, 2)).toEqual(['privacy-controller', 'privacy-tournament']);
    expect(el.querySelectorAll('#privacy-tournament').length).toBe(1);
  });

  it('nennt die KI-Dienste: Formular-Fotos gehen an Anthropic (Codereview A6-008)', () => {
    const el = render();
    // Ohne Sprachdateien stehen die Keys selbst da — sie muessen gerendert werden.
    expect(el.textContent).toContain('legal.privacy.aiTitle');
    expect(el.textContent).toContain('legal.privacy.aiScoresheet');
    expect(el.textContent).toContain('legal.privacy.thirdAnthropic');
    expect(el.textContent).toContain('legal.privacy.dataScoresheet');
    expect(el.textContent).toContain('legal.privacy.aiLocal');
    expect(el.textContent).toContain('legal.privacy.thirdTextLlm');
  });

  // UX-057: der Titel stand in <mat-card-title> (keine Ueberschriften-Rolle), die Abschnitte als h4 — kein h1, h2/h3
  // uebersprungen (WCAG 1.3.1); am Handy 2 679 px ohne Sprungmarken; der Link zur Loeschseite hiess „/account-deletion".
  describe('Gliederung, Inhaltsverzeichnis und Linktext (UX-057)', () => {
    const variants: [string, LegalSite | undefined][] = [
      ['RookHub', undefined],
      ['KidHub', { contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' }],
      ['LeagueHub', { contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub' }],
      ['Turnierseite', { contactEmail: OPERATOR.email, imprint: true, kind: 'turnier', accountHome: 'rookhub' }],
    ];

    for (const [name, site] of variants) {
      it(`${name}: genau ein h1 (der Titel), Abschnitte als h2, keine tieferen Ebenen`, () => {
        const el = render(site);
        const h1 = el.querySelectorAll('h1');
        expect(h1.length).toBe(1);
        expect(h1[0].textContent).toContain('legal.privacy.title');
        expect(el.querySelectorAll('h2').length).toBeGreaterThan(5);
        expect(el.querySelectorAll('h3, h4, h5, h6').length).toBe(0);
      });

      it(`${name}: das Inhaltsverzeichnis fuehrt zu JEDEM Abschnitt, in Reihenfolge, und zu nichts anderem`, () => {
        const el = render(site);
        const headings = Array.from(el.querySelectorAll('h2')).map(h => h.id);
        const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('nav.toc a'))
          .map(a => (a.getAttribute('href') ?? '').split('#')[1]);
        expect(links.length).withContext('Inhaltsverzeichnis vorhanden').toBeGreaterThan(5);
        expect(headings.every(id => !!id)).withContext('jede h2 traegt eine id').toBeTrue();
        expect(new Set(headings).size).toBe(headings.length);
        expect(links).toEqual(headings);
      });
    }

    it('ein Klick springt zum Abschnitt und setzt den Fokus auf die Ueberschrift', () => {
      const el = render();
      const target = el.querySelector<HTMLElement>('#privacy-rights')!;
      const scroll = spyOn(target, 'scrollIntoView');
      const link = Array.from(el.querySelectorAll<HTMLAnchorElement>('nav.toc a'))
        .find(a => a.getAttribute('href')?.endsWith('#privacy-rights'))!;
      const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
      link.dispatchEvent(click);
      expect(click.defaultPrevented).toBeTrue();
      expect(scroll).toHaveBeenCalled();
      expect(document.activeElement).toBe(target);
    });

    it('der Link zur Konto-Loeschung nennt sein Ziel in Worten statt als Pfad', () => {
      const link = render().querySelector<HTMLAnchorElement>('a[href="/account-deletion"]')!;
      expect(link.textContent?.trim()).toBe('legal.privacy.retentionLink');
    });
  });
});

/** Die Texte selbst: Anbieter, Drittland und der Upload-Hinweis in den gepflegten Sprachen. */
describe('Datenschutz-Texte (en/de/hr/hu)', () => {
  async function load(lang: string): Promise<Record<string, any>> {
    for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
      const res = await fetch(url);
      if (res.ok) return res.json();
    }
    throw new Error(`${lang}.json nicht ladbar`);
  }

  for (const lang of ['en', 'de', 'hr', 'hu']) {
    it(`${lang}: Anthropic als Empfaenger der Formular-Fotos, auch im Upload-Hinweis`, async () => {
      const t = await load(lang);
      const p = t['legal']['privacy'];
      expect(p['aiScoresheet']).toContain('Anthropic');
      expect(p['aiScoresheet']).toMatch(/USA|SAD|egyesült államok/);
      expect(p['thirdAnthropic']).toContain('Anthropic');
      expect(t['scoresheet']['help']).toContain('Anthropic');
      // „eigene Hardware" haengt an der Konfiguration (TextLlm), der Hilfetext der Kurs-Uebersetzung bleibt neutral.
      expect(t['courses']['translations']['help']).not.toMatch(/eigenen Hardware|own hardware|vlastitom hardveru|saját hardverünkön/);
      // Nacherzaehlung/Erklaerungen/Roast: nie Anthropic, aber auf dem DGX Spark eines anderen Betreibers — keine Zusage
      // „bleibt bei uns“, und der Server steht als Empfaenger in der Liste (Nacharbeit A6-008).
      expect(p['aiLocal']).toContain('Anthropic');
      expect(p['aiLocal']).toContain('DGX Spark');
      expect(p['aiLocal']).not.toMatch(/verlassen keine Daten|eigenen Hardware|no data leaves|own hardware|ne napuštaju|vlastitom hardveru|nem hagyja el|saját hardverünkön/);
      expect(p['thirdTextLlm']).toContain('DGX Spark');
      // LeagueHub: Foto geht erst beim Uebernehmen/Verwerfen, nicht schon „nach der Korrektur“.
      expect(p['aiScoresheet']).not.toMatch(/nach der Korrektur verworfen|discarded after the correction|odbacuje nakon ispravka|javítás után elvetjük/);
    });

    it(`${lang}: Chess.com/Lichess-Abruf wie im Code — automatisch alle 6 Stunden, US-Anbieter (UX-022)`, async () => {
      const p = (await load(lang))['legal']['privacy'];
      // PlayTimeSyncService fragt standardmaessig alle 6 h ab und schickt den Benutzernamen an chess.com (USA); der
      // Altstand vom Juni sagte das Gegenteil („keine automatische Datenuebertragung“).
      expect(p['thirdChesssites']).toMatch(/\b6\b/);
      expect(p['thirdChesssites']).toMatch(/US-Anbieter|US provider|SAD|egyesült államok/);
      expect(p['thirdChesssites']).not.toMatch(/keine automatische|no automatic|nema automatsk|nincs automatikus/);
    });

    it(`${lang}: Discord-Bot wie im Bot — DM-Log 300 Zeichen und 30 Tage, Abbestellen, Protokolle in ES (S4-008)`, async () => {
      const p = (await load(lang))['legal']['privacy'];
      // schach-bot core/dm_log.py: ein- und ausgehende DMs, auf 300 Zeichen gekuerzt, nach 30 Tagen weg.
      expect(p['botDmLog']).toMatch(/\b300\b/);
      expect(p['botDmLog']).toMatch(/\b30\b/);
      // Spiel-Status nur fuer /motivation-Abonnenten; Abbestellen ist der Ausweg.
      // Der Bot spricht nur deutsch: die Auswahl heisst „aus“ — englisch als Befehl + Option erklaert
      // (e1-rk-datenschutz-motivation-aus), nicht als deutsches Wort im Fliesstext.
      expect(p['botActivity']).toContain('/motivation');
      expect(p['botActivity']).toMatch(/\baus\b/);
      if (lang === 'en') expect(p['botActivity']).toContain('/motivation, option “aus” (off)');
      // Befehlsprotokoll und Motivations-Metadaten gehen nach Elasticsearch (ohne DM-Inhalt, schach-bot v2.83.14).
      expect(p['botLogs']).toContain('Elasticsearch');
    });

    it(`${lang}: Turnierdaten mit den Fristen des Crawlers — 30 und 180 Tage (S3-018)`, async () => {
      const p = (await load(lang))['legal']['privacy'];
      // chessresults_crawler RetentionService: CrawlJobs 30 Tage nach Abschluss, PlayerClubs 180 Tage ohne Auffrischung.
      expect(p['tournamentRetention']).toMatch(/\b30\b/);
      expect(p['tournamentRetention']).toMatch(/\b180\b/);
      // Daten Dritter (Spieler ohne Konto) aus chess-results.com, Vereins-Nachschlagen ueber die FIDE-ID.
      expect(p['tournamentIntro']).toContain('chess-results.com');
      expect(p['tournamentData']).toContain('FIDE');
    });

    it(`${lang}: LeagueHub-Abschnitt nennt Rechtsgrundlage, Widerspruch und Teilen-Links`, async () => {
      const p = (await load(lang))['legal']['privacy'];
      expect(p['leagueBasis']).toMatch(/6(\s*Abs\. 1|\(1\)|\. cikk \(1\))/);
      expect(p['leagueObjection']).toMatch(/21/);
      expect(p['leagueIntro']).toMatch(/14/);
      expect(p['leagueData']).toMatch(/Lichess/);
    });

    it(`${lang}: LeagueHub-Speicherdauer und Quellen wie im Code (Nacharbeit F7-006)`, async () => {
      const p = (await load(lang))['legal']['privacy'];
      // IP-Pruefwert der Formular-Fotos bleibt nach Uebernehmen/Verwerfen stehen (Tagesgrenze) und wird erst nach zwei
      // Tagen geleert (ForgetOldIpHashesAsync) — keine Zusage „hoechstens bis zur Uebernahme“.
      expect(p['leagueRetention']).not.toMatch(/höchstens, bis die Einreichung|only until the submission|najdulje dok se predaja|legfeljebb addig, amíg a beküldést/);
      expect(p['leagueRetention']).toMatch(/zwei Tage|two days|dva dana|két nap/);
      expect(p['leagueRetention']).toMatch(/30/);
      // Foto geht erst beim Uebernehmen/Verwerfen, nicht „nach der Korrektur“ (wie aiScoresheet, A6-008).
      expect(p['leagueRetention']).not.toMatch(/nach der Korrektur gelöscht|deleted after the correction|brišu se nakon ispravka|javítás után töröljük/);
      // „Schwaz“ ist nur die Vorgabe je Seite, abwaehlbar (LeagueClubService: anonymized = w.Replace || b.Replace).
      expect(p['leagueSourcesClub']).toMatch(/standardmäßig|by default|prema zadanim postavkama|alapértelmezés szerint/);
      // Der echte Name hinter „Schwaz“ bleibt intern (LeagueClubGame.WhiteRealName/BlackRealName, 0.648.0) — offen genannt.
      expect(p['leagueSourcesClub']).toMatch(/intern|internally|interno|belsőleg/);
      // Abruf der Online-Partien schickt Benutzernamen an chess.com (USA).
      expect(p['leagueSourcesOnline']).toMatch(/US-Anbieter|US provider|SAD|egyesült államok/);
      // Teilen-Link: gespielte Runde 7 Tage ab Teilen, ohne Datum 30 Tage (LeagueService.ExpiresFor).
      expect(p['leagueRecipients']).toMatch(/30/);
    });
  }
});

/**
 * Codereview R-2 a (Entscheidung 2026-10-25: offenlegen statt pseudonymisieren). Die neuen Abschnitte — Sichtbarkeit,
 * Nachrichten an das Admin-Team, Push, Server-Protokolle, Fristen-Tabelle — und der Drittdienst api.chesstools.org stehen
 * in JEDER Fassung (RookHub, KidHub, LeagueHub, ClubHub, Turnierseite), mit Eintrag im Inhaltsverzeichnis; KidHub hat die
 * Kurzfassung dazu. Schlüssel-Spiegel: jeder Schlüssel, den eine Fassung zeichnet, steht in de/en/hr/hu.
 */
describe('Datenschutz R-2 a: neue Abschnitte in allen Fassungen', () => {
  const KINDS: readonly LegalSiteKind[] = ['rookhub', 'kidhub', 'leaguehub', 'clubhub', 'turnier'];
  const NEW_SECTIONS = ['privacy-visibility', 'privacy-messages', 'privacy-push', 'privacy-logs'];

  function renderKind(kind: LegalSiteKind): HTMLElement {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: LEGAL_SITE, useValue: { contactEmail: 'x@example.org', imprint: kind !== 'kidhub', kind } satisfies LegalSite },
      ],
    });
    const f = TestBed.createComponent(PrivacyComponent);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  async function flatLang(lang: string): Promise<Set<string>> {
    for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
      const res = await fetch(url);
      if (!res.ok) continue;
      const out = new Set<string>();
      const walk = (o: Record<string, unknown>, p: string) => {
        for (const [k, v] of Object.entries(o)) {
          const key = p ? `${p}.${k}` : k;
          if (v && typeof v === 'object') walk(v as Record<string, unknown>, key); else out.add(key);
        }
      };
      walk(await res.json(), '');
      return out;
    }
    throw new Error(`${lang}.json nicht ladbar`);
  }

  for (const kind of KINDS) {
    it(`${kind}: Sichtbarkeit, Nachrichten, Push, Protokolle, Fristen-Tabelle und chesstools`, () => {
      const el = renderKind(kind);
      const tocTargets = Array.from(el.querySelectorAll<HTMLAnchorElement>('nav.toc a')).map(a => a.getAttribute('href')!.split('#')[1]);
      for (const id of NEW_SECTIONS) {
        expect(el.querySelector(`h2#${id}`)).withContext(`${kind}: #${id}`).not.toBeNull();
        expect(tocTargets).withContext(`${kind}: Inhaltsverzeichnis`).toContain(id);
      }
      // Inhaltsverzeichnis in derselben Reihenfolge wie die Überschriften.
      const heads = Array.from(el.querySelectorAll('mat-card-content > h2')).map(h => h.id);
      expect(tocTargets).withContext(kind).toEqual(heads);
      const table = el.querySelector('table#privacy-periods')!;
      expect(table).withContext(`${kind}: Fristen-Tabelle`).not.toBeNull();
      const rows = PRIVACY_PERIODS.reduce((n, g) => n + g.rows.length, 0);
      expect(table.querySelectorAll('tbody tr:not(.group)').length).toBe(rows);
      expect(table.querySelectorAll('tbody tr.group').length).toBe(PRIVACY_PERIODS.length);
      const text = el.textContent ?? '';
      for (const k of ['thirdChesstools', 'thirdPush', 'storageLogout', 'logsEmail', 'logsRetention', 'visibilitySearch'])
        expect(text).withContext(`${kind}: ${k}`).toContain(`legal.privacy.${k}`);
      for (const k of ['kidVisibility', 'kidLogs', 'kidPeriods']) {
        if (kind === 'kidhub') expect(text).withContext(k).toContain(`legal.privacy.${k}`);
        else expect(text).withContext(`${kind}: ${k}`).not.toContain(`legal.privacy.${k}`);
      }
    });
  }

  it('Schlüssel-Spiegel: jeder gezeichnete legal.privacy-Schlüssel jeder Fassung steht in de/en/hr/hu', async () => {
    const used = new Set<string>();
    for (const kind of KINDS) {
      // Je Textknoten: ein Schlüssel endet am Ende seines Knotens (nicht im angrenzenden Link-Text).
      const walker = document.createTreeWalker(renderKind(kind), NodeFilter.SHOW_TEXT);
      for (let n = walker.nextNode(); n; n = walker.nextNode())
        for (const m of (n.textContent ?? '').matchAll(/legal\.privacy\.[\w.]+/g)) used.add(m[0].replace(/\.$/, ''));
    }
    expect(used.size).toBeGreaterThan(60);
    for (const lang of ['de', 'en', 'hr', 'hu']) {
      const keys = await flatLang(lang);
      expect([...used].filter(k => !keys.has(k))).withContext(`${lang}: fehlende Schlüssel`).toEqual([]);
    }
  });
});

/** R-2 a: die offengelegten Inhalte in den gepflegten Sprachen — Klarnamen in Protokollen, Frist, chesstools, Push. */
describe('Datenschutz-Texte R-2 a (en/de/hr/hu)', () => {
  async function load(lang: string): Promise<Record<string, any>> {
    for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
      const res = await fetch(url);
      if (res.ok) return res.json();
    }
    throw new Error(`${lang}.json nicht ladbar`);
  }

  for (const lang of ['en', 'de', 'hr', 'hu']) {
    it(`${lang}: Protokolle nicht mehr „pseudonym“, 90 Tage, E-Mail-Adresse; chesstools; Push-Dienste`, async () => {
      const p = (await load(lang))['legal']['privacy'];
      // A10-009: angemeldet steht der Benutzername im Klartext — die alte Zusage „pseudonyme Besucher-Kennung“ ist weg.
      expect(p['dataTechnical']).not.toMatch(/pseudonyme Besucher|pseudonymous visitor|pseudonimni identifikator|álnevesített látogató/);
      expect(p['logsRetention']).toMatch(/\b90\b/);
      expect(p['logsEmail']).toMatch(/E-Mail|email|e-pošt|e-mail/i);
      expect(p['thirdChesstools']).toContain('api.chesstools.org');
      expect(p['push']).toMatch(/Google/);
      expect(p['push']).toMatch(/Mozilla/);
      expect(p['push']).toMatch(/Apple/);
      expect(p['messages']).toMatch(/\b365\b/);
      // A6-016 (0.840.0): keine Zusage mehr „ohne Frist, keine automatische Löschung“ für liegengelassene Einlesungen.
      expect(p['aiScoresheet']).not.toMatch(/ohne Frist, eine automatische|no time limit; there is no|bez roka; automatskog|határidő nélkül megőrzi/);
      expect(p['aiScoresheet']).toMatch(/\b14\b/);
    });
  }
});
