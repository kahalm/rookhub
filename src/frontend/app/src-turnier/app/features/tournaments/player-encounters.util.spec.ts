import { CrawlerPairing } from '@rh/core/models';
import { buildEncounters, resultForColour, resultPoints, PlayerRoundResult } from './player-encounters.util';

function ind(round: number, board: number, white: string, black: string, result: string | null): CrawlerPairing {
  return { id: round * 100 + board, roundNumber: round, boardNumber: board, white, black, result };
}

function team(round: number, match: number, homeTeam: string, awayTeam: string, homeScore: number | null, awayScore: number | null): CrawlerPairing {
  return { id: round * 100 + match, roundNumber: round, matchNumber: match, homeTeam, awayTeam, homeScore, awayScore };
}

describe('player-encounters.util', () => {
  describe('resultForColour', () => {
    it('liest das Ergebnis aus Sicht der Farbe', () => {
      expect(resultForColour('1-0', 'white')).toBe('1');
      expect(resultForColour('1-0', 'black')).toBe('0');
      expect(resultForColour('½-½', 'black')).toBe('½');
      expect(resultForColour('0-1', 'black')).toBe('1');
      expect(resultForColour('1/2-1/2', 'white')).toBe('½');
    });

    it('kampflos als „+" / „−"', () => {
      expect(resultForColour('+--', 'white')).toBe('+');
      expect(resultForColour('+--', 'black')).toBe('−');
      expect(resultForColour('--+', 'black')).toBe('+');
      expect(resultForColour('0-1F', 'white')).toBe('−');
    });

    it('ohne Ergebnis: offen', () => {
      expect(resultForColour(null, 'white')).toBeNull();
      expect(resultForColour('', 'white')).toBeNull();
      expect(resultForColour(' - ', 'black')).toBeNull();
    });

    it('unbekannte Schreibweise bleibt Rohtext', () => {
      expect(resultForColour('x?y', 'white')).toBe('x?y');
    });
  });

  it('resultPoints', () => {
    expect(resultPoints('1')).toBe(1);
    expect(resultPoints('+')).toBe(1);
    expect(resultPoints('½')).toBe(0.5);
    expect(resultPoints('−')).toBe(0);
    expect(resultPoints(null)).toBeNull();
  });

  describe('Einzelturnier', () => {
    const pairings = [
      ind(1, 3, 'Glek Igor', 'Oberschmid Patrik', '1-0'),
      ind(1, 4, 'A', 'B', '½-½'),
      ind(2, 36, 'Oberschmid Patrik', '', '½'),
      ind(3, 7, 'C', 'Oberschmid Patrik', '0-1'),
      ind(4, 5, 'Oberschmid Patrik', 'D', null),
    ];

    it('je Runde Farbe, Gegner, Ergebnis; Freilos; ausgeloste Runde ist die naechste', () => {
      const e = buildEncounters('Oberschmid Patrik', null, pairings);
      expect(e.teamTournament).toBeFalse();
      expect(e.rows.map(r => [r.round, r.colour, r.opponent, r.result, r.bye, r.next])).toEqual([
        [1, 'black', 'Glek Igor', '0', false, false],
        [2, null, null, '½', true, false],
        [3, 'black', 'C', '1', false, false],
        [4, 'white', 'D', null, false, true],
      ]);
      expect(e.points).toBe(1.5);
      expect(e.played).toBe(3);
    });

    it('ohne offene Runde gibt es keine naechste', () => {
      const e = buildEncounters('A', null, pairings);
      expect(e.rows.length).toBe(1);
      expect(e.rows[0].next).toBeFalse();
    });
  });

  describe('Mannschaftsturnier', () => {
    const pairings = [
      team(1, 4, 'Spg Fügen', 'Absam', 2.5, 3.5),
      team(1, 5, 'X', 'Y', 3, 3),
      team(2, 2, 'Absam', 'Reutte', 4, 2),
      team(3, 1, 'Zirl', 'Absam', null, null),
      team(4, 1, 'Absam', 'Schwaz', null, null),
    ];

    it('ohne Spielerseite: nur die Mannschaftskaempfe, erste offene Runde = naechste', () => {
      const e = buildEncounters('Embacher, Gregor', 'Absam', pairings, []);
      expect(e.teamTournament).toBeTrue();
      expect(e.hasIndividual).toBeFalse();
      expect(e.points).toBeNull();
      expect(e.rows.map(r => [r.round, r.match?.opponentTeam, r.match?.home, r.match?.score, r.pending, r.next])).toEqual([
        [1, 'Spg Fügen', false, '3.5 : 2.5', false, false],
        [2, 'Reutte', true, '4 : 2', false, false],
        [3, 'Zirl', false, null, true, true],
        [4, 'Schwaz', true, null, true, false],
      ]);
    });

    it('mit Spielerseite: Brett, Gegner und Ergebnis; nicht eingesetzt, wenn die Runde fehlt', () => {
      const results: PlayerRoundResult[] = [
        { roundNumber: 1, boardNumber: 1, opponentSnr: 130, opponentName: 'Simenon, Jozef', opponentElo: 2261, points: '1', result: '1' },
      ];
      const e = buildEncounters('Embacher, Gregor', 'Absam', pairings, results);
      expect(e.hasIndividual).toBeTrue();
      expect(e.points).toBe(1);
      const [r1, r2] = e.rows;
      expect([r1.board, r1.opponent, r1.opponentElo, r1.result, r1.notPlayed]).toEqual([1, 'Simenon, Jozef', 2261, '1', false]);
      expect([r2.opponent, r2.result, r2.notPlayed, r2.pending]).toEqual([null, null, true, false]);
      expect(e.rows.find(r => r.next)?.round).toBe(3);
    });
  });
});
