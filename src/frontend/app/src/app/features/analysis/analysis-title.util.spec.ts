import { pgnTitle, analysisTitle, analysisHeading, analysisHeadingMeta, analysisHeadingText } from './analysis-title.util';

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

/** Wunsch 2026-11-28: oben steht der Titel der Partie statt nur „Analyse". */
describe('analysisHeading', () => {
  const text = (h: Record<string, string> | null, ex?: string | null) => {
    const r = analysisHeading(h, ex);
    return r ? analysisHeadingText(r, 'ohne Name') : null;
  };
  const meta = (h: Record<string, string>) => analysisHeadingMeta(analysisHeading(h)!, r => `Runde ${r}`);

  it('„Weiß – Schwarz" aus den Kopfzeilen', () => {
    expect(text({ White: 'Carlsen, Magnus', Black: 'Nakamura, Hikaru' })).toBe('Carlsen, Magnus – Nakamura, Hikaru');
  });
  it('fehlen beide Namen („?", „-", leer) oder die Kopfzeilen, gibt es keinen Titel — auch nicht mit Turnier', () => {
    expect(analysisHeading(null)).toBeNull();
    expect(analysisHeading({})).toBeNull();
    expect(analysisHeading({ White: '?', Black: '?', Event: 'Open' })).toBeNull();
    expect(analysisHeading({ White: ' ', Black: '-' })).toBeNull();
    expect(analysisHeading(null, '? – ?')).toBeNull();
    expect(analysisHeading(null, '')).toBeNull();
  });
  it('ein fehlender Name steht als „ohne Name" da', () => {
    expect(text({ White: 'kahalm', Black: '?' })).toBe('kahalm – ohne Name');
    expect(text({ White: '??', Black: 'Gegner' })).toBe('ohne Name – Gegner');
  });
  it('ein ausdrücklicher Titel (Kapitel, Title) geht vor, das Turnier doppelt sich nicht', () => {
    expect(text({ ChapterName: 'Kapitel 3: Isolani', White: 'A', Black: 'B' })).toBe('Kapitel 3: Isolani');
    expect(text({ Title: 'Unsterbliche Partie', White: '?', Black: '?' })).toBe('Unsterbliche Partie');
    expect(analysisHeading({ Title: 'Open', Event: 'Open' })!.event).toBeNull();
  });
  it('Titel eines Verlauf-Eintrags: „A – B" wird zerlegt, sonst frei übernommen; Kopfzeilen gehen vor', () => {
    expect(text(null, 'Carlsen – Nakamura')).toBe('Carlsen – Nakamura');
    expect(text(null, 'kahalm – ?')).toBe('kahalm – ohne Name');
    expect(text(null, 'Landesliga')).toBe('Landesliga');
    expect(text({ White: 'X', Black: 'Y' }, 'Alt')).toBe('X – Y');
  });
  it('lange Namen bleiben ganz (gekürzt wird per CSS mit „…")', () => {
    const long = 'Wolfgang Amadeus Theophilus von Schachmattingen-Hohenzollern'.repeat(2);
    expect(text({ White: long, Black: 'B' })).toBe(`${long} – B`);
  });
  it('Zeile darunter: Ergebnis · Turnier, Runde · Jahr — nur, was da ist', () => {
    expect(meta({ White: 'A', Black: 'B', Result: '1/2-1/2', Event: 'Tata Steel', Round: '3', Date: '2024.01.20' }))
      .toBe('½–½ · Tata Steel, Runde 3 · 2024');
    expect(meta({ White: 'A', Black: 'B', Result: '1-0' })).toBe('1–0');
    expect(meta({ White: 'A', Black: 'B', Result: '*', Event: '?', Round: '-', Date: '????.??.??' })).toBe('');
    expect(meta({ White: 'A', Black: 'B', Result: '0-1', Round: '5' })).toBe('0–1 · Runde 5');
  });
});
