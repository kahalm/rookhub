import { de, pgnDate, roundLabel, shareText, shortTeam, tn, venueShort } from './league-format';
import { Fixture } from './league.models';

describe('league-format', () => {
  it('schreibt SAN in deutscher Notation', () => {
    expect(de('1.e4 c5 2.Nf3 Nc6 3.Bb5 Qc7 4.O-O')).toBe('1.e4 c5 2.Sf3 Sc6 3.Lb5 Dc7 4.O-O');
    expect(de('exd8=Q+ Rxe1 Kf2')).toBe('exd8=D+ Txe1 Kf2');
    expect(de('Nf6')).toBe('Sf6');
  });

  it('räumt gekürzte Teamnamen auf', () => {
    expect(tn('Spg Fügen-Mayrhofen/Zillertal/')).toBe('Spg Fügen-Mayrhofen/Zillertal');
    expect(shortTeam('Spg Kufstein/Wörgl')).toBe('Kufstein/Wörgl');
    expect(shortTeam('Schwaz')).toBe('Schwaz');
  });

  it('kürzt Spielort und Datum', () => {
    expect(venueShort('Kursaal, Hotel Vivea, Kurstraße 1, 6323 Bad Häring')).toBe('Bad Häring');
    expect(venueShort('Mehrzwecksaal der MS Schwaz (Vereinslokal)')).toBe('Mehrzwecksaal der MS Schwaz');
    expect(pgnDate('2024.05.01')).toBe('01.05.2024');
    expect(pgnDate('2000.??.??')).toBe('2000');
    expect(roundLabel({ round: 2, date: 'So 04.10.2026', played: false, open: true })).toBe('Runde 2, So 04.10. (Prognose)');
  });

  it('baut den WhatsApp-Text mit den drei Wahrscheinlichsten je Brett', () => {
    const e: Fixture = {
      opp: 'Spg Fügen-Mayrhofen/Zillertal/', home: true, date: 'Sa 03.10.2026', time: '14:00',
      venue: 'Kursaal, 6323 Bad Häring', status: 'open', phase: 'R1',
      boards: [
        { board: 1, opp_color: 's', other: 0.06, cand: [
          { n: 'Polterauer, Chiara', elo: 2112, rb: 1, p: 0.669, fide: '1' },
          { n: 'Kleissl, Helmut', elo: 2238, rb: 2, p: 0.218, fide: '2' },
          { n: 'Tabernig, Bernhard', elo: 2251, rb: 3, p: 0.049, fide: '3' }] },
        { board: 2, opp_color: 'w', other: 0.6, cand: [{ n: 'Hengl, Philip', elo: null, rb: 4, p: 0.14, fide: null }] },
      ],
      roster: [
        { rb: 1, n: 'Polterauer, Chiara', elo: 2112, p: .7, prev: '', cur: '', fide: '1', g: 0, acc: [] },
        { rb: 4, n: 'Hengl, Philip', elo: null, p: .3, prev: '', cur: '', fide: null, g: 0, acc: [] },
        { rb: 5, n: 'Hengl, Christian', elo: null, p: .3, prev: '', cur: '', fide: null, g: 0, acc: [] },
      ],
    };
    const t = shareText('Landesliga', 'Schwaz', 1, e);
    expect(t).toContain('*Landesliga R1 · Sa 03.10., 14:00 · Bad Häring*');
    expect(t).toContain('*Schwaz – Fügen-Mayrhofen/Zillertal*');
    expect(t).toContain('1 ⬛ *Polterauer 2112 – 67 %*');
    expect(t).toContain('Kleissl 2238 – 22 %, Tabernig 2251 – 5 %');
    expect(t).toContain('2 ⬜ *Hengl P. – 14 %*');          // Nachname doppelt → Anfangsbuchstabe
    expect(t).toContain('andere 60 %');
    expect(t).toContain('Runde 1 ist die unsicherste');
  });
});
