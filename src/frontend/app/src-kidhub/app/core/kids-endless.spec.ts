import { EndlessRun, endlessRatingAt, endlessThresholds, endlessWindows } from './kids-endless';

describe('Kurve des Endlos-Modus', () => {
  const run = (firstMistakeRating: number | null, maxRating: number, solved = 10): EndlessRun =>
    ({ at: 1, solved, maxRating, firstMistakeRating });

  it('Grundkurve: Start 700, fuenf Puzzles je 100 Elo — durchgehend +20', () => {
    const t = endlessThresholds([]);
    expect(t).toEqual({ t1: 900, t2: 1200 });
    expect([0, 5, 10, 15, 25, 30, 40].map(n => endlessRatingAt(n, t))).toEqual([700, 800, 900, 1000, 1200, 1300, 1500]);
  });

  it('adaptiv: wer weit kommt, bekommt steilere Laeufe', () => {
    const t = endlessThresholds([run(1100, 1600), run(1100, 1600), run(1100, 1600)]);
    expect(t).toEqual({ t1: 1100, t2: 1600 });
    expect(endlessRatingAt(5, t)).toBe(900);      // statt 800
    expect(endlessRatingAt(10, t)).toBe(1100);
    expect(endlessRatingAt(25, t)).toBe(1600);
    expect(endlessRatingAt(26, t)).toBe(1620);    // danach wieder flach
  });

  it('nie flacher als die Grundkurve', () => {
    expect(endlessThresholds([run(720, 740), run(760, 780)])).toEqual({ t1: 900, t2: 1200 });
  });

  it('T2 haengt an T1: mindestens 15 Puzzles zu je +20 darueber', () => {
    expect(endlessThresholds([run(1300, 900)])).toEqual({ t1: 1300, t2: 1600 });
  });

  it('nur die letzten zehn (T1) bzw. fuenf (T2) Laeufe zaehlen', () => {
    const old = Array.from({ length: 10 }, () => run(2000, 2500));
    const recent = Array.from({ length: 10 }, () => run(1000, 1300));
    expect(endlessThresholds([...old, ...recent])).toEqual({ t1: 1000, t2: 1300 });
  });

  it('Laeufe ohne Fehler oder ohne sauber geloeste Aufgabe fliessen nicht ein', () => {
    expect(endlessThresholds([run(null, 0), run(null, 0)])).toEqual({ t1: 900, t2: 1200 });
  });

  it('Fenster: ±20 um die Kurve, ab beliebiger Stelle', () => {
    const t = endlessThresholds([]);
    expect(endlessWindows(0, 3, t)).toEqual([
      { minRating: 680, maxRating: 720 }, { minRating: 700, maxRating: 740 }, { minRating: 720, maxRating: 760 },
    ]);
    expect(endlessWindows(20, 1, t)).toEqual([{ minRating: 1080, maxRating: 1120 }]);
  });
});
