import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { HelpComponent, MENU_HELP } from './help.component';
import { REPCHECK_CHROME_URL, REPCHECK_FIREFOX_URL } from '../../core/community';

describe('HelpComponent', () => {
  function build(fragment: string | null = null): HelpComponent {
    // Nur die im Component genutzte snapshot.fragment-Eigenschaft mocken.
    const route = { snapshot: { fragment } } as unknown as ActivatedRoute;
    return new HelpComponent(route);
  }

  let component: HelpComponent;

  beforeEach(() => {
    component = build();
  });

  it('listet alle Hilfe-Abschnitte mit eindeutigen Ids und je einem Icon', () => {
    expect(component.sections.length).toBeGreaterThan(0);
    const ids = component.sections.map(s => s.id);
    expect(new Set(ids).size).toBe(ids.length); // keine Duplikate
    expect(component.sections.every(s => !!s.icon)).toBeTrue();
    // Kernbereiche müssen abgedeckt sein
    ['welcome', 'tournaments', 'puzzles', 'trainingGoals', 'privacy', 'extension'].forEach(id =>
      expect(ids).toContain(id),
    );
  });

  /** Die vier Sprachen, in denen die Hilfeseite gepflegt wird (der Rest fällt auf en zurück). */
  async function helpTexts(lang: string): Promise<Record<string, { t: string; p: string[] }>> {
    for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
      const res = await fetch(url);
      if (res.ok) return (await res.json())['help']['s'];
    }
    throw new Error(`i18n/${lang}.json nicht gefunden`);
  }

  it('jeder Menüeintrag zeigt auf einen vorhandenen Hilfe-Abschnitt (UX-011)', () => {
    const ids = new Set(component.sections.map(s => s.id));
    for (const [key, section] of Object.entries(MENU_HELP)) {
      expect(ids.has(section)).withContext(`${key} → ${section}`).toBeTrue();
    }
    // Die Menüpunkte seit etwa v0.5xx haben einen EIGENEN Abschnitt, nicht nur einen Verweis.
    ['guess', 'games', 'reconstruct', 'worksheets', 'leaderboards', 'chessable'].forEach(id =>
      expect(ids).toContain(id),
    );
  });

  it('jeder Abschnitt hat Titel und Absätze in en/de/hr/hu (UX-011)', async () => {
    for (const lang of ['en', 'de', 'hr', 'hu']) {
      const s = await helpTexts(lang);
      for (const { id } of component.sections) {
        expect(s[id]?.t).withContext(`${lang}: help.s.${id}.t`).toBeTruthy();
        expect(Array.isArray(s[id]?.p) && s[id].p.length > 0 && s[id].p.every(x => !!x))
          .withContext(`${lang}: help.s.${id}.p`).toBeTrue();
      }
    }
  });

  it('beschreibt keine Bedienelemente, die es in der Leiste nicht mehr gibt (UX-011)', async () => {
    // Design, Sprache, Konto und Info liegen im ☰-Menü; die Turnierliste lebt auf der Turnierseite.
    const stale: Record<string, string[]> = {
      en: ['sun/moon', 'globe icon', 'person icon', 'info icon', 'tournament list', 'no server'],
      de: ['Sonne/Mond', 'Globus-Symbol', 'Personen-Symbol', 'Info-Symbol', 'Turnierliste', 'ohne Server'],
      hr: ['sunca/mjeseca', 'globusa', 'ikona osobe', 'ikone informacija', 'popisu turnira', 'bez poslužitelja'],
      hu: ['nap/hold', 'földgömb', 'személy ikon', 'info-ikon', 'versenylistá', 'szerver nélkül'],
    };
    for (const [lang, phrases] of Object.entries(stale)) {
      for (const [id, sec] of Object.entries(await helpTexts(lang))) {
        const text = [sec.t, ...(sec.p ?? [])].join(' ');
        for (const phrase of phrases) {
          expect(text.includes(phrase)).withContext(`${lang}: help.s.${id} nennt „${phrase}“`).toBeFalse();
        }
      }
    }
  });

  it('bewirbt kein Userscript mehr und nennt die Store-Seiten über die Konstanten (F5-015)', async () => {
    for (const lang of ['en', 'de', 'hr', 'hu']) {
      let raw = '';
      for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
        const res = await fetch(url);
        if (res.ok) { raw = await res.text(); break; }
      }
      // Das Tampermonkey-Userscript ist seit 2026-09-20 entfernt; der raw.githubusercontent-Link lieferte 404.
      expect(raw).withContext(lang).not.toMatch(/userscript|tampermonkey|raw\.githubusercontent|\.user\.js/i);
      const ext = (await helpTexts(lang))['extension'].p.join(' ');
      expect(ext).withContext(lang).toContain('{{chromeUrl}}');
      expect(ext).withContext(lang).toContain('{{firefoxUrl}}');
      expect(ext).withContext(lang).not.toMatch(/chromewebstore|addons\.mozilla/);
    }
  });

  it('setzt die Store-Adressen in die Absätze ein (F5-015)', async () => {
    await TestBed.configureTestingModule({
      imports: [HelpComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { help: { s: { extension: { t: 'RepCheck', p: ['Chrome: {{chromeUrl}} · Firefox: {{firefoxUrl}}'] } } } });
    translate.use('en');
    const fixture = TestBed.createComponent(HelpComponent);
    fixture.detectChanges();
    const hrefs = [...(fixture.nativeElement as HTMLElement).querySelectorAll('#extension mat-card-content p a')]
      .map(a => a.getAttribute('href'));
    expect(hrefs).toEqual([REPCHECK_CHROME_URL, REPCHECK_FIREFOX_URL]);
  });

  it('linkify() macht aus [Text](url) einen Link mit sprechendem Text (UX-013)', () => {
    const html = component.linkify('Im [Chrome Web Store](https://chromewebstore.google.com/detail/abc) und [<b>x</b>](https://a.example/p).');
    expect(html).toContain('<a href="https://chromewebstore.google.com/detail/abc" target="_blank" rel="noopener noreferrer">Chrome Web Store</a>');
    expect(html).not.toContain('>https://chromewebstore');
    // Markup im Linktext bleibt escaped, der Satzpunkt hinter der Klammer außerhalb.
    expect(html).toContain('>&lt;b&gt;x&lt;/b&gt;</a>.');
  });

  it('keine lange Roh-Adresse als Linktext in den Hilfetexten (UX-013)', async () => {
    for (const lang of ['en', 'de', 'hr', 'hu']) {
      for (const [id, sec] of Object.entries(await helpTexts(lang))) {
        for (const p of sec.p ?? []) {
          const outsideLinks = p.replace(/\[[^\]]+\]\([^)]+\)/g, '');
          expect(outsideLinks).withContext(`${lang}: help.s.${id}`).not.toMatch(/https?:\/\/\S{33,}|\{\{\w+Url\}\}/);
        }
      }
    }
  });

  it('eine lange Adresse macht die Seite am Handy nicht breiter (UX-013)', async () => {
    await TestBed.configureTestingModule({
      imports: [HelpComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const translate = TestBed.inject(TranslateService);
    const longUrl = 'https://raw.githubusercontent.com/example/' + 'a'.repeat(120) + '/file.js';
    translate.setTranslation('en', { help: { s: { extension: { t: 'RepCheck', p: [`Siehe ${longUrl} danach.`] } } } });
    translate.use('en');
    const fixture = TestBed.createComponent(HelpComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = '390px';
    fixture.detectChanges();
    const p = host.querySelector('#extension mat-card-content p') as HTMLElement;
    expect(getComputedStyle(p).overflowWrap).toBe('anywhere');
    expect(p.scrollWidth).toBeLessThanOrEqual(p.clientWidth + 1);
  });

  describe('Inhaltsverzeichnis und „Nach oben“ (UX-012)', () => {
    async function render() {
      await TestBed.configureTestingModule({
        imports: [HelpComponent],
        providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
      }).compileComponents();
      const fixture = TestBed.createComponent(HelpComponent);
      fixture.detectChanges();
      return { fixture, el: fixture.nativeElement as HTMLElement, cmp: fixture.componentInstance };
    }

    it('jeder Eintrag ist ein echter Link mit Adresse #abschnitt und ≥ 44 px hoch', async () => {
      const { el, cmp } = await render();
      const links = [...el.querySelectorAll<HTMLAnchorElement>('.help-toc a')];
      expect(links.length).toBe(cmp.sections.length);
      links.forEach((a, i) => {
        // Mit href ist der Eintrag ein Tab-Stopp, wird als Link angesagt und lässt sich kopieren.
        expect(a.getAttribute('href')).toMatch(new RegExp(`#${cmp.sections[i].id}$`));
        expect(a.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
      });
    });

    it('„Nach oben“ ist ein Knopf (Tastatur) mit ≥ 44 px Trefferfläche', async () => {
      const { el, cmp } = await render();
      const backs = [...el.querySelectorAll<HTMLElement>('.back-top')];
      expect(backs.length).toBe(cmp.sections.length);
      for (const b of backs) {
        expect(b.tagName).toBe('BUTTON');
        expect(b.getAttribute('type')).toBe('button');
        expect(b.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
      }
    });

    it('Klick scrollt zum Abschnitt und gibt ihm den Fokus; Strg-Klick (neuer Tab) nicht', async () => {
      const { el, cmp } = await render();
      const spy = spyOn(cmp, 'scrollTo').and.callThrough();
      const link = el.querySelector<HTMLAnchorElement>('.help-toc a[href$="#extension"]')!;
      link.addEventListener('click', e => e.preventDefault());   // der Testlauf soll nirgendwohin navigieren
      link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ctrlKey: true }));
      expect(spy).not.toHaveBeenCalled();
      link.click();
      expect(spy).toHaveBeenCalledWith('extension');
      expect(document.activeElement).toBe(el.querySelector('#extension'));
      el.querySelector<HTMLButtonElement>('#extension .back-top')!.click();
      expect(document.activeElement).toBe(el.querySelector('#help-toc'));
    });
  });

  it('asParagraphs() normalisiert Array, String und Leerwert', () => {
    expect(component.asParagraphs(['a', 'b'])).toEqual(['a', 'b']);
    expect(component.asParagraphs('einzeln')).toEqual(['einzeln']);
    expect(component.asParagraphs(null)).toEqual([]);
    expect(component.asParagraphs(undefined)).toEqual([]);
  });

  it('linkify() wandelt http(s)-URLs in klickbare Links (target=_blank, rel=noopener)', () => {
    const html = component.linkify('Siehe https://github.com/kahalm/repcheck und https://addons.mozilla.org/de/firefox/addon/repcheck/ danach.');
    expect(html).toContain('<a href="https://github.com/kahalm/repcheck" target="_blank" rel="noopener noreferrer">https://github.com/kahalm/repcheck</a>');
    expect(html).toContain('<a href="https://addons.mozilla.org/de/firefox/addon/repcheck/" target="_blank" rel="noopener noreferrer">');
  });

  it('linkify() lässt den abschließenden Satzpunkt außerhalb des Links', () => {
    const html = component.linkify('Import via https://raw.githubusercontent.com/kahalm/repcheck/master/repcheck.user.js.');
    expect(html).toContain('repcheck.user.js" target="_blank"');
    expect(html).toContain('</a>.');
  });

  it('linkify() escaped HTML und lässt linkfreien Text unverändert', () => {
    expect(component.linkify('a < b & c')).toBe('a &lt; b &amp; c');
    expect(component.linkify('kein Link hier')).toBe('kein Link hier');
  });

  it('scrollt bei vorhandenem Fragment (Deep-Link /help#extension) zum Abschnitt', () => {
    jasmine.clock().install();
    const withFragment = build('extension');
    const spy = spyOn(withFragment, 'scrollTo');
    withFragment.ngAfterViewInit();
    jasmine.clock().tick(1);
    expect(spy).toHaveBeenCalledWith('extension');
    jasmine.clock().uninstall();
  });

  it('scrollt ohne Fragment nicht', () => {
    const spy = spyOn(component, 'scrollTo');
    component.ngAfterViewInit();
    expect(spy).not.toHaveBeenCalled();
  });
});
