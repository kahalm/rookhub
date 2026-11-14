import { AUTO_MINE_KEY, AUTO_MINE_MAX_GAMES, autoMineApplies, readAutoMine, withPlayers, writeAutoMine } from './auto-my-games';

describe('auto-my-games (eine Regel für Formular und Partiedaten)', () => {
  afterEach(() => localStorage.removeItem(AUTO_MINE_KEY));

  it('Vorgabe an; abgewählt merkt sich das Gerät, wieder angewählt räumt den Schlüssel', () => {
    expect(readAutoMine()).toBeTrue();
    writeAutoMine(false);
    expect(localStorage.getItem(AUTO_MINE_KEY)).toBe('0');
    expect(readAutoMine()).toBeFalse();
    writeAutoMine(true);
    expect(localStorage.getItem(AUTO_MINE_KEY)).toBeNull();
  });

  it('gilt nur mit Konto, nicht abgewählt und bis zur Grenze für Massenimporte', () => {
    expect(AUTO_MINE_MAX_GAMES).toBe(5);
    expect(autoMineApplies({ account: true, autoMine: true, games: 1 })).toBeTrue();
    expect(autoMineApplies({ account: true, autoMine: true, games: 5 })).toBeTrue();
    expect(autoMineApplies({ account: true, autoMine: true, games: 6 })).toBeFalse();
    expect(autoMineApplies({ account: true, autoMine: true, games: 0 })).toBeFalse();
    expect(autoMineApplies({ account: false, autoMine: true, games: 1 })).toBeFalse();
    expect(autoMineApplies({ account: true, autoMine: false, games: 1 })).toBeFalse();
  });

  it('withPlayers ersetzt nur die genannte Seite, maskiert Anführungszeichen und ergänzt eine fehlende Kopfzeile', () => {
    const pgn = '[Event "Stadtmeisterschaft"]\n[White "Omer H"]\n[Black "Schwarz \\"x\\""]\n\n1. e4 e5 1/2-1/2\n';
    expect(withPlayers(pgn, null, null)).toBe(pgn);
    expect(withPlayers(pgn, 'Omer, Hogr', null)).toBe(pgn.replace('[White "Omer H"]', '[White "Omer, Hogr"]'));
    expect(withPlayers(pgn, null, 'Ober"schmid')).toContain('[Black "Ober\\"schmid"]\n\n1. e4');
    expect(withPlayers('1. e4 *', 'A', 'B')).toBe('[White "A"]\n[Black "B"]\n1. e4 *');
  });
});
