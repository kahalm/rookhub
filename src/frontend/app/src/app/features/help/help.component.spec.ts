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
