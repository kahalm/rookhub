import { ActivatedRoute } from '@angular/router';
import { HelpComponent } from './help.component';

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
