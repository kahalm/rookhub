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

  it('KidHub: einfache Sprache mit Elternhinweis, Verantwortlicher mit Name und Anschrift, Ruecklink zur Startseite (F7-003)', () => {
    const el = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    const text = el.textContent ?? '';
    expect(text).toContain('legal.privacy.kidIntro');
    expect(text).toContain('legal.privacy.kidParents');
    expect(text).toContain('legal.privacy.kidDevice');
    expect(text).not.toContain('legal.privacy.intro');
    // Ohne Impressum MUSS die Erklaerung selbst Name und Anschrift nennen (Art. 13 Abs. 1 lit. a DSGVO).
    expect(text).toContain(OPERATOR.name);
    expect(text).toContain(OPERATOR.address);
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

  it('nennt die KI-Dienste: Formular-Fotos gehen an Anthropic (Codereview A6-008)', () => {
    const el = render();
    // Ohne Sprachdateien stehen die Keys selbst da — sie muessen gerendert werden.
    expect(el.textContent).toContain('legal.privacy.aiTitle');
    expect(el.textContent).toContain('legal.privacy.aiScoresheet');
    expect(el.textContent).toContain('legal.privacy.thirdAnthropic');
    expect(el.textContent).toContain('legal.privacy.dataScoresheet');
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
    });
  }
});
