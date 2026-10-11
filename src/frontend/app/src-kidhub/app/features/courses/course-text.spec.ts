import { splitCourseText } from './course-text';

/** UI-Sweep 2026-10-11 (r2-k-course-heading): Texte wörtlich aus Buch 50 (Dev, /api/kids/courses/50/puzzles). */
describe('splitCourseText', () => {
  it('leer → nichts', () => {
    expect(splitCourseText(null)).toEqual([]);
    expect(splitCourseText('  ')).toEqual([]);
  });

  it('ohne Überschrift bleibt der Text ein Absatz', () => {
    expect(splitCourseText('Das Ergebnis ist jedoch genau anders herum, wenn in Stellung 1.4 Schwarz am Zug ist.'))
      .toEqual([{ kind: 'p', text: 'Das Ergebnis ist jedoch genau anders herum, wenn in Stellung 1.4 Schwarz am Zug ist.' }]);
  });

  it('angeklebte Überschrift am Anfang wird abgesetzt', () => {
    expect(splitCourseText('Den Weg verstellen und den Bauern mit dem König unterstützenSchauen wir uns nun Stellung 1.3 an.'))
      .toEqual([
        { kind: 'h', text: 'Den Weg verstellen und den Bauern mit dem König unterstützen' },
        { kind: 'p', text: 'Schauen wir uns nun Stellung 1.3 an.' },
      ]);
  });

  it('angeklebte Überschrift mitten im Text beginnt nach dem letzten Satzende', () => {
    expect(splitCourseText('Das ist normalerweise verloren. Das GewinnmanöverIn diesem ersten Beispiel gewinnt Weiß.'))
      .toEqual([
        { kind: 'p', text: 'Das ist normalerweise verloren.' },
        { kind: 'h', text: 'Das Gewinnmanöver' },
        { kind: 'p', text: 'In diesem ersten Beispiel gewinnt Weiß.' },
      ]);
  });

  it('„Abschnitt 5." ist kein Satzende', () => {
    expect(splitCourseText('Abschnitt 5. Der RandbauerDer Randbauer hat ihre eigenen Regeln.'))
      .toEqual([
        { kind: 'h', text: 'Abschnitt 5. Der Randbauer' },
        { kind: 'p', text: 'Der Randbauer hat ihre eigenen Regeln.' },
      ]);
  });

  it('eine eigene kurze Zeile ohne Satzzeichen ist eine Überschrift', () => {
    expect(splitCourseText('Opposition\\nWir haben gerade gesehen, was passiert.'.replace('\\n', '\n')))
      .toEqual([{ kind: 'h', text: 'Opposition' }, { kind: 'p', text: 'Wir haben gerade gesehen, was passiert.' }]);
  });

  it('Markennamen und kurze Wörter bleiben unangetastet', () => {
    const text = 'Gemäß den Kommentaren in ChessBase Magazine ist das remis. Mit demTurm geht es nicht.';
    expect(splitCourseText(text)).toEqual([{ kind: 'p', text }]);
  });

  it('Fließtext-Zeilen hintereinander bleiben ein Absatz', () => {
    expect(splitCourseText('Erster Satz.\nZweiter Satz.')).toEqual([{ kind: 'p', text: 'Erster Satz.\nZweiter Satz.' }]);
  });
});
