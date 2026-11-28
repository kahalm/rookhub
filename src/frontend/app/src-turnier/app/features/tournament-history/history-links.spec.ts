import { chessResultsCardUrl, fideProfileUrl } from './history-links';

describe('history-links', () => {
  it('FIDE-Profil nur mit gueltiger Kennung', () => {
    expect(fideProfileUrl('1693034')).toBe('https://ratings.fide.com/profile/1693034');
    expect(fideProfileUrl(' 1693034 ')).toBe('https://ratings.fide.com/profile/1693034');
    expect(fideProfileUrl(null)).toBeNull();
    expect(fideProfileUrl(undefined)).toBeNull();
    expect(fideProfileUrl('')).toBeNull();
    // „0" ist bei chess-results „keine Nummer".
    expect(fideProfileUrl('0')).toBeNull();
    expect(fideProfileUrl('12a')).toBeNull();
  });

  it('Spielerkarte mit Startnummer, sonst die Turnierseite', () => {
    expect(chessResultsCardUrl('1107064', 44))
      .toBe('https://chess-results.com/tnr1107064.aspx?lan=0&art=9&snr=44');
    expect(chessResultsCardUrl('1107064', null)).toBe('https://chess-results.com/tnr1107064.aspx?lan=0');
    expect(chessResultsCardUrl('1107064', undefined)).toBe('https://chess-results.com/tnr1107064.aspx?lan=0');
    expect(chessResultsCardUrl('1107064', 0)).toBe('https://chess-results.com/tnr1107064.aspx?lan=0');
  });

  it('ohne gueltige Turnierkennung kein Link', () => {
    expect(chessResultsCardUrl('', 44)).toBeNull();
    expect(chessResultsCardUrl('abc', 44)).toBeNull();
  });
});
