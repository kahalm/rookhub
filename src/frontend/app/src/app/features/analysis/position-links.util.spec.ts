import { chessableFenSearchUrl, positionShareUrl } from './position-links.util';

describe('position-links.util', () => {
  it('schreibt die FEN für Chessables Suche mit U und %20 (wie der RepCheck-Knopf)', () => {
    expect(chessableFenSearchUrl('rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1')).toBe(
      'https://www.chessable.com/courses/fen/rnbqkbnrUppppppppU8U8U4P3U8UPPPP1PPPURNBQKBNR%20b%20KQkq%20e3%200%201/');
  });

  it('der Teilen-Link öffnet das Analysebrett mit der Stellung; Schwarz unten nur, wenn gewünscht', () => {
    const fen = 'r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3';
    expect(positionShareUrl('https://rookhub.example', fen)).toBe(
      'https://rookhub.example/analysis?fen=r1bqkbnr%2Fpppp1ppp%2F2n5%2F4p3%2F4P3%2F5N2%2FPPPP1PPP%2FRNBQKB1R+w+KQkq+-+2+3');
    expect(positionShareUrl('https://rookhub.example', fen, 'black')).toContain('&orientation=black');
  });

  it('der Teilen-Link kommt beim Lesen unverändert wieder heraus', () => {
    const fen = '8/8/8/8/8/8/k7/K6R w - - 0 1';
    const url = new URL(positionShareUrl('https://x.test', fen, 'black'));
    expect(url.searchParams.get('fen')).toBe(fen);
    expect(url.searchParams.get('orientation')).toBe('black');
  });
});
