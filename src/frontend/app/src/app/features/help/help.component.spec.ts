import { HelpComponent } from './help.component';

describe('HelpComponent', () => {
  let component: HelpComponent;

  beforeEach(() => {
    // Keine DI-Abhängigkeiten — die Komponente ist rein (Struktur + zwei Helfer).
    component = new HelpComponent();
  });

  it('listet alle Hilfe-Abschnitte mit eindeutigen Ids und je einem Icon', () => {
    expect(component.sections.length).toBeGreaterThan(0);
    const ids = component.sections.map(s => s.id);
    expect(new Set(ids).size).toBe(ids.length); // keine Duplikate
    expect(component.sections.every(s => !!s.icon)).toBeTrue();
    // Kernbereiche müssen abgedeckt sein
    ['welcome', 'tournaments', 'puzzles', 'trainingGoals', 'privacy'].forEach(id =>
      expect(ids).toContain(id),
    );
  });

  it('asParagraphs() normalisiert Array, String und Leerwert', () => {
    expect(component.asParagraphs(['a', 'b'])).toEqual(['a', 'b']);
    expect(component.asParagraphs('einzeln')).toEqual(['einzeln']);
    expect(component.asParagraphs(null)).toEqual([]);
    expect(component.asParagraphs(undefined)).toEqual([]);
  });
});
