import {
  WIDE_FIT_MAX_WIDTH, ZOOM_STEPS, currentPage, fitWidthZoom, nextZoom, pagesToRender, parsePageInput, readingAnchor, scrollTopForAnchor,
} from './reader-progress.util';

describe('reader-progress.util', () => {
  it('nextZoom geht stufenweise und bleibt am Rand stehen', () => {
    expect(nextZoom(1, 1)).toBe(1.25);
    expect(nextZoom(1, -1)).toBe(0.8);
    expect(nextZoom(1.1, 1)).toBe(1.25);     // „Breite"-Faktor zwischen zwei Stufen
    expect(nextZoom(1.1, -1)).toBe(1);
    expect(nextZoom(ZOOM_STEPS[ZOOM_STEPS.length - 1], 1)).toBe(4);
    expect(nextZoom(ZOOM_STEPS[0], -1)).toBe(0.5);
  });

  it('fitWidthZoom füllt die Breite abzüglich Rand und bleibt in Grenzen', () => {
    expect(fitWidthZoom(632, 600)).toBe(1);
    expect(fitWidthZoom(390, 612)).toBeCloseTo(0.585, 3);
    expect(fitWidthZoom(0, 612)).toBe(1);
    expect(fitWidthZoom(100000, 10)).toBe(6);
  });

  it('fitWidthZoom deckelt die Kastenbreite (Breitbild, c1-eb-reader-ultrawide-layout)', () => {
    expect(WIDE_FIT_MAX_WIDTH).toBe(1400);
    expect(fitWidthZoom(2700, 684, 16, WIDE_FIT_MAX_WIDTH)).toBeCloseTo((1400 - 32) / 684, 3);
    expect(fitWidthZoom(900, 684, 16, WIDE_FIT_MAX_WIDTH)).toBe(fitWidthZoom(900, 684));   // schmaler: kein Unterschied
  });

  it('currentPage nimmt die Seite in der Sichtfeldmitte, die erste solange sie oben steht', () => {
    const tops = [0, 1000, 2000, 3000];
    const heights = [1000, 1000, 1000, 1000];
    expect(currentPage(0, 800, tops, heights)).toBe(1);
    expect(currentPage(599, 800, tops, heights)).toBe(1);
    expect(currentPage(600, 800, tops, heights)).toBe(2);
    expect(currentPage(2650, 800, tops, heights)).toBe(4);
    expect(currentPage(0, 800, [], [])).toBe(1);
    // Kurze Seiten: die Sonde liegt in der halben Seitenhöhe, nicht in der halben Sichtfeldhöhe.
    expect(currentPage(0, 2000, [0, 300, 600], [300, 300, 300])).toBe(1);
    expect(currentPage(200, 2000, [0, 300, 600], [300, 300, 300])).toBe(2);
  });

  it('pagesToRender nimmt die sichtbaren Seiten plus Vorlauf', () => {
    const tops = [0, 1000, 2000, 3000, 4000];
    const heights = [1000, 1000, 1000, 1000, 1000];
    expect([...pagesToRender(0, 800, tops, heights, 0)]).toEqual([1]);
    expect([...pagesToRender(0, 800, tops, heights, 1)]).toEqual([1, 2]);
    expect([...pagesToRender(2100, 800, tops, heights, 1)]).toEqual([2, 3, 4]);
    expect([...pagesToRender(99999, 800, tops, heights, 0)]).toEqual([5]);   // nie leer
  });

  it('parsePageInput nimmt nur Zahlen im Bereich', () => {
    expect(parsePageInput(' 12 ', 100)).toBe(12);
    expect(parsePageInput('0', 100)).toBeNull();
    expect(parsePageInput('101', 100)).toBeNull();
    expect(parsePageInput('abc', 100)).toBeNull();
    expect(parsePageInput('1', 0)).toBe(1);
  });

  it('readingAnchor/scrollTopForAnchor halten die Lesestelle, wenn die Seiten schmaler werden (Brett vergrößert)', () => {
    // Vorher: Seiten 1000 px hoch + 12 px Abstand; man steht 300 px tief in Seite 8 (Index 7).
    const tops = [0, 1012, 2024, 3036, 4048, 5060, 6072, 7084, 8096, 9108];
    const heights = tops.map(() => 1000);
    const anchor = readingAnchor(7384, tops, heights);
    expect(anchor).toEqual({ index: 7, frac: 0.3 });
    // Nachher: Leser schmaler, Seiten 700 px hoch — derselbe scrollTop 7384 läge auf Seite 11; die Lesestelle bleibt auf Seite 8.
    const tops2 = tops.map((_, i) => i * 712);
    const heights2 = tops.map(() => 700);
    expect(scrollTopForAnchor(anchor, tops2, heights2)).toBe(7 * 712 + 210);
    expect(currentPage(7 * 712 + 210, 300, tops2, heights2)).toBe(8);
  });

  it('readingAnchor: ganz oben, im Zwischenraum und ohne Seiten', () => {
    expect(readingAnchor(0, [0, 1012], [1000, 1000])).toEqual({ index: 0, frac: 0 });
    expect(readingAnchor(1005, [0, 1012], [1000, 1000])).toEqual({ index: 0, frac: 1 });   // Abstand gehört zur Seite darüber
    expect(readingAnchor(50, [], [])).toEqual({ index: 0, frac: 0 });
    expect(scrollTopForAnchor({ index: 5, frac: 0.5 }, [0, 712], [700, 700])).toBe(712 + 350);   // weniger Seiten → letzte
    expect(scrollTopForAnchor({ index: 0, frac: 0 }, [], [])).toBe(0);
  });
});
