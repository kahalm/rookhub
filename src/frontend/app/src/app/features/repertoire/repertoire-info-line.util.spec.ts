import { isInfoLineGame } from './repertoire-info-line.util';

describe('isInfoLineGame', () => {
  it('erkennt den [%info]-Marker im Zugtext', () => {
    expect(isInfoLineGame('[Event "R"]\n[White "Idee"]\n\n{[%info]} 1. -- {Text} *')).toBeTrue();
    expect(isInfoLineGame('[White "Idee"]\n\n1. d4 {[%INFO]Idee} d5 *')).toBeTrue();
  });

  it('erkennt das Download-Präfix „Info | " im White-Header', () => {
    expect(isInfoLineGame('[Event "R"]\n[White "Info | Idee"]\n\n1. e4 *')).toBeTrue();
  });

  it('normale Linien und „Info" an anderer Stelle zählen nicht', () => {
    expect(isInfoLineGame('[White "Linie"]\n\n1. e4 {[%tqu "En","","","","e7e5","",10]} e5 *')).toBeFalse();
    expect(isInfoLineGame('[White "Infos zur Linie"]\n\n1. e4 *')).toBeFalse();
    expect(isInfoLineGame('[Black "Info | x"]\n\n1. e4 *')).toBeFalse();
    expect(isInfoLineGame('')).toBeFalse();
  });
});
