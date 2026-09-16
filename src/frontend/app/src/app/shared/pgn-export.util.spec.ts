import { pgnFileName, stripInternalMarkers } from './pgn-export.util';

describe('pgn-export.util', () => {
  it('stripInternalMarkers entfernt [%alt]/[%info], behält die übrigen Marker', () => {
    expect(stripInternalMarkers('e6 {[%cal Gd7d5][%alt c5 e5 c6 d6]Bereits nach diesem Zug} 2. Nf3'))
      .toBe('e6 {[%cal Gd7d5]Bereits nach diesem Zug} 2. Nf3');
    expect(stripInternalMarkers('cxb4 {[%alt b6]Es gibt auch gute Alternativen} 5. a3'))
      .toBe('cxb4 {Es gibt auch gute Alternativen} 5. a3');
    expect(stripInternalMarkers('e5 {[%alt d5]} 2. Nf3')).toBe('e5 2. Nf3');          // leerer Kommentar fällt weg
    expect(stripInternalMarkers('{[%info]} 1. -- {Text} *')).toBe(' 1. -- {Text} *');
    expect(stripInternalMarkers('1. e4 {[%tqu "En","x","","","e7e6","",10]} e6 {[%csl Rc5]}'))
      .toBe('1. e4 {[%tqu "En","x","","","e7e6","",10]} e6 {[%csl Rc5]}');
  });

  it('stripInternalMarkers lässt Header und Zeilenumbrüche stehen', () => {
    expect(stripInternalMarkers('[Event "X"]\n[Round "1"]\n\n1. e4 e6 {[%alt c5]Französisch}\n2. d4 *\n'))
      .toBe('[Event "X"]\n[Round "1"]\n\n1. e4 e6 {Französisch}\n2. d4 *\n');
  });

  it('pgnFileName entspricht der Backend-Regel', () => {
    expect(pgnFileName('Lifetime Repertoires: Martinovićs Französisch', '1) Weiß spielt ohne 2.d4'))
      .toBe('Lifetime_Repertoires_Martinovićs_Französisch_1_Weiß_spielt_ohne_2_d4.pgn');
    expect(pgnFileName(null, '')).toBe('course.pgn');
    expect(pgnFileName('K', 'x'.repeat(100))).toBe(`K_${'x'.repeat(80)}.pgn`);
  });
});
