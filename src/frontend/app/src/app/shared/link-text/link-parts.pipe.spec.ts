import { LinkPartsPipe, splitLinks } from './link-parts.pipe';

describe('splitLinks', () => {
  it('lässt Text ohne Adresse als ein Stück', () => {
    expect(splitLinks('Hallo Welt')).toEqual([{ text: 'Hallo Welt' }]);
  });

  it('liefert für leeren/fehlenden Text keine Stücke', () => {
    expect(splitLinks('')).toEqual([]);
    expect(splitLinks(null)).toEqual([]);
    expect(splitLinks(undefined)).toEqual([]);
  });

  it('macht aus der Formular-Nachricht einen Link und behält Zeilenumbrüche im Text', () => {
    const body = 'Formulare: Stapel 5 ist eingelesen:\n• Uebold, Jonathan – Manz, Dieter 1-0 — https://leaguehub.oberschmid.homes/verein/formular/120\nUnsicherheiten stehen dort.';
    expect(splitLinks(body)).toEqual([
      { text: 'Formulare: Stapel 5 ist eingelesen:\n• Uebold, Jonathan – Manz, Dieter 1-0 — ' },
      { text: 'https://leaguehub.oberschmid.homes/verein/formular/120', href: 'https://leaguehub.oberschmid.homes/verein/formular/120' },
      { text: '\nUnsicherheiten stehen dort.' },
    ]);
  });

  it('nimmt Satzzeichen am Ende nicht in die Adresse, Abfragen und Anker aber schon', () => {
    expect(splitLinks('Siehe https://x.at/admin?tab=uploads#a.')).toEqual([
      { text: 'Siehe ' },
      { text: 'https://x.at/admin?tab=uploads#a', href: 'https://x.at/admin?tab=uploads#a' },
      { text: '.' },
    ]);
    expect(splitLinks('(http://x.at/a), dann')).toEqual([
      { text: '(' }, { text: 'http://x.at/a', href: 'http://x.at/a' }, { text: '), dann' },
    ]);
  });

  it('findet mehrere Adressen', () => {
    expect(splitLinks('https://a.at https://b.at').filter(p => p.href).map(p => p.href)).toEqual(['https://a.at', 'https://b.at']);
  });

  it('verlinkt nur http(s) — nie javascript:, data: oder ein nacktes Schema', () => {
    expect(splitLinks('javascript:alert(1) data:text/html,x https://')).toEqual([{ text: 'javascript:alert(1) data:text/html,x https://' }]);
  });

  it('die Pipe gibt dieselben Stücke aus', () => {
    expect(new LinkPartsPipe().transform('a https://x.at b')).toEqual(splitLinks('a https://x.at b'));
  });
});
