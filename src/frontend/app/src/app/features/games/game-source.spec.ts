import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { gameSourceIcon, gameSourceLabel } from './game-source';

describe('gameSourceLabel (b1-rk-partie-quelle-pgn)', () => {
  let translate: TranslateService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'de' })] });
    translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { games: { source: { scoresheet: 'Partieformular', pgn: 'PGN-Upload' } } });
    translate.use('de');
  });

  it('übersetzt eigene Uploads statt den Rohschlüssel zu zeigen', () => {
    expect(gameSourceLabel('pgn', translate)).toBe('PGN-Upload');
    expect(gameSourceLabel('scoresheet', translate)).toBe('Partieformular');
  });

  it('zeigt Plattformen mit ihrem Markennamen, Unbekanntes unverändert', () => {
    expect(gameSourceLabel('lichess', translate)).toBe('Lichess');
    expect(gameSourceLabel('chess.com', translate)).toBe('Chess.com');
    expect(gameSourceLabel('Tiroler Landesliga', translate)).toBe('Tiroler Landesliga');
    expect(gameSourceLabel(undefined, translate)).toBe('');
  });

  it('Symbol je Quelle', () => {
    expect(gameSourceIcon('pgn')).toBe('description');
    expect(gameSourceIcon('lichess')).toBe('public');
    expect(gameSourceIcon('scoresheet')).toBe('photo_camera');
    expect(gameSourceIcon('chess.com')).toBe('sports_esports');
  });
});
