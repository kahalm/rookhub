import { TestBed } from '@angular/core/testing';
import { EvalGraphComponent, EvalGraphMark } from './eval-graph.component';

describe('EvalGraphComponent', () => {
  function setup(series: (number | null)[], opts: { marks?: EvalGraphMark[]; currentIndex?: number } = {}) {
    TestBed.configureTestingModule({ imports: [EvalGraphComponent] });
    const fixture = TestBed.createComponent(EvalGraphComponent);
    fixture.componentRef.setInput('series', series);
    if (opts.marks) fixture.componentRef.setInput('marks', opts.marks);
    if (opts.currentIndex !== undefined) fixture.componentRef.setInput('currentIndex', opts.currentIndex);
    (fixture.nativeElement as HTMLElement).style.width = '400px';
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const lines = () => Array.from(el.querySelectorAll('polyline.line'))
      .map(l => (l.getAttribute('points') ?? '').split(' ').filter(Boolean));
    return { fixture, el, lines };
  }

  it('zeichnet je gerechneter Stellung einen Punkt, Start bis Endstellung über die ganze Breite', () => {
    const { lines } = setup([50, 60, 40, 55]);
    expect(lines().length).toBe(1);
    expect(lines()[0]).toEqual(['0,50', '333.33,40', '666.67,60', '1000,45']);
  });

  it('eine Lücke bricht die Linie, statt sie zu überbrücken', () => {
    const { lines } = setup([50, 60, null, 55, 45]);
    expect(lines().length).toBe(2);
    expect(lines()[0]).toEqual(['0,50', '250,40']);
    expect(lines()[1]).toEqual(['750,45', '1000,55']);
  });

  it('ein einzelner Punkt zwischen zwei Lücken wird als Punkt gezeigt, nicht als Linie', () => {
    const { el, lines } = setup([null, 60, null]);
    expect(lines().length).toBe(0);
    expect(el.querySelectorAll('.dot.lone').length).toBe(1);
  });

  it('Fläche: oberhalb der Mittellinie hell, unterhalb dunkel — geteilt am Schnittpunkt', () => {
    const { el } = setup([50, 70, 30]);
    const white = el.querySelector('polygon.area-white')!.getAttribute('points')!;
    const black = el.querySelector('polygon.area-black')!.getAttribute('points')!;
    // Weiß vorn bei Stellung 1 (70 % → y 30); Schnitt mit der Mitte zwischen 1 und 2 bei x = 750.
    expect(white).toContain('500,30');
    expect(white).toContain('750,50');
    expect(white).not.toContain('1000,70');
    expect(black).toContain('1000,70');
    expect(black).not.toContain('500,30');
  });

  it('Fehler und grobe Fehler als Punkt auf der Stellung NACH dem Zug', () => {
    const { el } = setup([50, 60, 20, 25], { marks: [{ ply: 1, kind: 'blunder' }, { ply: 2, kind: 'mistake' }] });
    const blunder = el.querySelector('.dot.blunder') as HTMLElement;
    expect(blunder).not.toBeNull();
    expect(parseFloat(blunder.style.left)).toBeCloseTo(66.667, 2);   // Stellung 2 von 3
    expect(parseFloat(blunder.style.top)).toBe(80);                    // 100 − 20 %
    expect(el.querySelectorAll('.dot.mistake').length).toBe(1);
  });

  it('kein Punkt auf einer Lücke', () => {
    const { el } = setup([50, 60, null], { marks: [{ ply: 1, kind: 'blunder' }] });
    expect(el.querySelector('.dot.blunder')).toBeNull();
  });

  it('Marke am aktuellen Zug (−1 = Startstellung ganz links)', () => {
    const { fixture, el } = setup([50, 60, 40, 55, 45], { currentIndex: 1 });
    expect(el.querySelector('line.cursor')!.getAttribute('x1')).toBe('500');
    fixture.componentRef.setInput('currentIndex', -1);
    fixture.detectChanges();
    expect(el.querySelector('line.cursor')!.getAttribute('x1')).toBe('0');
  });

  it('Klick springt zum nächstgelegenen Zug; links der ersten Marke ist die Startstellung (−1)', () => {
    const { fixture, el } = setup([50, 60, 40, 55, 45]);   // 4 Züge
    const emitted: number[] = [];
    fixture.componentInstance.moveClicked.subscribe(i => emitted.push(i));
    const graph = el.querySelector('.graph') as HTMLElement;
    const rect = el.getBoundingClientRect();
    const clickAt = (fraction: number) =>
      graph.dispatchEvent(new MouseEvent('click', { clientX: rect.left + fraction * rect.width, bubbles: true }));

    clickAt(0.05);   // nahe Stellung 0
    clickAt(0.3);    // Stellung 1 = nach Zug 0
    clickAt(1);      // Endstellung = nach Zug 3
    expect(emitted).toEqual([-1, 0, 3]);
  });

  it('ohne Züge: keine Marke, Klick tut nichts', () => {
    const { fixture, el } = setup([50]);
    const emitted: number[] = [];
    fixture.componentInstance.moveClicked.subscribe(i => emitted.push(i));
    (el.querySelector('.graph') as HTMLElement).click();
    expect(el.querySelector('line.cursor')).toBeNull();
    expect(emitted).toEqual([]);
  });
});
