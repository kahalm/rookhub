import { ActivatedRoute } from '@angular/router';
import { HelpComponent, MENU_HELP } from './help.component';

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
