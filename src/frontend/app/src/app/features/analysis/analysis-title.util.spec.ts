import { pgnTitle, analysisTitle } from './analysis-title.util';

/** v1-rk-analysis-pgn-placeholder-title: „? – ?" aus PGN-Platzhaltern ist kein Titel. */
describe('analysisTitle', () => {
  const texts = { untitled: 'Ohne Titel', noName: 'ohne Name' };
  it('macht aus leeren Titeln und reinen Platzhaltern „Ohne Titel"', () => {
    expect(analysisTitle(null, texts)).toBe('Ohne Titel');
    expect(analysisTitle('', texts)).toBe('Ohne Titel');
    expect(analysisTitle('? – ?', texts)).toBe('Ohne Titel');
    expect(analysisTitle('?? – -', texts)).toBe('Ohne Titel');
    expect(analysisTitle('?', texts)).toBe('Ohne Titel');
  });
  it('ersetzt einen fehlenden Namen durch „ohne Name"', () => {
    expect(analysisTitle('kahalm – ?', texts)).toBe('kahalm – ohne Name');
    expect(analysisTitle('? – Gegner', texts)).toBe('ohne Name – Gegner');
  });
  it('behält den Knoten-Zusatz und lässt echte Titel unverändert', () => {
    expect(analysisTitle('? – ? (50k nodes)', texts)).toBe('Ohne Titel (50k nodes)');
    expect(analysisTitle('Anderssen – Kieseritzky', texts)).toBe('Anderssen – Kieseritzky');
    expect(analysisTitle('Landesliga', texts)).toBe('Landesliga');
  });
});

describe('pgnTitle', () => {
  it('Weiß – Schwarz, sonst ein Name oder das Turnier, Platzhalter „?" zählen nicht', () => {
    expect(pgnTitle({ White: 'A', Black: 'B', Event: 'E' })).toBe('A – B');
    expect(pgnTitle({ White: '?', Black: 'B' })).toBe('B');
    expect(pgnTitle({ White: '?', Black: '?', Event: 'E' })).toBe('E');
    expect(pgnTitle({ Event: '?' })).toBeNull();
  });
});
