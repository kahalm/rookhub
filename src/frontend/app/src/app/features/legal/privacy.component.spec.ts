import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { PrivacyComponent } from './privacy.component';
import { LEGAL_SITE, LegalSite } from './legal-site';
import { OPERATOR } from '../../../environments/operator';

describe('PrivacyComponent', () => {
  function render(site?: LegalSite): HTMLElement {
    TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [
        provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : []),
      ],
    });
    const f = TestBed.createComponent(PrivacyComponent);
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  const hrefs = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('a') as NodeListOf<HTMLAnchorElement>).map(a => a.getAttribute('href'));

  it('RookHub: verweist auf das Impressum, Kontakt aus OPERATOR', () => {
    const el = render();
    expect(hrefs(el)).toContain('/impressum');
    expect(hrefs(el)).toContain('mailto:' + OPERATOR.email);
  });

  it('RookHub: Erwachsenen-Einleitung, Ruecklink zur Anmeldung', () => {
    const el = render();
    expect(el.textContent).toContain('legal.privacy.intro');
    expect(el.textContent).not.toContain('legal.privacy.kidIntro');
    expect(hrefs(el)).toContain('/login');
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
    const keys = ['leagueTitle', 'leagueIntro', 'leagueSources', 'leagueData', 'leagueBasis', 'leagueRecipients',
      'leagueRetention', 'leagueObjection'].map(k => 'legal.privacy.' + k);
    const league = render({ contactEmail: OPERATOR.email, imprint: true, kind: 'leaguehub' });
    for (const k of keys) expect(league.textContent).withContext(k).toContain(k);
    expect(hrefs(league)).toContain('/impressum');
    TestBed.resetTestingModule();
    const rookhub = render();
    expect(rookhub.textContent).not.toContain('legal.privacy.leagueTitle');
    TestBed.resetTestingModule();
    const kid = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    expect(kid.textContent).not.toContain('legal.privacy.leagueTitle');
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
      expect(p['botActivity']).toContain('/motivation aus');
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
      expect(p['leagueSources']).toMatch(/standardmäßig|by default|prema zadanim postavkama|alapértelmezés szerint/);
      // Der echte Name hinter „Schwaz“ bleibt intern (LeagueClubGame.WhiteRealName/BlackRealName, 0.648.0) — offen genannt.
      expect(p['leagueSources']).toMatch(/intern|internally|interno|belsőleg/);
      // Abruf der Online-Partien schickt Benutzernamen an chess.com (USA).
      expect(p['leagueSources']).toMatch(/US-Anbieter|US provider|SAD|egyesült államok/);
      // Teilen-Link: gespielte Runde 7 Tage ab Teilen, ohne Datum 30 Tage (LeagueService.ExpiresFor).
      expect(p['leagueRecipients']).toMatch(/30/);
    });
  }
});
